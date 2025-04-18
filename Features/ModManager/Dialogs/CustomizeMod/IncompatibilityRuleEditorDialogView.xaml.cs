using RimSharp.Infrastructure.Dialog;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading; // Needed for Dispatcher

namespace RimSharp.Features.ModManager.Dialogs.CustomizeMod
{
    public partial class IncompatibilityRuleEditorDialogView : BaseDialog
    {
        private IncompatibilityRuleEditorDialogViewModel _viewModel;
        private bool _isClosing = false; // Flag to prevent re-entrancy

        public IncompatibilityRuleEditorDialogView(IncompatibilityRuleEditorDialogViewModel viewModel)
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
                Debug.WriteLine($"[IncompatibilityRuleEditorDialogView] Error: ViewModel is null during construction.");
            }
        }

        private void ViewModel_RequestCloseDialog(object sender, EventArgs e)
        {
            // Prevent re-entrancy and check if VM is already cleared
            if (_isClosing || _viewModel == null)
            {
                Debug.WriteLine($"[IncompatibilityRuleEditorDialogView] ViewModel_RequestCloseDialog skipped: _isClosing={_isClosing}, _viewModel is null={_viewModel == null}.");
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
                    Debug.WriteLine($"[IncompatibilityRuleEditorDialogView] Setting DialogResult to {DialogResult}");
                }
                 else
                {
                    Debug.WriteLine($"[IncompatibilityRuleEditorDialogView] ViewModel became null before setting DialogResult. Aborting close schedule.");
                    _isClosing = false; // Reset flag as we are not closing
                    return;
                }

                // *** Schedule Close instead of calling directly ***
                Debug.WriteLine($"[IncompatibilityRuleEditorDialogView] Scheduling Close() for {this.Title} via Dispatcher.");
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                     // Check again if still relevant to close inside the dispatched action
                    if (this.IsLoaded) // Ensure window hasn't been closed by other means
                    {
                        Debug.WriteLine($"[IncompatibilityRuleEditorDialogView] Executing dispatched Close() for {this.Title}.");
                        try
                        {
                            Close(); // Actual close call
                        }
                        catch(Exception closeEx)
                        {
                            // Log if Close fails even when dispatched
                            Debug.WriteLine($"[IncompatibilityRuleEditorDialogView] EXCEPTION during dispatched Close(): {closeEx}");
                        }
                    }
                    else
                    {
                         Debug.WriteLine($"[IncompatibilityRuleEditorDialogView] Dispatched Close() skipped for {this.Title} as IsLoaded is false.");
                    }
                }));
            }
            catch (Exception ex)
            {
                 Debug.WriteLine($"[IncompatibilityRuleEditorDialogView] Exception during ViewModel_RequestCloseDialog scheduling: {ex}");
                 _isClosing = false; // Reset flag if scheduling fails
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe to prevent memory leaks
            if (_viewModel != null)
            {
                _viewModel.RequestCloseDialog -= ViewModel_RequestCloseDialog;
                _viewModel = null; // Clear reference AFTER unsubscribing
            }
            // _isClosing = false; // Optional reset
            base.OnClosed(e);
            Debug.WriteLine($"[IncompatibilityRuleEditorDialogView] Closed and cleaned up: {this.Title}");
        }
    }
}
