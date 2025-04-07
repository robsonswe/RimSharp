using System;
using System.Collections.Generic;
using System.Windows.Input;
using RimSharp.Core.Commands;

namespace RimSharp.MyApp.Dialogs
{
    public abstract class DialogViewModelBase
    {
        public string Title { get; protected set; }

        // Event to signal the view to close
        public event EventHandler RequestCloseDialog;

        // Command to trigger the close request
        public ICommand CloseCommand { get; }

        protected DialogViewModelBase(string title)
        {
            Title = title;
            CloseCommand = new RelayCommand(_ => OnRequestCloseDialog());
        }

        protected virtual void OnRequestCloseDialog()
        {
            RequestCloseDialog?.Invoke(this, EventArgs.Empty);
        }
    }

    // Optional: Generic version if you need to return results easily
    public abstract class DialogViewModelBase<TResult> : DialogViewModelBase
    {
        public TResult DialogResult { get; protected set; }

        // Command to close with a specific result
        public ICommand CloseWithResultCommand { get; }

        protected DialogViewModelBase(string title) : base(title)
        {
            // Generic command requires parameter conversion
            CloseWithResultCommand = new RelayCommand(param =>
            {
                if (param is TResult result)
                {
                    CloseDialog(result);
                }
                else if (param != null && typeof(TResult).IsEnum && Enum.IsDefined(typeof(TResult), param))
                {
                     CloseDialog((TResult)Enum.Parse(typeof(TResult), param.ToString()));
                }
                else if (param == null && !typeof(TResult).IsValueType) // Handle null for reference types
                {
                     CloseDialog(default(TResult)); // or handle appropriately
                }
                // Add more conversion logic if needed (e.g., string to bool)
            });
        }

        public void CloseDialog(TResult result)
        {
            DialogResult = result;
            OnRequestCloseDialog(); // Trigger the close event from base
        }

        // Override the parameterless close to provide a default result
        protected override void OnRequestCloseDialog()
        {
            // If DialogResult hasn't been set, set it to default before closing
            // This is useful for simple close actions (like clicking 'X')
            if (EqualityComparer<TResult>.Default.Equals(DialogResult, default(TResult)))
            {
                 // Or set a specific default like 'Cancel' if applicable
                 DialogResult = default(TResult);
            }
            base.OnRequestCloseDialog();
        }
    }
}