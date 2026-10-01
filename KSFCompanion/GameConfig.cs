using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KsfCompanion
{
    /// <summary>
    /// Everything KSF Companion puts in the game: a small block in autoexec.cfg plus a few ksf_*.cfg files.
    /// The KSF card is a set of "echo" lines that the card key prints into the console and then opens it.
    /// </summary>
    sealed class GameConfig
    {
        public const string Tag = "[ksf.surf]";
        public const string LogFileName = "ksf_console.log";
        public const string SaveMarker = Tag + " saving map for later";
        public const string ReadyMarker = Tag + " KSF Companion ready";
        public const string LinkMarker = Tag + " KSF Companion connected";

        const string BlockStart = "// >>> KSF Companion >>>";
        const string BlockEnd = "// <<< KSF Companion <<<";
        static readonly string[] OwnFiles = { "ksf_companion.cfg", "ksf_card.cfg", "ksf_later.cfg", "ksf_msg.cfg", "ksf_binds.cfg" };
        static readonly Encoding NoBom = new UTF8Encoding(false);

        static readonly Regex SafeCommand = new Regex(@"^[A-Za-z0-9_][A-Za-z0-9_ @.\-]{0,63}$");

        public GameConfig(string cstrikeDir)
        {
            CstrikeDir = cstrikeDir;
        }

        public string CstrikeDir { get; }
        string CfgPath(string name) => Path.Combine(CstrikeDir, "cfg", name);

        /// <summary>con_logfile writes here; console.log is what -condebug produces if the player already uses that.</summary>
        public IEnumerable<string> LogCandidates => new[] { Path.Combine(CstrikeDir, LogFileName), Path.Combine(CstrikeDir, "console.log") };

        public bool IsInstalled => File.Exists(CfgPath("ksf_companion.cfg")) && ReadAutoexec().Contains(BlockStart);

        public static string ValidKey(string key, string fallback) => GameKeys.Normalize(key?.Trim()) ?? fallback;

        /// <summary>Your in-game name, as saved in config.cfg.</summary>
        public string PlayerName()
        {
            try
            {
                var path = CfgPath("config.cfg");
                var m = File.Exists(path) ? Regex.Match(File.ReadAllText(path), "^name \"(.+)\"", RegexOptions.Multiline) : Match.Empty;
                return m.Success ? m.Groups[1].Value : null;
            }
            catch (IOException) { return null; }
        }

        /// <summary>The server_commands setting, limited to plain command names and arguments.</summary>
        public static string ServerCommands(Settings settings) => string.Join("; ",
            settings.Get("server_commands").Split(';').Select(c => c.Trim()).Where(c => SafeCommand.IsMatch(c)).Take(4));

        public void Install(Settings settings)
        {
            var keys = KeyNames.From(settings);
            RememberOriginals(settings, new[] { keys.Save, keys.Card, keys.List });

            Write("ksf_companion.cfg", CompanionCfg(keys));
            if (!File.Exists(CfgPath("ksf_binds.cfg"))) Write("ksf_binds.cfg", "// KSF Companion binds - set them on the Binds page of the dashboard\r\n");
            if (!File.Exists(CfgPath("ksf_card.cfg"))) WriteCard(new[] { "no map yet - join a map and its KSF info shows up here" });
            if (!File.Exists(CfgPath("ksf_msg.cfg"))) WriteMessage(new[] { "KSF Companion is running" });

            var rest = RemoveBlock(ReadAutoexec()).TrimEnd();
            var block = string.Join("\r\n",
                BlockStart,
                "// Lets KSF Companion follow map changes and adds its keys. Delete this block to turn it off.",
                $"con_logfile \"{LogFileName}\"",
                "exec ksf_companion",
                Echo($"KSF Companion ready - {GameKeys.Label(keys.Save)} saves the map for later, hold {GameKeys.Label(keys.Card)} for the map card, hold {GameKeys.Label(keys.List)} for your play-later list"),
                BlockEnd);
            WriteFile(CfgPath("autoexec.cfg"), (rest.Length > 0 ? rest + "\r\n\r\n" : "") + block + "\r\n");
        }

        /// <summary>Removes our cfgs and autoexec block and puts the original key binds back into config.cfg.</summary>
        public void Uninstall(Settings settings)
        {
            var rest = RemoveBlock(ReadAutoexec()).TrimEnd();
            var autoexec = CfgPath("autoexec.cfg");
            if (rest.Length == 0) TryDelete(autoexec);
            else WriteFile(autoexec, rest + "\r\n");

            foreach (var f in OwnFiles) TryDelete(CfgPath(f));
            TryDelete(Path.Combine(CstrikeDir, LogFileName));

            RestoreBinds(settings.Get("original_binds"));
            settings.Set("original_binds", "");
        }

        public void WriteCard(IList<string> lines) => Write("ksf_card.cfg", EchoBlock(lines, separator: true));
        public void WriteList(IList<string> lines) => Write("ksf_later.cfg", EchoBlock(lines, separator: true));
        public void WriteMessage(IList<string> lines) => Write("ksf_msg.cfg", EchoBlock(lines, separator: false));

        // Only aliases and binds, so KSF Companion can also exec it in a game that is already running.
        static string CompanionCfg(KeyNames keys) => string.Join("\r\n",
            "// KSF Companion in-game keys. This file is rewritten every time KSF Companion starts;",
            "// change the keys in Documents\\KSF Companion\\settings.ini instead.",
            $"alias ksf_save \"echo {SaveMarker}; play buttons/blip1.wav\"",
            "alias +ksf_card \"exec ksf_card; showconsole\"",
            "alias -ksf_card \"hideconsole; gameui_hide\"",
            "alias +ksf_list \"exec ksf_later; showconsole\"",
            "alias -ksf_list \"hideconsole; gameui_hide\"",
            $"bind \"{keys.Save}\" \"ksf_save\"",
            $"bind \"{keys.Card}\" \"+ksf_card\"",
            $"bind \"{keys.List}\" \"+ksf_list\"",
            "exec ksf_binds",
            "");

        /// <summary>
        /// The binds page's keys: each runs its command through a ksf_b_ alias (so taking a bind away can always
        /// tell our keys from yours, and put yours back). KSF commands run from the console - nothing is typed in chat.
        /// </summary>
        public void WriteBinds(IEnumerable<(BindAction Action, string Key)> binds, int turnSpeed)
        {
            var lines = new List<string>
            {
                "// KSF Companion binds - rewritten when you change them on the Binds page of the dashboard.",
                "// Your own bind for one of these keys is put back when you remove it there.",
            };
            var list = binds.ToList();
            foreach (var (action, key) in list)
            {
                var alias = AliasOf(action);
                if (action.Hold)
                {
                    lines.Add($"alias +{alias} \"{action.Command}\"");
                    lines.Add($"alias -{alias} \"-{action.Command.Substring(1)}\"");
                    lines.Add($"bind \"{key}\" \"+{alias}\"");
                }
                else
                {
                    lines.Add($"alias {alias} \"{action.Command}\"");
                    lines.Add($"bind \"{key}\" \"{alias}\"");
                }
            }
            if (list.Any(b => b.Action.IsTurn)) lines.Add($"cl_yawspeed {turnSpeed}");
            Write("ksf_binds.cfg", string.Join("\r\n", lines) + "\r\n");
        }

        static string AliasOf(BindAction action)
        {
            var id = Regex.Replace(action.Id.Replace(BindCatalog.CustomPrefix, "c_"), "[^A-Za-z0-9_]", "_");
            return "ksf_b_" + (id.Length > 40 ? id.Substring(0, 40) : id);
        }

        /// <summary>What these keys do in the game now (from config.cfg), kept the first time KSF Companion takes them over.</summary>
        public void RememberOriginals(Settings settings, IEnumerable<string> keys)
        {
            var originals = Originals(settings);
            var current = ReadBinds();
            var changed = false;
            foreach (var key in keys.Where(k => k != null))
            {
                if (originals.ContainsKey(key)) continue;
                originals[key] = current.TryGetValue(key, out var command) && command.IndexOf("ksf_", StringComparison.OrdinalIgnoreCase) < 0 ? command : "";
                changed = true;
            }
            if (changed) settings.Set("original_binds", string.Join("|", originals.Select(o => o.Key + "=" + o.Value)));
        }

        /// <summary>What the key did before KSF Companion took it over ("" = nothing), or what it does now if it hasn't; null if nothing.</summary>
        public string OriginalBind(Settings settings, string key)
        {
            if (Originals(settings).TryGetValue(key, out var original)) return original.Length > 0 ? original : null;
            return ReadBinds().TryGetValue(key, out var command) && command.IndexOf("ksf_", StringComparison.OrdinalIgnoreCase) < 0 ? command : null;
        }

        /// <summary>
        /// Gives the key back what it did before (in config.cfg, for when the game isn't running) and returns the
        /// console command that does the same in a running game.
        /// </summary>
        public string GiveBack(Settings settings, string key)
        {
            var originals = Originals(settings);
            originals.TryGetValue(key, out var original);
            original = original ?? "";
            RestoreBinds(key + "=" + original);
            originals.Remove(key);
            settings.Set("original_binds", string.Join("|", originals.Select(o => o.Key + "=" + o.Value)));
            return original.Length > 0 ? $"bind \"{key}\" \"{original.Replace("\"", "")}\"" : $"unbind \"{key}\"";
        }

        /// <summary>Your binds in config.cfg (as the game saved them when it last closed), without KSF Companion's own.</summary>
        public Dictionary<string, string> CurrentBinds()
        {
            var binds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var b in ReadBinds())
                    if (b.Value.IndexOf("ksf_", StringComparison.OrdinalIgnoreCase) < 0) binds[b.Key] = b.Value;
            }
            catch (IOException) { }
            return binds;
        }

        /// <summary>
        /// Takes one of your own binds off (from config.cfg, for when the game isn't running) and returns the console
        /// command that does the same in a running game.
        /// </summary>
        public string RemoveBind(string key)
        {
            var path = CfgPath("config.cfg");
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                var line = new Regex("^bind \"" + Regex.Escape(key) + "\" \"[^\"]*\"\\r?\\n?", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (line.IsMatch(text)) WriteFile(path, line.Replace(text, ""));
            }
            return $"unbind \"{key}\"";
        }

        static Dictionary<string, string> Originals(Settings settings)
        {
            var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in settings.Get("original_binds").Split('|'))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0) originals[pair.Substring(0, eq)] = pair.Substring(eq + 1);
            }
            return originals;
        }

        static string EchoBlock(IList<string> lines, bool separator)
        {
            var sb = new StringBuilder("// written by KSF Companion - regenerated automatically\r\n");
            if (separator) sb.Append(Echo(new string('-', 64))).Append("\r\n");
            foreach (var line in lines) sb.Append(Echo(line)).Append("\r\n");
            return sb.ToString();
        }

        /// <summary>One quoted echo line. Quotes/semicolons are stripped so KSF data can never become a command.</summary>
        public static string Echo(string text) => $"echo \"{Tag} {Clean(text)}\"";

        static string Clean(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text.Normalize(NormalizationForm.FormKD))
            {
                if (ch == '"' || ch == ';') sb.Append('\'');
                else if (ch == '\t' || ch == '\r' || ch == '\n') sb.Append(' ');
                else if (char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                else if (ch < 32 || ch > 126) sb.Append('?');
                else sb.Append(ch);
            }
            var clean = sb.ToString();
            return clean.Length > 200 ? clean.Substring(0, 200) : clean;
        }

        Dictionary<string, string> ReadBinds()
        {
            var binds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = CfgPath("config.cfg");
            if (!File.Exists(path)) return binds;
            foreach (Match m in Regex.Matches(File.ReadAllText(path), "^bind \"([^\"]+)\" \"([^\"]*)\"", RegexOptions.Multiline))
                binds[m.Groups[1].Value] = m.Groups[2].Value;
            return binds;
        }

        void RestoreBinds(string original)
        {
            var path = CfgPath("config.cfg");
            if (string.IsNullOrEmpty(original) || !File.Exists(path)) return;
            var text = File.ReadAllText(path);
            foreach (var pair in original.Split('|'))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                var key = pair.Substring(0, eq);
                var command = pair.Substring(eq + 1);
                var ours = new Regex("^bind \"" + Regex.Escape(key) + "\" \"[+]?ksf_[^\"]*\"", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                var restored = command.Length > 0 ? $"bind \"{key}\" \"{command}\"" : $"unbind \"{key}\"";
                text = ours.Replace(text, _ => restored);
            }
            WriteFile(path, text);
        }

        string ReadAutoexec()
        {
            var path = CfgPath("autoexec.cfg");
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }

        static string RemoveBlock(string text)
        {
            int start;
            while ((start = text.IndexOf(BlockStart, StringComparison.Ordinal)) >= 0)
            {
                var end = text.IndexOf(BlockEnd, start, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + BlockEnd.Length;
                text = text.Remove(start, end - start);
            }
            return text;
        }

        void Write(string cfgName, string content) => WriteFile(CfgPath(cfgName), content);

        static void WriteFile(string path, string content)
        {
            // Write to a temp file first so the game never execs a half-written cfg.
            var temp = path + ".tmp";
            File.WriteAllText(temp, content, NoBom);
            try
            {
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (IOException)
            {
                File.WriteAllText(path, content, NoBom);
                TryDelete(temp);
            }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
