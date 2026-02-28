using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using ReactiveUI;
using RimSharp.AppDir.AppFiles;
using RimSharp.AppDir.Dialogs;
using RimSharp.Shared.Models;

namespace RimSharp.Features.ModManager.Dialogs.ActiveIssues
{
    public class ModIssueGroup : ViewModelBase
    {
        public ModItem Mod { get; }
        public ObservableCollection<ModIssue> Issues { get; }

        public ModIssueGroup(ModItem mod, IEnumerable<ModIssue> issues)
        {
            Mod = mod;
            Issues = new ObservableCollection<ModIssue>(issues);
        }
    }

    public class ActiveIssuesDialogViewModel : DialogViewModelBase<bool>
    {
        public ObservableCollection<ModIssueGroup> IssueGroups { get; }

        public ICommand CloseIssuesCommand { get; }

        public ActiveIssuesDialogViewModel(IEnumerable<ModIssue> issues) 
            : base("Active Mod List Issues")
        {
            var issueList = issues?.ToList() ?? new List<ModIssue>();
            
            var groups = issueList
                .GroupBy(i => i.Mod)
                .Select(g => new ModIssueGroup(g.Key, g))
                .ToList();

            IssueGroups = new ObservableCollection<ModIssueGroup>(groups);

            CloseIssuesCommand = CreateCommand(() => CloseDialog(true));
        }
    }
}
