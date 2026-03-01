using System;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.WorkshopDownloader.Components.Browser;
using RimSharp.Features.WorkshopDownloader.Services;
using Xunit;

namespace RimSharp.Tests.Features.WorkshopDownloader.Services
{
    public class WebNavigationServiceTests
    {
        [Theory]
        [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=12345", true)]
        [InlineData("https://steamcommunity.com/workshop/filedetails/?id=12345", true)]
        [InlineData("https://steamcommunity.com/sharedfiles/filedetails/", false)]
        [InlineData("https://google.com", false)]
        public void SetBrowserControl_WhenNavigationCompletes_ShouldIdentifyWorkshopPage(string url, bool expected)
        {
            // Arrange
            var service = new WebNavigationService();
            var mockBrowser = Substitute.For<IBrowserControl>();
            service.SetBrowserControl(mockBrowser);

            bool identified = false;
            service.PotentialWorkshopPageLoaded += (s, e) => identified = true;

            // Act
            mockBrowser.NavigationCompleted += Raise.Event<EventHandler<string>>(mockBrowser, url);

            // Assert
            identified.Should().Be(expected);
        }

        [Fact]
        public void GoHome_ShouldNavigateToWorkshop()
        {
            // Arrange
            var service = new WebNavigationService();
            var mockBrowser = Substitute.For<IBrowserControl>();
            service.SetBrowserControl(mockBrowser);

            // Act
            service.GoHome();

            // Assert
            mockBrowser.Received(1).Navigate(Arg.Is<string>(s => s.Contains("workshop")));
        }
    }
}
