namespace RimSharp.Shared.Models
{
    public record GitHubContentItem
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "file" or "dir"
        public string DownloadUrl { get; set; } = string.Empty;
    }
}
