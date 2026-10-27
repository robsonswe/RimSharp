#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.AI
{
    /// <summary>
    /// Concrete IAIProviderClient implementation that talks to the OpenRouter
    /// chat completions API (https://openrouter.ai/api/v1/chat/completions).
    /// Reads the API key and preferred model from IConfigService:
    ///   - "openrouter_api_key"
    ///   - "ai_model" (e.g. "openai/gpt-4o-mini")
    /// </summary>
    public class OpenRouterProviderClient : IAIProviderClient
    {
        private const string BaseUrl = "https://openrouter.ai/api/v1/chat/completions";
        private const string ConfigApiKey = "openrouter_api_key";
        private const string ConfigModel = "ai_model";
        private const string DefaultModel = "openrouter/free";

        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfigService _configService;

        public string ProviderId => "openrouter";
        public string DisplayName => "OpenRouter";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_configService.GetConfigValue(ConfigApiKey));

        public OpenRouterProviderClient(IHttpClientFactory httpFactory, IConfigService configService)
        {
            _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        public void SetApiKey(string apiKey) => _configService.SetConfigValue(ConfigApiKey, apiKey);
        public string GetApiKey() => _configService.GetConfigValue(ConfigApiKey);

        public void SetModel(string model) => _configService.SetConfigValue(ConfigModel, model);
        public string GetModel() => _configService.GetConfigValue(ConfigModel);

        /// <summary>
        /// Single provider request — NO internal retry loop. Rate limits are
        /// reported to the caller in TWO forms: the proper HTTP 429, and
        /// (observed in practice) an HTTP 200 whose body wraps the error:
        /// {"error":{"message":"Rate limit exceeded: ...","code":429}}.
        /// Both are parsed uniformly and returned with RetryAfterSeconds so
        /// the agent service owns the retry policy and shows the live
        /// attempt counter in the dialog.
        /// </summary>
        public async Task<AICompletionResponse> CompleteAsync(AICompletionRequest request, CancellationToken ct = default)
        {
            var apiKey = _configService.GetConfigValue(ConfigApiKey);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new AICompletionResponse { Success = false, ErrorMessage = "OpenRouter API key is not configured." };
            }

            var model = _configService.GetConfigValue(ConfigModel);
            if (string.IsNullOrWhiteSpace(model)) model = DefaultModel;

            var payload = new Dictionary<string, object?>
            {
                ["model"] = model,
                ["temperature"] = request.Temperature,
                ["max_tokens"] = request.MaxTokens,
                ["messages"] = request.Messages.Select(m => new Dictionary<string, string>
                {
                    ["role"] = m.Role,
                    ["content"] = m.Content
                }).ToList()
            };
            if (request.RequestJsonOutput)
            {
                payload["response_format"] = new Dictionary<string, string> { ["type"] = "json_object" };
            }

            try
            {
                var client = _httpFactory.CreateClient("AIProvider");
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", "https://rimsharp.local");
                httpRequest.Headers.TryAddWithoutValidation("X-Title", "RimSharp");

                httpRequest.Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

                using var response = await client.SendAsync(httpRequest, ct).ConfigureAwait(false);
                var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                int? retryAfter = null;
                string? errorMessage = null;

                JsonDocument? bodyDoc = null;
                try { bodyDoc = JsonDocument.Parse(raw); } catch (JsonException) { }

                using (bodyDoc)
                {
                    if (bodyDoc != null)
                    {
                        var root = bodyDoc.RootElement;

                        // Success path.
                        if (response.IsSuccessStatusCode &&
                            root.TryGetProperty("choices", out var choicesNode) &&
                            choicesNode.ValueKind == JsonValueKind.Array &&
                            choicesNode.GetArrayLength() > 0)
                        {
                            var message = choicesNode[0].TryGetProperty("message", out var msgNode) ? msgNode : default;
                            var content = message.ValueKind == JsonValueKind.Object &&
                                          message.TryGetProperty("content", out var contentNode)
                                ? contentNode.GetString() ?? string.Empty
                                : string.Empty;
                            return new AICompletionResponse { Success = true, Content = content };
                        }

                        // Error envelope — OpenRouter sometimes wraps the
                        // rate-limit error INSIDE a 200 body; treat any body
                        // "error" node as a failure regardless of status.
                        if (root.TryGetProperty("error", out var errNode) &&
                            errNode.ValueKind == JsonValueKind.Object)
                        {
                            (retryAfter, errorMessage) = ParseRateLimitError(errNode, (int)response.StatusCode);
                        }
                    }
                }

                // Fall back to a plain HTTP Retry-After header when the body
                // carried no reset information.
                if (retryAfter == null && response.Headers.TryGetValues("Retry-After", out var retryVals) &&
                    double.TryParse(retryVals.FirstOrDefault(), System.Globalization.CultureInfo.InvariantCulture, out var secs))
                {
                    retryAfter = (int)Math.Ceiling(secs);
                }

                try { RimSharp.Core.Services.DiagLog.WriteLine($"OpenRouter HTTP {(int)response.StatusCode} — retryAfter={retryAfter}"); } catch { }

                errorMessage ??= $"OpenRouter HTTP {(int)response.StatusCode}: {response.ReasonPhrase} — " +
                    (raw.Length > 400 ? raw.Substring(0, 400) + "..." : raw);

                return new AICompletionResponse
                {
                    Success = false,
                    Content = raw,
                    ErrorMessage = errorMessage,
                    RetryAfterSeconds = retryAfter
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return new AICompletionResponse { Success = false, ErrorMessage = ex.Message };
            }
        }

        /// <summary>
        /// OpenRouter-precise rate-limit parsing (do NOT generalize): the
        /// error node may carry metadata.headers with X-RateLimit-Reset as an
        /// epoch in MILLISSECONDS, counters and limit_source, plus
        /// error.message with the remedy hint. Builds a FRIENDLY message —
        /// no raw JSON dump.
        /// </summary>
        private static (int? RetryAfterSeconds, string? FriendlyMessage) ParseRateLimitError(
            JsonElement errNode, int httpStatus)
        {
            int? retryAfter = null;
            string? friendly = null;

            try
            {
                var hdrNode = errNode.ValueKind == JsonValueKind.Object &&
                              errNode.TryGetProperty("metadata", out var metaNode) &&
                              metaNode.TryGetProperty("headers", out var headersNode)
                          ? headersNode
                          : default;

                if (hdrNode.ValueKind == JsonValueKind.Object &&
                    hdrNode.TryGetProperty("X-RateLimit-Reset", out var resetNode) &&
                    long.TryParse(resetNode.ToString(), out var resetValue))
                {
                    const long MsEpochThreshold = 1_000_000_000_000; // > ~2001 in ms
                    var resetMs = resetValue >= MsEpochThreshold ? resetValue : resetValue * 1000;
                    var waitSec = Math.Max((int)Math.Ceiling(
                        (resetMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000.0), 0);
                    retryAfter = Math.Max(waitSec, 1);

                    var resetLocal = DateTimeOffset.FromUnixTimeMilliseconds(resetMs).ToLocalTime();
                    var remaining = hdrNode.TryGetProperty("X-RateLimit-Remaining", out var rem) ? rem.GetString() : null;
                    var limit = hdrNode.TryGetProperty("X-RateLimit-Limit", out var lim) ? lim.GetString() : null;
                    var limitSource = hdrNode.TryGetProperty("limit_source", out var src) ? src.GetString() : null;

                    var sb = new StringBuilder();
                    sb.Append($"OpenRouter rate limit reached ({limitSource ?? "free tier"}): {remaining}/{limit} requests left.");
                    sb.Append($" The limit resets at {resetLocal:HH:mm} (in {HumanizeWait(waitSec)}).");

                    // error.message carries the provider's remedy hint, e.g.
                    // "Rate limit exceeded: free-models-per-day. Add 10 credits
                    // to unlock 1000 free requests per day".
                    if (errNode.TryGetProperty("message", out var providerMsgNode) &&
                        providerMsgNode.ValueKind == JsonValueKind.String)
                    {
                        var providerMessage = providerMsgNode.GetString();
                        if (!string.IsNullOrWhiteSpace(providerMessage))
                        {
                            sb.Append(' ').Append(providerMessage!.Trim());
                        }
                    }
                    friendly = sb.ToString();
                }
            }
            catch (Exception)
            {
                // Malformed metadata — degrade silently; the status-based
                // message below will be used instead.
            }

            if (friendly == null && retryAfter == null)
            {
                // Error envelope without headers — still surface the message.
                var code = errNode.TryGetProperty("code", out var codeNode) ? codeNode.ToString() : null;
                var msg = errNode.TryGetProperty("message", out var msgNode) &&
                          msgNode.ValueKind == JsonValueKind.String
                    ? msgNode.GetString()
                    : null;
                if (msg != null)
                {
                    friendly = $"OpenRouter rate limit (code {code ?? httpStatus.ToString()}): {msg}";
                }
            }

            return (retryAfter, friendly);
        }

        /// <summary>Formats a wait duration for rate-limit messages (e.g. "2h 10m").</summary>
        private static string HumanizeWait(int totalSeconds)
        {
            if (totalSeconds < 90) return $"{totalSeconds}s";
            var minutes = (int)Math.Round(totalSeconds / 60.0);
            if (minutes < 90) return $"{minutes}m";
            var hours = minutes / 60;
            var remainder = minutes % 60;
            return remainder > 0 ? $"{hours}h {remainder}m" : $"{hours}h";
        }
    }
}
