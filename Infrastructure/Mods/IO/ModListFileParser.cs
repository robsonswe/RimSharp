using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using RimSharp.Shared.Services.Contracts;

namespace RimSharp.Infrastructure.Mods.IO
{
    public class ModListFileParser : IModListFileParser
    {
        public List<string> Parse(XDocument doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            var activeModsElement = doc.Root?.Element("activeMods");
            if (activeModsElement == null)
            {
                return new List<string>();
            }

            return activeModsElement.Elements("li")
                .Select(e => e.Value?.Trim().ToLowerInvariant())
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList()!;
        }

        public XDocument Generate(IEnumerable<string> packageIds)
        {
            if (packageIds == null) throw new ArgumentNullException(nameof(packageIds));

            var doc = new XDocument(
                new XElement("ModsConfigData",
                    new XElement("version", "1.0"),
                    new XElement("activeMods")
                )
            );

            var activeModsElement = doc.Root!.Element("activeMods")!;

            foreach (var id in packageIds)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    activeModsElement.Add(new XElement("li", id.ToLowerInvariant()));
                }
            }

            return doc;
        }
    }
}
