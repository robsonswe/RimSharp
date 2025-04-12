using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Windows.Input;
using RimSharp.Features.GitModManager.ViewModels;

namespace RimSharp.Features.GitModManager.Views
{
    public partial class GitModsView : UserControl
    {
        public GitModsView()
        {
            InitializeComponent();
            this.DataContextChanged += GitModsView_DataContextChanged;
            this.Loaded += GitModsView_Loaded;
        }

        private void GitModsView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Debug.WriteLine($"[DEBUG] DataContext Changed. New Type: {e.NewValue?.GetType().Name ?? "NULL"}");
            
            if (e.NewValue is GitModsViewModel vm)
            {
                Debug.WriteLine($"[DEBUG] VM Initialized. GitMods count: {vm.GitMods?.Count ?? 0}");
                // Verify command exists
                Debug.WriteLine($"[DEBUG] OpenGitHubRepoCommand: {(vm.OpenGitHubRepoCommand != null ? "Exists" : "NULL")}");
            }
        }

        private void GitModsView_Loaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"[DEBUG] View Loaded. DataContext: {DataContext?.GetType().Name ?? "NULL"}");
            
            if (DataContext is GitModsViewModel vm)
            {
                Debug.WriteLine($"[DEBUG] Current GitMods count: {vm.GitMods?.Count ?? 0}");
            }
        }

        private void GitModsListView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var listView = sender as ListView;
            if (listView == null)
            {
                Debug.WriteLine("[ERROR] ContextMenuOpening: Sender is not ListView");
                e.Handled = true;
                return;
            }

            if (DataContext is not GitModsViewModel viewModel)
            {
                Debug.WriteLine("[ERROR] ContextMenuOpening: DataContext is not GitModsViewModel");
                e.Handled = true;
                return;
            }

            if (listView.SelectedItems.Count != 1)
            {
                Debug.WriteLine($"[DEBUG] ContextMenuOpening: {listView.SelectedItems.Count} items selected - no menu");
                e.Handled = true;
                return;
            }

            var selectedItem = listView.SelectedItem as GitModItemWrapper;
            if (selectedItem == null || string.IsNullOrEmpty(selectedItem.ModItem?.GitRepo))
            {
                Debug.WriteLine($"[DEBUG] ContextMenuOpening: Invalid selected item or missing GitRepo");
                e.Handled = true;
                return;
            }

            Debug.WriteLine($"[DEBUG] Creating context menu for: {selectedItem.ModItem.Name}");

            var contextMenu = new ContextMenu();
            
            var openRepoItem = new MenuItem
            {
                Header = "Open GitHub Repo",
                Command = viewModel.OpenGitHubRepoCommand,
                CommandParameter = selectedItem.ModItem.GitRepo,
                Icon = new TextBlock { Text = "🌐", FontSize = 14 },
                ToolTip = $"Open {selectedItem.ModItem.GitRepo} in browser"
            };

            // Add debug handlers
            openRepoItem.Click += (s, args) => 
                Debug.WriteLine($"[DEBUG] MenuItem Clicked. Command: {viewModel.OpenGitHubRepoCommand}, Param: {selectedItem.ModItem.GitRepo}");

            if (viewModel.OpenGitHubRepoCommand?.CanExecute(selectedItem.ModItem.GitRepo) == false)
            {
                Debug.WriteLine($"[DEBUG] Command cannot execute for: {selectedItem.ModItem.GitRepo}");
                openRepoItem.IsEnabled = false;
            }

            contextMenu.Items.Add(openRepoItem);
            listView.ContextMenu = contextMenu;

            Debug.WriteLine("[DEBUG] Context menu created successfully");
        }
    }
}