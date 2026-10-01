using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace KsfCompanion
{
    static class SteamLocator
    {
        const ulong SteamId64Base = 76561197960265728UL;

        /// <summary>Finds ...\Counter-Strike Source\cstrike, either from settings or across all Steam libraries.</summary>
        public static string FindCstrikeDir(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && !configured.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                var dir = configured.Trim().Trim('"');
                if (Directory.Exists(Path.Combine(dir, "cstrike"))) dir = Path.Combine(dir, "cstrike");
                return Directory.Exists(Path.Combine(dir, "cfg")) ? dir : null;
            }

            foreach (var library in SteamLibraries())
            {
                var dir = Path.Combine(library, "steamapps", "common", "Counter-Strike Source", "cstrike");
                if (Directory.Exists(Path.Combine(dir, "cfg"))) return dir;
            }
            return null;
        }

        static IEnumerable<string> SteamLibraries()
        {
            var steam = (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string)?.Replace('/', '\\');
            if (string.IsNullOrEmpty(steam)) steam = @"C:\Program Files (x86)\Steam";
            yield return steam;

            string vdf;
            try { vdf = File.ReadAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf")); }
            catch (Exception) { yield break; }

            foreach (Match m in Regex.Matches(vdf, "\"path\"\\s+\"([^\"]+)\""))
                yield return m.Groups[1].Value.Replace(@"\\", @"\");
        }

        /// <summary>
        /// The player's SteamID in the STEAM_0:X:Y form KSF uses. "auto" means whoever is logged into Steam right now.
        /// </summary>
        public static string FindSteamId(string configured)
        {
            configured = configured?.Trim() ?? "";
            if (configured.Length > 0 && !configured.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return ParseSteamId(configured);

            if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", null) is int active && active != 0)
                return FromAccountId(unchecked((uint)active));
            return null;
        }

        public static string ParseSteamId(string text)
        {
            var steam2 = Regex.Match(text, @"^STEAM_[0-5]:([01]):(\d+)$", RegexOptions.IgnoreCase);
            if (steam2.Success) return $"STEAM_0:{steam2.Groups[1].Value}:{steam2.Groups[2].Value}";

            var steam3 = Regex.Match(text, @"^\[?U:1:(\d+)\]?$", RegexOptions.IgnoreCase);
            if (steam3.Success && uint.TryParse(steam3.Groups[1].Value, out var account)) return FromAccountId(account);

            if (ulong.TryParse(text, out var id64) && id64 > SteamId64Base) return FromAccountId((uint)(id64 - SteamId64Base));
            return null;
        }

        public static string FromAccountId(uint accountId) => $"STEAM_0:{accountId & 1}:{accountId >> 1}";

        /// <summary>STEAM_0:Y:Z back to the account number the game's "status" shows as [U:1:number].</summary>
        public static uint? AccountId(string steam2)
        {
            var m = Regex.Match(steam2 ?? "", @"^STEAM_[0-5]:([01]):(\d+)$");
            return m.Success && uint.TryParse(m.Groups[2].Value, out var z) ? z * 2 + uint.Parse(m.Groups[1].Value) : (uint?)null;
        }
    }
}
