using FluentAssertions;
using RimSharp.Infrastructure.Mods.Replacements;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods.Replacements
{
    public class IndexedRecordsFormatAdapterTests
    {
        private const string CuratedRecord = @"{
            ""notices"": [
                { ""date"": ""2023-06-12"", ""notice"": { ""BadPerformance"": ""some text"" },
                  ""certainty"": ""High"", ""source"": { ""JumliDataset"": ""Dubs Community Slow Mods Spreadsheet"" } },
                { ""date"": ""2025-09-10"", ""notice"": { ""UseAlternative"": [""Keyz' Allow Utilities"", 3524716849, ""Better performance, same features.""] },
                  ""certainty"": ""High"", ""source"": { ""JumliDataset"": ""Common Performance Advice"" } }
            ],
            ""identifiers"": [ { ""PackageId"": ""unlimitedhugs.allowtool"" }, { ""WorkshopId"": 761421485 } ]
        }";

        private const string UtiSourcedRecord = @"{
            ""notices"": [
                { ""date"": null, ""notice"": { ""UseAlternative"": [""Eternal Youth 1.6 Fix"", 3788130822, null] },
                  ""certainty"": ""Inapplicable"", ""source"": ""UseThisInsteadDatabase"" }
            ],
            ""identifiers"": [ { ""PackageId"": ""hams.EternalYouthTraits"" }, { ""WorkshopId"": 2106011986 } ]
        }";

        [Fact]
        public void ParseRecord_CuratedUseAlternative_ShouldFillReasonAndTarget()
        {
            var record = IndexedRecordsFormatAdapter.ParseRecord(CuratedRecord);

            record.Should().NotBeNull();
            record!.PackageId.Should().Be("unlimitedhugs.allowtool");
            record.WorkshopId.Should().Be("761421485");
            record.CuratedAlternative.Should().NotBeNull();
            record.CuratedAlternative!.TargetName.Should().Be("Keyz' Allow Utilities");
            record.CuratedAlternative.TargetWorkshopId.Should().Be("3524716849");
            record.CuratedAlternative.Reason.Should().Be("Better performance, same features.");
        }

        [Fact]
        public void ParseRecord_UtiSourcedRecord_ShouldContributeNothing()
        {
            var record = IndexedRecordsFormatAdapter.ParseRecord(UtiSourcedRecord);

            record.Should().NotBeNull();
            record!.CuratedAlternative.Should().BeNull("the source is the plain-string UseThisInsteadDatabase unit variant");
        }

        [Fact]
        public void ParseRecord_UnitVariantNotice_ShouldNotBeTreatedAsUseAlternative()
        {
            const string json = @"{
                ""notices"": [ { ""notice"": ""UseAlternative"", ""source"": { ""JumliDataset"": ""x"" } } ],
                ""identifiers"": []
            }";

            var record = IndexedRecordsFormatAdapter.ParseRecord(json);

            record!.CuratedAlternative.Should().BeNull("a unit-variant notice has no payload to read a reason from");
        }

        [Fact]
        public void ParseRecord_CuratedNoticeWithNullReason_ShouldLeaveReasonNull()
        {
            const string json = @"{
                ""notices"": [ { ""notice"": { ""UseAlternative"": [""Target"", 123, null] },
                                 ""source"": { ""JumliDataset"": ""x"" } } ],
                ""identifiers"": []
            }";

            var record = IndexedRecordsFormatAdapter.ParseRecord(json);

            record!.CuratedAlternative.Should().NotBeNull();
            record.CuratedAlternative!.Reason.Should().BeNull();
        }

        [Fact]
        public void ParseRecord_NonCuratedPayloadSource_ShouldContributeNothing()
        {
            const string json = @"{
                ""notices"": [ { ""notice"": { ""UseAlternative"": [""Target"", 123, ""reason""] },
                                 ""source"": { ""SomeOtherDataset"": ""x"" } } ],
                ""identifiers"": []
            }";

            var record = IndexedRecordsFormatAdapter.ParseRecord(json);

            record!.CuratedAlternative.Should().BeNull();
        }
    }
}
