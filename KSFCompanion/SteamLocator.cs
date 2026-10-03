using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KsfCompanion
{
    static class SteamLocator
    {
        const ulong SteamId64Base = 76561197960265728UL;

        static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <summary>Finds .../Counter-Strike Source/cstrike, either from settings or across all Steam libraries.</summary>
        public static string FindCstrikeDir(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && !configured.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                var dir = configured.Trim().Trim('"');
                if (dir.StartsWith("~/", StringComparison.Ordinal)) dir = Path.Combine(Home, dir.Substring(2));
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

        /// <summary>
        /// Where Steam is installed: the usual ~/.local/share/Steam (also behind ~/.steam/steam), the Flatpak and the
        /// Snap. The same folder reached through a link only counts once.
        /// </summary>
        static IEnumerable<string> SteamRoots()
        {
            var candidates = new[]
            {
                Path.Combine(Home, ".local", "share", "Steam"),
                Path.Combine(Home, ".steam", "steam"),
                Path.Combine(Home, ".steam", "root"),
                Path.Combine(Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
                Path.Combine(Home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam"),
                Path.Combine(Home, "snap", "steam", "common", ".local", "share", "Steam"),
            };
            var seen = new HashSet<string>();
            foreach (var candidate in candidates)
            {
                if (!Directory.Exists(candidate)) continue;
                string real;
                try { real = new DirectoryInfo(candidate).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(candidate); }
                catch (IOException) { real = candidate; }
                if (seen.Add(real.TrimEnd('/'))) yield return candidate;
            }
        }

        static IEnumerable<string> SteamLibraries()
        {
            foreach (var steam in SteamRoots())
            {
                yield return steam;
                string vdf;
                try { vdf = File.ReadAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf")); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { continue; }

                foreach (Match m in Regex.Matches(vdf, "\"path\"\\s+\"([^\"]+)\""))
                    yield return m.Groups[1].Value.Replace(@"\\", @"\");
            }
        }

        /// <summary>
        /// The player's SteamID in the STEAM_0:X:Y form KSF uses. "auto" means whoever last logged into Steam on this
        /// computer (Steam's loginusers.vdf: the account marked MostRecent, else the latest login).
        /// </summary>
        public static string FindSteamId(string configured)
        {
            configured = configured?.Trim() ?? "";
            if (configured.Length > 0 && !configured.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return ParseSteamId(configured);

            foreach (var steam in SteamRoots())
            {
                string vdf;
                try { vdf = File.ReadAllText(Path.Combine(steam, "config", "loginusers.vdf")); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { continue; }

                var users = Regex.Matches(vdf, "\"(7656\\d{13})\"\\s*\\{([^}]*)\\}")
                    .Select(m => (Id: m.Groups[1].Value, Body: m.Groups[2].Value))
                    .Select(u => (u.Id,
                        Recent: Regex.IsMatch(u.Body, "\"MostRecent\"\\s+\"1\"", RegexOptions.IgnoreCase),
                        At: long.TryParse(Regex.Match(u.Body, "\"Timestamp\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase).Groups[1].Value, out var at) ? at : 0))
                    .ToList();
                var user = users.OrderByDescending(u => u.Recent).ThenByDescending(u => u.At).FirstOrDefault();
                if (user.Id != null) return ParseSteamId(user.Id);
            }
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
