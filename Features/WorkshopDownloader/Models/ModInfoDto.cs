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
}
