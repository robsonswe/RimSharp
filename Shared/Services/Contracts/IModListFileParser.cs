using System.Collections.Generic;
using System.Xml.Linq;

namespace RimSharp.Shared.Services.Contracts
{
    public interface IModListFileParser
    {
        /// <summary>

        /// </summary>
        List<string> Parse(XDocument doc);

        /// <summary>

        /// </summary>
        XDocument Generate(IEnumerable<string> packageIds);
    }
}

