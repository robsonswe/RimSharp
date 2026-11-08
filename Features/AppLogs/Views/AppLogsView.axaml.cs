using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RimSharp.Features.AppLogs.ViewModels;
using RimSharp.Infrastructure.Dialog;
using System;

namespace RimSharp.Features.AppLogs.Views
{
    public partial class AppLogsView : BaseDialog
    {
        private readonly DispatcherTimer _searchDebounceTimer;
        private string _pendingSearch = string.Empty;

        public AppLogsView()
        {
            InitializeComponent();

            // The search box refilters the whole display on each keystroke;
            // debounce so typing does not rebuild per character.
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchDebounceTimer.Tick += (_, _) =>
            {
                _searchDebounceTimer.Stop();
                if (DataContext is AppLogsViewModel viewModel)
                {
                    viewModel.ApplySearch(_pendingSearch);
                }
            };
        }

        public AppLogsView(AppLogsViewModel viewModel) : this()
        {
            SetupViewModel(viewModel);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
        {
            _pendingSearch = (sender as TextBox)?.Text ?? string.Empty;
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }
    }
}
