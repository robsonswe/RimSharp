using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using RimSharp.Features.ModManager.Services.Management;
using RimSharp.Infrastructure.Mods.Validation.Incompatibilities;
using RimSharp.Shared.Models;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods.Validation.Incompatibilities
{
    public class ModIncompatibilityServiceTests
    {
        private readonly ModLookupService _mockLookupService;
        private readonly ModIncompatibilityService _service;

        public ModIncompatibilityServiceTests()
        {
            _mockLookupService = Substitute.For<ModLookupService>();
            _service = new ModIncompatibilityService(_mockLookupService);
        }

        [Fact]
        public void FindIncompatibilities_ShouldDetectDirectIncompatibility()
        {

            var modA = new ModItem { Name = "Mod A", PackageId = "modA" };
            var modB = new ModItem { Name = "Mod B", PackageId = "modB" };
            modA.IncompatibleWith.Add("modB", new ModIncompatibilityRule { Comment = new List<string> { "Conflict" } });
            
            var activeMods = new List<ModItem> { modA, modB };

            var result = _service.FindIncompatibilities(activeMods);

            result.Should().HaveCount(2); 
            result.Should().Contain(i => i.SourceMod == modA && i.TargetMod == modB && i.Reason == "Conflict");
            result.Should().Contain(i => i.SourceMod == modB && i.TargetMod == modA && i.Reason == "Conflict");
        }

        [Fact]
        public void FindIncompatibilities_ShouldDetectTransitiveIncompatibility()
        {

            var modA = new ModItem { Name = "Mod A", PackageId = "modA" };
            var modB = new ModItem { Name = "Mod B", PackageId = "modB" };
            var modC = new ModItem 
            { 
                Name = "Mod C", 
                PackageId = "modC", 
                ModDependencies = new List<ModDependency> { new ModDependency { PackageId = "modA" } } 
            };
            
            modA.IncompatibleWith.Add("modB", new ModIncompatibilityRule { Comment = new List<string> { "Conflict" } });
            
            var activeMods = new List<ModItem> { modA, modB, modC };

            var result = _service.FindIncompatibilities(activeMods);

            // A vs B (2)
            // C (dep of A) vs B (2)
            result.Should().HaveCount(4);
            result.Should().Contain(i => i.SourceMod == modC && i.TargetMod == modB && i.Reason.Contains("Depends on Mod A"));
        }

        [Fact]
        public void GroupIncompatibilities_ShouldGroupRelatedMods()
        {

            var modA = new ModItem { Name = "Mod A", PackageId = "modA" };
            var modB = new ModItem { Name = "Mod B", PackageId = "modB" };
            var modC = new ModItem { Name = "Mod C", PackageId = "modC" };
            
            modA.IncompatibleWith.Add("modB", new ModIncompatibilityRule { Comment = new List<string> { "Conflict AB" } });
            modB.IncompatibleWith.Add("modC", new ModIncompatibilityRule { Comment = new List<string> { "Conflict BC" } });
            
            var activeMods = new List<ModItem> { modA, modB, modC };
            var incompatibilities = _service.FindIncompatibilities(activeMods);

            var groups = _service.GroupIncompatibilities(incompatibilities);

            groups.Should().HaveCount(1);
            groups[0].InvolvedMods.Should().HaveCount(3);
            groups[0].InvolvedMods.Should().Contain(new[] { modA, modB, modC });
        }
    }
}


