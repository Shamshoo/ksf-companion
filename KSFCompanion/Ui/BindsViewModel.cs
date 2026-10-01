using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace KsfCompanion.Ui
{
    sealed class BindRow : Observable
    {
        string key, replaces;
        bool capturing;

        public BindRow(BindAction action) { Action = action; }

        public BindAction Action { get; }
        public string Name => Action.Name;
        public string Help => Action.Help;
        /// <summary>What it runs, as shown on the row: "sm_restart", "+left", or KSF Companion's own key.</summary>
        public string CommandText => Action.AppSetting != null ? "KSF Companion" : Action.Command;
        public bool CanClear => Action.AppSetting == null;
        public bool IsOwn => Action.IsCustom;

        public string Key
        {
            get => key;
            set
            {
                if (!Set(ref key, value)) return;
                Raise(nameof(KeyLabel));
                Raise(nameof(HasKey));
            }
        }
        public string KeyLabel => IsCapturing ? "Press a key..." : key != null ? GameKeys.Label(key) : InGame.Count > 0 ? "Add key" : "Set key";
        public bool HasKey => key != null;

        /// <summary>Keys bound to this in your game already (bind "r" "sm_restart" in config.cfg), not by KSF Companion.</summary>
        public ObservableCollection<GameKeyChip> InGame { get; } = new ObservableCollection<GameKeyChip>();
        public bool HasInGame => InGame.Count > 0;

        internal void SetInGame(IEnumerable<string> keys)
        {
            var list = keys.ToList();
            if (list.SequenceEqual(InGame.Select(c => c.Key))) return;
            InGame.Clear();
            foreach (var k in list) InGame.Add(new GameKeyChip { Key = k, Row = this });
            Raise(nameof(HasInGame));
            Raise(nameof(KeyLabel));
        }

        public bool IsCapturing
        {
            get => capturing;
            set
            {
                if (!Set(ref capturing, value)) return;
                Raise(nameof(KeyLabel));
            }
        }

        /// <summary>"replaces +reload" - what the key did in the game before (put back when the bind is removed).</summary>
        public string Replaces { get => replaces; set { if (Set(ref replaces, value)) Raise(nameof(HasReplaces)); } }
        public bool HasReplaces => replaces != null;

        internal string SearchText => (Name + " " + Help + " " + Action.Command + " " + string.Join(" ", Action.Also) + " " + Action.Group + " "
            + string.Join(" ", InGame.Select(c => c.Label + " " + c.Key)) + " " + (key == null ? "" : GameKeys.Label(key) + " " + key)).ToLowerInvariant();
    }

    /// <summary>A key your game already has on an action (its own bind, not one KSF Companion made).</summary>
    sealed class GameKeyChip
    {
        public string Key { get; set; }
        public BindRow Row { get; set; }
        public string Label => GameKeys.Label(Key);
        public string Tip => $"{Label} does this in your game already (your own bind). x takes it off.";
    }

    sealed class BindGroup
    {
        public string Title { get; set; }
        public ObservableCollection<BindRow> Rows { get; } = new ObservableCollection<BindRow>();
        /// <summary>The movement group carries the turn speed slider.</summary>
        public bool HasTurnSpeed { get; set; }
        public bool IsOwn { get; set; }
        /// <summary>KSF VIP commands: their own outlined section.</summary>
        public bool IsVip { get; set; }
    }

    /// <summary>One key moved: the action, the key it had (null: none) and the one it has now (null: none).</summary>
    sealed class BindChange
    {
        public BindAction Action;
        public string OldKey, NewKey;
    }

    /// <summary>The binds page: KSF commands (and turning) on keys, with a search; key changes go to Companion to put in the game.</summary>
    sealed class BindsViewModel : Observable
    {
        readonly List<BindRow> rows = new List<BindRow>();
        Func<string, string> originalOf = _ => null;
        // What keys do in your game (config.cfg), apart from the binds KSF Companion made: key -> command.
        Dictionary<string, string> gameBinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string search = "", status = "", ownCommand = "";
        double turnSpeed = 210;
        BindRow capturing;

        public BindsViewModel()
        {
            foreach (var action in BindCatalog.All) rows.Add(new BindRow(action));
            CaptureCommand = new RelayCommand(p => BeginCapture(p as BindRow));
            ClearCommand = new RelayCommand(p => Clear(p as BindRow));
            RemoveOwnCommand = new RelayCommand(p => RemoveOwn(p as BindRow));
            AddOwnCommand = new RelayCommand(_ => AddOwn());
            ClearSearchCommand = new RelayCommand(_ => Search = "");
            RemoveGameKeyCommand = new RelayCommand(p => RemoveGameKey(p as GameKeyChip));
            Rebuild();
        }

        public ObservableCollection<BindGroup> Groups { get; } = new ObservableCollection<BindGroup>();
        public ICommand CaptureCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand RemoveOwnCommand { get; }
        public ICommand AddOwnCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand RemoveGameKeyCommand { get; }

        /// <summary>One of your own binds was taken off on the page: the key should do nothing now.</summary>
        public event Action<string> GameKeyRemoved;

        /// <summary>A key was set, moved or taken away.</summary>
        public event Action<BindChange> Changed;
        /// <summary>The turn speed slider moved.</summary>
        public event Action<int> TurnSpeedChanged;
        /// <summary>Something to tell you (shown as a toast).</summary>
        public event Action<string> Message;

        public string Search
        {
            get => search;
            set
            {
                if (!Set(ref search, value ?? "")) return;
                Raise(nameof(HasSearch));
                Rebuild();
            }
        }
        public bool HasSearch => search.Length > 0;
        public string Status { get => status; private set => Set(ref status, value); }
        public bool IsCapturing => capturing != null;

        /// <summary>Typed in the "your own command" box.</summary>
        public string OwnCommand { get => ownCommand; set => Set(ref ownCommand, value ?? ""); }

        /// <summary>cl_yawspeed: how fast the turn keys turn, in degrees a second.</summary>
        public double TurnSpeed
        {
            get => turnSpeed;
            set
            {
                var speed = Math.Round(Math.Max(50, Math.Min(600, value)) / 5) * 5;
                if (!Set(ref turnSpeed, speed)) return;
                Raise(nameof(TurnSpeedText));
                TurnSpeedChanged?.Invoke((int)speed);
            }
        }
        public string TurnSpeedText => $"{turnSpeed:0}°/s";

        /// <summary>
        /// The keys you've set, KSF Companion's own keys, and what keys did in the game before they were taken over
        /// (shown as "replaces ..."). Doesn't raise Changed.
        /// </summary>
        public void Load(BindSet set, IDictionary<string, string> appKeys, int speed, Func<string, string> original)
        {
            originalOf = original ?? (_ => null);
            turnSpeed = Math.Max(50, Math.Min(600, speed));
            Raise(nameof(TurnSpeed));
            Raise(nameof(TurnSpeedText));
            rows.RemoveAll(r => r.IsOwn);
            foreach (var id in set.Keys.Keys.Where(k => k.StartsWith(BindCatalog.CustomPrefix, StringComparison.Ordinal)))
                rows.Add(new BindRow(BindCatalog.OwnAction(id.Substring(BindCatalog.CustomPrefix.Length))));
            foreach (var row in rows)
            {
                row.Key = row.Action.AppSetting != null
                    ? (appKeys.TryGetValue(row.Action.Id, out var appKey) ? appKey : null)
                    : (set.Keys.TryGetValue(row.Action.Id, out var key) ? key : null);
            }
            RefreshInGame();
            RefreshReplaces();
            Rebuild();
        }

        /// <summary>
        /// What keys do in your game (config.cfg, without KSF Companion's binds): shown on the rows they belong to;
        /// other sm_ commands you've bound get a row under your own commands.
        /// </summary>
        public void SetGameBinds(IDictionary<string, string> binds)
        {
            gameBinds = new Dictionary<string, string>(binds ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var command in gameBinds.Values.Where(c => BindCatalog.Matching(c) == null).Select(OwnCommandOf).Where(c => c != null).Distinct())
                if (!rows.Any(r => r.IsOwn && r.Action.Command == command)) rows.Add(new BindRow(BindCatalog.OwnAction(command)));
            RefreshInGame();
            RefreshReplaces();
            Rebuild();
        }

        // A bind of yours to a KSF command that isn't in the list (sm_noclip...).
        static string OwnCommandOf(string command)
        {
            var clean = BindCatalog.CleanOwnCommand(command);
            return clean != null && clean.StartsWith("sm_", StringComparison.OrdinalIgnoreCase) ? clean : null;
        }

        bool Does(BindRow row, string command)
        {
            if (row.Action.AppSetting != null) return false;
            var match = BindCatalog.Matching(command);
            return match != null ? match == row.Action : row.IsOwn && OwnCommandOf(command) == row.Action.Command;
        }

        /// <summary>Your own binds on the rows they do - except keys KSF Companion has taken over since.</summary>
        void RefreshInGame()
        {
            var ours = new HashSet<string>(rows.Where(r => r.Key != null).Select(r => r.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
                row.SetInGame(gameBinds.Where(b => !ours.Contains(b.Key) && Does(row, b.Value)).Select(b => b.Key)
                    .OrderBy(k => k.Length).ThenBy(k => k, StringComparer.OrdinalIgnoreCase));
        }

        void RemoveGameKey(GameKeyChip chip)
        {
            if (chip == null) return;
            CancelCapture();
            gameBinds.Remove(chip.Key);
            GameKeyRemoved?.Invoke(chip.Key);
            Message?.Invoke($"{chip.Label} doesn't do \"{chip.Row.Name}\" any more");
            RefreshInGame();
            Rebuild();
        }

        /// <summary>The game may have new binds of its own since (config.cfg is rewritten when it closes).</summary>
        public void RefreshReplaces()
        {
            foreach (var row in rows)
            {
                var before = row.Key == null || row.Action.AppSetting != null ? null : originalOf(row.Key);
                // Your own bind for the same thing isn't worth a "replaces".
                row.Replaces = before == null || Does(row, before) ? null : "replaces " + before;
            }
        }

        void Rebuild()
        {
            var words = search.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Groups.Clear();
            foreach (var title in BindCatalog.Groups)
            {
                var own = title == BindCatalog.Own;
                var group = new BindGroup { Title = title, HasTurnSpeed = title == BindCatalog.Movement, IsOwn = own, IsVip = title == BindCatalog.Vip };
                foreach (var row in rows.Where(r => r.Action.Group == title && words.All(w => r.SearchText.Contains(w)))) group.Rows.Add(row);
                // Your own commands' box is there even when you have none yet (unless a search hides it).
                if (group.Rows.Count > 0 || (own && words.Length == 0)) Groups.Add(group);
            }
            var set = rows.Where(r => r.Action.AppSetting == null).Sum(r => (r.HasKey ? 1 : 0) + r.InGame.Count);
            Status = words.Length > 0 && Groups.Count == 0 ? "Nothing matches"
                   : set == 0 ? "No keys set yet" : set == 1 ? "1 key bound" : $"{set} keys bound";
        }

        void BeginCapture(BindRow row)
        {
            if (row == null) return;
            var again = capturing == row;
            CancelCapture();
            if (again) return;
            capturing = row;
            row.IsCapturing = true;
            Raise(nameof(IsCapturing));
        }

        public void CancelCapture()
        {
            if (capturing == null) return;
            capturing.IsCapturing = false;
            capturing = null;
            Raise(nameof(IsCapturing));
        }

        /// <summary>The key pressed while a row was waiting for one (a game key name).</summary>
        public void Capture(string key)
        {
            var row = capturing;
            CancelCapture();
            key = GameKeys.Normalize(key);
            if (row == null || key == null) return;
            if (key == row.Key) return;
            if (row.InGame.Any(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)))
            {
                Message?.Invoke($"{GameKeys.Label(key)} already does \"{row.Name}\" in your game");
                return;
            }
            var holder = rows.FirstOrDefault(r => r != row && r.Key == key);
            if (holder != null && holder.Action.AppSetting != null)
            {
                Message?.Invoke($"{GameKeys.Label(key)} is KSF Companion's \"{holder.Name}\" key - give that one another key first");
                return;
            }
            if (row.Action.AppSetting != null && holder != null)
            {
                Message?.Invoke($"{GameKeys.Label(key)} is already \"{holder.Name}\" - take it off there first");
                return;
            }
            if (holder != null)
            {
                var old = holder.Key;
                holder.Key = null;
                Changed?.Invoke(new BindChange { Action = holder.Action, OldKey = old, NewKey = null });
                Message?.Invoke($"{GameKeys.Label(key)} moved from \"{holder.Name}\" to \"{row.Name}\"");
            }
            else if (GameKeys.IsMovementKey(key))
            {
                Message?.Invoke($"Careful: {GameKeys.Label(key)} is a movement key - it now does \"{row.Name}\" instead");
            }
            var before = row.Key;
            row.Key = key;
            Changed?.Invoke(new BindChange { Action = row.Action, OldKey = before, NewKey = key });
            RefreshInGame();
            RefreshReplaces();
            Rebuild();
        }

        void Clear(BindRow row)
        {
            if (row?.Key == null || !row.CanClear) return;
            CancelCapture();
            var old = row.Key;
            row.Key = null;
            row.Replaces = null;
            Changed?.Invoke(new BindChange { Action = row.Action, OldKey = old, NewKey = null });
            // The key gets back what it did before - which may be one of these actions too.
            var before = originalOf(old);
            if (before != null) gameBinds[old] = before;
            else gameBinds.Remove(old);
            RefreshInGame();
            Rebuild();
        }

        void AddOwn()
        {
            var command = BindCatalog.CleanOwnCommand(ownCommand);
            if (command == null)
            {
                Message?.Invoke(string.IsNullOrWhiteSpace(ownCommand) ? "Type a command first, like sm_r or !stage 2"
                    : "That can't be bound here: one command and its words (no ; or quotes, and not say)");
                return;
            }
            var row = rows.FirstOrDefault(r => r.IsOwn && r.Action.Command == command);
            if (row == null)
            {
                row = new BindRow(BindCatalog.OwnAction(command));
                rows.Add(row);
            }
            OwnCommand = "";
            Search = "";
            Rebuild();
            BeginCapture(row);
        }

        void RemoveOwn(BindRow row)
        {
            if (row == null || !row.IsOwn) return;
            if (row.Key != null) Clear(row);
            rows.Remove(row);
            Rebuild();
        }
    }
}
