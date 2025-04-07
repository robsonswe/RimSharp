namespace RimSharp.Shared.Models
{
    public enum RulesStorageType
    {
        Json,
        Sqlite
    }

    public class RulesConfiguration
    {
        public RulesStorageType StorageType { get; set; } = RulesStorageType.Json;
        public string SqliteConnectionString { get; set; }
    }
}
