#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using RimSharp.AppDir.Dialogs; // For DialogViewModelBase<T>
using RimSharp.Features.WorkshopDownloader.Models;

namespace RimSharp.Features.WorkshopDownloader.Dialogs.Collection
{
    // The result is a list of selected Steam IDs
    public class CollectionDialogViewModel : DialogViewModelBase<List<string>>
    {
        public ObservableCollection<CollectionItemViewModel> Items { get; }

        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand AddSelectedCommand { get; }
        public ICommand CancelCommand { get; } // Use base CloseCommand

        public CollectionDialogViewModel(IEnumerable<CollectionItemInfo> collectionItems)
            : base("Select Collection Items to Add") // Dialog Title
        {
            Items = new ObservableCollection<CollectionItemViewModel>(
                collectionItems.Select(info => new CollectionItemViewModel(info.SteamId, info.Name, info.Author))
                               .OrderBy(vm => vm.Name) // Optional: Sort by name
            );

            SelectAllCommand = CreateCommand(SelectAll);
            SelectNoneCommand = CreateCommand(SelectNone);

            // AddSelectedCommand uses CloseWithResultCommand logic from base class
            // The parameter passed to the command will be the list of selected IDs
            AddSelectedCommand = CreateCommand(AddSelected, CanAddSelected);

            // Cancel uses the base CloseCommand, which will call CloseDialog with default(List<string>) == null
             CancelCommand = base.CloseCommand;

            // Need to observe changes in IsSelected to update CanAddSelected
             foreach(var item in Items)
             {
                 item.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(CollectionItemViewModel.IsSelected)) (AddSelectedCommand as Core.Commands.Base.DelegateCommand)?.RaiseCanExecuteChanged(); };
             }
        }

        private void SelectAll()
        {
            foreach (var item in Items)
            {
                item.IsSelected = true;
            }
        }

        private void SelectNone()
        {
            foreach (var item in Items)
            {
                item.IsSelected = false;
            }
        }

        private bool CanAddSelected()
        {
            return Items.Any(item => item.IsSelected);
        }

        private void AddSelected()
        {
            var selectedIds = Items
                .Where(item => item.IsSelected)
                .Select(item => item.SteamId)
                .ToList();

            // Call CloseDialog which sets DialogResult and DialogResultForWindow
            // and raises the RequestCloseDialog event
            CloseDialog(selectedIds);
        }

         // Override the default mapping if needed, although the base might be sufficient
        protected override void MapResultToWindowResult(List<string> result)
        {
             // Treat closing with a non-null, non-empty list as "OK" (true)
             // Treat closing with null or empty list (e.g., Cancel or Select None then Add) as "Cancel" (false)
             DialogResultForWindow = result != null && result.Any();
        }

    }
}
