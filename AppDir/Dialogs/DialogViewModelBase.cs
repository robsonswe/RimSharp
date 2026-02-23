using System;
using System.Collections.Generic;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles;
using ReactiveUI;

namespace RimSharp.AppDir.Dialogs
{
    public abstract class DialogViewModelBase : ViewModelBase
    {
        private string _title;
        private bool _closeable = true;
        private bool? _dialogResultForWindow = null;

        public string Title
        {
            get => _title;
            protected set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        public bool Closeable
        {
            get => _closeable;
            protected set => this.RaiseAndSetIfChanged(ref _closeable, value);
        }

        public bool? DialogResultForWindow 
        { 
            get => _dialogResultForWindow; 
            protected set => this.RaiseAndSetIfChanged(ref _dialogResultForWindow, value); 
        }

        public event EventHandler? RequestCloseDialog;

        private ICommand? _closeCommand;
        public ICommand CloseCommand => _closeCommand ??= CreateCommand(OnRequestCloseDialog);

        protected DialogViewModelBase(string title)
        {
            _title = title;
        }

        protected virtual void OnRequestCloseDialog()
        {
            RequestCloseDialog?.Invoke(this, EventArgs.Empty);
        }
    }

    public abstract class DialogViewModelBase<TResult> : DialogViewModelBase
    {
        private TResult _dialogResult = default!;
        public TResult DialogResult
        {
            get => _dialogResult;
            protected set => this.RaiseAndSetIfChanged(ref _dialogResult, value);
        }

        private ICommand? _closeWithResultCommand;
        public ICommand CloseWithResultCommand => _closeWithResultCommand ??= CreateCommand<object>(
            execute: param => ExecuteCloseWithResult(param)
        );

        protected DialogViewModelBase(string title) : base(title)
        {
        }

        private void ExecuteCloseWithResult(object param)
        {
            TResult result = default!;
            bool converted = false;

            if (param is TResult specificResult)
            {
                result = specificResult;
                converted = true;
            }
            else if (param != null && typeof(TResult).IsEnum)
            {
                try
                {
                   result = (TResult)Enum.Parse(typeof(TResult), param.ToString()!);
                   converted = true;
                } catch {} 
            }
             else if (param != null) 
            {
                try
                {
                    result = (TResult)Convert.ChangeType(param, typeof(TResult));
                    converted = true;
                }
                catch {} 
            }
            else if (param == null && !typeof(TResult).IsValueType) 
            {
                result = default!;
                converted = true;
            }

            if (converted)
            {
                 CloseDialog(result);
            }
        }

        public void CloseDialog(TResult result)
        {
            DialogResult = result;
            MapResultToWindowResult(result);
            OnRequestCloseDialog(); 
        }

        protected override void OnRequestCloseDialog()
        {
            if (EqualityComparer<TResult>.Default.Equals(DialogResult, default!))
            {
                DialogResult = default!;
                MapResultToWindowResult(default!);
            }
            base.OnRequestCloseDialog(); 
        }

        protected virtual void MapResultToWindowResult(TResult result)
        {
            if (result is Enum enumResult) 
            {
                string resultString = enumResult.ToString();
                if (resultString.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                    resultString.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
                    resultString.Equals("Save", StringComparison.OrdinalIgnoreCase) ||
                    resultString.Equals("Proceed", StringComparison.OrdinalIgnoreCase)) 
                {
                    DialogResultForWindow = true;
                }
                else
                {
                    DialogResultForWindow = false; 
                }
            }
             else if (result is bool boolResult) 
             {
                 DialogResultForWindow = boolResult;
             }
            else
            {
                DialogResultForWindow = !EqualityComparer<TResult>.Default.Equals(result, default!);
            }
        }
    }
}
