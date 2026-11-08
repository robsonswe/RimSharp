using System.IO;
using System.Runtime.InteropServices;
using FluentAssertions;
using RimSharp.Features.ModManager.ViewModels.Actions;
using Xunit;

namespace RimSharp.Tests.Features.ModManager.ViewModels.Actions.Commands
{
    public class MacAppBundleLaunchTests : System.IDisposable
    {
        private readonly string _tempRoot;
        private readonly string _appBundlePath;
        private readonly string _contentsMacOsPath;
        private readonly string _innerExePath;

        public MacAppBundleLaunchTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "rimsharp-maclaunch-" + Path.GetRandomFileName());
            _appBundlePath = Path.Combine(_tempRoot, "RimWorld.app");
            _contentsMacOsPath = Path.Combine(_appBundlePath, "Contents", "MacOS");
            _innerExePath = Path.Combine(_contentsMacOsPath, "RimWorld");

            Directory.CreateDirectory(_contentsMacOsPath);
            File.WriteAllText(_innerExePath, "#!/bin/sh\necho stub");
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
            ModActionsViewModel.MacAppBundleLauncher = null;
        }

        [Fact]
        public void TryLaunchMacAppBundle_OpenSucceeds_ReturnsTrueWithOpenMechanism()
        {
            // Arrange
            var calledWith = (AppBundle: "", WorkingDir: "", Mechanism: "");
            ModActionsViewModel.MacAppBundleLauncher = (appBundle, workingDir) =>
            {
                calledWith = (appBundle, workingDir, "open");
                return (true, "open");
            };

            // Act
            var result = ModActionsViewModel.TryLaunchMacAppBundle(_appBundlePath, _tempRoot);

            // Assert
            result.Should().BeTrue();
            calledWith.AppBundle.Should().Be(_appBundlePath);
            calledWith.WorkingDir.Should().Be(_tempRoot);
            calledWith.Mechanism.Should().Be("open");
        }

        [Fact]
        public void TryLaunchMacAppBundle_OpenFailsInnerSucceeds_ReturnsTrueWithInnerMechanism()
        {
            // Arrange
            var calledWith = (AppBundle: "", WorkingDir: "", Mechanism: "");
            ModActionsViewModel.MacAppBundleLauncher = (appBundle, workingDir) =>
            {
                calledWith = (appBundle, workingDir, "inner");
                return (true, "inner");
            };

            // Act
            var result = ModActionsViewModel.TryLaunchMacAppBundle(_appBundlePath, _tempRoot);

            // Assert
            result.Should().BeTrue();
            calledWith.Mechanism.Should().Be("inner");
        }

        [Fact]
        public void TryLaunchMacAppBundle_BothFail_ReturnsFalseWithNoneMechanism()
        {
            // Arrange
            var calledWith = (AppBundle: "", WorkingDir: "", Mechanism: "");
            ModActionsViewModel.MacAppBundleLauncher = (appBundle, workingDir) =>
            {
                calledWith = (appBundle, workingDir, "none");
                return (false, "none");
            };

            // Act
            var result = ModActionsViewModel.TryLaunchMacAppBundle(_appBundlePath, _tempRoot);

            // Assert
            result.Should().BeFalse();
            calledWith.Mechanism.Should().Be("none");
        }
    }
}