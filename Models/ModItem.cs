namespace RimSharp.Models
{
    public class ModItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string PackageId { get; set; }
        public string Authors { get; set; }
        public string Description { get; set; }
        public bool IsCore { get; set; }
        public bool IsDirty { get; set; }
    }
}