using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using RimSharp.Infrastructure.Configuration;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Services.ModManagement;
using Xunit;

namespace RimSharp.Tests.Services.ModManagement
{
    public class SaveHeaderServiceTests : IDisposable
    {
        private readonly ILoggerService _logger = Substitute.For<ILoggerService>();
        private readonly IPathService _pathService = Substitute.For<IPathService>();
        private readonly string _testTempDir;
        private readonly string _configDir;
        private readonly string _savesDir;

        public SaveHeaderServiceTests()
        {
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharp_SaveHeaderTests_" + Guid.NewGuid().ToString("N"));
            _configDir = Path.Combine(_testTempDir, "Config");
            _savesDir = Path.Combine(_configDir, "Saves");
            Directory.CreateDirectory(_savesDir);

            _pathService.GetConfigPath().Returns(_configDir);
            _pathService.GetSavesPath().Returns(_savesDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }

        private SaveHeaderService CreateService()
        {
            return new SaveHeaderService(_pathService, _logger);
        }

        #region Fixtures

        /// <summary>
        /// The minimal verified 1.6+ head: a plain UTF-8 save with a BOM and the
        /// three parallel meta lists (modIds / modSteamIds / modNames).
        /// </summary>
        private const string PlainSaveWithSteamIds =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<savegame>\r\n" +
            "\t<meta>\r\n" +
            "\t\t<gameVersion>1.7.4990 rev1065</gameVersion>\r\n" +
            "\t\t<modIds>\r\n" +
            "\t\t\t<li>ludeon.rimworld</li>\r\n" +
            "\t\t\t<li>test.mod.alpha</li>\r\n" +
            "\t\t\t<li>test.mod.beta</li>\r\n" +
            "\t\t</modIds>\r\n" +
            "\t\t<modSteamIds>\r\n" +
            "\t\t\t<li>0</li>\r\n" +
            "\t\t\t<li>2000000001</li>\r\n" +
            "\t\t\t<li>2000000002</li>\r\n" +
            "\t\t</modSteamIds>\r\n" +
            "\t\t<modNames>\r\n" +
            "\t\t\t<li>RimWorld</li>\r\n" +
            "\t\t\t<li>Test Mod Alpha</li>\r\n" +
            "\t\t\t<li>Test Mod Beta</li>\r\n" +
            "\t\t</modNames>\r\n" +
            "\t</meta>\r\n" +
            "\t<game>\r\n" +
            "\t\t<currentMapIndex>0</currentMapIndex>\r\n" +
            "\t</game>\r\n" +
            "</savegame>";

        /// <summary>
        /// The 1.5-era head: the same save without the modSteamIds list.
        /// </summary>
        private const string PlainSaveWithoutSteamIds =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<savegame>\r\n" +
            "\t<meta>\r\n" +
            "\t\t<gameVersion>1.5.1234 rev678</gameVersion>\r\n" +
            "\t\t<modIds>\r\n" +
            "\t\t\t<li>ludeon.rimworld</li>\r\n" +
            "\t\t\t<li>test.mod.alpha</li>\r\n" +
            "\t\t</modIds>\r\n" +
            "\t\t<modNames>\r\n" +
            "\t\t\t<li>RimWorld</li>\r\n" +
            "\t\t\t<li>Test Mod Alpha</li>\r\n" +
            "\t\t</modNames>\r\n" +
            "\t</meta>\r\n" +
            "\t<game>\r\n" +
            "\t\t<currentMapIndex>0</currentMapIndex>\r\n" +
            "\t</game>\r\n" +
            "</savegame>";

        /// <summary>
        /// The head of the save up to (but excluding) the history block.
        /// </summary>
        private const string HeadPrefix =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<savegame>\r\n" +
            "\t<meta>\r\n" +
            "\t\t<gameVersion>1.7.4990 rev1065</gameVersion>\r\n" +
            "\t\t<modIds>\r\n" +
            "\t\t\t<li>ludeon.rimworld</li>\r\n" +
            "\t\t</modIds>\r\n" +
            "\t\t<modNames>\r\n" +
            "\t\t\t<li>RimWorld</li>\r\n" +
            "\t\t</modNames>\r\n" +
            "\t</meta>\r\n" +
            "\t<game>\r\n" +
            "\t\t<currentMapIndex>0</currentMapIndex>\r\n" +
            "\t\t<info>\r\n" +
            // The play time is in the save but is not extracted any more - its
            // values could not be reconciled with the age of the saves.
            "\t\t\t<realPlayTimeInteracting>11.3429451</realPlayTimeInteracting>\r\n" +
            // The planet tile of the colony: it feeds the calendar (the game
            // derives the longitude and the latitude from it).
            "\t\t\t<startingTile>43378,0</startingTile>\r\n" +
            "\t\t\t<startingAndOptionalPawns>\r\n" +
            "\t\t\t\t<li>Thing_Human136</li>\r\n" +
            "\t\t\t</startingAndOptionalPawns>\r\n" +
            "\t\t</info>\r\n" +
            // The season the game itself reported for the save.
            "\t\t<dateNotifier>\r\n" +
            "\t\t\t<lastSeason>Spring</lastSeason>\r\n" +
            "\t\t</dateNotifier>\r\n" +
            "\t\t<rules>\r\n" +
            "\t\t\t<disallowedBuildings />\r\n" +
            "\t\t\t<disallowedDesignatorTypes />\r\n" +
            "\t\t</rules>\r\n" +
            "\t\t<scenario>\r\n" +
            "\t\t\t<name>Crashlanded</name>\r\n" +
            "\t\t\t<summary>Three crashlanded survivors.</summary>\r\n" +
            "\t\t\t<description>The three of you awake in your cryptosleep sarcophagi.</description>\r\n" +
            "\t\t\t<playerFaction>\r\n" +
            "\t\t\t\t<def>PlayerFaction</def>\r\n" +
            "\t\t\t\t<factionDef>PlayerColony</factionDef>\r\n" +
            "\t\t\t</playerFaction>\r\n" +
            "\t\t\t<parts>\r\n" +
            "\t\t\t\t<li Class=\"ScenPart_PlanetLayer\">\r\n" +
            "\t\t\t\t\t<def>PlanetLayer</def>\r\n" +
            "\t\t\t\t\t<layer>Orbit</layer>\r\n" +
            "\t\t\t\t</li>\r\n" +
            "\t\t\t</parts>\r\n" +
            "\t\t</scenario>\r\n" +
            "\t\t<tickManager>\r\n" +
            "\t\t\t<ticksGame>12674</ticksGame>\r\n" +
            "\t\t\t<gameStartAbsTick>1500000</gameStartAbsTick>\r\n" +
            "\t\t\t<startingYear>5500</startingYear>\r\n" +
            "\t\t</tickManager>\r\n" +
            "\t\t<storyteller>\r\n" +
            "\t\t\t<def>Cassandra</def>\r\n" +
            "\t\t\t<difficulty>\r\n" +
            "\t\t\t\t<def>Rough</def>\r\n" +
            "\t\t\t</difficulty>\r\n" +
            "\t\t\t<incidentQueue />\r\n" +
            "\t\t</storyteller>\r\n";

        /// <summary>
        /// The tail of the head: the components block and the beginning of the world
        /// block. The world block opens the info block that carries the name and the
        /// seed of the world - the reader reads up to the tag that closes it.
        /// </summary>
        private const string HeadSuffix =
            "\t\t<components>\r\n" +
            "\t\t\t<li Class=\"GameComponent_Anomaly\">\r\n" +
            "\t\t\t\t<li Class=\"GameComponent_Nested\">\r\n" +
            "\t\t\t\t\t<def>GameComponent_Nested</def>\r\n" +
            "\t\t\t\t</li>\r\n" +
            "\t\t\t</li>\r\n" +
            "\t\t\t<li Class=\"GameComponent_Ideology\" />\r\n" +
            "\t\t</components>\r\n" +
            "\t\t<world>\r\n" +
            "\t\t\t<info>\r\n" +
            "\t\t\t\t<name>Castor</name>\r\n" +
            "\t\t\t\t<planetCoverage>0.300000012</planetCoverage>\r\n" +
            "\t\t\t\t<seedString>gray</seedString>\r\n" +
            "\t\t\t</info>\r\n" +
            "\t\t</world>\r\n" +
            "\t</game>\r\n" +
            "</savegame>";

        /// <summary>
        /// A realistic head: the verified two-tab structure of the game block with
        /// scenario, storyteller, tick manager and components.
        /// </summary>
        private const string RealisticHeadSave = HeadPrefix + HeadSuffix;

        /// <summary>
        /// A head that carries none of the elements the new fields come from:
        /// no scenario summary, no description, no faction, no parts and no name
        /// in the world info. Every new field has to stay unknown then.
        /// </summary>
        private const string BareHeadSave =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<savegame>\r\n" +
            "\t<meta>\r\n" +
            "\t\t<gameVersion>1.7.4990 rev1065</gameVersion>\r\n" +
            "\t\t<modIds>\r\n" +
            "\t\t\t<li>ludeon.rimworld</li>\r\n" +
            "\t\t</modIds>\r\n" +
            "\t\t<modNames>\r\n" +
            "\t\t\t<li>RimWorld</li>\r\n" +
            "\t\t</modNames>\r\n" +
            "\t</meta>\r\n" +
            "\t<game>\r\n" +
            "\t\t<scenario>\r\n" +
            "\t\t\t<name>Crashlanded</name>\r\n" +
            "\t\t</scenario>\r\n" +
            "\t\t<world>\r\n" +
            "\t\t\t<info>\r\n" +
            "\t\t\t\t<planetCoverage>0.300000012</planetCoverage>\r\n" +
            "\t\t\t</info>\r\n" +
            "\t\t</world>\r\n" +
            "\t</game>\r\n" +
            "</savegame>";

        /// <summary>
        /// A human pawn with a gender but without a xenotype - the xenotype
        /// line is left out then.
        /// </summary>
        private const string HumanPawnHayden =
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<thing Class=\"Pawn\">\r\n" +
            "\t\t\t\t\t\t\t<def>Human</def>\r\n" +
            "\t\t\t\t\t\t\t<id>Human136</id>\r\n" +
            "\t\t\t\t\t\t\t<map>0</map>\r\n" +
            "\t\t\t\t\t\t\t<faction>Faction_10</faction>\r\n" +
            "\t\t\t\t\t\t\t<kindDef>Colonist</kindDef>\r\n" +
            "\t\t\t\t\t\t\t<gender>Male</gender>\r\n" +
            "\t\t\t\t\t\t\t<name Class=\"NameTriple\">\r\n" +
            "\t\t\t\t\t\t\t\t<first>Hayden</first>\r\n" +
            "\t\t\t\t\t\t\t\t<nick>Hayhouse</nick>\r\n" +
            "\t\t\t\t\t\t\t\t<last>Simons</last>\r\n" +
            "\t\t\t\t\t\t\t</name>\r\n" +
            "\t\t\t\t\t\t\t<guest>\r\n" +
            "\t\t\t\t\t\t\t\t<hostFaction>null</hostFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<slaveFaction>null</slaveFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<joinStatus>JoinAsColonist</joinStatus>\r\n" +
            "\t\t\t\t\t\t\t\t<interactionMode>MaintainOnly</interactionMode>\r\n" +
            "\t\t\t\t\t\t\t</guest>\r\n" +
            "\t\t\t\t\t\t</thing>\r\n" +
            "\t\t\t\t\t</li>\r\n";

        /// <summary>
        /// A human pawn whose nick is empty - the name is shown with what the
        /// save carries.
        /// </summary>
        private const string HumanPawnKelux =
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<thing Class=\"Pawn\">\r\n" +
            "\t\t\t\t\t\t\t<def>Human</def>\r\n" +
            "\t\t\t\t\t\t\t<id>Human204</id>\r\n" +
            "\t\t\t\t\t\t\t<map>0</map>\r\n" +
            "\t\t\t\t\t\t\t<faction>Faction_10</faction>\r\n" +
            "\t\t\t\t\t\t\t<kindDef>Colonist</kindDef>\r\n" +
            "\t\t\t\t\t\t\t<name Class=\"NameTriple\">\r\n" +
            "\t\t\t\t\t\t\t\t<first>Kelux</first>\r\n" +
            "\t\t\t\t\t\t\t\t<nick />\r\n" +
            "\t\t\t\t\t\t\t\t<last>Hybridge</last>\r\n" +
            "\t\t\t\t\t\t\t</name>\r\n" +
            "\t\t\t\t\t\t\t<guest>\r\n" +
            "\t\t\t\t\t\t\t\t<hostFaction>null</hostFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<slaveFaction>null</slaveFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<joinStatus>JoinAsColonist</joinStatus>\r\n" +
            "\t\t\t\t\t\t\t\t<interactionMode>MaintainOnly</interactionMode>\r\n" +
            "\t\t\t\t\t\t\t</guest>\r\n" +
            "\t\t\t\t\t\t</thing>\r\n" +
            "\t\t\t\t\t</li>\r\n";

        /// <summary>
        /// A human pawn whose name is written as an element with the parts as
        /// attributes.
        /// </summary>
        private const string HumanPawnTobias =
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<thing Class=\"Pawn\">\r\n" +
            "\t\t\t\t\t\t\t<def>Human</def>\r\n" +
            "\t\t\t\t\t\t\t<id>Human313</id>\r\n" +
            "\t\t\t\t\t\t\t<map>0</map>\r\n" +
            "\t\t\t\t\t\t\t<faction>Faction_10</faction>\r\n" +
            "\t\t\t\t\t\t\t<nameTriple first=\"Tobias\" nick=\"Toby\" last=\"Ytterstad\" />\r\n" +
            "\t\t\t\t\t\t\t<guest>\r\n" +
            "\t\t\t\t\t\t\t\t<hostFaction>null</hostFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<slaveFaction>null</slaveFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<joinStatus>JoinAsColonist</joinStatus>\r\n" +
            "\t\t\t\t\t\t\t\t<interactionMode>MaintainOnly</interactionMode>\r\n" +
            "\t\t\t\t\t\t\t</guest>\r\n" +
            "\t\t\t\t\t\t</thing>\r\n" +
            "\t\t\t\t\t</li>\r\n";

        /// <summary>
        /// A pawn of another species - it is no colonist.
        /// </summary>
        private const string AnimalPawn =
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<thing Class=\"Pawn\">\r\n" +
            "\t\t\t\t\t\t\t<def>Dog</def>\r\n" +
            "\t\t\t\t\t\t\t<id>Dog12</id>\r\n" +
            "\t\t\t\t\t\t</thing>\r\n" +
            "\t\t\t\t\t</li>\r\n";

        /// <summary>
        /// A human pawn with everything a save carries for a colonist: a nick,
        /// a gender, a xenotype, an age tracker, traits (a vanilla one, a mod
        /// one and a gene derived one), skills (a vanilla one with a passion,
        /// one without a level and a mod one), conditions (an injury, a
        /// condition with a fraction and a mod one).
        /// The age ticks are the measured ones of a real pawn: 97 421 354
        /// ticks are 27.061 years and a birth tick of -259 073 116 against
        /// the absolute tick 3 049 546 484 are 919.061 years - the game shows
        /// 27 and 919.
        /// </summary>
        private const string HumanPawnFull =
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<thing Class=\"Pawn\">\r\n" +
            "\t\t\t\t\t\t\t<def>Human</def>\r\n" +
            "\t\t\t\t\t\t\t<id>Human263</id>\r\n" +
            "\t\t\t\t\t\t\t<map>0</map>\r\n" +
            "\t\t\t\t\t\t\t<faction>Faction_10</faction>\r\n" +
            "\t\t\t\t\t\t\t<kindDef>Colonist</kindDef>\r\n" +
            "\t\t\t\t\t\t\t<gender>Female</gender>\r\n" +
            "\t\t\t\t\t\t\t<genes>\r\n" +
            "\t\t\t\t\t\t\t\t<xenogenes />\r\n" +
            "\t\t\t\t\t\t\t\t<xenotype>Baseliner</xenotype>\r\n" +
            "\t\t\t\t\t\t\t</genes>\r\n" +
            "\t\t\t\t\t\t\t<name Class=\"NameTriple\">\r\n" +
            "\t\t\t\t\t\t\t\t<first>Hayden</first>\r\n" +
            "\t\t\t\t\t\t\t\t<nick>Hayhouse</nick>\r\n" +
            "\t\t\t\t\t\t\t\t<last>Simons</last>\r\n" +
            "\t\t\t\t\t\t\t</name>\r\n" +
            "\t\t\t\t\t\t\t<story>\r\n" +
            "\t\t\t\t\t\t\t\t<traits>\r\n" +
            "\t\t\t\t\t\t\t\t\t<allTraits>\r\n" +
            "\t\t\t\t\t\t\t\t\t\t<li><def>Kind</def><sourceGene>null</sourceGene><suppressedBy>null</suppressedBy></li>\r\n" +
            "\t\t\t\t\t\t\t\t\t\t<li><def>VFEP_WarcasketTrait</def><sourceGene>null</sourceGene><suppressedBy>null</suppressedBy></li>\r\n" +
            "\t\t\t\t\t\t\t\t\t\t<li><def>SpeedOffset</def><sourceGene>Gene_201</sourceGene><suppressedBy>null</suppressedBy></li>\r\n" +
            "\t\t\t\t\t\t\t\t\t</allTraits>\r\n" +
            "\t\t\t\t\t\t\t\t</traits>\r\n" +
            "\t\t\t\t\t\t\t</story>\r\n" +
            "\t\t\t\t\t\t\t<ageTracker>\r\n" +
            "\t\t\t\t\t\t\t\t<ageBiologicalTicks>97421354</ageBiologicalTicks>\r\n" +
            "\t\t\t\t\t\t\t\t<birthAbsTicks>-259073116</birthAbsTicks>\r\n" +
            "\t\t\t\t\t\t\t</ageTracker>\r\n" +
            "\t\t\t\t\t\t\t<skills>\r\n" +
            "\t\t\t\t\t\t\t\t<skills>\r\n" +
            "\t\t\t\t\t\t\t\t\t<li><def>Shooting</def><level>11</level><passion>Major</passion></li>\r\n" +
            "\t\t\t\t\t\t\t\t\t<li><def>Melee</def><level>13</level><passion>Minor</passion></li>\r\n" +
            "\t\t\t\t\t\t\t\t\t<li><def>Plants</def></li>\r\n" +
            "\t\t\t\t\t\t\t\t\t<li><def>VFEModdedSkill</def><level>5</level></li>\r\n" +
            "\t\t\t\t\t\t\t\t</skills>\r\n" +
            "\t\t\t\t\t\t\t</skills>\r\n" +
            "\t\t\t\t\t\t\t<guest>\r\n" +
            // The join status is in the save but is not read any more - the
            // recorded values did not match the colony of the tester. It is
            // the recruitment intent, not the status of the pawn: a free
            // colonist carries JoinAsSlave as well.
            "\t\t\t\t\t\t\t\t<hostFaction>null</hostFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<slaveFaction>null</slaveFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<joinStatus>JoinAsSlave</joinStatus>\r\n" +
            "\t\t\t\t\t\t\t\t<interactionMode>MaintainOnly</interactionMode>\r\n" +
            "\t\t\t\t\t\t\t</guest>\r\n" +
            "\t\t\t\t\t\t\t<healthTracker>\r\n" +
            "\t\t\t\t\t\t\t\t<hediffSet>\r\n" +
            "\t\t\t\t\t\t\t\t\t<hediffs>\r\n" +
            "\t\t\t\t\t\t\t\t\t\t<li Class=\"Hediff_Injury\"><def>Stab</def><severity>3</severity><painCategory>LowPain</painCategory></li>\r\n" +
            "\t\t\t\t\t\t\t\t\t\t<li Class=\"HediffWithComps\"><def>BadBack</def><severity>0.33</severity></li>\r\n" +
            "\t\t\t\t\t\t\t\t\t\t<li Class=\"Hediff_Addiction\"><def>VFEI2_RoyalJellyAddiction</def><severity>0.66</severity></li>\r\n" +
            "\t\t\t\t\t\t\t\t\t</hediffs>\r\n" +
            "\t\t\t\t\t\t\t\t</hediffSet>\r\n" +
            "\t\t\t\t\t\t\t</healthTracker>\r\n" +
            "\t\t\t\t\t\t</thing>\r\n" +
            "\t\t\t\t\t</li>\r\n";

        /// <summary>
        /// The faction list of a save, in the measured shape: the player
        /// faction (PlayerColony, loadID 10) behind two outlander factions.
        /// The first faction carries no loadID element at all - the scribe
        /// omits it when it equals its default - and the player faction writes
        /// it right behind its def. The pawns write their faction as "Faction_"
        /// plus that loadID.
        /// </summary>
        private const string AllFactionsWithPlayer =
            "\t\t<world>\r\n" +
            "\t\t\t<factionManager>\r\n" +
            "\t\t\t\t<allFactions>\r\n" +
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<leader>Thing_Human68</leader>\r\n" +
            "\t\t\t\t\t\t<def>OutlanderCivil</def>\r\n" +
            "\t\t\t\t\t\t<name>Southwestern Daan</name>\r\n" +
            "\t\t\t\t\t\t<randomKey>1903439751</randomKey>\r\n" +
            "\t\t\t\t\t\t<relations>\r\n" +
            "\t\t\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t\t\t<other>Faction_10</other>\r\n" +
            "\t\t\t\t\t\t\t</li>\r\n" +
            "\t\t\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t\t\t<other>Faction_7</other>\r\n" +
            "\t\t\t\t\t\t\t\t<kind>Hostile</kind>\r\n" +
            "\t\t\t\t\t\t\t\t<goodwill>-80</goodwill>\r\n" +
            "\t\t\t\t\t\t\t</li>\r\n" +
            "\t\t\t\t\t\t</relations>\r\n" +
            "\t\t\t\t\t\t<questTags IsNull=\"True\" />\r\n" +
            "\t\t\t\t\t</li>\r\n" +
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<leader>null</leader>\r\n" +
            "\t\t\t\t\t\t<def>PlayerColony</def>\r\n" +
            "\t\t\t\t\t\t<loadID>10</loadID>\r\n" +
            "\t\t\t\t\t\t<randomKey>1594890563</randomKey>\r\n" +
            "\t\t\t\t\t\t<relations>\r\n" +
            "\t\t\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t\t\t<other>Faction_0</other>\r\n" +
            "\t\t\t\t\t\t\t</li>\r\n" +
            "\t\t\t\t\t\t</relations>\r\n" +
            "\t\t\t\t\t</li>\r\n" +
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<leader>Thing_Human77</leader>\r\n" +
            "\t\t\t\t\t\t<def>OutlanderRough</def>\r\n" +
            "\t\t\t\t\t\t<name>East Lanhiuay</name>\r\n" +
            "\t\t\t\t\t\t<loadID>7</loadID>\r\n" +
            "\t\t\t\t\t\t<randomKey>1796941324</randomKey>\r\n" +
            "\t\t\t\t\t</li>\r\n" +
            "\t\t\t\t</allFactions>\r\n" +
            "\t\t\t</factionManager>\r\n" +
            "\t\t</world>\r\n";

        /// <summary>
        /// A faction list without a player faction: the colony of the save
        /// cannot be told from any other faction, so no pawn can be classified.
        /// </summary>
        private const string AllFactionsWithoutPlayer =
            "\t\t<world>\r\n" +
            "\t\t\t<factionManager>\r\n" +
            "\t\t\t\t<allFactions>\r\n" +
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<leader>Thing_Human68</leader>\r\n" +
            "\t\t\t\t\t\t<def>OutlanderCivil</def>\r\n" +
            "\t\t\t\t\t\t<name>Southwestern Daan</name>\r\n" +
            "\t\t\t\t\t\t<loadID>7</loadID>\r\n" +
            "\t\t\t\t\t\t<randomKey>1903439751</randomKey>\r\n" +
            "\t\t\t\t\t</li>\r\n" +
            "\t\t\t\t</allFactions>\r\n" +
            "\t\t\t</factionManager>\r\n" +
            "\t\t</world>\r\n";

        /// <summary>
        /// A human pawn of another faction - a raider. It keeps its own
        /// faction, so it is no member of the colony.
        /// </summary>
        private const string HumanPawnRaider =
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<thing Class=\"Pawn\">\r\n" +
            "\t\t\t\t\t\t\t<def>Human</def>\r\n" +
            "\t\t\t\t\t\t\t<id>Human401</id>\r\n" +
            "\t\t\t\t\t\t\t<map>0</map>\r\n" +
            "\t\t\t\t\t\t\t<faction>Faction_7</faction>\r\n" +
            "\t\t\t\t\t\t\t<kindDef>Villager</kindDef>\r\n" +
            "\t\t\t\t\t\t\t<name Class=\"NameTriple\">\r\n" +
            "\t\t\t\t\t\t\t\t<first>Rudir</first>\r\n" +
            "\t\t\t\t\t\t\t\t<nick>Rud</nick>\r\n" +
            "\t\t\t\t\t\t\t\t<last>Kogen</last>\r\n" +
            "\t\t\t\t\t\t\t</name>\r\n" +
            "\t\t\t\t\t\t</thing>\r\n" +
            "\t\t\t\t\t</li>\r\n";

        /// <summary>
        /// A visitor of the colony: a human of another faction that the colony
        /// hosts. It keeps its own faction and carries a host faction.
        /// </summary>
        private const string HumanPawnGuest =
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<thing Class=\"Pawn\">\r\n" +
            "\t\t\t\t\t\t\t<def>Human</def>\r\n" +
            "\t\t\t\t\t\t\t<id>Human402</id>\r\n" +
            "\t\t\t\t\t\t\t<map>0</map>\r\n" +
            "\t\t\t\t\t\t\t<faction>Faction_7</faction>\r\n" +
            "\t\t\t\t\t\t\t<kindDef>Villager</kindDef>\r\n" +
            "\t\t\t\t\t\t\t<name Class=\"NameTriple\">\r\n" +
            "\t\t\t\t\t\t\t\t<first>Gesa</first>\r\n" +
            "\t\t\t\t\t\t\t\t<nick>Ges</nick>\r\n" +
            "\t\t\t\t\t\t\t\t<last>Arrim</last>\r\n" +
            "\t\t\t\t\t\t\t</name>\r\n" +
            "\t\t\t\t\t\t\t<guest>\r\n" +
            "\t\t\t\t\t\t\t\t<hostFaction>Faction_10</hostFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<slaveFaction>null</slaveFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<joinStatus>JoinAsGuest</joinStatus>\r\n" +
            "\t\t\t\t\t\t\t\t<interactionMode>MaintainOnly</interactionMode>\r\n" +
            "\t\t\t\t\t\t\t</guest>\r\n" +
            "\t\t\t\t\t\t</thing>\r\n" +
            "\t\t\t\t\t</li>\r\n";

        /// <summary>
        /// A prisoner of the colony: a human of another faction that the
        /// colony holds. It keeps its own faction and carries the colony as
        /// its host faction.
        /// </summary>
        private const string HumanPawnPrisoner =
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<thing Class=\"Pawn\">\r\n" +
            "\t\t\t\t\t\t\t<def>Human</def>\r\n" +
            "\t\t\t\t\t\t\t<id>Human403</id>\r\n" +
            "\t\t\t\t\t\t\t<map>0</map>\r\n" +
            "\t\t\t\t\t\t\t<faction>Faction_7</faction>\r\n" +
            "\t\t\t\t\t\t\t<kindDef>Villager</kindDef>\r\n" +
            "\t\t\t\t\t\t\t<name Class=\"NameTriple\">\r\n" +
            "\t\t\t\t\t\t\t\t<first>Pirit</first>\r\n" +
            "\t\t\t\t\t\t\t\t<nick>Piri</nick>\r\n" +
            "\t\t\t\t\t\t\t\t<last>Masur</last>\r\n" +
            "\t\t\t\t\t\t\t</name>\r\n" +
            "\t\t\t\t\t\t\t<guest>\r\n" +
            "\t\t\t\t\t\t\t\t<hostFaction>Faction_10</hostFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<slaveFaction>null</slaveFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<guestStatus>Prisoner</guestStatus>\r\n" +
            "\t\t\t\t\t\t\t\t<joinStatus>JoinAsColonist</joinStatus>\r\n" +
            "\t\t\t\t\t\t\t\t<interactionMode>MaintainOnly</interactionMode>\r\n" +
            "\t\t\t\t\t\t\t</guest>\r\n" +
            "\t\t\t\t\t\t</thing>\r\n" +
            "\t\t\t\t\t</li>\r\n";

        /// <summary>
        /// A colonist that raiders hold: it carries the faction of the colony
        /// and another faction as its host faction.
        /// </summary>
        private const string HumanPawnHeldColonist =
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<thing Class=\"Pawn\">\r\n" +
            "\t\t\t\t\t\t\t<def>Human</def>\r\n" +
            "\t\t\t\t\t\t\t<id>Human404</id>\r\n" +
            "\t\t\t\t\t\t\t<map>0</map>\r\n" +
            "\t\t\t\t\t\t\t<faction>Faction_10</faction>\r\n" +
            "\t\t\t\t\t\t\t<kindDef>Colonist</kindDef>\r\n" +
            "\t\t\t\t\t\t\t<name Class=\"NameTriple\">\r\n" +
            "\t\t\t\t\t\t\t\t<first>Hella</first>\r\n" +
            "\t\t\t\t\t\t\t\t<nick>Hel</nick>\r\n" +
            "\t\t\t\t\t\t\t\t<last>Vrast</last>\r\n" +
            "\t\t\t\t\t\t\t</name>\r\n" +
            "\t\t\t\t\t\t\t<guest>\r\n" +
            "\t\t\t\t\t\t\t\t<hostFaction>Faction_7</hostFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<slaveFaction>null</slaveFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<joinStatus>JoinAsColonist</joinStatus>\r\n" +
            "\t\t\t\t\t\t\t\t<interactionMode>MaintainOnly</interactionMode>\r\n" +
            "\t\t\t\t\t\t\t</guest>\r\n" +
            "\t\t\t\t\t\t</thing>\r\n" +
            "\t\t\t\t\t</li>\r\n";

        /// <summary>
        /// A prisoner that carries the faction of the colony and no host
        /// faction - the shape a mod can produce. Only the prisoner status
        /// rejects it.
        /// </summary>
        private const string HumanPawnPrisonerOfTheColony =
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<thing Class=\"Pawn\">\r\n" +
            "\t\t\t\t\t\t\t<def>Human</def>\r\n" +
            "\t\t\t\t\t\t\t<id>Human405</id>\r\n" +
            "\t\t\t\t\t\t\t<map>0</map>\r\n" +
            "\t\t\t\t\t\t\t<faction>Faction_10</faction>\r\n" +
            "\t\t\t\t\t\t\t<kindDef>Villager</kindDef>\r\n" +
            "\t\t\t\t\t\t\t<name Class=\"NameTriple\">\r\n" +
            "\t\t\t\t\t\t\t\t<first>Otta</first>\r\n" +
            "\t\t\t\t\t\t\t\t<nick>Ott</nick>\r\n" +
            "\t\t\t\t\t\t\t\t<last>Renk</last>\r\n" +
            "\t\t\t\t\t\t\t</name>\r\n" +
            "\t\t\t\t\t\t\t<guest>\r\n" +
            "\t\t\t\t\t\t\t\t<hostFaction>null</hostFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<slaveFaction>null</slaveFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<guestStatus>Prisoner</guestStatus>\r\n" +
            "\t\t\t\t\t\t\t\t<joinStatus>JoinAsColonist</joinStatus>\r\n" +
            "\t\t\t\t\t\t\t\t<interactionMode>MaintainOnly</interactionMode>\r\n" +
            "\t\t\t\t\t\t\t</guest>\r\n" +
            "\t\t\t\t\t\t</thing>\r\n" +
            "\t\t\t\t\t</li>\r\n";

        /// <summary>
        /// A slave of the colony: it carries the faction of the colony and no
        /// host faction (Pawn_GuestTracker.SetFaction forces the host faction
        /// of a slave to null), and its status element says Slave. It is a
        /// living member of the colony and is kept.
        /// </summary>
        private const string HumanPawnSlave =
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<thing Class=\"Pawn\">\r\n" +
            "\t\t\t\t\t\t\t<def>Human</def>\r\n" +
            "\t\t\t\t\t\t\t<id>Human406</id>\r\n" +
            "\t\t\t\t\t\t\t<map>0</map>\r\n" +
            "\t\t\t\t\t\t\t<faction>Faction_10</faction>\r\n" +
            "\t\t\t\t\t\t\t<kindDef>Slave</kindDef>\r\n" +
            "\t\t\t\t\t\t\t<gender>Female</gender>\r\n" +
            "\t\t\t\t\t\t\t<name Class=\"NameTriple\">\r\n" +
            "\t\t\t\t\t\t\t\t<first>Miel</first>\r\n" +
            "\t\t\t\t\t\t\t\t<nick>Mie</nick>\r\n" +
            "\t\t\t\t\t\t\t\t<last>Orind</last>\r\n" +
            "\t\t\t\t\t\t\t</name>\r\n" +
            "\t\t\t\t\t\t\t<guest>\r\n" +
            "\t\t\t\t\t\t\t\t<hostFaction>null</hostFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<slaveFaction>null</slaveFaction>\r\n" +
            "\t\t\t\t\t\t\t\t<guestStatus>Slave</guestStatus>\r\n" +
            "\t\t\t\t\t\t\t\t<joinStatus>JoinAsSlave</joinStatus>\r\n" +
            "\t\t\t\t\t\t\t\t<interactionMode>MaintainOnly</interactionMode>\r\n" +
            "\t\t\t\t\t\t\t</guest>\r\n" +
            "\t\t\t\t\t\t</thing>\r\n" +
            "\t\t\t\t\t</li>\r\n";

        /// <summary>
        /// A corpse on the map, in the measured shape: its inner pawn is a
        /// bare loadID reference, never a nested pawn thing, so the scan
        /// cannot match it (M6).
        /// </summary>
        private const string HumanCorpse =
            "\t\t\t\t\t<li>\r\n" +
            "\t\t\t\t\t\t<thing Class=\"Corpse\">\r\n" +
            "\t\t\t\t\t\t\t<def>Corpse_Human</def>\r\n" +
            "\t\t\t\t\t\t\t<id>Corpse_Human4621</id>\r\n" +
            "\t\t\t\t\t\t\t<map>0</map>\r\n" +
            "\t\t\t\t\t\t\t<pos>(66, 0, 209)</pos>\r\n" +
            "\t\t\t\t\t\t\t<timeOfDeath>-16993438</timeOfDeath>\r\n" +
            "\t\t\t\t\t\t\t<innerContainer>\r\n" +
            "\t\t\t\t\t\t\t\t<maxStacks>1</maxStacks>\r\n" +
            "\t\t\t\t\t\t\t\t<contentsLookMode>Reference</contentsLookMode>\r\n" +
            "\t\t\t\t\t\t\t\t<innerList>\r\n" +
            "\t\t\t\t\t\t\t\t\t<li>Thing_Human4616</li>\r\n" +
            "\t\t\t\t\t\t\t\t</innerList>\r\n" +
            "\t\t\t\t\t\t\t</innerContainer>\r\n" +
            "\t\t\t\t\t\t</thing>\r\n" +
            "\t\t\t\t\t</li>\r\n";

        /// <summary>
        /// A head that stops in the middle of the scenario block.
        /// </summary>
        private const string TruncatedHeadSave =
            HeadPrefix +
            "\t\t<scenario>\r\n" +
            "\t\t\t<name>Crashlanded</name>\r\n";

        private string WriteSaveFile(string name, string content, bool withBom = true)
        {
            string path = Path.Combine(_savesDir, name);
            File.WriteAllText(path, content, new UTF8Encoding(withBom));
            return path;
        }

        /// <summary>
        /// Builds a save whose map holds the given pawn blocks and whose head
        /// carries the faction list with the player faction.
        /// </summary>
        private static string BuildSaveWithPawns(params string[] pawns)
            => BuildSave(AllFactionsWithPlayer, pawns);

        /// <summary>
        /// Builds a save with the given faction list in its head whose map
        /// holds the given pawn blocks.
        /// </summary>
        private static string BuildSave(string factionList, params string[] pawns)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                   "<savegame>\r\n" +
                   "\t<game>\r\n" +
                   factionList +
                   "\t\t<maps>\r\n" +
                   "\t\t\t<map>\r\n" +
                   "\t\t\t\t<mapPawns>\r\n" +
                   string.Concat(pawns) +
                   "\t\t\t\t</mapPawns>\r\n" +
                   "\t\t\t</map>\r\n" +
                   "\t\t</maps>\r\n" +
                   "\t</game>\r\n" +
                   "</savegame>";
        }

