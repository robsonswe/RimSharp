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

            bool executed = false;
            var dispatcher = Dispatcher.UIThread;

            dispatcher.SafeInvoke(() => executed = true);

            executed.Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task SafeInvokeAsync_OnUIThread_ShouldExecuteImmediately()
        {

            bool executed = false;
            var dispatcher = Dispatcher.UIThread;

            await dispatcher.SafeInvokeAsync(async () => 
            {
                await Task.Yield();
                executed = true;
            });

            executed.Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task SafeInvokeAsync_WithResult_ShouldReturnResult()
        {

            var dispatcher = Dispatcher.UIThread;

            var result = await dispatcher.SafeInvokeAsync(() => "success");

            result.Should().Be("success");
        }

        [AvaloniaFact]
        public void SafeBeginInvoke_OnUIThread_ShouldExecuteImmediately()
        {

            bool executed = false;
            var dispatcher = Dispatcher.UIThread;

            dispatcher.SafeBeginInvoke(() => executed = true);

            executed.Should().BeTrue();
        }
    }
}

