#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using RimSharp.AppDir.AppFiles;
using RimSharp.Features.LogViewer.Services;

namespace RimSharp.Features.LogViewer.ViewModels
{
    /// <summary>
    /// The report tabs' state: troubleshooter findings and the summary. Rebuilt
    /// from the loaded log content whenever a log is read; the reports are
    /// derived from the log's own lines, so neither the display filter nor the
    /// repeat-collapse toggle changes what they show.
    /// </summary>
    public class LogDiagnosticsViewModel : ViewModelBase
    {
        private string _issueDetail = "Select an issue to see what it means and what to try.";
        private string _issueDetailTitle = string.Empty;
        private string _scopeNote = string.Empty;
        private string _summaryFormat = string.Empty;
        private string _summaryVersion = string.Empty;
        private bool _hasContent;

        public LogDiagnosticsViewModel()
        {
            Issues.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IssueSummaryText));
        }

        /// <summary>Findings, most severe and most frequent first.</summary>
        public ObservableCollection<LogDiagnosticIssue> Issues { get; } = new();

        /// <summary>Key/value rows for the summary tab.</summary>
        public ObservableCollection<string> SummaryRows { get; } = new();

        public bool HasContent
        {
            get => _hasContent;
            private set => SetProperty(ref _hasContent, value);
        }

        public bool HasIssues => Issues.Count > 0;

        public string IssueSummaryText => HasIssues
            ? $"{Issues.Count} distinct issue signatures ({Issues.Count(i => i.Severity == LogDiagnosticSeverity.Error)} error, " +
              $"{Issues.Count(i => i.Severity == LogDiagnosticSeverity.Warning)} warning, " +
              $"{Issues.Count(i => i.Severity == LogDiagnosticSeverity.Info)} info)"
            : "No known issues detected in this log.";

        public string IssueDetail
        {
            get => _issueDetail;
            private set => SetProperty(ref _issueDetail, value);
        }

        public string IssueDetailTitle
        {
            get => _issueDetailTitle;
            private set => SetProperty(ref _issueDetailTitle, value);
        }

        /// <summary>How much of the file the reports were able to analyze.</summary>
        public string ScopeNote
        {
            get => _scopeNote;
            private set => SetProperty(ref _scopeNote, value);
        }

        public string SummaryFormat
        {
            get => _summaryFormat;
            private set => SetProperty(ref _summaryFormat, value);
        }

        public string SummaryVersion
        {
            get => _summaryVersion;
            private set => SetProperty(ref _summaryVersion, value);
        }

        private LogDiagnosticIssue? _selectedIssue;
        public LogDiagnosticIssue? SelectedIssue
        {
            get => _selectedIssue;
            set
            {
                if (!SetProperty(ref _selectedIssue, value)) return;
                ApplyIssueDetail();
            }
        }

        /// <summary>
        /// Rebuilds both reports from the current content. The installed game
        /// version settles the engine generation when the content does not, and
        /// <paramref name="stats"/> carries the viewer's own message counters
        /// (the numbers the filter buttons used to show).
        /// </summary>
        public void Rebuild(
            IReadOnlyList<LogDiagnosticLine> lines,
            string? majorGameVersion,
            string? scopeNote = null,
            LogStats? stats = null,
            HugsLogEnvelope? envelope = null)
        {
            var format = LogFormatResolver.Resolve(
                lines.Select(l => l.Text).ToList(),
                majorGameVersion);
            var summary = LogSummaryAnalyzer.Analyze(lines, format);

            FillIssues(summary.Issues);
            FillSummary(summary, format, scopeNote ?? "Whole log analyzed.", stats, envelope);

            HasContent = lines.Count > 0;
            SelectedIssue = Issues.FirstOrDefault();
        }

        /// <summary>
        /// Clears both reports — used when an invalid file is being explained,
        /// so the tabs show nothing rather than the previous log's findings.
        /// </summary>
        public void Reset()
        {
            Issues.Clear();
            SummaryRows.Clear();
            HasContent = false;
            ScopeNote = string.Empty;
            SummaryFormat = string.Empty;
            SummaryVersion = string.Empty;
            SelectedIssue = null;
            OnPropertyChanged(nameof(HasIssues));
            OnPropertyChanged(nameof(IssueSummaryText));
        }

        private void FillIssues(IReadOnlyList<LogDiagnosticIssue> issues)
        {
            Issues.Clear();
            foreach (var issue in issues)
            {
                Issues.Add(issue);
            }

            OnPropertyChanged(nameof(HasIssues));
            OnPropertyChanged(nameof(IssueSummaryText));
        }

        private void FillSummary(
            LogSummaryReport summary,
            LogFileFormat format,
            string scopeNote,
            LogStats? stats,
            HugsLogEnvelope? envelope)
        {
            SummaryFormat = LogFormatResolver.Label(format);
            SummaryVersion = summary.GameVersion.Length > 0 && summary.GameRevision.Length > 0
                ? $"{summary.GameVersion} rev {summary.GameRevision}"
                : summary.GameVersion.Length > 0 ? summary.GameVersion : "not reported by this log";

            ScopeNote = scopeNote;

            var f = (int value) => value.ToString("N0", CultureInfo.CurrentCulture);
            SummaryRows.Clear();
            SummaryRows.Add($"Log format: {SummaryFormat}");
            SummaryRows.Add($"Game version: {SummaryVersion}");
            SummaryRows.Add($"Session started: {(summary.SessionStart.HasValue ? summary.SessionStart.Value.ToString("G", CultureInfo.CurrentCulture) : "not reported")}");
            SummaryRows.Add($"Last entry: {(summary.LastEntryTime.HasValue ? summary.LastEntryTime.Value.ToString("G", CultureInfo.CurrentCulture) : "not reported")}");
            SummaryRows.Add($"Lines scanned: {f(summary.TotalLines)}");
            SummaryRows.Add($"Mods listed: {f(envelope?.Mods.Count > 0 ? envelope.Mods.Count : summary.ModsListed)}");

            if (stats != null)
            {
                // The viewer's own counts over the messages it lists — the same
                // numbers the filter buttons used to show.
                SummaryRows.Add($"Messages: {f(stats.TotalLines)} ({f(stats.Infos)} info, {f(stats.AllIssues)} with issues)");
                SummaryRows.Add($"Errors: {f(stats.Errors)}   Exceptions: {f(stats.Exceptions)}   Warnings: {f(stats.Warnings)}");
                SummaryRows.Add($"Mod issues: {f(stats.ModIssues)}   Keybinds: {f(stats.Keybinds)}");
            }

            // Line-level severity, which is a different basis from the message
            // counts above (a repeated exception collapses to one message but
            // keeps every one of its lines).
            SummaryRows.Add($"Error lines: {f(summary.ErrorCount)}   Warning lines: {f(summary.WarningCount)}");
            SummaryRows.Add($"Severity by: {summary.SeverityBasis}");

            foreach (var memory in summary.Memory)
            {
                SummaryRows.Add($"{memory.Name}: {memory.DisplaySize} ({memory.Source})");
            }

            AddEnvelopeRows(f, envelope);

            SummaryRows.Add($"Issue signatures: {f(summary.Issues.Count)}");
            SummaryRows.Add(scopeNote);
        }

        /// <summary>
        /// The shared-export block: what the publisher attached in front of the
        /// log and what it admits doing to it. The Harmony version map is the
        /// load-bearing part — every Harmony build in that list was loaded into
        /// the session by the mods carrying it, and old copies beside the
        /// current one are a known source of "works for some, breaks for others".
        /// </summary>
        private void AddEnvelopeRows(Func<int, string> f, HugsLogEnvelope? envelope)
        {
            if (envelope == null) return;

            SummaryRows.Add($"Shared export: uploaded {(envelope.UploadedOn.HasValue ? envelope.UploadedOn.Value.ToString("G", CultureInfo.CurrentCulture) : envelope.UploadedOnText ?? "time not reported")}");
            SummaryRows.Add($"Mod list source: the sharer's publisher roster ({f(envelope.Mods.Count)} mods, with versions and assemblies)");

            if (envelope.HarmonyVersions.Count > 0)
            {
                SummaryRows.Add($"Harmony builds: {string.Join(", ", envelope.HarmonyVersions.Select(v => $"{v.Version} ({f(v.Owners.Count)} mods)"))}");
            }

            if (envelope.PatchMethods.Count > 0)
            {
                SummaryRows.Add($"Patched methods: {f(envelope.PatchMethods.Count)} (from the sharer's patch inventory)");
            }

            if (envelope.HarmonyVersions.Count > 1)
            {
                SummaryRows.Add($"Note: {envelope.HarmonyVersions.Count} different Harmony builds are loaded at once — mods carrying old copies can miss fixes or re-break patched methods");
            }

            if (!string.IsNullOrEmpty(envelope.PlatformInfo))
            {
                SummaryRows.Add($"Publisher platform info: {envelope.PlatformInfo}");
            }

            if (envelope.UploadedPreviousLaunch)
            {
                SummaryRows.Add("The uploaded log is from the previous game launch (Player-prev.log), not the session the sharer was in.");
            }

            if (envelope.BodyTrimmed)
            {
                SummaryRows.Add("The publisher trimmed this log at its 10,000-line cap — the end of that session is missing from this file.");
            }

            if (envelope.PathsScrubbed)
            {
                SummaryRows.Add("The publisher scrubbed local paths ([Rimworld_dir]/[Home_dir]) — file paths in this log are redacted.");
            }
        }

        private void ApplyIssueDetail()
        {
            if (_selectedIssue == null)
            {
                IssueDetailTitle = string.Empty;
                IssueDetail = "Select an issue to see what it means and what to try.";
                return;
            }

            var issue = _selectedIssue;
            IssueDetailTitle = issue.Title;
            IssueDetail =
                $"Severity: {LogDiagnosticPatterns.SeverityLabel(issue.Severity)}\n" +
                $"Occurrences: {issue.Count.ToString("N0", CultureInfo.CurrentCulture)}\n" +
                $"First at line {issue.FirstLineNumber.ToString("N0", CultureInfo.CurrentCulture)} of the log file\n\n" +
                $"What it means\n{issue.Description}\n\n" +
                $"What to try\n{issue.Suggestion}";
        }
    }
}
