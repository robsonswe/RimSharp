using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.Features.ModManager.Dialogs.ActiveIssues;
using RimSharp.Shared.Models;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.Dialogs.ActiveIssues
{
    public class ActiveIssuesDialogViewModelTests
    {
        public ActiveIssuesDialogViewModelTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void Grouping_ShouldWorkCorrectly()
        {

            var mod1 = new ModItem { Name = "Mod 1" };
            var mod2 = new ModItem { Name = "Mod 2" };
            var issues = new List<ModIssue>
            {
                new ModIssue(mod1, ModIssueType.Sorting, "desc1"),
                new ModIssue(mod1, ModIssueType.Dependency, "desc2"),
                new ModIssue(mod2, ModIssueType.Duplicate, "desc3")
            };

            var vm = new ActiveIssuesDialogViewModel(issues);

            vm.IssueGroups.Should().HaveCount(2);
            var group1 = vm.IssueGroups.First(g => g.Mod == mod1);
            group1.Issues.Should().HaveCount(2);
            
            var group2 = vm.IssueGroups.First(g => g.Mod == mod2);
            group2.Issues.Should().HaveCount(1);
        }

        [AvaloniaFact]
        public void CloseIssuesCommand_ShouldSetResult()
        {

            var vm = new ActiveIssuesDialogViewModel(new List<ModIssue>());
            bool closeRequested = false;
            vm.RequestCloseDialog += (s, e) => closeRequested = true;

            vm.CloseIssuesCommand.Execute(null);

            vm.DialogResult.Should().BeTrue();
            closeRequested.Should().BeTrue();
        }
    }
}

