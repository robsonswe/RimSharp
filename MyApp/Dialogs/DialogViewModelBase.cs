using System;
using System.Collections.Generic;
using System.Windows.Input;
using RimSharp.MyApp.AppFiles;

namespace RimSharp.MyApp.Dialogs
{
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

        // Command to trigger the close request
        private ICommand _closeCommand;
        public ICommand CloseCommand => _closeCommand ??= CreateCommand(OnRequestCloseDialog);

        protected DialogViewModelBase(string title)
        {
            Title = title;
        }

        protected virtual void OnRequestCloseDialog()
        {
            RequestCloseDialog?.Invoke(this, EventArgs.Empty);
        }
    }

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
            return param is TResult
                || (param != null && typeof(TResult).IsEnum && Enum.IsDefined(typeof(TResult), param))
                || (param == null && !typeof(TResult).IsValueType);
        }

        private void ExecuteCloseWithResult(object param)
        {
            if (param is TResult result)
            {
                CloseDialog(result);
            }
            else if (param != null && typeof(TResult).IsEnum && Enum.IsDefined(typeof(TResult), param))
            {
                CloseDialog((TResult)Enum.Parse(typeof(TResult), param.ToString()));
            }
            else if (param == null && !typeof(TResult).IsValueType)
            {
                CloseDialog(default(TResult));
            }
        }

        public void CloseDialog(TResult result)
        {
            DialogResult = result;
            OnRequestCloseDialog();
        }

        protected override void OnRequestCloseDialog()
        {
            if (EqualityComparer<TResult>.Default.Equals(DialogResult, default(TResult)))
            {
                DialogResult = default(TResult);
            }
            base.OnRequestCloseDialog();
        }
    }
}