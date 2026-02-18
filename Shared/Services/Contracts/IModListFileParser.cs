using System.Collections.Generic;
using System.Xml.Linq;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModListFileParser
    {
        /// <summary>
        /// Parses a mod list XML and returns the list of PackageIds.
        /// </summary>
        List<string> Parse(XDocument doc);

        /// <summary>
        /// Creates an XDocument representing the given list of PackageIds.
        /// </summary>
        XDocument Generate(IEnumerable<string> packageIds);
    }
}
