using System.Collections.Generic;

public class ModInfoDto
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string SteamId { get; set; } = string.Empty;
        public string PublishDate { get; set; } = string.Empty;
        public string StandardDate { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public List<string> LatestVersions { get; set; } = new List<string>();

        /// <summary>
        /// Full plain-text Workshop description (from the embedded browser DOM
        /// or the Steam API fallback), carried with the queue item so the
        /// post-download hook can cache it without a second API fetch.
        /// </summary>
        public string SteamDescription { get; set; } = string.Empty;
}
