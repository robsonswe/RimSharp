using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using RimSharp.Core.Extensions;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Core.Extensions
{
    public class DispatcherExtensionsTests
    {
        public DispatcherExtensionsTests()
        {
            TestApp.InitializeTestApp();
        }

        [AvaloniaFact]
        public void SafeInvoke_OnUIThread_ShouldExecuteImmediately()
        {
            // Arrange
            bool executed = false;
            var dispatcher = Dispatcher.UIThread;

            // Act
            dispatcher.SafeInvoke(() => executed = true);

            // Assert
            executed.Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task SafeInvokeAsync_OnUIThread_ShouldExecuteImmediately()
        {
            // Arrange
            bool executed = false;
            var dispatcher = Dispatcher.UIThread;

            // Act
            await dispatcher.SafeInvokeAsync(async () => 
            {
                await Task.Yield();
                executed = true;
            });

            // Assert
            executed.Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task SafeInvokeAsync_WithResult_ShouldReturnResult()
        {
            // Arrange
            var dispatcher = Dispatcher.UIThread;

            // Act
            var result = await dispatcher.SafeInvokeAsync(() => "success");

            // Assert
            result.Should().Be("success");
        }

        [AvaloniaFact]
        public void SafeBeginInvoke_OnUIThread_ShouldExecuteImmediately()
        {
            // Arrange
            bool executed = false;
            var dispatcher = Dispatcher.UIThread;

            // Act
            dispatcher.SafeBeginInvoke(() => executed = true);

            // Assert
            executed.Should().BeTrue();
        }
    }
}
