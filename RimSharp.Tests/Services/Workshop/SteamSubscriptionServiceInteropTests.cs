using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Services.Workshop;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using Xunit;

namespace RimSharp.Tests.Services.Workshop
{
    /// <summary>
    /// Platform-independent tests of the subscription logic through the ISteamworksInterop seam,
    /// plus an architecture guard that keeps Steamworks.NET types out of SteamSubscriptionService.
    /// 
    /// Why the guard exists: on runtimes where the Steamworks.NET assembly is not in the output
    /// (observed on osx-arm64) the JIT throws FileNotFoundException the moment it compiles ANY
    /// method body that mentions a Steamworks type - before an in-method "if (!supported)" check can
    /// run - and the CLR cannot even load an async state machine that hoists a Steamworks struct.
    /// Runtime guards inside the service therefore cannot work; the service must never reference
    /// Steamworks.NET at all. This test fails on Windows/Linux CI if someone re-introduces that.
    /// </summary>
    public class SteamSubscriptionServiceInteropTests
    {
        private readonly ILoggerService _logger = Substitute.For<ILoggerService>();
        private readonly SteamWorkshopApiClient _apiClient = Substitute.For<SteamWorkshopApiClient>(Substitute.For<System.Net.Http.HttpClient>());
        private readonly ISteamworksInterop _interop = Substitute.For<ISteamworksInterop>();
        private readonly GameInstance _gameInstance = new GameInstance
        {
            GameFolder = @"C:\Test\RimWorld",
            WorkshopFolder = @"C:\Test\Steam\steamapps\workshop\content\294100",
            IsSteamInstallation = true,
            SteamClientIntegration = true
        };

        private SteamSubscriptionService CreateService()
            => new SteamSubscriptionService(_gameInstance, _logger, _apiClient, interop: _interop);

        // ── Behaviour through the seam (runs identically on every OS) ───────────────

        [Fact]
        public async Task SubscribeAsync_WhenSteamAccepts_ReturnsTrue()
        {
            _interop.Init().Returns(true);
            _interop.GetSubscribedItems().Returns(Array.Empty<ulong>());
            _interop.SubscribeItemAsync(123456789UL, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new SteamCallOutcome(true, true, "k_EResultOK")));
            _interop.GetItemState(123456789UL).Returns(SteamItemState.Subscribed | SteamItemState.Installed);
            using var service = CreateService();

            var result = await service.SubscribeAsync("123456789", null, TestContext.Current.CancellationToken);

