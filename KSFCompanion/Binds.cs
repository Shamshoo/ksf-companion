using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace KsfCompanion
{
    /// <summary>
    /// Something a key can do: a KSF command (sent from the console, so nothing is typed in chat), turning, or one
    /// of KSF Companion's own keys.
    /// </summary>
    sealed class BindAction
    {
        public string Id, Group, Name, Help;
        /// <summary>What the key runs; for a held key the "+" command (its "-" is the same with a minus).</summary>
        public string Command;
        /// <summary>Other names KSF's timer takes for the same thing (sm_r for sm_restart...): binds to these show on the row too.</summary>
        public string[] Also = new string[0];
        public bool Hold;
        /// <summary>One of KSF Companion's own keys (key_save, key_card, key_list): it can be moved, not removed.</summary>
        public string AppSetting;
        public bool IsCustom => Id.StartsWith(BindCatalog.CustomPrefix, StringComparison.Ordinal);
        public bool IsTurn => Command == "+left" || Command == "+right";
    }

    /// <summary>KSF's commands (from ksf.surf/commands) that make sense on a key, grouped like the binds page shows them.</summary>
    static class BindCatalog
    {
        public const string CustomPrefix = "custom:";
        public const string Movement = "Movement", Timer = "Timer", Practice = "Practice", View = "View",
            Info = "Info (KSF answers in your chat - only you see it)", Server = "Server", Vip = "VIP", App = "KSF Companion", Own = "Your own commands";

        public static readonly string[] Groups = { Movement, Timer, Practice, View, Info, Server, Vip, App, Own };

        public static readonly BindAction[] All =
        {
            A(Movement, "turn_left", "Turn left", "+left", "Turns left while held, at the turn speed below", hold: true),
            A(Movement, "turn_right", "Turn right", "+right", "Turns right while held, at the turn speed below", hold: true),

            A(Timer, "restart", "Restart", "sm_restart", "Back to the map start", also: new[] { "sm_r", "sm_start" }),
            A(Timer, "restart_stage", "Restart stage", "sm_teleport", "Back to the start of the stage or bonus you're on (or to your saved location, if you saved one)", also: new[] { "sm_stuck", "sm_s", "sm_back" }),
            A(Timer, "goback", "Previous stage", "sm_goback", "Back to the previous stage's start"),
            A(Timer, "repeat", "Repeat stage", "sm_repeat", "On/off: a stage's end puts you back at its start"),
            A(Timer, "startpos", "Set start position", "sm_startpos", "Where restarts put you in this zone"),
            A(Timer, "surftimer", "Timer options", "sm_surftimer", "KSF's timer menu"),

            A(Practice, "saveloc", "Save location", "sm_saveloc", "Saves where you are, your speed and direction"),
            A(Practice, "loadloc", "Load location", "sm_tele", "Back to the location you saved last", also: new[] { "sm_loadloc" }),
            A(Practice, "teleprev", "Previous saved location", "sm_teleprev", "Goes to the location you saved before"),
            A(Practice, "telenext", "Next saved location", "sm_telenext", "Goes to the location you saved after"),
            A(Practice, "slm", "Saved locations menu", "sm_slm", "KSF's saveloc menu"),
            A(Practice, "speed", "Speed menu", "sm_speed", "Slow down or speed up the game for you"),
            A(Practice, "showzones", "Show zones", "sm_showzones", "Makes the timer's zones visible"),
            A(Practice, "showtriggers", "Show teleport triggers", "sm_showtriggers_toggle", "Makes the map's teleport triggers visible"),

            A(View, "hide", "Hide players", "sm_hide", "Hides the other players (again to show them)"),
            A(View, "hidechat", "Hide chat", "sm_hidechat", "Hides chat (again to show it)"),
            A(View, "keys", "Key overlay", "sm_keys", "Shows or hides the key input overlay"),
            A(View, "spec", "Spectate", "sm_spec", "Spectate a player or bot", also: new[] { "sm_spectate" }),
            A(View, "specbot", "Spectate the record bot", "sm_specbot", "Watch the replay of the record"),

            A(Info, "mapinfo", "Map info", "sm_m", "Tier, stages and bonuses of the map", also: new[] { "sm_mapinfo" }),
            A(Info, "mapinfo_more", "Map details", "sm_mi", "Detailed map info, with the group cutoffs"),
            A(Info, "pr", "Personal record", "sm_pr", "Your time on this map"),
            A(Info, "wr", "World record", "sm_wr", "The map's record"),
            A(Info, "wrcp", "Stage record", "sm_wrcp", "The record of the stage you're on"),
            A(Info, "mrank", "Map rank", "sm_mrank", "Your rank on this map"),
            A(Info, "rank", "Rank", "sm_rank", "Your global rank"),
            A(Info, "cpr", "Checkpoints vs WR", "sm_cpr", "Your checkpoints compared to the record's"),

            A(Server, "rtv", "Rock the vote", "sm_rtv", "Votes to change the map", also: new[] { "sm_rockthevote" }),
            A(Server, "nominate", "Nominate", "sm_nominate", "Opens the nominate menu"),
            A(Server, "votemap", "Vote map", "sm_votemap", "Vote to extend or switch the map"),
            A(Server, "nextmap", "Next map", "sm_nextmap", "Shows the next map"),
            A(Server, "hop", "Switch server", "sm_hop", "KSF's server list"),

            // KSF VIP only (ksf.surf/commands marks them "vip"): the server ignores them without it.
            A(Vip, "replays", "Replays", "sm_replays", "Menu of the replays you can watch"),
            A(Vip, "thirdperson", "Third person", "sm_thirdperson", "Switches between first and third person"),
            A(Vip, "fov", "Field of view", "sm_fov", "Changes your FOV"),
            A(Vip, "knife", "Knife", "sm_knife", "Gives you a knife"),
            A(Vip, "paint", "Paint (hold)", "+paint", "Paints while held", hold: true),
            A(Vip, "paintmenu", "Paint menu", "sm_paintmenu", "All the paint options"),
            A(Vip, "savepaint", "Save paint", "sm_savepaint", "Saves your paint to KSF"),
            A(Vip, "loadpaint", "Load paint", "sm_loadpaint", "Loads your saved paint"),
            A(Vip, "clearpaint", "Clear paint", "sm_clearpaint", "Deletes your paint, here and saved"),
            A(Vip, "makezone", "Make practice zone", "sm_makezone", "Creates a custom practice zone"),
            A(Vip, "customzones", "Custom zones", "sm_customzones", "Shows the zones you've made"),
            A(Vip, "trails", "Trail", "sm_trails", "Sets a custom trail"),
            A(Vip, "vipmodel", "Player model", "sm_vipmodel", "Changes your player model"),
            A(Vip, "size", "Player size", "sm_size", "Changes your player size"),
            A(Vip, "demos", "Demos", "sm_demos", "Recent demos, to upload them"),
            A(Vip, "cvote", "Custom vote", "sm_cvote", "Starts a custom vote"),
            A(Vip, "cancelvote", "Cancel vote", "sm_cancelvote", "Cancels the custom vote"),
            A(Vip, "vipstatus", "VIP status", "sm_vipstatus", "Your VIP and when it runs out"),

            new BindAction { Group = App, Id = "app_save", Name = "Save map for later", Command = "ksf_save", AppSetting = "key_save",
                Help = "Puts the map on your play-later list" },
            new BindAction { Group = App, Id = "app_card", Name = "Map card (hold)", Command = "+ksf_card", Hold = true, AppSetting = "key_card",
                Help = "Shows the map's KSF info in the console while held" },
            new BindAction { Group = App, Id = "app_list", Name = "Play-later list (hold)", Command = "+ksf_list", Hold = true, AppSetting = "key_list",
                Help = "Shows your play-later list in the console while held" },
        };

        static BindAction A(string group, string id, string name, string command, string help, bool hold = false, string[] also = null) =>
            new BindAction { Group = group, Id = id, Name = name, Command = command, Help = help, Hold = hold, Also = also ?? new string[0] };

        public static BindAction Find(string id) => All.FirstOrDefault(a => a.Id == id);

        /// <summary>The action a bind in your game does (bind "r" "sm_restart" is Restart), or null.</summary>
        public static BindAction Matching(string command)
        {
            var c = Regex.Replace((command ?? "").Trim(), @"\s+", " ").ToLowerInvariant();
            if (c.Length == 0) return null;
            return All.FirstOrDefault(a => a.AppSetting == null && (a.Command == c || a.Also.Contains(c)));
        }

        // A command of your own: a name and arguments, nothing that could chain commands (no ; or quotes).
        static readonly Regex OwnCommand = new Regex(@"^[A-Za-z_][A-Za-z0-9_]{0,40}( [A-Za-z0-9_@.:+\-]{1,40}){0,4}$");

        /// <summary>A command typed on the binds page, cleaned up ("!r" and "/r" become sm_r), or null when it can't be bound.</summary>
        public static string CleanOwnCommand(string text)
        {
            var command = Regex.Replace((text ?? "").Trim(), @"\s+", " ");
            if (command.StartsWith("!", StringComparison.Ordinal) || command.StartsWith("/", StringComparison.Ordinal)) command = "sm_" + command.Substring(1);
            if (!OwnCommand.IsMatch(command)) return null;
            // Chat commands would be seen by everyone - the point of the binds is that nothing is.
            var name = command.Split(' ')[0].ToLowerInvariant();
            if (name == "say" || name == "say_team") return null;
            return command;
        }

        public static BindAction OwnAction(string command) => new BindAction
        {
            Group = Own, Id = CustomPrefix + command, Name = command, Command = command,
            Help = "Runs " + command + " from the console",
        };
    }

    /// <summary>The keys you've given actions: action id to game key name, saved in settings.ini as "restart=r|saveloc=MOUSE4".</summary>
    sealed class BindSet
    {
        public readonly Dictionary<string, string> Keys = new Dictionary<string, string>(StringComparer.Ordinal);

        public static BindSet Parse(string text)
        {
            var set = new BindSet();
            foreach (var part in (text ?? "").Split('|'))
            {
                var eq = part.LastIndexOf('=');
                if (eq <= 0) continue;
                var id = part.Substring(0, eq).Trim();
                var key = GameKeys.Normalize(part.Substring(eq + 1).Trim());
                if (key == null) continue;
                if (id.StartsWith(BindCatalog.CustomPrefix, StringComparison.Ordinal))
                {
                    var command = BindCatalog.CleanOwnCommand(id.Substring(BindCatalog.CustomPrefix.Length));
                    if (command == null) continue;
                    id = BindCatalog.CustomPrefix + command;
                }
                else if (BindCatalog.Find(id) == null || BindCatalog.Find(id).AppSetting != null) continue;
                set.Keys[id] = key;
            }
            return set;
        }

        public override string ToString() => string.Join("|", Keys.Select(k => k.Key + "=" + k.Value));
    }

    /// <summary>Source engine key names (what "bind" takes), from Windows keys and mouse buttons, and how to show them.</summary>
    static class GameKeys
    {
        static readonly HashSet<string> Named = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SPACE", "TAB", "ENTER", "BACKSPACE", "CAPSLOCK", "SHIFT", "RSHIFT", "CTRL", "RCTRL", "ALT", "RALT",
            "INS", "DEL", "HOME", "END", "PGUP", "PGDN", "UPARROW", "DOWNARROW", "LEFTARROW", "RIGHTARROW",
            "PAUSE", "SCROLLLOCK", "NUMLOCK", "SEMICOLON",
            "KP_INS", "KP_END", "KP_DOWNARROW", "KP_PGDN", "KP_LEFTARROW", "KP_5", "KP_RIGHTARROW", "KP_HOME", "KP_UPARROW", "KP_PGUP",
            "KP_SLASH", "KP_MULTIPLY", "KP_MINUS", "KP_PLUS", "KP_ENTER", "KP_DEL",
            "MOUSE2", "MOUSE3", "MOUSE4", "MOUSE5", "MWHEELUP", "MWHEELDOWN",
        };
        const string Punctuation = "',-./=[]";

        /// <summary>The key name as the game writes it (letters lower case, the rest upper case), or null if it isn't one we bind.</summary>
        public static string Normalize(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (key.Length == 1)
            {
                var c = char.ToLowerInvariant(key[0]);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || Punctuation.IndexOf(c) >= 0) return c.ToString();
                return null;
            }
            var upper = key.ToUpperInvariant();
            if (Named.Contains(upper)) return upper;
            if (upper.Length >= 2 && upper[0] == 'F' && int.TryParse(upper.Substring(1), out var f) && f >= 1 && f <= 12) return upper;
            return null;
        }

        /// <summary>"R", "Mouse 4", "Num 1", "Space", "Wheel up".</summary>
        public static string Label(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (key.Length == 1) return key.ToUpperInvariant();
            switch (key.ToUpperInvariant())
            {
                case "MWHEELUP": return "Wheel up";
                case "MWHEELDOWN": return "Wheel down";
                case "UPARROW": return "Up";
                case "DOWNARROW": return "Down";
                case "LEFTARROW": return "Left";
                case "RIGHTARROW": return "Right";
                case "PGUP": return "Page up";
                case "PGDN": return "Page down";
                case "INS": return "Insert";
                case "DEL": return "Delete";
                case "RSHIFT": return "Right Shift";
                case "RCTRL": return "Right Ctrl";
                case "RALT": return "Right Alt";
                case "SEMICOLON": return ";";
                case "CAPSLOCK": return "Caps Lock";
                case "SCROLLLOCK": return "Scroll Lock";
                case "NUMLOCK": return "Num Lock";
                case "KP_INS": return "Num 0";
                case "KP_END": return "Num 1";
                case "KP_DOWNARROW": return "Num 2";
                case "KP_PGDN": return "Num 3";
                case "KP_LEFTARROW": return "Num 4";
                case "KP_5": return "Num 5";
                case "KP_RIGHTARROW": return "Num 6";
                case "KP_HOME": return "Num 7";
                case "KP_UPARROW": return "Num 8";
                case "KP_PGUP": return "Num 9";
                case "KP_SLASH": return "Num /";
                case "KP_MULTIPLY": return "Num *";
                case "KP_MINUS": return "Num -";
                case "KP_PLUS": return "Num +";
                case "KP_ENTER": return "Num Enter";
                case "KP_DEL": return "Num .";
            }
            if (key.StartsWith("MOUSE", StringComparison.OrdinalIgnoreCase)) return "Mouse " + key.Substring(5);
            if (key.Length > 1 && key[0] == 'F' && char.IsDigit(key[1])) return key.ToUpperInvariant();
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.ToLowerInvariant());
        }

        /// <summary>Keys you need for playing anyway: binding them gets a warning.</summary>
        public static bool IsMovementKey(string key) =>
            key == "w" || key == "a" || key == "s" || key == "d" || key == "SPACE" || key == "CTRL";
    }
}
