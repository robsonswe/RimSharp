#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.Validation.AI
{
    /// <summary>
    /// Per-mod AI suspect analysis for *indirect* incompatibilities.
    ///
    /// For a target mod:
    ///   1. generate thematic keywords for it,
    ///   2. search ALL installed mods (community content only) with those
    ///      keywords to assemble the target's suspect list,
    ///   3. verify the suspect list with the provider in one call.
    ///
    /// Ludeon content (Core + Expansions) is never analyzed — community mods
    /// only. Existing (direct) conflicts and dependency pairs are skipped;
    /// verdict "none" is discarded so the AI never invents conflicts. The
    /// pipeline is provider-agnostic through IAIProviderClient.
    /// </summary>
    public class AIIncompatibilityAgentService : IAIncompatibilityAgentService
    {
        // Tuning: how much work is delegated to the provider per mod.
        private const int MaxSuspectsPerMod = 12;   // one provider call per run
        private const int MinKeywordLength = 3;     // ignore noise words
        private const int TextExcerptLength = 240;  // prompt excerpt clip
        private const int SearchDescriptionLength = 600; // suspect-search scan depth

        // Provider reliability: free-tier providers rate-limit aggressively.
        // When the provider reports when to retry (Retry-After / reset), that
        // exact wait is honored; only when it reports nothing do we use
        // dynamic exponential backoff. Waits above MaxAutoRetryWaitSeconds
        // (daily resets) cancel the retry loop and surface the real reason.
        private const int MaxProviderRetries = 5;   // retries after the first attempt
        private const int MaxAutoRetryWaitSeconds = 300;

        private readonly IAIProviderClient _provider;

        /// <summary>Diagnostic summary of the most recent run.</summary>
        public string LastRunSummary { get; private set; } = string.Empty;

        /// <summary>Raw error message from the most recent failed provider call.</summary>
        private string _lastProviderError = string.Empty;

        /// <summary>Success flag of the most recent provider call (set by retry wrapper).</summary>
        private bool _providerSuccess;

        public bool IsConfigured => _provider.IsConfigured;

        public AIIncompatibilityAgentService(IAIProviderClient provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public async Task<List<AIIncompatibilityCandidate>> AnalyzeSuspectsForModAsync(
            ModItem mod,
            List<ModItem> allMods,
            Action<string, double>? reportProgress,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var results = new List<AIIncompatibilityCandidate>();

            if (mod == null || string.IsNullOrWhiteSpace(mod.PackageId))
            {
                LastRunSummary = "target mod is missing or has no package id";
                return results;
            }
            if (IsLudeonContent(mod))
            {
                LastRunSummary = $"\"{mod.Name}\" is Ludeon content (Core/Expansion) — only community mods are analyzed";
                return results;
            }

            // Search base: every installed community mod except the target,
            // deduped by PackageId so duplicate installs never duplicate work.
            var pool = (allMods ?? new List<ModItem>())
                .Where(m => !string.IsNullOrWhiteSpace(m.PackageId))
                .Where(m => !IsLudeonContent(m))
                .Where(m => !string.Equals(m.PackageId, mod.PackageId, StringComparison.OrdinalIgnoreCase))
                .GroupBy(m => m.PackageId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            reportProgress?.Invoke($"Scanning {pool.Count} installed mods for possible conflicts with \"{mod.Name}\"...", 5);

            // 1) Keywords for the target mod (one provider call, with retries).
            reportProgress?.Invoke("Extracting keywords...", 15);
            try { System.IO.File.AppendAllText(@"D:\temp\opencode\ai_retry_diag.txt", $"[{DateTime.Now:HH:mm:ss}] Start keyword extraction for {mod.PackageId}\n"); } catch { }
            var (keywords, kwOk) = await GenerateKeywordsForModAsync(
                mod, ct, msg => reportProgress?.Invoke(msg, 20)).ConfigureAwait(false);
            if (!kwOk)
            {
                LastRunSummary = $"provider error while generating keywords: {_lastProviderError}";
                reportProgress?.Invoke($"Provider error — {_lastProviderError}", 100);
                return results;
            }
            if (keywords.Count == 0)
            {
                LastRunSummary = "the provider returned no usable keywords for this mod";
                reportProgress?.Invoke("No keywords returned.", 100);
                return results;
            }
            reportProgress?.Invoke($"{keywords.Count} keyword(s) extracted", 40);

            // 2) Suspect list: score every other mod by keywords found in
            //    its name/description.
            var suspects = FindSuspects(mod, keywords, pool);
            if (suspects.Count == 0)
            {
                LastRunSummary = $"{pool.Count} mods searched — no possible conflicts flagged";
                reportProgress?.Invoke("No possible conflicts flagged.", 100);
                return results;
            }
            reportProgress?.Invoke($"{suspects.Count} possible conflict(s) found — verifying with AI...", 55);

            // 3) Verify the suspect list (one provider call per run).
            var pairs = suspects.Select(other => (A: mod, B: other)).ToList();
            var (verdicts, evalOk) = await EvaluatePairsWithStatusAsync(
                pairs, ct, msg => reportProgress?.Invoke(msg, 75)).ConfigureAwait(false);
            if (!evalOk)
            {
                LastRunSummary = $"provider error while verifying suspects: {_lastProviderError}";
                reportProgress?.Invoke($"Provider error — {_lastProviderError}", 100);
                return results;
            }

            var suspectLookup = suspects.ToDictionary(s => s.PackageId, s => s, StringComparer.OrdinalIgnoreCase);
            foreach (var (a, b, type, reason) in verdicts)
            {
                ModItem source;
                ModItem target;
                if (string.Equals(a, mod.PackageId, StringComparison.OrdinalIgnoreCase) &&
                    suspectLookup.TryGetValue(b, out var tb))
                {
                    source = mod; target = tb;
                }
                else if (string.Equals(b, mod.PackageId, StringComparison.OrdinalIgnoreCase) &&
                         suspectLookup.TryGetValue(a, out var ta))
                {
                    source = ta; target = mod;
                }
                else
                {
                    continue;
                }

                results.Add(new AIIncompatibilityCandidate
                {
                    SourceMod = source,
                    TargetMod = target,
                    Type = type,
                    Reason = reason,
                    Confirmed = false
                });
            }

            LastRunSummary = $"{suspects.Count} suspect(s) verified against \"{mod.Name}\" · {results.Count} candidate(s)";
            reportProgress?.Invoke(
                results.Count > 0 ? $"{results.Count} probable conflict(s) found." : "No real conflicts found.",
                100);

            return results;
        }

        #region Provider calls (with retry/backoff)

        private async Task<(List<string> Keywords, bool ProviderOk)> GenerateKeywordsForModAsync(
            ModItem mod, CancellationToken ct, Action<string>? notify)
        {
            var (map, providerOk) = await GenerateKeywordsAsync(
                new List<ModItem> { mod }, ct, notify).ConfigureAwait(false);
            var keywords = map.TryGetValue(mod.PackageId, out var kws) ? kws : new List<string>();
            return (keywords, providerOk);
        }

        private async Task<(Dictionary<string, List<string>> Map, bool ProviderOk)> GenerateKeywordsAsync(
            List<ModItem> mods, CancellationToken ct, Action<string>? notify)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("For each RimWorld mod below, extract 5-8 short thematic keywords that describe what it does to the game. Answer in JSON only:");
            sb.AppendLine("{\"mods\":[{\"id\":\"<given id>\",\"keywords\":[\"...\"]}] }");
            sb.AppendLine();
            for (var i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                sb.AppendLine($"[{i}] id={m.PackageId}");
                sb.AppendLine($"    name={m.Name}");
                sb.AppendLine($"    desc={ClipDescription(m)}");
            }

            var req = new AICompletionRequest
            {
                Messages = { new AIDialogMessage("system", "You are a RimWorld mod-expert assistant. Answer only with the requested JSON, no commentary."),
                             new AIDialogMessage("user", sb.ToString()) },
                Temperature = 0.2,
                MaxTokens = 1500,
                RequestJsonOutput = true
            };
            var resp = await CompleteWithRetryAsync(req, ct, notify).ConfigureAwait(false);
            if (!resp.Success) return (new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase), false);

            return (ParseKeywordsResponse(mods, StripCodeFences(resp.Content)), true);
        }

        private static Dictionary<string, List<string>> ParseKeywordsResponse(List<ModItem> mods, string json)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("mods", out var array)) return result;
                foreach (var entry in array.EnumerateArray())
                {
                    if (!entry.TryGetProperty("id", out var idEl)) continue;
                    var id = idEl.GetString();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var keywords = new List<string>();
                    if (entry.TryGetProperty("keywords", out var kwsEl) && kwsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var k in kwsEl.EnumerateArray())
                        {
                            var kw = k.GetString();
                            if (!string.IsNullOrWhiteSpace(kw)) keywords.Add(kw.Trim());
                        }
                    }
                    result[id] = keywords;
                }
            }
            catch (JsonException)
            {
                // Malformed output — safest to return empty results for this batch.
            }
            // Only keep ids we actually asked about.
            return result
                .Where(kv => mods.Any(m => string.Equals(m.PackageId, kv.Key, StringComparison.OrdinalIgnoreCase)))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        private async Task<(List<(string A, string B, AIIncompatibilityType Type, string Reason)> Verdicts, bool ProviderOk)>
            EvaluatePairsWithStatusAsync(
                List<(ModItem A, ModItem B)> pairs, CancellationToken ct, Action<string>? notify)
        {
            var verdicts = await EvaluatePairsAsync(pairs, ct, notify).ConfigureAwait(false);
            return (verdicts, _providerSuccess);
        }

        /// <summary>
        /// Sends one provider call with retries on rate limits: when the
        /// provider reports a wait, that exact wait is honored (and waits too
        /// long to bridge cancel the loop); when it reports nothing, the delay
        /// grows dynamically. Every retry is reported live to the dialog
        /// through <paramref name="notify"/>.
        /// </summary>
        private async Task<AICompletionResponse> CompleteWithRetryAsync(
            AICompletionRequest request, CancellationToken ct, Action<string>? notify)
        {
            AICompletionResponse resp = new AICompletionResponse { Success = false, ErrorMessage = "not attempted" };
            for (var attempt = 0; attempt <= MaxProviderRetries; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if (attempt > 0)
                {
                    // Rate-limit policy (provider-agnostic, ready for future
                    // providers): when the provider reports WHEN to come back
                    // (Retry-After / reset timestamp), respect exactly that —
                    // no invented cap, no roulette. If it reports a wait too
                    // long to bridge in this dialog (daily reset), cancel
                    // retrying and surface the real reason. Only when the
                    // provider gives NO wait do we fall back to dynamic
                    // exponential backoff.
                    if (resp.RetryAfterSeconds is > 0)
                    {
                        if (resp.RetryAfterSeconds.Value > MaxAutoRetryWaitSeconds)
                        {
                            // Unbridgeable wait (e.g. daily quota reset) —
                            // cancel the retry loop; resp already carries the
                            // friendly message with the reset time.
                            return resp;
                        }
                        var apiDelaySeconds = resp.RetryAfterSeconds.Value;
                        notify?.Invoke($"Provider reached the rate limit — waiting {apiDelaySeconds}s (as instructed by the provider) before retry {attempt}/{MaxProviderRetries}…");
                        try { System.IO.File.AppendAllText(@"D:\temp\opencode\ai_retry_diag.txt", $"[{DateTime.Now:HH:mm:ss}] Retry {attempt}/{MaxProviderRetries} after {apiDelaySeconds}s (provider-instructed) — {_lastProviderError.Replace("\r", " ").Replace("\n", " ")}\n"); } catch { }
                        await Task.Delay(TimeSpan.FromSeconds(apiDelaySeconds), ct).ConfigureAwait(false);
                        notify?.Invoke($"Retrying ({attempt}/{MaxProviderRetries})…");
                    }
                    else
                    {
                        // No wait from the provider — dynamic exponential backoff.
                        var delaySeconds = (int)Math.Min(Math.Pow(2, attempt - 1) * 5, 60);
                        notify?.Invoke($"Provider reached the rate limit — waiting {delaySeconds}s before retry {attempt}/{MaxProviderRetries}…");
                        try { System.IO.File.AppendAllText(@"D:\temp\opencode\ai_retry_diag.txt", $"[{DateTime.Now:HH:mm:ss}] Retry {attempt}/{MaxProviderRetries} after {delaySeconds}s (dynamic) — {_lastProviderError.Replace("\r", " ").Replace("\n", " ")}\n"); } catch { }
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct).ConfigureAwait(false);
                        notify?.Invoke($"Retrying ({attempt}/{MaxProviderRetries})…");
                    }
                }

                resp = await _provider.CompleteAsync(request, ct).ConfigureAwait(false);
                _providerSuccess = resp.Success;
                if (resp.Success)
                {
                    _lastProviderError = string.Empty;
                    return resp;
                }
                _lastProviderError = resp.ErrorMessage ?? "unknown provider error";
            }
            return resp;
        }

        #endregion

        /// <summary>
        /// Scores every other mod by how many of <paramref name="keywords"/>
        /// appear in its name/description; returns the strongest hits.
        /// </summary>
        private static List<ModItem> FindSuspects(ModItem mod, List<string> keywords, List<ModItem> pool)
        {
            var kwSet = keywords
                .Select(k => k.ToLowerInvariant().Trim())
                .Where(k => k.Length >= MinKeywordLength)
                .Distinct()
                .ToList();
            if (kwSet.Count == 0) return new List<ModItem>();

            var scored = new List<(ModItem Mod, int Score)>();
            foreach (var other in pool)
            {
                // Skip existing direct incompatibility rules and deps.
                if (HasDirectConflict(mod, other)) continue;
                if (IsDependency(mod, other)) continue;

                var name = (other.Name ?? string.Empty).ToLowerInvariant();
                var desc = BestSearchText(other).ToLowerInvariant();
                if (desc.Length > SearchDescriptionLength) desc = desc.Substring(0, SearchDescriptionLength);

                var score = kwSet.Count(kw => ContainsWholeWord(name, kw) || ContainsWholeWord(desc, kw));
                if (score > 0) scored.Add((other, score));
            }

            return scored
                .OrderByDescending(s => s.Score)
                .Take(MaxSuspectsPerMod)
                .Select(s => s.Mod)
                .ToList();
        }

        /// <summary>
        /// Evaluates a batch of pairs and returns verdicts. Verdict "none" is
        /// dropped: the AI verified there is no real conflict, so the pair is
        /// never surfaced (and never forced).
        /// </summary>
        private async Task<List<(string A, string B, AIIncompatibilityType Type, string Reason)>> EvaluatePairsAsync(
            List<(ModItem A, ModItem B)> pairs, CancellationToken ct, Action<string>? notify)
        {
            var verdicts = new List<(string, string, AIIncompatibilityType, string)>();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Below are pairs of RimWorld mods. For each pair decide how the two mods relate:")
              .AppendLine("  - \"hard\"  = one must be removed for the other to function (true conflict).")
              .AppendLine("  - \"soft\"  = the two mods implement the SAME feature, so keeping both is pointless (one typically replaces the other).")
              .AppendLine("  - \"none\"  = anything else — IMPORTANT: merely touching the same broad category (e.g. both add armor, both are UI/QoL, one tweaks a subsystem the other expands) is NOT enough. They must compete for the same feature to be flagged. Do not invent conflicts; if unsure, use \"none\".")
              .AppendLine("Answer in JSON only: {\"pairs\":[{\"a\":\"<idA>\",\"b\":\"<idB>\",\"type\":\"hard\"|\"soft\"|\"none\",\"reason\":\"...\"}]}")
              .AppendLine();
            for (var i = 0; i < pairs.Count; i++)
            {
                var (a, b) = pairs[i];
                sb.AppendLine($"[{i}] A id={a.PackageId} name={a.Name}");
                sb.AppendLine($"      desc={ClipDescription(a)}");
                sb.AppendLine($"     B id={b.PackageId} name={b.Name}");
                sb.AppendLine($"      desc={ClipDescription(b)}");
            }

            var req = new AICompletionRequest
            {
                Messages = { new AIDialogMessage("system", "You are a meticulous RimWorld mod-compatibility expert. Return strictly the requested JSON."),
                             new AIDialogMessage("user", sb.ToString()) },
                Temperature = 0.1,
                MaxTokens = 2000,
                RequestJsonOutput = true
            };
            var resp = await CompleteWithRetryAsync(req, ct, notify).ConfigureAwait(false);
            if (!resp.Success) return verdicts;

            try
            {
                using var doc = JsonDocument.Parse(StripCodeFences(resp.Content));
                if (!doc.RootElement.TryGetProperty("pairs", out var arr)) return verdicts;
                foreach (var entry in arr.EnumerateArray())
                {
                    var a = entry.TryGetProperty("a", out var y) ? y.GetString() : null;
                    var b = entry.TryGetProperty("b", out var x) ? x.GetString() : null;
                    if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) continue;
                    var typeRaw = entry.TryGetProperty("type", out var tEl) ? tEl.GetString()?.Trim().ToLowerInvariant() : null;
                    if (typeRaw == "none")
                    {
                        // The AI verified there is no real conflict — do not surface it.
                        continue;
                    }
                    var type = typeRaw == "hard" ? AIIncompatibilityType.Hard : AIIncompatibilityType.Soft;
                    var reason = entry.TryGetProperty("reason", out var rEl) ? rEl.GetString() ?? string.Empty : string.Empty;
                    verdicts.Add((a, b, type, reason));
                }
            }
            catch (JsonException)
            {
                // Malformed — simply return what we have so far.
            }

            // Canonical order (mirror the pairs we asked about).
            verdicts = verdicts
                .Select(v =>
                {
                    var pair = pairs.FirstOrDefault(p =>
                        string.Equals(p.A.PackageId, v.Item1, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.B.PackageId, v.Item2, StringComparison.OrdinalIgnoreCase));
                    if (pair.A != null && pair.B != null) return v;
                    var mirrored = pairs.FirstOrDefault(p =>
                        string.Equals(p.A.PackageId, v.Item2, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(p.B.PackageId, v.Item1, StringComparison.OrdinalIgnoreCase));
                    if (mirrored.A != null && mirrored.B != null) return (mirrored.B.PackageId, mirrored.A.PackageId, v.Item3, v.Item4);
                    return v;
                })
                .Where(v => v.Item1 != null && v.Item2 != null)
                .ToList();

            return verdicts;
        }

        /// <summary>
        /// Word-boundary match: "armor" must not light up inside unrelated
        /// compound words, and generic category words match far fewer mods.
        /// </summary>
        private static bool ContainsWholeWord(string text, string word)
        {
            var start = text.IndexOf(word, StringComparison.Ordinal);
            while (start >= 0)
            {
                var beforeOk = start == 0 || !char.IsLetterOrDigit(text[start - 1]);
                var end = start + word.Length;
                var afterOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (beforeOk && afterOk) return true;
                start = text.IndexOf(word, end, StringComparison.Ordinal);
            }
            return false;
        }

        private static bool IsLudeonContent(ModItem m) =>
            m.ModType == ModType.Core || m.ModType == ModType.Expansion;

        private static bool HasDirectConflict(ModItem a, ModItem b) =>
            a.IncompatibleWith.ContainsKey(b.PackageId) ||
            b.IncompatibleWith.ContainsKey(a.PackageId);

        private static bool IsDependency(ModItem a, ModItem b) =>
            a.ModDependencies.Any(d => string.Equals(d.PackageId, b.PackageId, StringComparison.OrdinalIgnoreCase)) ||
            b.ModDependencies.Any(d => string.Equals(d.PackageId, a.PackageId, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Free-tier models often wrap JSON in markdown fences (```json ... ```).
        /// Strip them so JsonDocument.Parse does not fail silently.
        /// </summary>
        private static string StripCodeFences(string? content)
        {
            var s = content?.Trim() ?? string.Empty;
            if (s.StartsWith("```", StringComparison.Ordinal))
            {
                var firstLine = s.IndexOf('\n');
                if (firstLine >= 0) s = s.Substring(firstLine + 1);
                var tail = s.LastIndexOf("```", StringComparison.Ordinal);
                if (tail >= 0) s = s.Substring(0, tail);
                s = s.Trim();
            }
            return s;
        }

        /// <summary>
        /// The richest available text for keyword search: the cached full
        /// Workshop description when present, otherwise the About.xml text.
        /// </summary>
        private static string BestSearchText(ModItem mod) =>
            string.IsNullOrWhiteSpace(mod.SteamDescription) ? (mod.Description ?? string.Empty) : mod.SteamDescription;

        private string ClipDescription(ModItem mod)
        {
            var d = BestSearchText(mod).Trim();
            // Approx description shake-up for prompt economy.
            var idx = d.IndexOf('\n');
            if (idx > 0) d = idx > TextExcerptLength ? d.Substring(0, TextExcerptLength) : d.Substring(0, idx);
            if (d.Length > TextExcerptLength) d = d.Substring(0, TextExcerptLength) + "...";
            return d;
        }
    }
}
