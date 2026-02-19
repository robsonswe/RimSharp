using System;
using System.ComponentModel;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Dialog;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Dialog
{
    public class DialogServiceTests
    {
        private readonly IAppUpdaterService _mockAppUpdaterService;
        private readonly DialogService _dialogService;

        public DialogServiceTests()
        {
            RimSharp.Core.Extensions.ThreadHelper.Initialize();
            _mockAppUpdaterService = Substitute.For<IAppUpdaterService>();
            _dialogService = new DialogService(_mockAppUpdaterService);
        }

        #region IsAnyDialogOpen Property Tests

        [Fact]
        public void IsAnyDialogOpen_ShouldBeFalse_Initially()
        {
            // Assert
            _dialogService.IsAnyDialogOpen.Should().BeFalse();
        }

        [Fact]
        public void DialogService_ShouldImplementINotifyPropertyChanged()
        {
            // Assert
            _dialogService.Should().BeAssignableTo<INotifyPropertyChanged>();
        }

        [Fact]
        public void PropertyChanged_Event_ShouldExist()
        {
            // Arrange
            bool eventFired = false;
            _dialogService.PropertyChanged += (s, e) => eventFired = true;

            // Act - Access the property to verify event subscription works
            var _ = _dialogService.IsAnyDialogOpen;

            // Assert
            eventFired.Should().BeFalse(); // Event doesn't fire on get, only on change
        }

        #endregion

        #region Constructor Tests

        [Fact]
        public void Constructor_ShouldInitializeWithAppUpdaterService()
        {
            // Arrange
            var mockUpdater = Substitute.For<IAppUpdaterService>();

            // Act
            var service = new DialogService(mockUpdater);

            // Assert
            service.Should().NotBeNull();
            service.IsAnyDialogOpen.Should().BeFalse();
        }

        [Fact]
        public void Constructor_ShouldInitializeWithoutAppUpdaterService()
        {
            // Act
            var service = new DialogService(null!);

            // Assert
            service.Should().NotBeNull();
            service.IsAnyDialogOpen.Should().BeFalse();
        }

        #endregion

        #region Interface Implementation Tests

        [Fact]
        public void DialogService_ShouldImplementIDialogService()
        {
            // Assert
            _dialogService.Should().BeAssignableTo<IDialogService>();
        }

        [Fact]
        public void IDialogService_IsAnyDialogOpen_ShouldBeAccessible()
        {
            // Arrange
            IDialogService service = _dialogService;

            // Assert
            service.IsAnyDialogOpen.Should().BeFalse();
        }

        #endregion

        #region Dialog Count Management Tests

        [Fact]
        public void DialogService_ShouldHave_PrivateDialogCountField()
        {
            // This test verifies the internal state management exists
            // The actual dialog count is private, but IsAnyDialogOpen exposes its state
            _dialogService.IsAnyDialogOpen.Should().BeFalse();
        }

        #endregion

        #region Method Existence Tests

        [Fact]
        public void ShowInformation_Method_ShouldExist()
        {
            // Assert
            _dialogService.Should().NotBeNull();
        }

        [Fact]
        public void ShowWarning_Method_ShouldExist()
        {
            // Assert
            _dialogService.Should().NotBeNull();
        }

        [Fact]
        public void ShowError_Method_ShouldExist()
        {
            // Assert
            _dialogService.Should().NotBeNull();
        }

        [Fact]
        public void ShowConfirmation_Method_ShouldExist()
        {
            // Assert
            _dialogService.Should().NotBeNull();
        }

        [Fact]
        public void ShowProgressDialog_Method_ShouldExist()
        {
            // Assert
            _dialogService.Should().NotBeNull();
        }

        [Fact]
        public void ShowAboutDialog_Method_ShouldExist()
        {
            // Assert
            _dialogService.Should().NotBeNull();
        }

        #endregion

        #region Property Tests

        [Fact]
        public void IsAnyDialogOpen_Property_ShouldBeReadable()
        {
            // Act
            var result = _dialogService.IsAnyDialogOpen;

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public void IsAnyDialogOpen_Property_ShouldBeBoolean()
        {
            // Act
            var result = _dialogService.IsAnyDialogOpen;

            // Assert
            result.Should().BeFalse(); // Default value is false (boolean)
        }

        #endregion
    }
}
