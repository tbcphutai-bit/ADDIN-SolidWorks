// Adapted from user-supplied DXF.zip; see SOURCE.md.
using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Resolves the installed GDI family selected by the user to the actual
    /// TTF/TTC/OTF file name required by DXF STYLE group code 3.
    /// </summary>
    internal static class DxfFontFileResolver
    {
        private const string FontsRegistryPath =
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

        private static readonly ConcurrentDictionary<string, string> Cache =
            new ConcurrentDictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        public static string Resolve(string familyName)
        {
            string requested = (familyName ?? string.Empty).Trim();
            if (requested.Length == 0) return null;

            string cached;
            if (Cache.TryGetValue(requested, out cached)) return cached;

            string resolved = ResolveUncached(requested);
            if (!string.IsNullOrWhiteSpace(resolved))
                Cache.TryAdd(requested, resolved);
            return resolved;
        }

        private static string ResolveUncached(string requested)
        {
            List<FontRegistration> registrations =
                ReadFontRegistrations();
            string requestedIdentity = NormalizeIdentity(requested);

            FontRegistration direct = registrations.FirstOrDefault(item =>
                GetRegisteredFamilyNames(item.DisplayName).Any(name =>
                    string.Equals(
                        NormalizeIdentity(name),
                        requestedIdentity,
                        StringComparison.Ordinal)));
            string directFile = GetUsableFileName(direct);
            if (!string.IsNullOrWhiteSpace(directFile)) return directFile;

            // Some older Bitstream/Corel fonts expose a shortened GDI family
            // (for example "BankGothic Md BT") while their registry display
            // name is "Bank Gothic Medium BT". If the normalized registry
            // name still cannot identify it, inspect the font's embedded
            // family name without installing or copying anything.
            foreach (FontRegistration registration in registrations)
            {
                string path = ResolveFontPath(registration.FileValue);
                if (string.IsNullOrWhiteSpace(path)) continue;

                try
                {
                    using (var collection = new PrivateFontCollection())
                    {
                        collection.AddFontFile(path);
                        if (!collection.Families.Any(family =>
                            string.Equals(
                                family.Name,
                                requested,
                                StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }
                    }

                    return Path.GetFileName(path);
                }
                catch
                {
                    // A damaged or protected registration must not prevent
                    // checking the remaining installed fonts.
                }
            }

            return null;
        }

        private static List<FontRegistration> ReadFontRegistrations()
        {
            var result = new List<FontRegistration>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            RegistryView[] views = Environment.Is64BitOperatingSystem
                ? new[] { RegistryView.Registry64, RegistryView.Registry32 }
                : new[] { RegistryView.Registry32 };

            foreach (RegistryHive hive in new[]
                { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                foreach (RegistryView view in views)
                {
                    try
                    {
                        using (RegistryKey baseKey =
                            RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey fontsKey = baseKey.OpenSubKey(
                            FontsRegistryPath,
                            writable: false))
                        {
                            if (fontsKey == null) continue;
                            foreach (string valueName in
                                fontsKey.GetValueNames())
                            {
                                string fileValue = fontsKey.GetValue(
                                    valueName,
                                    null,
                                    RegistryValueOptions
                                        .DoNotExpandEnvironmentNames) as string;
                                if (string.IsNullOrWhiteSpace(fileValue))
                                    continue;

                                string key = valueName + "|" + fileValue;
                                if (!seen.Add(key)) continue;
                                result.Add(new FontRegistration
                                {
                                    DisplayName = valueName,
                                    FileValue = fileValue
                                });
                            }
                        }
                    }
                    catch
                    {
                        // Continue with the other hive/view.
                    }
                }
            }

            return result;
        }

        private static IEnumerable<string> GetRegisteredFamilyNames(
            string displayName)
        {
            string value = displayName ?? string.Empty;
            int fontTypeIndex = value.LastIndexOf(" (", StringComparison.Ordinal);
            if (fontTypeIndex >= 0)
                value = value.Substring(0, fontTypeIndex);

            return value.Split(new[] { '&' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim());
        }

        private static string NormalizeIdentity(string value)
        {
            string normalized = (value ?? string.Empty).ToLowerInvariant()
                .Replace("medium", "md")
                .Replace("regular", string.Empty);
            var builder = new StringBuilder(normalized.Length);
            foreach (char character in normalized)
            {
                if (char.IsLetterOrDigit(character))
                    builder.Append(character);
            }
            return builder.ToString();
        }

        private static string GetUsableFileName(
            FontRegistration registration)
        {
            if (registration == null) return null;
            string path = ResolveFontPath(registration.FileValue);
            return string.IsNullOrWhiteSpace(path)
                ? null
                : Path.GetFileName(path);
        }

        private static string ResolveFontPath(string fileValue)
        {
            string value = Environment.ExpandEnvironmentVariables(
                (fileValue ?? string.Empty).Trim().Trim('"'));
            if (value.Length == 0) return null;

            string path = Path.IsPathRooted(value)
                ? value
                : Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.Fonts),
                    value);
            try
            {
                return File.Exists(path) ? Path.GetFullPath(path) : null;
            }
            catch
            {
                return null;
            }
        }

        private sealed class FontRegistration
        {
            public string DisplayName { get; set; }
            public string FileValue { get; set; }
        }
    }
}
