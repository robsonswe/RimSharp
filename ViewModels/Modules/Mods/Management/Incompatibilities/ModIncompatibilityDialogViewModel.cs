using RimSharp.Handlers;
using RimSharp.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace RimSharp.ViewModels.Modules.Mods.Management
{
    public class ModIncompatibilityDialogViewModel : ViewModelBase
    {
        public ObservableCollection<IncompatibilityGroupViewModel> IncompatibilityGroups { get; }
            = new ObservableCollection<IncompatibilityGroupViewModel>();
            
        private bool _isCancelEnabled = true;
        
        public bool IsCancelEnabled
        {
            get => _isCancelEnabled; 
            set => SetProperty(ref _isCancelEnabled, value);
        }
        
        public ICommand ApplyResolutionsCommand { get; }
        public ICommand CancelCommand { get; }
        
        private readonly List<IncompatibilityGroup> _originalGroups;
        private readonly Action<List<ModItem>> _applyCallback;
        private readonly Action _cancelCallback;
        public event Action<bool> RequestClose;

        
        public ModIncompatibilityDialogViewModel(
            List<IncompatibilityGroup> groups, 
            Action<List<ModItem>> applyCallback,
            Action cancelCallback,
            bool canCancel = true)
        {
            _originalGroups = groups;
            _applyCallback = applyCallback;
            _cancelCallback = cancelCallback;
            IsCancelEnabled = canCancel;
            
            foreach (var group in groups)
            {
                IncompatibilityGroups.Add(new IncompatibilityGroupViewModel(group));
            }
            
            ApplyResolutionsCommand = new RelayCommand(_ => ApplyResolutions());
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsCancelEnabled);
        }
        
        private void ApplyResolutions()
        {
            var modsToRemove = new List<ModItem>();
            
            foreach (var group in IncompatibilityGroups)
            {
                modsToRemove.AddRange(group.GetSelectedModsToRemove());
            }
            
            _applyCallback?.Invoke(modsToRemove.Distinct().ToList());
            RequestClose?.Invoke(true);
        }

        private void Cancel()
        {
            _cancelCallback?.Invoke();
            RequestClose?.Invoke(false);
        }
    }
}