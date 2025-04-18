using System;
using System.Collections.Generic;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles; // Assuming ViewModelBase is here

namespace RimSharp.AppDir.Dialogs
{
    // Non-generic base
    public abstract class DialogViewModelBase : ViewModelBase
    {
        private string _title;
        private bool _closeable = true;
        public string Title
        {
            get => _title;
            protected set => SetProperty(ref _title, value);
        }
        public bool Closeable
        {
            get => _closeable;
            protected set => SetProperty(ref _closeable, value);
        }

        // Event to signal the view to close
        public event EventHandler RequestCloseDialog;

        // *** ADD THIS PROPERTY ***
        /// <summary>
        /// Stores the intended DialogResult for the window, mapped to bool?.
        /// Set by derived classes before RequestCloseDialog is invoked.
        /// </summary>
        public bool? DialogResultForWindow { get; protected set; } = null; // Default to null

        // Command to trigger the close request (can potentially be simplified later)
        private ICommand _closeCommand;
        public ICommand CloseCommand => _closeCommand ??= CreateCommand(OnRequestCloseDialog);

        protected DialogViewModelBase(string title)
        {
            Title = title;
        }

        // Make OnRequestCloseDialog public or internal if BaseDialog needs to call it directly
        // Or keep it protected and let commands call it. Raising event is key.
        protected virtual void OnRequestCloseDialog()
        {
            // Base implementation might set a default result if needed
            // DialogResultForWindow = null; // Or false? Depends on desired default close behavior
            RequestCloseDialog?.Invoke(this, EventArgs.Empty);
        }
    }

    // Generic base
    public abstract class DialogViewModelBase<TResult> : DialogViewModelBase
    {
        private TResult _dialogResult;
        public TResult DialogResult
        {
            get => _dialogResult;
            protected set => SetProperty(ref _dialogResult, value);
        }

        // Command to close with a specific result
        private ICommand _closeWithResultCommand;
        public ICommand CloseWithResultCommand => _closeWithResultCommand ??= CreateCommand<object>(
            execute: param => ExecuteCloseWithResult(param),
            canExecute: param => CanExecuteCloseWithResult(param)
        );

        protected DialogViewModelBase(string title) : base(title)
        {
        }

        private bool CanExecuteCloseWithResult(object param)
        {
            // Simplified check, assuming conversion happens in Execute
            return true; // Or add more specific checks if needed
        }

        private void ExecuteCloseWithResult(object param)
        {
            TResult result = default;
            bool converted = false;

            if (param is TResult specificResult)
            {
                result = specificResult;
                converted = true;
            }
            else if (param != null && typeof(TResult).IsEnum && Enum.IsDefined(typeof(TResult), param))
            {
                try
                {
                   result = (TResult)Enum.Parse(typeof(TResult), param.ToString());
                   converted = true;
                } catch {} // Ignore parse errors
            }
             else if (param != null) // Attempt general conversion
            {
                try
                {
                    result = (TResult)Convert.ChangeType(param, typeof(TResult));
                    converted = true;
                }
                catch {} // Ignore conversion errors
            }
            else if (param == null && !typeof(TResult).IsValueType) // Handle null for reference types
            {
                result = default(TResult); // which is null
                converted = true;
            }

            if (converted)
            {
                 CloseDialog(result);
            }
            // Else: Parameter couldn't be converted, maybe log a warning?
        }


        // Call this when closing with a result
        public void CloseDialog(TResult result)
        {
            DialogResult = result;
            // *** MAP TResult to bool? before raising event ***
            MapResultToWindowResult(result);
            OnRequestCloseDialog(); // Raise event AFTER setting properties
        }

        // Default close (e.g., Cancel button) - sets default result and closes
        protected override void OnRequestCloseDialog()
        {
            // Ensure DialogResult and DialogResultForWindow are set
            // if they haven't been explicitly set via CloseDialog(result)
            if (EqualityComparer<TResult>.Default.Equals(DialogResult, default(TResult)))
            {
                DialogResult = default(TResult);
                MapResultToWindowResult(default(TResult)); // Map the default
            }
            // If CloseDialog(result) was called, DialogResultForWindow is already set.
            base.OnRequestCloseDialog(); // Raise the event
        }

        // *** ADD THIS HELPER ***
        /// <summary>
        /// Maps the generic TResult to the bool? DialogResultForWindow.
        /// Override this in specific ViewModels if complex mapping is needed.
        /// Default: Treats common dialog results like OK/Yes/Save as true, others as false.
        /// </summary>
        protected virtual void MapResultToWindowResult(TResult result)
        {
            if (result is Enum enumResult) // Handle common enum patterns
            {
                string resultString = enumResult.ToString();
                if (resultString.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                    resultString.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                    resultString.Equals("Save", StringComparison.OrdinalIgnoreCase) ||
                    resultString.Equals("Proceed", StringComparison.OrdinalIgnoreCase)) // Add other "success" cases
                {
                    DialogResultForWindow = true;
                }
                else
                {
                    DialogResultForWindow = false; // Assume others (Cancel, No, Abort) are false
                }
            }
             else if (result is bool boolResult) // Handle bool directly
             {
                 DialogResultForWindow = boolResult;
             }
            else
            {
                // Default mapping for unknown types: true if not default, false if default/null
                DialogResultForWindow = !EqualityComparer<TResult>.Default.Equals(result, default(TResult));
            }
        }
    }
}
