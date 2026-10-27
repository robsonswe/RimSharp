#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.AI
{
    /// <summary>
    /// Resolves the currently active AI provider client by id.
    /// Currently only OpenRouter is wired up. Adding new providers later:
    /// implement IAIProviderClient, add a case here, and register it in the DI container.
    /// </summary>
    public class AIProviderFactory
    {
        private readonly IReadOnlyDictionary<string, IAIProviderClient> _providers;

        public AIProviderFactory(IEnumerable<IAIProviderClient> providers)
        {
            _providers = providers.ToDictionary(p => p.ProviderId, p => p, StringComparer.OrdinalIgnoreCase);
        }

        public IAIProviderClient? FindById(string providerId) =>
            _providers.TryGetValue(providerId, out var provider) ? provider : null;

        public IAIProviderClient GetDefault() =>
            _providers["openrouter"];
    }
}
