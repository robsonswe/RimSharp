using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RimSharp.Core.Services
{
    public enum LinuxDistributionFamily
    {
        Unknown,
        Debian,
        Fedora,
        Arch
    }

    public static class LinuxDistributionDetector
    {
        public static LinuxDistributionFamily Detect(string path = "/etc/os-release")
        {
            if (!OperatingSystem.IsLinux() || !File.Exists(path))
            {
                return LinuxDistributionFamily.Unknown;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(path))
            {
                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim().Trim('"', '\'');
                values[key] = value;
            }

            var identifiers = new[]
            {
                values.GetValueOrDefault("ID"),
                values.GetValueOrDefault("ID_LIKE")
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.ToLowerInvariant())
            .ToHashSet();

            if (identifiers.Overlaps(new[] { "debian", "ubuntu", "linuxmint", "pop" }))
            {
                return LinuxDistributionFamily.Debian;
            }

            if (identifiers.Overlaps(new[] { "fedora", "rhel", "centos", "rocky", "almalinux" }))
            {
                return LinuxDistributionFamily.Fedora;
            }

            if (identifiers.Overlaps(new[] { "arch", "manjaro", "endeavouros", "steamos" }))
            {
                return LinuxDistributionFamily.Arch;
            }

            return LinuxDistributionFamily.Unknown;
        }
    }
}
