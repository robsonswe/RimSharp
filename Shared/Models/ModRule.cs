#nullable enable
using System.Collections.Generic;
using System.Text.Json.Serialization;
using RimSharp.Shared.JsonConverters; // Or your actual namespace for the converter

namespace RimSharp.Shared.Models
{
    public class ModRule
    {
        public Dictionary<string, ModDependencyRule> LoadBefore { get; set; } = new();
        public Dictionary<string, ModDependencyRule> LoadAfter { get; set; } = new();
        public LoadBottomRule? LoadBottom { get; set; }
        public Dictionary<string, ModIncompatibilityRule> Incompatibilities { get; set; } = new();
        
        // Add SupportedVersions property
        [JsonConverter(typeof(StringOrStringListConverter))]
        public List<string> SupportedVersions { get; set; } = new();
    }

    public class ModDependencyRule
    {
        [JsonConverter(typeof(StringOrStringListConverter))]
        public List<string> Name { get; set; } = new();

        [JsonConverter(typeof(StringOrStringListConverter))]
        public List<string> Comment { get; set; } = new();
    }

    public class ModIncompatibilityRule
    {
        public bool HardIncompatibility { get; set; }

        [JsonConverter(typeof(StringOrStringListConverter))]
        public List<string> Comment { get; set; } = new();

        [JsonConverter(typeof(StringOrStringListConverter))]
        public List<string> Name { get; set; } = new();
    }

    public class LoadBottomRule
    {
        [JsonConverter(typeof(StringOrStringListConverter))]
        public List<string> Comment { get; set; } = new();

        public bool Value { get; set; }
    }
}