            result.Should().BeTrue();
            await _interop.Received(1).SubscribeItemAsync(123456789UL, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task SubscribeAsync_WhenSteamRejects_LogsResultNameAndReturnsFalse()
        {
            _interop.Init().Returns(true);
            _interop.GetSubscribedItems().Returns(Array.Empty<ulong>());
            _interop.SubscribeItemAsync(123456789UL, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new SteamCallOutcome(true, false, "k_EResultAccessDenied")));
            using var service = CreateService();

            var result = await service.SubscribeAsync("123456789", null, TestContext.Current.CancellationToken);

            result.Should().BeFalse();
            _logger.Received().LogError(Arg.Is<string>(s => s.Contains("k_EResultAccessDenied")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task SubscribeAsync_WhenCallTimesOut_LogsNoResult()
        {
            _interop.Init().Returns(true);
            _interop.GetSubscribedItems().Returns(Array.Empty<ulong>());
            _interop.SubscribeItemAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(SteamCallOutcome.NoResult));
            using var service = CreateService();

            var result = await service.SubscribeAsync("123456789", null, TestContext.Current.CancellationToken);

            result.Should().BeFalse();
            _logger.Received().LogError(Arg.Is<string>(s => s.Contains("No result")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task IsSubscribedAsync_UsesSteamSubscribedList()
        {
            _interop.Init().Returns(true);
            _interop.GetSubscribedItems().Returns(new ulong[] { 111, 222 });
            using var service = CreateService();

            (await service.IsSubscribedAsync("222")).Should().BeTrue();
            (await service.IsSubscribedAsync("333")).Should().BeFalse();
        }

        [Fact]
        public async Task GetSubscribedModsAsync_ReturnsIdsFromSteam()
        {
            _interop.Init().Returns(true);
            _interop.GetSubscribedItems().Returns(new ulong[] { 111, 222 });
            using var service = CreateService();

            var result = await service.GetSubscribedModsAsync();

            result.Should().Equal("111", "222");
        }

        [Fact]
        public async Task GetDownloadProgressAsync_MapsDownloadingFlag()
        {
            _interop.Init().Returns(true);
            _interop.TryGetDownloadInfo(5UL, out Arg.Any<ulong>(), out Arg.Any<ulong>())
                .Returns(x => { x[1] = 50UL; x[2] = 200UL; return true; });
            _interop.GetItemState(5UL).Returns(SteamItemState.Downloading);
            using var service = CreateService();

            var result = await service.GetDownloadProgressAsync("5");

            result.Should().NotBeNull();
            result!.Value.isDownloading.Should().BeTrue();
            result.Value.bytesDownloaded.Should().Be(50UL);
            result.Value.bytesTotal.Should().Be(200UL);
        }

        [Fact]
        public async Task CheckOwnershipAsync_WhenOwned_ReturnsTrue()
        {
            _interop.Init().Returns(true);
            _interop.QueryOwnership(294100).Returns(new SteamOwnershipInfo(true, 1, 2, true));
            using var service = CreateService();

            var result = await service.CheckOwnershipAsync(TestContext.Current.CancellationToken);

            result.Should().BeTrue();
            _logger.Received().LogInfo(Arg.Is<string>(s => s.Contains("Family sharing detected")), "SteamSubscriptionService");
        }

        [Fact]
        public async Task CheckOwnershipAsync_WhenNotOwned_ReturnsFalse()
        {
            _interop.Init().Returns(true);
            _interop.QueryOwnership(294100).Returns(new SteamOwnershipInfo(false, 0, 0, false));
            using var service = CreateService();

            (await service.CheckOwnershipAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        }

        [Fact]
        public async Task Dispose_AfterSuccessfulInit_ShutsDownSteamOnce()
        {
            _interop.Init().Returns(true);
            var service = CreateService();
            await service.IsSubscribedAsync("1"); // triggers init

            service.Dispose();
            service.Dispose();

            _interop.Received(1).Shutdown();
        }

        [Fact]
        public void Dispose_WithoutInit_NeverTouchesInterop()
        {
            var service = CreateService();

            service.Dispose();

            _interop.DidNotReceiveWithAnyArgs().Shutdown();
        }

        // ── Deployment smoke tests (the osx-arm64 regression) ───────────────────────

        [Fact]
        public void SteamworksManagedAssembly_IsDeployedToOutput()
        {
            // On osx-arm64 the old package compiled fine but its managed dll was missing at runtime.
            // Loading by name (no static type reference) turns that into a clear failure here.
            var assembly = Assembly.Load(new AssemblyName(SteamworksAssemblyName));

            File.Exists(assembly.Location).Should().BeTrue("the Steamworks.NET managed assembly must be copied to the output on every platform");
            TestContext.Current.SendDiagnosticMessage($"Steamworks.NET loaded: {assembly.FullName} @ {assembly.Location}");
        }

        [Fact]
        public void RealInterop_Init_DoesNotFailWithManagedLoadErrors()
        {
            // Without a running Steam client Init returns false or throws a native-library error, both fine.
            // A managed load/format error means the package/assembly wiring is broken for this platform.
            var interop = SteamworksInteropFactory.Create();
            var initialized = false;
            var ex = Record.Exception(() => initialized = interop.Init());
            try
            {
                // A null exception is the good outcome on a runner without Steam: the native
                // library resolved and Init simply returned false. Only a real failure has
                // assertions to make, and they must be checked against the exception we got.
                if (ex != null)
                {
                    ex.Should().NotBeOfType<FileNotFoundException>();
                    ex.Should().NotBeOfType<FileLoadException>();
                    ex.Should().NotBeOfType<BadImageFormatException>();
                    ex.Should().NotBeOfType<TypeLoadException>();
                }
                TestContext.Current.SendDiagnosticMessage($"SteamAPI.Init -> {(ex == null ? initialized.ToString() : ex.GetType().Name + ": " + ex.Message)}");
            }
            finally
            {
                if (initialized) interop.Shutdown();
            }
        }

        // ── Architecture guard ───────────────────────────────────────────────────────

        private const string SteamworksAssemblyName = "Steamworks.NET";

        [Fact]
        public void SteamSubscriptionService_NeverReferencesSteamworksNetTypes()
        {
            var violations = new List<string>();
            foreach (var type in EnumerateTypesWithNested(typeof(SteamSubscriptionService)))
                AuditType(type, violations);

            violations.Should().BeEmpty(
                "SteamSubscriptionService (and its compiler-generated closures/state machines) must not reference " +
                "Steamworks.NET: when that assembly is absent (osx-arm64) the JIT fails before runtime guards can run. " +
                "Put Steamworks calls in SteamworksInterop instead.");
        }

        private static IEnumerable<Type> EnumerateTypesWithNested(Type root)
        {
            yield return root;
            foreach (var nested in root.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (var t in EnumerateTypesWithNested(nested))
                    yield return t;
        }

        private static void AuditType(Type type, List<string> violations)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                     BindingFlags.Static | BindingFlags.DeclaredOnly;

            void Check(Type? t, string where)
            {
                if (t != null && MentionsSteamworks(t)) violations.Add($"{type.Name}: {where} uses {t}");
            }

            foreach (var f in type.GetFields(all)) Check(f.FieldType, $"field {f.Name}");

            var methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
            foreach (var m in methods)
            {
                var label = $"{type.Name}.{m.Name}";
                if (m is MethodInfo mi) Check(mi.ReturnType, $"{label} return type");
                foreach (var p in m.GetParameters()) Check(p.ParameterType, $"{label} parameter {p.Name}");

                MethodBody? body;
                try { body = m.GetMethodBody(); }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException) { violations.Add($"{label}: {ex.Message}"); continue; }
                if (body == null) continue;

                foreach (var local in body.LocalVariables) Check(local.LocalType, $"{label} local");

                foreach (var token in ReadMetadataTokens(body.GetILAsByteArray()!))
                {
                    MemberInfo? member;
                    try { member = m.Module.ResolveMember(token); }
                    catch (Exception ex) when (ex is FileNotFoundException or FileLoadException)
                    {
                        violations.Add($"{label}: IL token 0x{token:X8} -> {ex.Message}");
                        continue;
                    }
                    catch (ArgumentException) { continue; } // tokens that need a generic context

                    switch (member)
                    {
                        case Type t: Check(t, $"{label} IL type operand"); break;
                        case FieldInfo f: Check(f.DeclaringType, $"{label} IL field {f.Name}"); Check(f.FieldType, $"{label} IL field {f.Name}"); break;
                        case MethodBase mb:
                            Check(mb.DeclaringType, $"{label} IL call {mb.Name}");
                            if (mb is MethodInfo called) Check(called.ReturnType, $"{label} IL call {mb.Name} return");
                            foreach (var p in mb.GetParameters()) Check(p.ParameterType, $"{label} IL call {mb.Name} arg");
                            break;
                    }
                }
            }
        }

        private static bool MentionsSteamworks(Type t)
        {
            while (t.HasElementType) t = t.GetElementType()!;
            if (t.IsGenericType && t.GetGenericArguments().Any(MentionsSteamworks)) return true;
            return t.Assembly.GetName().Name == SteamworksAssemblyName;
        }

        private static readonly Dictionary<short, OpCode> OpCodeTable = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);

        private static IEnumerable<int> ReadMetadataTokens(byte[] il)
        {
            var i = 0;
            while (i < il.Length)
            {
                short value = il[i++];
                if (value == 0xFE) value = (short)(0xFE00 | il[i++]);
                var op = OpCodeTable[value];

                switch (op.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: i += 1; break;
                    case OperandType.InlineVar: i += 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: i += 8; break;
                    case OperandType.InlineSwitch: i += 4 + 4 * BitConverter.ToInt32(il, i); break;
                    case OperandType.InlineMethod:
                    case OperandType.InlineField:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                        yield return BitConverter.ToInt32(il, i);
                        i += 4;
                        break;
                    default: i += 4; break; // InlineI, InlineBrTarget, InlineString, InlineSig, ShortInlineR
                }
            }
        }
    }
}