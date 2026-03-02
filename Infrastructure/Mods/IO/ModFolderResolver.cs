#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace RimSharp.Infrastructure.Mods.IO
{
    public record ResolvedFolders(List<string> Current, List<string> Max, List<string> Dependencies);

    public static class ModFolderResolver
    {
        public static ResolvedFolders Resolve(string modPath, string majorGameVersion, IReadOnlySet<string>? activeModPackageIds = null, StringBuilder? log = null)
        {
            var current = new List<string>();
            var max = new List<string>();
            var dependencies = new List<string>();

            var normalizedActiveMods = activeModPackageIds != null 
                ? new HashSet<string>(activeModPackageIds.Select(id => id.ToLowerInvariant()))
                : new HashSet<string>();

            string? xmlPath = GetLoadFoldersXmlPath(modPath);

            if (xmlPath != null)
            {
                log?.AppendLine($"[XML] Found loadFolders.xml at: {Path.GetRelativePath(modPath, xmlPath)}");
                try
                {
                    var doc = XDocument.Load(xmlPath);
                    var root = doc.Root;

                    if (root != null)
                    {
                        string exactTagName = "v" + majorGameVersion;
                        var targetNodes = root.Elements().Where(e => e.Name.LocalName.Equals(exactTagName, StringComparison.OrdinalIgnoreCase)).ToList();
                        
                        if (targetNodes.Count == 0)
                        {
                            log?.AppendLine($"[XML] No <{exactTagName}> found. Checking for <default>.");
                            targetNodes = root.Elements().Where(e => e.Name.LocalName.Equals("default", StringComparison.OrdinalIgnoreCase)).ToList();
                        }
                        
                        if (targetNodes.Count == 0)
                        {
                            var availableVersions = root.Elements()
                                .Select(e => e.Name.LocalName.ToLowerInvariant())
                                .Where(n => n.StartsWith("v") && Version.TryParse(n.Substring(1), out _))
                                .Distinct()
                                .ToList();

                            if (availableVersions.Count > 0)
                            {
                                string bestVersion = GetBestFallbackVersion(availableVersions, majorGameVersion);
                                log?.AppendLine($"[XML] No <default> found. Falling back to closest available version tag: <{bestVersion}>");
                                targetNodes = root.Elements().Where(e => e.Name.LocalName.Equals(bestVersion, StringComparison.OrdinalIgnoreCase)).ToList();
                            }
                            else
                            {
                                log?.AppendLine($"[XML] Neither <{exactTagName}> nor <default> existed in the XML. Falling back to directory structure.");
                            }
                        }
                        
                        if (targetNodes.Count > 0)
                        {
                            foreach (var targetNode in targetNodes)
                            {
                                bool isNodeMet = EvaluateConditions(targetNode, normalizedActiveMods, dependencies, log);

                                foreach (var li in targetNode.Elements().Where(e => e.Name.LocalName.Equals("li", StringComparison.OrdinalIgnoreCase)))
                                {
                                    bool isLiMet = EvaluateConditions(li, normalizedActiveMods, dependencies, log);
                                    string folder = li.Value.Trim().Replace('/', Path.DirectorySeparatorChar);
                                    
                                    folder = folder.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                    if (string.IsNullOrEmpty(folder) || folder == ".") folder = ".";
                                    
                                    if (isNodeMet && isLiMet) current.Add(folder);
                                    max.Add(folder);
                                }
                            }
                            return new ResolvedFolders(current, max, dependencies);
                        }
                    }
                } 
                catch (Exception ex)
                { 
                    log?.AppendLine($"[XML ERROR] Parsing failed: {ex.Message}");
                }
            }
            else
            {
                log?.AppendLine($"[INFO] No loadFolders.xml found. Proceeding with directory fallback logic.");
            }

            current.Add(".");
            if (Directory.Exists(Path.Combine(modPath, "Common"))) current.Add("Common");
            
            string ver = majorGameVersion;
            if (Directory.Exists(Path.Combine(modPath, ver))) 
            {
                current.Add(ver);
            }
            else if (Directory.Exists(Path.Combine(modPath, "v" + ver))) 
            {
                current.Add("v" + ver);
            }
            else
            {
                var dirs = Directory.GetDirectories(modPath)
                    .Select(Path.GetFileName)
                    .Where(d => d != null && (Version.TryParse(d, out _) || (d.StartsWith("v", StringComparison.OrdinalIgnoreCase) && Version.TryParse(d.Substring(1), out _))))
                    .Select(d => d!)
                    .ToList();

                if (dirs.Count > 0)
                {
                    string bestDir = GetBestFallbackVersion(dirs, majorGameVersion);
                    log?.AppendLine($"[DIR FALLBACK] Exact folder '{ver}' not found. Falling back to best match: '{bestDir}'");
                    current.Add(bestDir);
                }
            }

            max.AddRange(current);
            return new ResolvedFolders(current, max, dependencies);
        }

        private static string? GetLoadFoldersXmlPath(string modPath)
        {
            string[] possibleLocations = { modPath, Path.Combine(modPath, "About"), Path.Combine(modPath, "LoadFolders") };
            foreach (var loc in possibleLocations)
            {
                if (!Directory.Exists(loc)) continue;
                var files = Directory.GetFiles(loc, "*.xml");
                var exactFile = files.FirstOrDefault(f => Path.GetFileName(f).Equals("loadfolders.xml", StringComparison.OrdinalIgnoreCase));
                if (exactFile != null) return exactFile;
            }
            return null;
        }

        private static string GetBestFallbackVersion(IEnumerable<string> availableVersions, string targetVersionStr)
        {
            if (!Version.TryParse(targetVersionStr, out var targetVersion))
                return availableVersions.OrderByDescending(v => v).FirstOrDefault() ?? targetVersionStr;

            var parsedVersions = new List<(string original, Version version)>();
            foreach (var vStr in availableVersions)
            {
                string clean = vStr.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? vStr.Substring(1) : vStr;
                if (Version.TryParse(clean, out var v))
                    parsedVersions.Add((vStr, v));
            }

            if (!parsedVersions.Any()) return targetVersionStr;

            var lessOrEqual = parsedVersions.Where(x => x.version <= targetVersion).OrderByDescending(x => x.version).FirstOrDefault();
            if (lessOrEqual.original != null) return lessOrEqual.original;

            var greater = parsedVersions.Where(x => x.version > targetVersion).OrderBy(x => x.version).FirstOrDefault();
            return greater.original ?? targetVersionStr;
        }

        private static bool EvaluateConditions(XElement element, HashSet<string> activeMods, List<string> dependencies, StringBuilder? log)
        {
            bool isMet = true;

            foreach (var attr in element.Attributes())
            {
                string name = attr.Name.LocalName.ToLowerInvariant();
                string val = attr.Value;
                if (string.IsNullOrWhiteSpace(val)) continue;

                var mods = val.Split(',').Select(m => m.Trim().ToLowerInvariant()).ToList();

                if (name == "ifmodactive" || name == "mayrequire" || name == "ifmodactiveany")
                {
                    bool met = mods.Any(m => activeMods.Contains(m));
                    isMet &= met;
                    foreach(var m in mods) if (!dependencies.Contains(m)) dependencies.Add(m);
                    log?.AppendLine($"[COND] {name}=\"{val}\" -> Evaluated to {met}");
                }
                else if (name == "ifmodactiveall")
                {
                    bool met = mods.All(m => activeMods.Contains(m));
                    isMet &= met;
                    foreach (var m in mods) if (!dependencies.Contains(m)) dependencies.Add(m);
                    log?.AppendLine($"[COND] {name}=\"{val}\" -> Evaluated to {met}");
                }
                else if (name == "ifmodnotactive" || name == "ifmodnotactiveany")
                {
                    bool met = !mods.Any(m => activeMods.Contains(m));
                    isMet &= met;
                    foreach (var m in mods) if (!dependencies.Contains(m)) dependencies.Add(m);
                    log?.AppendLine($"[COND] {name}=\"{val}\" -> Evaluated to {met}");
                }
                else if (name == "ifmodnotactiveall")
                {
                    bool met = !mods.All(m => activeMods.Contains(m));
                    isMet &= met;
                    foreach (var m in mods) if (!dependencies.Contains(m)) dependencies.Add(m);
                    log?.AppendLine($"[COND] {name}=\"{val}\" -> Evaluated to {met}");
                }
            }
            return isMet;
        }
    }
}

