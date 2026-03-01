using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using RimSharp.Core.Extensions;
using RimSharp.Tests.Base;
using Xunit;

namespace RimSharp.Tests.Core.Extensions
{
    public class ThreadHelperTests
    {
        public ThreadHelperTests()
        {
            TestApp.InitializeTestApp();
            ThreadHelper.Initialize();
        }

        [AvaloniaFact]
        public void IsTestMode_ShouldBeTrue()
        {
            ThreadHelper.IsTestMode.Should().BeTrue();
        }

        [AvaloniaFact]
        public void IsUiThread_ShouldBeTrue_InTest()
        {
            ThreadHelper.IsUiThread.Should().BeTrue();
        }

        [AvaloniaFact]
        public void EnsureUiThread_ShouldExecuteImmediately()
        {
            bool executed = false;
            ThreadHelper.EnsureUiThread(() => executed = true);
            executed.Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task RunOnUIThreadAsync_ShouldExecuteImmediately()
        {
            bool executed = false;
            await ThreadHelper.RunOnUIThreadAsync(() => executed = true);
            executed.Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task RunOnUIThreadAsync_WithResult_ShouldReturnResult()
        {
            var result = await ThreadHelper.RunOnUIThreadAsync(() => "result");
            result.Should().Be("result");
        }

        [AvaloniaFact]
        public async Task RunOnUIThreadAsync_WithAsyncTask_ShouldExecute()
        {
            bool executed = false;
            await ThreadHelper.RunOnUIThreadAsync(async () => 
            {
                await Task.Yield();
                executed = true;
            });
            executed.Should().BeTrue();
        }

        [AvaloniaFact]
        public async Task DelayAsync_InTest_ShouldCompleteImmediately()
        {
            var start = DateTime.Now;
            await ThreadHelper.DelayAsync(1000);
            var end = DateTime.Now;

            (end - start).TotalMilliseconds.Should().BeLessThan(500);
        }
    }
}
