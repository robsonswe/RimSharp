#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Input;
using RimSharp.AppDir.AppFiles;
using RimSharp.Core.Commands.Base;
using RimSharp.Shared.Services.Contracts;
using ReactiveUI;

namespace RimSharp.Features.ModManager.Dialogs.AIIncompatibilityReview
{
    /// <summary>
    /// Walks the caller through AI-suggested indirect incompatibilities,
    /// one pair at a time, with Previous/Next navigation and per-pair
    /// confirm (hard/soft) or decline actions.
    /// </summary>
    public class AIIncompatibilityReviewDialogViewModel : RimSharp.AppDir.Dialogs.DialogViewModelBase<bool>
    {
        private readonly IReadOnlyList<AIIncompatibilityCandidate> _candidates;
        private readonly Func<AIIncompatibilityCandidate, Task> _applyCandidateAsync;

        private int _currentIndex;
        private string _pairHeading = string.Empty;
        private string _summaryLine = string.Empty;
        private string _reasonLine = string.Empty;
        private bool _isBusy;

        public AIIncompatibilityReviewDialogViewModel(
            IReadOnlyList<AIIncompatibilityCandidate> candidates,
            Func<AIIncompatibilityCandidate, Task> applyCandidateAsync)
            : base("AI Incompatibility Mini-Review")
        {
            _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            _applyCandidateAsync = applyCandidateAsync ?? throw new ArgumentNullException(nameof(applyCandidateAsync));

            PreviousCommand = CreateCommand(NavigatePrevious, () => CurrentIndex > 0);
            NextCommand = CreateCommand(NavigateNext, () => CurrentIndex < _candidates.Count - 1, nameof(CurrentIndex));
            ConfirmHardCommand = CreateCommand(() => ConfirmAndAdvanceAsync(RimSharp.Shared.Services.Contracts.AIIncompatibilityType.Hard), () => !IsBusy);
            ConfirmSoftCommand = CreateCommand(() => ConfirmAndAdvanceAsync(RimSharp.Shared.Services.Contracts.AIIncompatibilityType.Soft), () => !IsBusy);
            DeclineCommand = CreateCommand(DeclineAndAdvance, () => !IsBusy);
            CancelCommand = CreateCommand(() => CloseDialog(false));

            _currentIndex = 0;
            RaiseCandidateChanged();
        }

        public IReadOnlyList<AIIncompatibilityCandidate> Candidates => _candidates;
        public int CurrentIndex { get => _currentIndex; private set { if (SetProperty(ref _currentIndex, value)) RaiseCandidateChanged(); } }
        public int TotalCandidates => _candidates.Count;
        public bool HasNext => CurrentIndex < _candidates.Count - 1;
        public bool HasPrevious => CurrentIndex > 0;
        public bool IsBusy { get => _isBusy; private set => this.RaiseAndSetIfChanged(ref _isBusy, value); }

        public string PairHeading { get => _pairHeading; private set => this.RaiseAndSetIfChanged(ref _pairHeading, value); }
        public string SummaryLine { get => _summaryLine; private set => this.RaiseAndSetIfChanged(ref _summaryLine, value); }
        public string ReasonLine { get => _reasonLine; private set => this.RaiseAndSetIfChanged(ref _reasonLine, value); }

        public string SourceModLine => CurrentCandidate?.SourceMod is { } a ? $"{a.Name} — {a.PackageId}\n{a.Description}" : string.Empty;
        public string TargetModLine => CurrentCandidate?.TargetMod is { } b ? $"{b.Name} — {b.PackageId}\n{b.Description}" : string.Empty;

        public AIIncompatibilityCandidate? CurrentCandidate =>
            _candidates.Count > 0 ? _candidates[Math.Min(CurrentIndex, _candidates.Count - 1)] : null;

        public ICommand PreviousCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand ConfirmHardCommand { get; }
        public ICommand ConfirmSoftCommand { get; }
        public ICommand DeclineCommand { get; }
        public ICommand CancelCommand { get; }

        private void NavigatePrevious() =>
            CurrentIndex = Math.Max(0, CurrentIndex - 1);

        private void NavigateNext() =>
            CurrentIndex = Math.Min(_candidates.Count - 1, CurrentIndex + 1);

        /// <summary>
        /// Declining advances to the next pair, and closes the dialog once the
        /// last pair is declined — the user is never left stuck on a pair.
        /// </summary>
        private void DeclineAndAdvance()
        {
            if (CurrentIndex < _candidates.Count - 1)
                CurrentIndex++;
            else
                CloseDialog(false);
        }

        private async void ConfirmAndAdvanceAsync(RimSharp.Shared.Services.Contracts.AIIncompatibilityType type)
        {
            var candidate = CurrentCandidate;
            if (candidate == null) return;
            IsBusy = true;
            try
            {
                candidate.Type = type;
                candidate.Confirmed = true;
                await _applyCandidateAsync(candidate).ConfigureAwait(true);

                if (CurrentIndex < _candidates.Count - 1)
                    CurrentIndex++;
                else
                    CloseDialog(true);
            }
            catch (Exception ex)
            {
                SummaryLine = $"Failed to save rule: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RaiseCandidateChanged()
        {
            var c = CurrentCandidate;
            if (c == null)
            {
                PairHeading = "No AI-suggested candidates to review.";
                SummaryLine = string.Empty;
                ReasonLine = string.Empty;
            }
            else
            {
                PairHeading = $"Mod Pair {CurrentIndex + 1} of {TotalCandidates}";
                SummaryLine = c.SourceMod?.Name ?? "?" + " ↔ " + (c.TargetMod?.Name ?? "?");
                ReasonLine = string.IsNullOrWhiteSpace(c.Reason) ? "(no reason supplied)" : c.Reason;
            }
            this.RaisePropertyChanged(nameof(SourceModLine));
            this.RaisePropertyChanged(nameof(TargetModLine));
            this.RaisePropertyChanged(nameof(CurrentCandidate));
        }
    }
}
