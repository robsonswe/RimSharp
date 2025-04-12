namespace RimSharp.Shared.Models
{
    public record GitHubContentItem
    {
        public string Name { get; set; }
        public string Type { get; set; } // "file" or "dir"
        public string DownloadUrl { get; set; }
    }
}
