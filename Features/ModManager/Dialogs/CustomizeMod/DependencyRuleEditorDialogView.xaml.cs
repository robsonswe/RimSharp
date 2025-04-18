using RimSharp.Infrastructure.Dialog;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading; // Needed for Dispatcher

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public partial class DependencyRuleEditorDialogView : BaseDialog
    {
        private DependencyRuleEditorDialogViewModel _viewModel;
        private bool _isClosing = false; // Flag to prevent re-entrancy

        public DependencyRuleEditorDialogView(DependencyRuleEditorDialogViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            DataContext = _viewModel;

            if (_viewModel != null)
            {
                _viewModel.RequestCloseDialog += ViewModel_RequestCloseDialog;
            }
            else
            {
                Debug.WriteLine($"[DependencyRuleEditorDialogView] Error: ViewModel is null during construction.");
            }
        }

        private void ViewModel_RequestCloseDialog(object sender, EventArgs e)
        {
            // Prevent re-entrancy and check if VM is already cleared
            if (_isClosing || _viewModel == null)
            {
                Debug.WriteLine($"[DependencyRuleEditorDialogView] ViewModel_RequestCloseDialog skipped: _isClosing={_isClosing}, _viewModel is null={_viewModel == null}.");
                return;
            }

            // Set flag immediately
            _isClosing = true;

            try
            {
                // Set the window's DialogResult based on the VM's result *before* scheduling close
                if (_viewModel != null) // Double check VM
                {
                    DialogResult = _viewModel.DialogResult;
                    Debug.WriteLine($"[DependencyRuleEditorDialogView] Setting DialogResult to {DialogResult}");
                }
                else
                {
                    Debug.WriteLine($"[DependencyRuleEditorDialogView] ViewModel became null before setting DialogResult. Aborting close schedule.");
                    _isClosing = false; // Reset flag as we are not closing
                    return;
                }

                // *** Schedule Close instead of calling directly ***
                Debug.WriteLine($"[DependencyRuleEditorDialogView] Scheduling Close() for {this.Title} via Dispatcher.");
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    // Check again if still relevant to close inside the dispatched action
                    if (this.IsLoaded) // Ensure window hasn't been closed by other means
                    {
                         Debug.WriteLine($"[DependencyRuleEditorDialogView] Executing dispatched Close() for {this.Title}.");
                         try
                         {
                            Close(); // Actual close call
                         }
                         catch(Exception closeEx)
                         {
                             // Log if Close fails even when dispatched
                             Debug.WriteLine($"[DependencyRuleEditorDialogView] EXCEPTION during dispatched Close(): {closeEx}");
                         }
                    }
                    else
                    {
                        Debug.WriteLine($"[DependencyRuleEditorDialogView] Dispatched Close() skipped for {this.Title} as IsLoaded is false.");
                    }
                }));
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"[DependencyRuleEditorDialogView] Exception during ViewModel_RequestCloseDialog scheduling: {ex}");
                 _isClosing = false; // Reset flag if scheduling fails
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe to prevent memory leaks
            if (_viewModel != null)
            {
                // Check if handler is still subscribed before removing
                // (This check is mostly for paranoia, removal should be safe)
                _viewModel.RequestCloseDialog -= ViewModel_RequestCloseDialog;
                _viewModel = null; // Clear reference AFTER unsubscribing
            }
            // _isClosing flag will be reset if window is somehow reused,
            // but for a closed window, its state doesn't matter much anymore.
            // _isClosing = false; // Optional reset
            base.OnClosed(e);
            Debug.WriteLine($"[DependencyRuleEditorDialogView] Closed and cleaned up: {this.Title}");
        }
    }
}
