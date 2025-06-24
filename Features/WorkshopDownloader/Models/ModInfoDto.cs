using System.Collections.Generic;

public class ModInfoDto
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string SteamId { get; set; }
        public string PublishDate { get; set; }
        public string StandardDate { get; set; }
        public long FileSize { get; set; }
        public List<string> LatestVersions { get; set; } = new List<string>();
}
