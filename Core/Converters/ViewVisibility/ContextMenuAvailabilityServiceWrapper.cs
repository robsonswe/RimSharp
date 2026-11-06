using System;
using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RimSharp.Infrastructure.Logging;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Core.Converters.ViewVisibility
{
    /// <summary>
    /// Wrapper to expose IContextMenuAvailabilityService to XAML bindings with change notifications.
    /// </summary>
    public class ContextMenuAvailabilityServiceWrapper : INotifyPropertyChanged
    {
        private ILoggerService _logger;
        private IContextMenuAvailabilityService? _service;

        public ContextMenuAvailabilityServiceWrapper() : this(null) { }

        public ContextMenuAvailabilityServiceWrapper(ILoggerService? logger = null)
        {
            _logger = logger ?? new NullLogger();
        }

        // Allow logger injection after XAML construction
        public ILoggerService Logger => _logger;

        public IContextMenuAvailabilityService? Service
        {
            get => _service;
            set
            {
                _logger?.LogDebug($"[ContextMenuWrapper] Service set: {value != null}", nameof(ContextMenuAvailabilityServiceWrapper));
                _service = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Sets the logger after XAML construction (when created via parameterless constructor).
        /// </summary>
        public void SetLogger(ILoggerService logger)
        {
            _logger = logger;
        }

        // Expose properties for direct binding
        public bool HasSelection => _service?.HasSelection ?? false;
        public bool CanDeleteMod => _service?.CanDeleteMod ?? false;
        public bool CanDeleteAndUnsubscribe => _service?.CanDeleteAndUnsubscribe ?? false;
        public bool CanDeleteAndResubscribe => _service?.CanDeleteAndResubscribe ?? false;
        public bool CanUnsubscribe => _service?.CanUnsubscribe ?? false;
        public bool CanRedownloadMod => _service?.CanRedownloadMod ?? false;
        public bool CanCheckForUpdates => _service?.CanCheckForUpdates ?? false;
        public bool CanUpdateMod => _service?.CanUpdateMod ?? false;
        public bool CanSubscribe => _service?.CanSubscribe ?? false;
        public bool CanOpenFolder => _service?.CanOpenFolder ?? false;
        public bool CanOpenWorkshopPage => _service?.CanOpenWorkshopPage ?? false;
        public bool CanOpenExternalUrl => _service?.CanOpenExternalUrl ?? false;
        public bool CanAiConflictScan => _service?.CanAiConflictScan ?? false;
        public bool CanCustomizeMod => _service?.CanCustomizeMod ?? false;
        public bool CanVerifyGameFiles => _service?.CanVerifyGameFiles ?? false;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            _logger?.LogDebug($"[ContextMenuWrapper] PropertyChanged: {propertyName}", nameof(ContextMenuAvailabilityServiceWrapper));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Updates the selection in the underlying service and raises property changed notifications for all exposed properties.
        /// </summary>
        public void UpdateSelection(IList? selectedItems)
        {
            _logger?.LogDebug($"[ContextMenuWrapper] UpdateSelection called with {selectedItems?.Count ?? 0} items", nameof(ContextMenuAvailabilityServiceWrapper));
            _service?.UpdateSelection(selectedItems);
            
            // Raise property changed for all properties to refresh bindings
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanDeleteMod));
            OnPropertyChanged(nameof(CanDeleteAndUnsubscribe));
            OnPropertyChanged(nameof(CanDeleteAndResubscribe));
            OnPropertyChanged(nameof(CanUnsubscribe));
            OnPropertyChanged(nameof(CanRedownloadMod));
            OnPropertyChanged(nameof(CanCheckForUpdates));
            OnPropertyChanged(nameof(CanUpdateMod));
            OnPropertyChanged(nameof(CanSubscribe));
            OnPropertyChanged(nameof(CanOpenFolder));
            OnPropertyChanged(nameof(CanOpenWorkshopPage));
            OnPropertyChanged(nameof(CanOpenExternalUrl));
            OnPropertyChanged(nameof(CanAiConflictScan));
            OnPropertyChanged(nameof(CanCustomizeMod));
            OnPropertyChanged(nameof(CanVerifyGameFiles));
        }

        private class NullLogger : ILoggerService
        {
            public void Log(LogLevel level, string message, string module = "General") { }
            public void LogDebug(string message, string module = "General") { }
            public void LogInfo(string message, string module = "General") { }
            public void LogWarning(string message, string module = "General") { }
            public void LogError(string message, string module = "General") { }
            public void LogCritical(string message, string module = "General") { }
            public void LogException(Exception ex, string? message = null, string module = "General") { }
        }
    }
}