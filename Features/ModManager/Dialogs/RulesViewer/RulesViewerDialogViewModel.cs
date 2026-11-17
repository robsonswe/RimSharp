#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using RimSharp.AppDir.Dialogs;
using RimSharp.AppDir.AppFiles;
using RimSharp.Shared.Models;
using RimSharp.Shared.Services.Contracts;
using RimSharp.Infrastructure.Mods.Rules;

namespace RimSharp.Features.ModManager.Dialogs.RulesViewer
{
    /// <summary>
    /// One rule row shown in the read-only Rules viewer dialog, labelled with
    /// every origin that claimed it (About.xml, Custom, and/or a rule source).
    /// </summary>
    public class RuleRowViewModel : ViewModelBase
    {
        public string Kind { get; } // "Load Before", "Load After", "Incompatibility", "Load Top", "Load Bottom", "Supported Versions"
        public string Target { get; } = string.Empty;
        public string? Name { get; }
        public string? Comment { get; }
        public bool Hard { get; }
        public ObservableCollection<string> Origins { get; } = new();

        public RuleRowViewModel(string kind, string target, string? name = null, string? comment = null, bool hard = false)
        {
            Kind = kind;
            Target = target;
            Name = name;
            Comment = comment;
            Hard = hard;
        }
    }

    public class RulesViewerDialogViewModel : DialogViewModelBase<bool>
    {
        private readonly IModService _modService;
        private readonly ModRuleSourceRegistry _registry;
        private ModCustomInfo? _customInfo;

        public RulesViewerDialogViewModel(
            ModItem mod,
            IModService modService,
            ModRuleSourceRegistry registry)
            : base($"Rules — {mod.DisplayName}")
        {
            _modService = modService ?? throw new ArgumentNullException(nameof(modService));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Mod = mod ?? throw new ArgumentNullException(nameof(mod));

            BuildRows();
        }

        public ModItem Mod { get; }

        public ObservableCollection<RuleRowViewModel> Rows { get; } = new();

        public bool HasRules => Rows.Count > 0;

        private void BuildRows()
        {
            Rows.Clear();

            // --- Load Before ---
            foreach (var target in Mod.LoadBefore)
            {
                AddRow("Load Before", target, OriginsForLoadBefore(target));
            }

            // --- Load After ---
            foreach (var target in Mod.LoadAfter)
            {
                AddRow("Load After", target, OriginsForLoadAfter(target));
            }

            // --- Incompatibilities ---
            foreach (var kvp in Mod.IncompatibleWith)
            {
                var target = kvp.Key;
                var rule = kvp.Value;
                AddRow("Incompatibility", target, OriginsForIncompatibility(target),
                    rule.Name?.FirstOrDefault(), CommentText(rule.Comment), rule.HardIncompatibility);
            }

            // --- Load Top ---
            if (Mod.LoadTop)
            {
                var origins = new List<string>();
                if (CustomInfo.LoadTop != null) origins.Add("Custom");
                if (!string.IsNullOrEmpty(Mod.DatabaseLoadTopSource)) origins.Add(SourceLabel(Mod.DatabaseLoadTopSource));
                AddRow("Load Top", "(this mod)", origins);
            }

            // --- Load Bottom ---
            if (Mod.LoadBottom)
            {
                var origins = new List<string>();
                if (CustomInfo.LoadBottom != null) origins.Add("Custom");
                if (!string.IsNullOrEmpty(Mod.DatabaseLoadBottomSource)) origins.Add(SourceLabel(Mod.DatabaseLoadBottomSource));
                AddRow("Load Bottom", "(this mod)", origins);
            }

            // --- Supported Versions (legacy) ---
            if (Mod.SupportedVersions != null && Mod.SupportedVersions.Any())
            {
                var versionOrigins = new List<string>();
                foreach (var source in Mod.SupportedVersions.Select(v => v.Source).Distinct())
                {
                    switch (source)
                    {
                        case VersionSource.Official: versionOrigins.Add("Official"); break;
                        case VersionSource.Mlie: versionOrigins.Add("Mlie"); break;
                        case VersionSource.Custom: versionOrigins.Add("Custom"); break;
                        // Database-sourced supported versions are no longer surfaced
                        // here: version suppression moved to its own data source.
                    }
                }
                if (versionOrigins.Count == 0) versionOrigins.Add("Unknown");
                AddRow("Supported Versions", string.Join(", ", Mod.SupportedVersions.Select(v => v.Version)), versionOrigins);
            }
        }

        private void AddRow(string kind, string target, List<string> origins, string? name = null, string? comment = null, bool hard = false)
        {
            if (origins.Count == 0) origins.Add("Unknown");
            var row = new RuleRowViewModel(kind, target, name, comment, hard);
            foreach (var o in origins) row.Origins.Add(o);
            Rows.Add(row);
        }

        private List<string> OriginsForLoadBefore(string target)
        {
            var origins = new List<string>();
            if (Mod.AboutLoadBefore.Contains(target)) origins.Add("Official");
            if (Mod.DatabaseLoadBefore.TryGetValue(target, out var dbSrc)) origins.Add(SourceLabel(dbSrc));
            if (CustomInfo.LoadBefore.ContainsKey(target)) origins.Add("Custom");
            return origins;
        }

        private List<string> OriginsForLoadAfter(string target)
        {
            var origins = new List<string>();
            if (Mod.AboutLoadAfter.Contains(target)) origins.Add("Official");
            if (Mod.DatabaseLoadAfter.TryGetValue(target, out var dbSrc)) origins.Add(SourceLabel(dbSrc));
            if (CustomInfo.LoadAfter.ContainsKey(target)) origins.Add("Custom");
            return origins;
        }

        private List<string> OriginsForIncompatibility(string target)
        {
            var origins = new List<string>();
            if (Mod.AboutIncompatibleWith.Contains(target)) origins.Add("Official");
            if (Mod.DatabaseIncompatibleWith.TryGetValue(target, out var dbSrc)) origins.Add(SourceLabel(dbSrc));
            if (CustomInfo.IncompatibleWith.ContainsKey(target)) origins.Add("Custom");
            return origins;
        }

        private string SourceLabel(string sourceId) =>
            RuleProvenanceLabels.GetSourceDisplayName(sourceId, _registry);

        private ModCustomInfo CustomInfo => _customInfo ??= _modService.GetCustomModInfo(Mod.PackageId) ?? new ModCustomInfo();

        private static string? CommentText(List<string>? comments) =>
            comments != null && comments.Count > 0 ? string.Join(" ", comments) : null;
    }
}
