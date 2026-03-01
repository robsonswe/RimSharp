using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;

namespace RimSharp.Infrastructure.Dialog
{
    public class BaseDialog : Window
    {
        public BaseDialog()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Classes.Add("base-dialog");
        }

        public static readonly StyledProperty<bool> CloseableProperty =
            AvaloniaProperty.Register<BaseDialog, bool>(nameof(Closeable), true);

        public bool Closeable
        {
            get => GetValue(CloseableProperty);
            set => SetValue(CloseableProperty, value);
        }

        public static readonly StyledProperty<object?> MainContentProperty =
            AvaloniaProperty.Register<BaseDialog, object?>(nameof(MainContent));

        public object? MainContent
        {
            get => GetValue(MainContentProperty);
            set => SetValue(MainContentProperty, value);
        }

        public static readonly StyledProperty<object?> ButtonContentProperty =
            AvaloniaProperty.Register<BaseDialog, object?>(nameof(ButtonContent));

        public object? ButtonContent
        {
            get => GetValue(ButtonContentProperty);
            set => SetValue(ButtonContentProperty, value);
        }

        protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);

            var titleBar = e.NameScope.Find<Border>("PART_TitleBar");
            if (titleBar != null)
            {
                titleBar.PointerPressed += (sender, args) =>
                {
                    BeginMoveDrag(args);
                };
            }
        }

        protected void SetupViewModel(DialogViewModelBase viewModel)
        {
            DataContext = viewModel;
            viewModel.RequestCloseDialog += (s, e) =>
            {
                Close(viewModel.DialogResultForWindow);
            };
        }
    }
}
