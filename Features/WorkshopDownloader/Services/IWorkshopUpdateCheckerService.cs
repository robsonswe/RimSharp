using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimSharp.Shared.Models; // For ModItem

namespace RimSharp.Features.WorkshopDownloader.Services
{
    // Result structure for the update check operation
    public class UpdateCheckResult
    {
        private int _modsChecked;
        private int _updatesFound;
        private int _errorsEncountered;

        public int ModsChecked
        {
            get => _modsChecked;
            set => _modsChecked = value;
        }

        public int UpdatesFound
        {
            get => _updatesFound;
            set => _updatesFound = value;
        }

        public int ErrorsEncountered
        {
            get => _errorsEncountered;
            set => _errorsEncountered = value;
        }

        public List<string> ErrorMessages { get; } = new List<string>();

        public void IncrementModsChecked() => Interlocked.Increment(ref _modsChecked);
        public void IncrementUpdatesFound() => Interlocked.Increment(ref _updatesFound);
        public void IncrementErrorsEncountered() => Interlocked.Increment(ref _errorsEncountered);
    }


    public interface IWorkshopUpdateCheckerService
    {
        /// <summary>
        /// Checks a list of mods for updates against the Steam Workshop API.
        /// Adds mods with available updates to the download queue.
        /// </summary>
        /// <param name="modsToCheck">The collection of ModItems to check.</param>
        /// <returns>A summary of the update check operation.</returns>
        Task<UpdateCheckResult> CheckForUpdatesAsync(IEnumerable<ModItem> modsToCheck,
            IProgress<(int current, int total, string modName)> progress = null);

    }
}
