using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KsfCompanion
{
    /// <summary>key = value settings in a plain text file that can be edited with Notepad.</summary>
    sealed class Settings
    {
        static readonly (string Key, string Default, string Help)[] Documented =
        {
            ("steamid", "auto", "whose KSF stats to show: auto (the account logged into Steam), STEAM_0:X:Y, or a SteamID64"),
            ("tick", "auto", "which KSF records to show: auto (follows the server you're on), 66 or 100"),
            ("ksf_style", "0", "0 = normal, 1 = sideways, 2 = half-sideways, 3 = backwards"),
            ("run_server_commands", "0", "1 = when you join a map on a KSF server, run server_commands for you. KSF answers them in chat, so that's where the map info and your rank show up; off by default, as the dashboard and the F6 card show the same"),
            ("server_commands", "sm_m; sm_mrank", "KSF commands to run then (same as typing !m and !mrank in chat), separated by ;"),
            ("dashboard_on_game_start", "1", "1 = open the dashboard on your second monitor when CS:S starts (it won't take focus from the game)"),
            ("view", "advanced", "the dashboard: simple (the essentials, bigger and cleaner) or advanced (every detail) - also the switch at the top of the dashboard"),
            ("hidden", "", "parts of the dashboard you've hidden (Customize at the top brings them back): map, timer, numbers, times, leaderboard, keys, live, session, level, later, servers, recent"),
            ("size", "100", "how big the dashboard is drawn, in percent (80 to 150) - also the Size slider in Customize"),
            ("live_hud", "1", "1 = on KSF servers, have the game record a demo (cstrike/ksfc_live.dem, replaced every map, deleted when the game closes) so the dashboard can read the timer's on-screen text live: the stage you're on and your stage/bonus times the moment you finish them. Only reads the file - nothing touches the game"),
            ("turn_speed", "210", "how fast the turn binds turn, in degrees a second (cl_yawspeed) - also the slider on the dashboard's Binds page, where the binds are set"),
            ("key_save", "F5", "saves the current map to your play-later list (these three can also be changed on the Binds page)"),
            ("key_card", "F6", "hold to open the console with the KSF card for the current map"),
            ("key_list", "F7", "hold to open the console with your play-later list"),
            ("game_dir", "auto", "your .../Counter-Strike Source/cstrike folder, or auto to find it through Steam"),
        };

        // Replaced by newer settings; dropped when the file is rewritten.
        // "announce" (on by default) became run_server_commands (off by default): the replies land in chat.
        static readonly string[] Obsolete = { "ksf_game", "auto_card", "card_delay_seconds", "card_seconds", "announce" };

        readonly string path;
        readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Settings(string path)
        {
            this.path = path;
            if (File.Exists(path))
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var eq = line.IndexOf('=');
                    if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            foreach (var key in Obsolete) values.Remove(key);
            Save();
        }

        /// <summary>The KSF game to show when the server isn't known: css (66 tick) or css100t.</summary>
        public string FixedGame
        {
            get
            {
                var tick = Get("tick").Trim().ToLowerInvariant();
                if (tick == "66" || tick == "66t" || tick == "css") return "css";
                if (tick == "100" || tick == "100t" || tick == "css100t") return "css100t";
                return null;
            }
        }

        public string Get(string key)
        {
            if (values.TryGetValue(key, out var value)) return value;
            return Documented.FirstOrDefault(d => d.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Default ?? "";
        }

        public int GetInt(string key, int min, int max)
        {
            if (!int.TryParse(Get(key), out var value))
                int.TryParse(Documented.FirstOrDefault(d => d.Key == key).Default, out value);
            return Math.Max(min, Math.Min(max, value));
        }

        public bool GetBool(string key) => Get(key).Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";

        public void Set(string key, string value)
        {
            values[key] = value;
            Save();
        }

        void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# KSF Companion settings. Edit with any text editor, then restart KSF Companion (right-click the tray icon > Exit).");
            foreach (var d in Documented)
            {
                sb.AppendLine();
                sb.AppendLine("# " + d.Help);
                sb.AppendLine($"{d.Key} = {Get(d.Key)}");
            }

            var managed = values.Keys
                .Where(k => !Documented.Any(d => d.Key.Equals(k, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(k => k)
                .ToList();
            if (managed.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("# remembered by KSF Companion");
                foreach (var k in managed) sb.AppendLine($"{k} = {values[k]}");
            }

            try { File.WriteAllText(path, sb.ToString()); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