        private string WriteGzipSaveFile(string name, string content)
        {
            string path = Path.Combine(_savesDir, name);
            using (var file = File.Create(path))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }

            return path;
        }

        #endregion

        #region ListSavesAsync

        [Fact]
        public async Task ListSavesAsync_ReturnsTopLevelRwsFilesNewestFirst()
        {
            // Arrange
            var service = CreateService();
            string oldest = WriteSaveFile("Colony.rws", PlainSaveWithSteamIds);
            string newest = WriteSaveFile("Settlement.rws", PlainSaveWithSteamIds);
            string middle = WriteSaveFile("Outpost.rws", PlainSaveWithSteamIds);
            File.SetLastWriteTimeUtc(oldest, new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(middle, new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newest, new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));

            // Not a save file.
            File.WriteAllText(Path.Combine(_savesDir, "notes.txt"), "ignore me");

            // Not a top level file.
            string subFolder = Path.Combine(_savesDir, "backup");
            Directory.CreateDirectory(subFolder);
            string nested = Path.Combine(subFolder, "Nested.rws");
            File.WriteAllText(nested, PlainSaveWithSteamIds);

            // Act
            var saves = await service.ListSavesAsync(TestContext.Current.CancellationToken);

            // Assert
            saves.Should().HaveCount(3);
            saves.Select(save => save.Name).Should().Equal("Settlement", "Outpost", "Colony");
            saves.All(save => save.Path.EndsWith(".rws", StringComparison.Ordinal)).Should().BeTrue();
            saves.Should().NotContain(save => save.Path == nested);
            saves[0].SizeBytes.Should().Be(new FileInfo(newest).Length);
            saves[0].LastModifiedUtc.Should().Be(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));
        }

        [Fact]
        public async Task ListSavesAsync_MissingSavesFolder_ReturnsEmptyList()
        {
            // Arrange
            var service = CreateService();
            _pathService.GetSavesPath().Returns(Path.Combine(_configDir, "NoSuchFolder"));

            // Act
            var saves = await service.ListSavesAsync(TestContext.Current.CancellationToken);

            // Assert
            saves.Should().BeEmpty();
        }

        #endregion

        #region ReadHeaderAsync

        [Fact]
        public async Task ReadHeaderAsync_PlainSaveWithBom_ParsesVersionAndMods()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Colony.rws", PlainSaveWithSteamIds);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().NotBeNull();
            header!.Name.Should().Be("Colony");
            header.Path.Should().Be(path);
            header.GameVersion.Should().Be("1.7.4990 rev1065");
            header.ModIds.Should().Equal("ludeon.rimworld", "test.mod.alpha", "test.mod.beta");
            header.ModNames.Should().Equal("RimWorld", "Test Mod Alpha", "Test Mod Beta");
        }

        [Fact]
        public async Task ReadHeaderAsync_ParallelSteamIds_KeepsAlignmentAndZeroEntries()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Colony.rws", PlainSaveWithSteamIds);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().NotBeNull();
            header!.ModSteamIds.Should().Equal("0", "2000000001", "2000000002");
            header.ModSteamIds.Should().HaveCount(header.ModIds.Count);
            header.ModSteamIds[0].Should().Be("0");
        }

        [Fact]
        public async Task ReadHeaderAsync_GzipSave_ParsesWithoutSteamIds()
        {
            // Arrange
            var service = CreateService();
            string path = WriteGzipSaveFile("OldColony.rws", PlainSaveWithoutSteamIds);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().NotBeNull();
            header!.GameVersion.Should().Be("1.5.1234 rev678");
            header.ModIds.Should().Equal("ludeon.rimworld", "test.mod.alpha");
            header.ModNames.Should().Equal("RimWorld", "Test Mod Alpha");
            header.ModSteamIds.Should().BeEmpty();
        }

        [Fact]
        public async Task ReadHeaderAsync_PlainSaveWithoutBom_Parses()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Colony.rws", PlainSaveWithSteamIds, withBom: false);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().NotBeNull();
            header!.GameVersion.Should().Be("1.7.4990 rev1065");
            header.ModIds.Should().HaveCount(3);
        }

        [Fact]
        public async Task ReadHeaderAsync_UnequalArrayLengths_KeepsListsIndependent()
        {
            // Arrange
            var service = CreateService();
            const string unevenSave =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<savegame>\r\n" +
                "\t<meta>\r\n" +
                "\t\t<gameVersion>1.7.4990 rev1065</gameVersion>\r\n" +
                "\t\t<modIds>\r\n" +
                "\t\t\t<li>test.mod.alpha</li>\r\n" +
                "\t\t\t<li>test.mod.beta</li>\r\n" +
                "\t\t</modIds>\r\n" +
                "\t\t<modSteamIds>\r\n" +
                "\t\t\t<li>2000000001</li>\r\n" +
                "\t\t</modSteamIds>\r\n" +
                "\t\t<modNames>\r\n" +
                "\t\t\t<li>Test Mod Alpha</li>\r\n" +
                "\t\t\t<li>Test Mod Beta</li>\r\n" +
                "\t\t\t<li>Test Mod Gamma</li>\r\n" +
                "\t\t</modNames>\r\n" +
                "\t</meta>\r\n" +
                "\t<game />\r\n" +
                "</savegame>";
            string path = WriteSaveFile("Uneven.rws", unevenSave);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().NotBeNull();
            header!.ModIds.Should().HaveCount(2);
            header.ModSteamIds.Should().HaveCount(1);
            header.ModNames.Should().HaveCount(3);
            header.ModSteamIds.Should().Equal("2000000001");
        }

        [Fact]
        public async Task ReadHeaderAsync_GarbageContent_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            string path = Path.Combine(_savesDir, "Garbage.rws");
            File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x7F, 0xFF, 0xFE, 0x42, 0x13 });

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().BeNull();
        }

        [Fact]
        public async Task ReadHeaderAsync_MissingFile_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            string path = Path.Combine(_savesDir, "DoesNotExist.rws");

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().BeNull();
        }

        [Fact]
        public async Task ReadHeaderAsync_TruncatedBeforeMetaEnd_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            const string truncatedSave =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<savegame>\r\n" +
                "\t<meta>\r\n" +
                "\t\t<gameVersion>1.7.4990 rev1065</gameVersion>\r\n" +
                "\t\t<modIds>\r\n" +
                "\t\t\t<li>test.mod.alpha</li>\r\n";
            string path = WriteSaveFile("Truncated.rws", truncatedSave);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().BeNull();
        }

        [Fact]
        public async Task ReadHeaderAsync_MetaExceedingCap_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            service.TestMetaCapBytes = 256;
            string path = WriteSaveFile("Colony.rws", PlainSaveWithSteamIds);

            // Act
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().BeNull();
        }

        [Fact]
        public async Task ReadHeaderAsync_LockedFile_ReturnsNull()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Skip("File locking semantics differ on this platform.");
            }

            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Locked.rws", PlainSaveWithSteamIds);

            // Act
            using var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            var header = await service.ReadHeaderAsync(path, TestContext.Current.CancellationToken);

            // Assert
            header.Should().BeNull();
        }

        [Fact]
        public async Task ReadHeaderAsync_PreCancelledToken_Throws()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Colony.rws", PlainSaveWithSteamIds);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            // Act
            Func<Task> act = async () => await service.ReadHeaderAsync(path, cancellation.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        #endregion

        #region Compare and Diff

        [Fact]
        public void Compare_EmptyModIds_ReturnsUnknown()
        {
            // Arrange
            var service = CreateService();
            var header = new SaveHeader("Colony", "C:\\saves\\Colony.rws", "1.7.4990",
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

            // Act
            var result = service.Compare(header, new[] { "test.mod.alpha" }, new[] { "test.mod.alpha" });

            // Assert
            result.Should().Be(SaveCompat.Unknown);
        }

        [Fact]
        public void Compare_SameMods_ReturnsCompatible()
        {
            // Arrange
            var service = CreateService();
            var header = new SaveHeader("Colony", "C:\\saves\\Colony.rws", "1.7.4990",
                new[] { "test.mod.alpha", "test.mod.beta" }, Array.Empty<string>(), Array.Empty<string>());

            // Act
            var result = service.Compare(header,
                new[] { "Test.Mod.Beta", "TEST.MOD.ALPHA" },
                new[] { "test.mod.alpha", "test.mod.beta", "test.mod.gamma" });

            // Assert
            result.Should().Be(SaveCompat.Compatible);
        }

        [Fact]
        public void Compare_DifferentMods_ReturnsChanged()
        {
            // Arrange
            var service = CreateService();
            var header = new SaveHeader("Colony", "C:\\saves\\Colony.rws", "1.7.4990",
                new[] { "test.mod.alpha" }, Array.Empty<string>(), Array.Empty<string>());

            // Act
            var result = service.Compare(header,
                new[] { "test.mod.alpha", "test.mod.beta" },
                new[] { "test.mod.alpha", "test.mod.beta" });

            // Assert
            result.Should().Be(SaveCompat.Changed);
        }

        [Fact]
        public void Compare_SaveModNotInstalled_ReturnsMissing()
        {
            // Arrange
            var service = CreateService();
            var header = new SaveHeader("Colony", "C:\\saves\\Colony.rws", "1.7.4990",
                new[] { "test.mod.alpha", "test.mod.removed" }, Array.Empty<string>(), Array.Empty<string>());

            // Act
            var result = service.Compare(header,
                new[] { "test.mod.alpha" },
                new[] { "test.mod.alpha" });

            // Assert
            result.Should().Be(SaveCompat.Missing);
        }

        [Fact]
        public void Diff_ReturnsSortedAddedAndRemoved()
        {
            // Arrange
            var service = CreateService();
            var header = new SaveHeader("Colony", "C:\\saves\\Colony.rws", "1.7.4990",
                new[] { "test.mod.alpha", "test.mod.beta" }, Array.Empty<string>(), Array.Empty<string>());

            // Act
            var (added, removed) = service.Diff(header, new[] { "test.mod.alpha", "test.mod.delta", "test.mod.gamma" });

            // Assert
            added.Should().Equal("test.mod.delta", "test.mod.gamma");
            removed.Should().Equal("test.mod.beta");
        }

        [Fact]
        public void Diff_IgnoresCase()
        {
            // Arrange
            var service = CreateService();
            var header = new SaveHeader("Colony", "C:\\saves\\Colony.rws", "1.7.4990",
                new[] { "Test.Mod.Alpha" }, Array.Empty<string>(), Array.Empty<string>());

            // Act
            var (added, removed) = service.Diff(header, new[] { "test.mod.alpha" });

            // Assert
            added.Should().BeEmpty();
            removed.Should().BeEmpty();
        }

        #endregion

        #region ReadDetailsAsync

        [Fact]
        public async Task ReadDetailsAsync_RealisticHead_ExtractsEveryField()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Realistic.rws", RealisticHeadSave);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().NotBeNull();
            details!.ScenarioName.Should().Be("Crashlanded");
            details.StorytellerDef.Should().Be("Cassandra");
            details.DifficultyDef.Should().Be("Rough");
            details.TicksGame.Should().Be(12674);
            details.GameStartAbsTick.Should().Be(1500000);
            details.StartingYear.Should().Be(5500);

            // The planet tile of the colony and the season the game reported.
            details.StartingTile.Should().Be("43378,0");
            details.ReportedSeason.Should().Be("Spring");

            // The world name and the seed sit behind the <world> marker.
            details.WorldName.Should().Be("Castor");
            details.WorldSeed.Should().Be("gray");

            // The scenario carries the story of the colony.
            details.PlayerFactionType.Should().Be("PlayerColony");
            details.ScenarioSummary.Should().Be("Three crashlanded survivors.");
            details.ScenarioDescription.Should().Be("The three of you awake in your cryptosleep sarcophagi.");
            details.IsPlanetMode.Should().BeTrue();

            // The DLC list is not read from the head window - the components
            // block of the fixture carries GameComponent_* entries of mods too.
            details.ActiveDlcNames.Should().BeEmpty();
        }

        [Fact]
        public async Task ReadDetailsAsync_WithoutTheNewElements_LeavesEveryNewFieldNull()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Bare.rws", BareHeadSave);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().NotBeNull();

            // Nothing is invented - a field the save does not carry stays empty.
            details!.WorldName.Should().BeNull();
            details.WorldSeed.Should().BeNull();
            details.PlayerFactionType.Should().BeNull();
            details.ScenarioSummary.Should().BeNull();
            details.ScenarioDescription.Should().BeNull();
            details.IsPlanetMode.Should().BeNull();

            // The fields that were there before are still read.
            details.ScenarioName.Should().Be("Crashlanded");
        }

        [Fact]
        public async Task ReadDetailsAsync_TheWorldInfoIsReadBehindTheWorldMarker()
        {
            // Arrange - the name and the seed of the world sit behind the
            // <world> marker, in the info block that follows it.
            var service = CreateService();
            string path = WriteSaveFile("World.rws", RealisticHeadSave);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().NotBeNull();
            details!.WorldName.Should().Be("Castor");
            details.WorldSeed.Should().Be("gray");
        }

        [Fact]
        public async Task ReadDetailsAsync_TheWorldInfoIsRead_HoweverBigItGrows()
        {
            // Arrange - the info block behind the marker grows: the read has
            // to follow it instead of stopping at a fixed budget.
            var service = CreateService();
            string filler = new string('x', 4096);
            string content =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<savegame>\r\n" +
                "\t<game>\r\n" +
                "\t\t<world>\r\n" +
                "\t\t\t<info>\r\n" +
                "\t\t\t\t<filler>" + filler + "</filler>\r\n" +
                "\t\t\t\t<name>Faraway</name>\r\n" +
                "\t\t\t\t<seedString>deep</seedString>\r\n" +
                "\t\t\t</info>\r\n" +
                "\t\t</world>\r\n" +
                "\t</game>\r\n" +
                "</savegame>";
            string path = WriteSaveFile("BigWorld.rws", content);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().NotBeNull();
            details!.WorldName.Should().Be("Faraway");
            details.WorldSeed.Should().Be("deep");
        }

        [Fact]
        public async Task ReadDetailsAsync_TheScenarioParts_SayWhetherTheColonyIsOnAPlanet()
        {
            // Arrange - a scenario without the planet layer part.
            var service = CreateService();
            string content =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<savegame>\r\n" +
                "\t<game>\r\n" +
                "\t\t<scenario>\r\n" +
                "\t\t\t<name>Crashlanded</name>\r\n" +
                "\t\t\t<parts>\r\n" +
                "\t\t\t\t<li Class=\"ScenPart_ConfigPage_ConfigureStartingPawns\">\r\n" +
                "\t\t\t\t\t<pawnCount>5</pawnCount>\r\n" +
                "\t\t\t\t</li>\r\n" +
                "\t\t\t</parts>\r\n" +
                "\t\t</scenario>\r\n" +
                "\t</game>\r\n" +
                "</savegame>";
            string path = WriteSaveFile("NoPlanet.rws", content);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().NotBeNull();

            // The parts are there - the planet layer is not.
            details!.IsPlanetMode.Should().BeFalse();
        }

        [Fact]
        public async Task ReadDetailsAsync_IgnoresTheGameComponentsOfTheHeadWindow()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Realistic.rws", RealisticHeadSave);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().NotBeNull();

            // Mods register GameComponents as well - they are no DLC signal.
            details!.ActiveDlcNames.Should().BeEmpty();
        }

        [Fact]
        public void ResolveActiveDlcNames_ListsTheOfficialDlcPackagesOnly()
        {
            // Act
            var names = ISaveHeaderService.ResolveActiveDlcNames(new[]
            {
                "ludeon.rimworld",
                "ludeon.rimworld.royalty",
                "some.mod",
                "ludeon.rimworld.anomaly"
            });

            // Assert - the base game is no DLC and unknown ids are ignored.
            names.Should().BeEquivalentTo(new[] { "Royalty", "Anomaly" });
        }

        [Fact]
        public void ResolveActiveDlcNames_IgnoresTheCaseOfThePackageIds()
        {
            // Act
            var names = ISaveHeaderService.ResolveActiveDlcNames(new[] { "Ludeon.RimWorld.Royalty" });

            // Assert
            names.Should().BeEquivalentTo(new[] { "Royalty" });
        }

        [Fact]
        public void ResolveActiveDlcNames_WithoutOfficialDlcPackages_IsEmpty()
        {
            // Act + Assert
            ISaveHeaderService.ResolveActiveDlcNames(Array.Empty<string>()).Should().BeEmpty();
            ISaveHeaderService.ResolveActiveDlcNames(new[] { "ludeon.rimworld" }).Should().BeEmpty();
        }

        [Fact]
        public async Task ReadDetailsAsync_HeadBiggerThanTheOldFixedCap_IsStillRead()
        {
            // Arrange - the head window is marker-defined: the read continues
            // until <world>, so a head far bigger than the old fixed 512 KB cap
            // is absorbed as well.
            var service = CreateService();
            string padding = new string('x', 600 * 1024);
            string content = HeadPrefix
                + "\t\t<padding>" + padding + "</padding>\r\n"
                + HeadSuffix;
            string path = WriteSaveFile("BigHead.rws", content);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().NotBeNull();
            details!.ScenarioName.Should().Be("Crashlanded");
            details.StartingYear.Should().Be(5500);
        }

        [Fact]
        public async Task ReadDetailsAsync_TruncatedFile_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Truncated.rws", TruncatedHeadSave);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().BeNull();
        }

        [Fact]
        public async Task ReadDetailsAsync_CapSmallerThanWorldPosition_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            service.TestDetailsCapBytes = 512;
            string path = WriteSaveFile("Realistic.rws", RealisticHeadSave);

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().BeNull();
        }

        [Fact]
        public async Task ReadDetailsAsync_GarbageContent_ReturnsNull()
        {
            // Arrange
            var service = CreateService();
            string path = Path.Combine(_savesDir, "Garbage.rws");
            File.WriteAllBytes(path, new byte[] { 0x00, 0x01, 0x02, 0x7F, 0xFF, 0xFE, 0x42, 0x13 });

            // Act
            var details = await service.ReadDetailsAsync(path, TestContext.Current.CancellationToken);

            // Assert
            details.Should().BeNull();
        }

        #endregion

        #region ReadColonistNamesAsync

        [Fact]
        public async Task ReadColonistNamesAsync_FindsTheHumanPawns()
        {
            // Arrange - a human pawn, a human pawn without a nick and a pawn of
            // another species.
            var service = CreateService();
            string path = WriteSaveFile("Pawns.rws",
                BuildSaveWithPawns(HumanPawnHayden, AnimalPawn, HumanPawnKelux));

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().HaveCount(2);
            colonists[0].First.Should().Be("Hayden");
            colonists[0].Nick.Should().Be("Hayhouse");
            colonists[0].Last.Should().Be("Simons");
            colonists[0].DisplayName.Should().Be("Hayden \"Hayhouse\" Simons");
            colonists[0].Gender.Should().Be("Male");

            // The pawn without a xenotype carries none.
            colonists[0].Xenotype.Should().BeNull();

            // The pawn without a nick is shown with what the save carries.
            colonists[1].First.Should().Be("Kelux");
            colonists[1].Nick.Should().BeNull();
            colonists[1].Last.Should().Be("Hybridge");
            colonists[1].DisplayName.Should().Be("Kelux Hybridge");

            // A pawn without a gender element is male: the scribe omits the
            // element when the value equals its default.
            colonists[1].Gender.Should().Be("Male");
        }

        [Fact]
        public async Task ReadColonistNamesAsync_ReadsTheNameWhosePartsAreAttributes()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Attributes.rws", BuildSaveWithPawns(HumanPawnTobias));

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().HaveCount(1);
            colonists[0].First.Should().Be("Tobias");
            colonists[0].Nick.Should().Be("Toby");
            colonists[0].Last.Should().Be("Ytterstad");
            colonists[0].DisplayName.Should().Be("Tobias \"Toby\" Ytterstad");
        }

        [Fact]
        public async Task ReadColonistNamesAsync_ReadsTheVanillaDataOfAPawn()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Full.rws", BuildSaveWithPawns(HumanPawnFull));

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().HaveCount(1);
            SaveColonist colonist = colonists[0];

            // The gender and the xenotype are read as the save records them.
            colonist.Gender.Should().Be("Female");
            colonist.Xenotype.Should().Be("Baseliner");

            // The ages: 97 421 354 ticks are 27.061 years. Only the biological
            // age is read - the chronological one is the game's internal
            // logic only.
            colonist.AgeBiologicalYears.Should().NotBeNull();
            colonist.AgeBiologicalYears!.Value.Should().BeApproximately(27.061, 0.001);

            // Only the vanilla trait is kept - the mod trait and the gene
            // derived one are dropped.
            colonist.Traits.Should().Equal("Kind");

            // Only the vanilla skills are kept. The level the save omits is 0,
            // the passion the save omits is none.
            colonist.Skills.Should().HaveCount(3);
            colonist.Skills[0].Def.Should().Be("Shooting");
            colonist.Skills[0].Level.Should().Be(11);
            colonist.Skills[0].Passion.Should().Be("Major");
            colonist.Skills[1].Def.Should().Be("Melee");
            colonist.Skills[1].Level.Should().Be(13);
            colonist.Skills[1].Passion.Should().Be("Minor");
            colonist.Skills[2].Def.Should().Be("Plants");
            colonist.Skills[2].Level.Should().Be(0);
            colonist.Skills[2].Passion.Should().BeNull();

            // Only the vanilla conditions are kept - the mod one is dropped.
            colonist.Conditions.Should().HaveCount(2);
            colonist.Conditions[0].Def.Should().Be("Stab");
            colonist.Conditions[0].Severity.Should().Be(3);
            colonist.Conditions[0].Class.Should().Be("Hediff_Injury");
            colonist.Conditions[0].PainCategory.Should().Be("LowPain");
            colonist.Conditions[1].Def.Should().Be("BadBack");
            colonist.Conditions[1].Severity.Should().BeApproximately(0.33, 0.0001);
            colonist.Conditions[1].Class.Should().Be("HediffWithComps");
            colonist.Conditions[1].PainCategory.Should().BeNull();
        }

        [Fact]
        public async Task ReadColonistNamesAsync_WithoutHumanPawns_ReturnsAnEmptyList()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("NoHumans.rws", BuildSaveWithPawns(AnimalPawn));

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().BeEmpty();
        }

        [Fact]
        public async Task ReadColonistNamesAsync_MissingFile_ReturnsAnEmptyList()
        {
            // Arrange
            var service = CreateService();
            string path = Path.Combine(_savesDir, "DoesNotExist.rws");

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().BeEmpty();
        }

        [Fact]
        public async Task ReadColonistNamesAsync_PreCancelledToken_Throws()
        {
            // Arrange
            var service = CreateService();
            string path = WriteSaveFile("Pawns.rws", BuildSaveWithPawns(HumanPawnHayden));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            // Act
            Func<Task> act = async () => await service.ReadColonistNamesAsync(path, cancellation.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task ReadColonistNamesAsync_FindsTheHumansOfABigFile()
        {
            // Arrange - the humans sit at the very end of the file: the scan
            // has to read the whole file to find them.
            var service = CreateService();
            string filler = new string('x', 3 * 1024 * 1024);
            string content =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<savegame>\r\n" +
                "\t<game>\r\n" +
                AllFactionsWithPlayer +
                "\t\t<maps>\r\n" +
                "\t\t\t<map>\r\n" +
                "\t\t\t\t<mapPawns>\r\n" +
                filler +
                HumanPawnHayden +
                "\t\t\t\t</mapPawns>\r\n" +
                "\t\t\t</map>\r\n" +
                "\t\t</maps>\r\n" +
                "\t</game>\r\n" +
                "</savegame>";
            string path = WriteSaveFile("Big.rws", content);

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().HaveCount(1);
            colonists[0].DisplayName.Should().Be("Hayden \"Hayhouse\" Simons");
        }

        [Fact]
        public async Task ReadColonistNamesAsync_KeepsOnlyTheLivingMembersOfTheColony()
        {
            // Arrange - a colonist, a slave, a raider, a visitor, a prisoner,
            // a colonist that raiders hold, a prisoner that carries the faction
            // of the colony, a pawn of another species and a corpse. Only the
            // colonist and the slave are living members of the colony.
            var service = CreateService();
            string path = WriteSaveFile("Colony.rws", BuildSaveWithPawns(
                HumanPawnHayden, HumanPawnSlave, HumanPawnRaider, HumanPawnGuest,
                HumanPawnPrisoner, HumanPawnHeldColonist, HumanPawnPrisonerOfTheColony,
                AnimalPawn, HumanCorpse));

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().HaveCount(2);
            colonists[0].DisplayName.Should().Be("Hayden \"Hayhouse\" Simons");
            colonists[1].DisplayName.Should().Be("Miel \"Mie\" Orind");
        }

        [Fact]
        public async Task ReadColonistNamesAsync_KeepsTheSlaves()
        {
            // Arrange - a slave carries the faction of the colony, no host
            // faction and the status Slave.
            var service = CreateService();
            string path = WriteSaveFile("Slave.rws", BuildSaveWithPawns(HumanPawnSlave));

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().HaveCount(1);
            colonists[0].DisplayName.Should().Be("Miel \"Mie\" Orind");
        }

        [Fact]
        public async Task ReadColonistNamesAsync_DropsThePawnsOfAnotherFaction()
        {
            // Arrange - a raider and a visitor: both keep their own faction.
            var service = CreateService();
            string path = WriteSaveFile("Foreigners.rws",
                BuildSaveWithPawns(HumanPawnRaider, HumanPawnGuest));

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().BeEmpty();
        }

        [Fact]
        public async Task ReadColonistNamesAsync_DropsThePawnsTheColonyHolds()
        {
            // Arrange - a prisoner, a colonist that raiders hold and a prisoner
            // that carries the faction of the colony: all of them are held by
            // another faction or are prisoners.
            var service = CreateService();
            string path = WriteSaveFile("Held.rws", BuildSaveWithPawns(
                HumanPawnPrisoner, HumanPawnHeldColonist, HumanPawnPrisonerOfTheColony));

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().BeEmpty();
        }

        [Fact]
        public async Task ReadColonistNamesAsync_DoesNotMatchACorpse()
        {
            // Arrange - the inner pawn of a corpse is a loadID reference, never
            // a nested pawn thing (M6).
            var service = CreateService();
            string path = WriteSaveFile("Corpse.rws", BuildSaveWithPawns(HumanCorpse));

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().BeEmpty();
        }

        [Fact]
        public async Task ReadColonistNamesAsync_WithoutAPlayerFaction_ListsNobody()
        {
            // Arrange - the faction list carries no PlayerColony and no
            // PlayerTribe, so no pawn can be classified. The honesty policy
            // holds: an unclassifiable save lists nobody.
            var service = CreateService();
            string path = WriteSaveFile("NoPlayerFaction.rws",
                BuildSave(AllFactionsWithoutPlayer, HumanPawnHayden, HumanPawnSlave));

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().BeEmpty();
        }

        [Fact]
        public async Task ReadColonistNamesAsync_WithoutAFactionList_ListsNobody()
        {
            // Arrange - a save whose head carries no faction list at all.
            var service = CreateService();
            string path = WriteSaveFile("NoFactionList.rws",
                BuildSave(string.Empty, HumanPawnHayden, HumanPawnSlave));

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().BeEmpty();
        }

        [Fact]
        public async Task ReadColonistNamesAsync_FindsTheColonyWhenTheFactionListSitsBehindAChunkBoundary()
        {
            // Arrange - the faction list sits at ~1 MB, so the pre-pass reads
            // more than one chunk and the "<allFactions>" marker itself is
            // split by the chunk boundary (the scan chunk is 1 MB). The file
            // starts with the three bytes of the BOM.
            var service = CreateService();
            string head = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                          "<savegame>\r\n" +
                          "\t<game>\r\n";
            string filler = new string('x', 1024 * 1024 - head.Length - 3 - 6);
            string content =
                head +
                filler +
                AllFactionsWithPlayer +
                "\t\t<maps>\r\n" +
                "\t\t\t<map>\r\n" +
                "\t\t\t\t<mapPawns>\r\n" +
                HumanPawnHayden +
                "\t\t\t\t</mapPawns>\r\n" +
                "\t\t\t</map>\r\n" +
                "\t\t</maps>\r\n" +
                "\t</game>\r\n" +
                "</savegame>";
            string path = WriteSaveFile("Boundary.rws", content);

            // Act
            var colonists = await service.ReadColonistNamesAsync(path, TestContext.Current.CancellationToken);

            // Assert
            colonists.Should().HaveCount(1);
            colonists[0].DisplayName.Should().Be("Hayden \"Hayhouse\" Simons");
        }

        #endregion

        #region FormatInGameDate

        [Fact]
        public void FormatInGameDate_FormatsTheDocumentedExample()
        {
            // Act - 1 512 674 ticks: the fifth twelfth (Jugust), the 11th day
            // of the quadrum, 05:04 (the game's own calendar math).
            string? formatted = ISaveHeaderService.FormatInGameDate(12674, 1500000, 5500);

            // Assert
            formatted.Should().Be("Year 5500, Jugust, day 11, 05:04");
        }

        [Fact]
        public void FormatInGameDate_RollsOverIntoTheNextQuadrum()
        {
            // Act - the twelfth of a quadrum is 300 000 ticks long.
            string? lastDayOfAprimay = ISaveHeaderService.FormatInGameDate(899999, 0, 5500);
            string? firstDayOfJugust = ISaveHeaderService.FormatInGameDate(900000, 0, 5500);

            // Assert
            lastDayOfAprimay.Should().Be("Year 5500, Aprimay, day 15, 23:59");
            firstDayOfJugust.Should().Be("Year 5500, Jugust, day 1, 00:00");
        }

        [Fact]
        public void FormatInGameDate_TheYearCarriesIntoTheNextOne()
        {
            // Act - the first minute of the second year (the game counts the
            // twelfths of the year, not the seasons).
            string? firstOfSecondYear = ISaveHeaderService.FormatInGameDate(3_600_000, 0, 5500);

            // and the last minute of the first year
            string? lastOfFirstYear = ISaveHeaderService.FormatInGameDate(3_599_999, 0, 5500);

            // Assert
            firstOfSecondYear.Should().Be("Year 5501, Aprimay, day 1, 00:00");
            lastOfFirstYear.Should().Be("Year 5500, Decembary, day 15, 23:59");
        }

        [Fact]
        public void FormatInGameDate_NamesTheFourQuadrumsOfTheYear()
        {
            // Act - the first twelfth of each quadrum of the year.
            string? aprimay = ISaveHeaderService.FormatInGameDate(0, 0, 5501);
            string? jugust = ISaveHeaderService.FormatInGameDate(900_000, 0, 5501);
            string? septober = ISaveHeaderService.FormatInGameDate(1_800_000, 0, 5501);
            string? decembary = ISaveHeaderService.FormatInGameDate(2_700_000, 0, 5501);

            // Assert - the names of the game's own Quadrum enum.
            aprimay.Should().Be("Year 5501, Aprimay, day 1, 00:00");
            jugust.Should().Be("Year 5501, Jugust, day 1, 00:00");
            septober.Should().Be("Year 5501, Septober, day 1, 00:00");
            decembary.Should().Be("Year 5501, Decembary, day 1, 00:00");
        }

        [Fact]
        public void FormatInGameDate_ShowsTheSeasonTheGameReported()
        {
            // Act - the season is a climate value: the save carries the game's
            // own answer (the reported season of the date notifier).
            string? withSeason = ISaveHeaderService.FormatInGameDate(12674, 1500000, 5500,
                startingTile: "43378,0", reportedSeason: "PermanentSummer");
            string? withoutSeason = ISaveHeaderService.FormatInGameDate(12674, 1500000, 5500,
                startingTile: "43378,0");

            // Assert
            withSeason.Should().Be("Year 5500, Jugust, day 11, 05:04, Permanent Summer");
            withoutSeason.Should().Be("Year 5500, Jugust, day 11, 05:04");
        }

        [Fact]
        public void FormatInGameDate_At_TheLongitudeShiftsTheLocalTicks()
        {
            // Act - the tile of the colony cannot be resolved by a reader, so
            // the date falls back to the absolute ticks. A known longitude
            // shifts them: 180 degrees are 12 time zones, 12 hours.
            string? withoutShift = ISaveHeaderService.FormatInGameDate(100, 0, 5500);
            string? withShift = ISaveHeaderService.FormatInGameDateAt(100, 0, 5500, longitude: 180f);

            // Assert - the shift is 12 hours (180 degrees are 12 time zones).
            withoutShift.Should().Be("Year 5500, Aprimay, day 1, 00:02");
            withShift.Should().Be("Year 5500, Aprimay, day 1, 12:02");
        }

        [Fact]
        public void FormatInGameDate_TheSameForTheTileShapesOfBothGameVersions()
        {
            // Act - 1.7 writes a planet tile ("tileId,layerId"), 1.5 a plain
            // tile id. Neither of them carries a position, so the date is the
            // same: the calendar and the longitude shift are identical in both
            // versions (1.5: RimWorld/GenDate.cs).
            string? withPlanetTile = ISaveHeaderService.FormatInGameDate(12674, 1500000, 5500,
                startingTile: "43378,0");
            string? withPlainTile = ISaveHeaderService.FormatInGameDate(12674, 1500000, 5500,
                startingTile: "43378");
            string? withoutTile = ISaveHeaderService.FormatInGameDate(12674, 1500000, 5500);

            // Assert
            withPlanetTile.Should().Be("Year 5500, Jugust, day 11, 05:04");
            withPlainTile.Should().Be(withPlanetTile);
            withoutTile.Should().Be(withPlanetTile);
        }

        [Fact]
        public void FormatInGameDate_MissingOrEmptyInput_ReturnsNull()
        {
            // Act
            string? missingTicks = ISaveHeaderService.FormatInGameDate(null, 1500000, 5500);
            string? missingStart = ISaveHeaderService.FormatInGameDate(12674, null, 5500);
            string? missingYear = ISaveHeaderService.FormatInGameDate(12674, 1500000, null);
            string? everythingZero = ISaveHeaderService.FormatInGameDate(0, 0, 0);

            // Assert
            missingTicks.Should().BeNull();
            missingStart.Should().BeNull();
            missingYear.Should().BeNull();
            everythingZero.Should().BeNull();
        }

        #endregion

        #region GetSavesPath

        [Fact]
        public void GetSavesPath_IsTheSavesSiblingFolderBesideTheConfigFolder()
        {
            // Arrange
            var configService = Substitute.For<IConfigService>();
            configService.GetConfigValue("config_folder").Returns(_configDir);
            var pathService = new PathService(configService);

            // Act
            string savesPath = pathService.GetSavesPath();

            // Assert
            savesPath.Should().Be(Path.Combine(
                Path.GetDirectoryName(_configDir)!, "Saves"));
        }

        #endregion
    }
}
