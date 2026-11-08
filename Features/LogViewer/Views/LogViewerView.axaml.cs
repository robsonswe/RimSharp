using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RimSharp.Features.LogViewer.ViewModels;
using RimSharp.Infrastructure.Dialog;
using System;

namespace RimSharp.Features.LogViewer.Views
{
    public partial class LogViewerView : BaseDialog
    {
        private readonly DispatcherTimer _modFilterDebounceTimer;
        private string _pendingLineFilter = string.Empty;

        public LogViewerView()
        {
            InitializeComponent();

            // The search box refilters the whole display on each keystroke;
            // debounce so typing does not rebuild per character.
            _modFilterDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _modFilterDebounceTimer.Tick += (_, _) =>
            {
                _modFilterDebounceTimer.Stop();
                if (DataContext is LogViewerViewModel viewModel)
                {
                    viewModel.ApplyLineFilter(_pendingLineFilter);
                }
            };
        }

        public LogViewerView(LogViewerViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnLineFilterTextChanged(object? sender, TextChangedEventArgs e)
        {
            _pendingLineFilter = (sender as TextBox)?.Text ?? string.Empty;
            _modFilterDebounceTimer.Stop();
            _modFilterDebounceTimer.Start();
        }
    }
}
