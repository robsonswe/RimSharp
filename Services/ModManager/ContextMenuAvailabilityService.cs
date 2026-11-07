using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimSharp.Infrastructure.Logging;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Services.ModManager
{
    /// <summary>
    /// Computes which context menu actions are available for the current multi-selection.
    /// An action is only available if it applies to ALL selected mods (intersection semantics).
    /// This prevents accidental operations on protected mods (Core/Expansion) when mixed with other mods.
    /// </summary>
    public class ContextMenuAvailabilityService : IContextMenuAvailabilityService
    {
        private readonly ILoggerService _logger;
        private readonly GameInstance _gameInstance;
        private IList? _selectedItems;
        private readonly HashSet<ModType> _deletableTypes = new()
        {
            ModType.Tracked,
            ModType.Untracked,
            ModType.Git
        };

        private readonly HashSet<ModType> _workshopOnlyTypes = new()
        {
            ModType.Workshop
        };

        private readonly HashSet<ModType> _trackedOnlyTypes = new()
        {
            ModType.Tracked
        };

        private readonly HashSet<ModType> _protectedTypes = new()
        {
            ModType.Core,
            ModType.Expansion
        };

        private readonly HashSet<ModType> _customizableTypes = new()
        {
            ModType.Tracked,
            ModType.Untracked,
            ModType.Git,
            ModType.Workshop
        };

        public ContextMenuAvailabilityService(ILoggerService logger, GameInstance gameInstance)
        {
            _logger = logger;
            _gameInstance = gameInstance;
        }

        public bool HasSelection => _selectedItems != null && _selectedItems.Count > 0;

        public bool CanDeleteMod => HasSelection && AllMatch(m => _deletableTypes.Contains(m.ModType) && !string.IsNullOrEmpty(m.Path));

        public bool CanDeleteAndUnsubscribe => HasSelection && AllMatch(m => m.ModType == ModType.Workshop && !string.IsNullOrEmpty(m.SteamId));

        public bool CanDeleteAndResubscribe => HasSelection && AllMatch(m => m.ModType == ModType.Workshop && !string.IsNullOrEmpty(m.SteamId));

        public bool CanUnsubscribe => HasSelection && AllMatch(m => m.ModType == ModType.Workshop && !string.IsNullOrEmpty(m.SteamId));

        public bool CanRedownloadMod => HasSelection && AllMatch(m => m.ModType == ModType.Tracked && !string.IsNullOrEmpty(m.Path));

        public bool CanCheckForUpdates => HasSelection && AllMatch(m => m.ModType == ModType.Workshop);

        public bool CanUpdateMod => HasSelection && AllMatch(m => m.ModType == ModType.Workshop);

        public bool CanSubscribe => HasSelection && AllMatch(m => !_protectedTypes.Contains(m.ModType) && !string.IsNullOrEmpty(m.SteamUrl));

        public bool CanOpenFolder => HasSelection && AllMatch(m => !string.IsNullOrEmpty(m.Path));

        public bool CanOpenWorkshopPage => HasSelection && AllMatch(m => !string.IsNullOrEmpty(m.SteamUrl));

        public bool CanOpenExternalUrl => HasSelection && AllMatch(m => !string.IsNullOrEmpty(m.ExternalUrl));

        public bool CanAiConflictScan => HasSelection && AllMatch(m => _customizableTypes.Contains(m.ModType));

        public bool CanCustomizeMod => HasSelection && AllMatch(m => _customizableTypes.Contains(m.ModType));

        public bool CanVerifyGameFiles => _gameInstance?.IsSteamInstallation == true;

        public void UpdateSelection(IList? selectedItems)
        {
            _logger.LogDebug($"[ContextMenuService] UpdateSelection: {selectedItems?.Count ?? 0} items", nameof(ContextMenuAvailabilityService));
            if (selectedItems != null)
            {
                foreach (var item in selectedItems)
                {
                    if (item is ModItem mod)
                    {
                        _logger.LogDebug($"  - {mod.Name} (Type: {mod.ModType}, Path: {mod.Path}, SteamId: {mod.SteamId})", nameof(ContextMenuAvailabilityService));
                    }
                }
            }
            _selectedItems = selectedItems;
            // Properties will be re-evaluated on next access
        }

        private bool AllMatch(System.Func<ModItem, bool> predicate)
        {
            if (_selectedItems == null) return false;
            
            foreach (var item in _selectedItems)
            {
                if (item is ModItem mod && !predicate(mod))
                {
                    _logger.LogDebug($"[ContextMenuService] AllMatch failed for {mod.Name} (Type: {mod.ModType})", nameof(ContextMenuAvailabilityService));
                    return false;
                }
            }
            return true;
        }
    }
}