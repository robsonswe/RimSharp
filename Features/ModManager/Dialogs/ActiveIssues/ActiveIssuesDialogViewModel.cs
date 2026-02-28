using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ReactiveUI;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Dialogs.ActiveIssues
{
    public class ActiveIssuesDialogViewModel : DialogViewModelBase<bool>
    {
        public ObservableCollection<ModIssue> AllIssues { get; }
        public ObservableCollection<ModIssue> SortingIssues { get; }
        public ObservableCollection<ModIssue> DependencyIssues { get; }
        public ObservableCollection<ModIssue> IncompatibilityIssues { get; }
        public ObservableCollection<ModIssue> OutdatedIssues { get; }

        public bool HasSortingIssues => SortingIssues.Any();
        public bool HasDependencyIssues => DependencyIssues.Any();
        public bool HasIncompatibilityIssues => IncompatibilityIssues.Any();
        public bool HasOutdatedIssues => OutdatedIssues.Any();

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedTabIndex, value);
        }

        public ICommand CloseIssuesCommand { get; }

        public ActiveIssuesDialogViewModel(IEnumerable<ModIssue> issues) 
            : base("Active Mod List Issues")
        {
            var issueList = issues?.ToList() ?? new List<ModIssue>();
            AllIssues = new ObservableCollection<ModIssue>(issueList);
            
            SortingIssues = new ObservableCollection<ModIssue>(issueList.Where(i => i.Type == ModIssueType.Sorting));
            DependencyIssues = new ObservableCollection<ModIssue>(issueList.Where(i => i.Type == ModIssueType.Dependency));
            IncompatibilityIssues = new ObservableCollection<ModIssue>(issueList.Where(i => i.Type == ModIssueType.Incompatibility));
            OutdatedIssues = new ObservableCollection<ModIssue>(issueList.Where(i => i.Type == ModIssueType.Outdated));

            // Set default selected tab to the first one that has issues
            if (HasSortingIssues) SelectedTabIndex = 0;
            else if (HasDependencyIssues) SelectedTabIndex = 1;
            else if (HasIncompatibilityIssues) SelectedTabIndex = 2;
            else if (HasOutdatedIssues) SelectedTabIndex = 3;

            CloseIssuesCommand = CreateCommand(() => CloseDialog(true));
        }
    }
}
