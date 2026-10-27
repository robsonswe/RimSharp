#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RimSharp.Shared.Services.Contracts
{
    /// <summary>
    /// A single message in a chat-style AI conversation.
    /// </summary>
    public class AIDialogMessage
    {
        public string Role { get; set; } = "user";   // "system" | "user" | "assistant"
        public string Content { get; set; } = string.Empty;

        public AIDialogMessage() { }
        public AIDialogMessage(string role, string content) { Role = role; Content = content; }
    }

    /// <summary>
    /// Provider-agnostic request wrapper.
    /// </summary>
    public class AICompletionRequest
    {
        public List<AIDialogMessage> Messages { get; set; } = new();
        public double Temperature { get; set; } = 0.2;
        public int MaxTokens { get; set; } = 512;
        public bool RequestJsonOutput { get; set; } = false;
    }

    /// <summary>
    /// Provider-agnostic response wrapper.
    /// </summary>
    public class AICompletionResponse
    {
        public bool Success { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }

        /// <summary>Seconds the provider asked us to wait before retrying (Retry-After).</summary>
        public int? RetryAfterSeconds { get; set; }
    }

    /// <summary>
    /// Abstraction over an AI provider (OpenRouter, OpenAI, Anthropic, local, ...).
    /// Implementations translate messages into the provider-specific wire format.
    /// </summary>
    public interface IAIProviderClient
    {
        /// <summary>A short identifier, e.g. "openrouter", "openai".</summary>
        string ProviderId { get; }

        /// <summary>Friendly display name for UI/prompting.</summary>
        string DisplayName { get; }

        /// <summary>
        /// Sends the conversation to the underlying model and returns raw text (or JSON) content.
        /// </summary>
        Task<AICompletionResponse> CompleteAsync(AICompletionRequest request, CancellationToken ct = default);

        /// <summary>True when the provider is configured (e.g. API key present).</summary>
        bool IsConfigured { get; }
    }
}
