using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace KsfCompanion.Ui
{
    /// <summary>One part of the dashboard that can be hidden, for the Customize list.</summary>
    sealed class LayoutPart : INotifyPropertyChanged
    {
        readonly DashboardLayout layout;

        public LayoutPart(DashboardLayout layout, string key, string label, bool left)
        {
            this.layout = layout;
            Key = key;
            Label = label;
            IsLeft = left;
        }

        public string Key { get; }
        public string Label { get; }
        /// <summary>In the left (map) column; the others are on the right.</summary>
        public bool IsLeft { get; }

        public bool IsShown
        {
            get => layout[Key];
            set => layout[Key] = value;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        internal void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShown)));
    }

    /// <summary>
    /// What's on the dashboard: the parts you haven't hidden (bound as Layout[key], true = shown), and the view -
    /// Simple (the essentials, bigger and cleaner) or Advanced (every detail).
    /// </summary>
    sealed class DashboardLayout : INotifyPropertyChanged
    {
        readonly HashSet<string> hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool simple;
        double scale = 1;

        public DashboardLayout()
        {
            Parts = new ObservableCollection<LayoutPart>
            {
                new LayoutPart(this, "map", "Map picture", true),
                new LayoutPart(this, "timer", "Time left (big)", true),
                new LayoutPart(this, "numbers", "Record, your best, gap, finishes", true),
                new LayoutPart(this, "times", "Your times", true),
                new LayoutPart(this, "leaderboard", "Leaderboard", true),
                new LayoutPart(this, "keys", "In-game keys", true),
                new LayoutPart(this, "live", "Your server, live", false),
                new LayoutPart(this, "session", "This session", false),
                new LayoutPart(this, "level", "Your level", false),
                new LayoutPart(this, "later", "Play later", false),
                new LayoutPart(this, "servers", "KSF servers", false),
                new LayoutPart(this, "recent", "Your recent records", false),
            };
        }

        public ObservableCollection<LayoutPart> Parts { get; }

        public event PropertyChangedEventHandler PropertyChanged;
        /// <summary>Something was shown, hidden or switched by you (to save it).</summary>
        public event Action Changed;

        public bool this[string key]
        {
            get => !hidden.Contains(key);
            set
            {
                if (!(value ? hidden.Remove(key) : hidden.Add(key))) return;
                Raise();
                Changed?.Invoke();
            }
        }

        public bool IsSimple
        {
            get => simple;
            set
            {
                if (simple == value) return;
                simple = value;
                Raise();
                Changed?.Invoke();
            }
        }

        public bool IsAdvanced => !simple;

        /// <summary>How big everything is drawn (1 = normal), from the Size slider in Customize.</summary>
        public double Scale
        {
            get => scale;
            set
            {
                value = Math.Round(Math.Max(0.8, Math.Min(1.5, value)), 2);
                if (Math.Abs(scale - value) < 0.001) return;
                scale = value;
                Raise();
                Changed?.Invoke();
            }
        }

        /// <summary>"110%"</summary>
        public string ScaleText => $"{Math.Round(scale * 100)}%";
        public IEnumerable<string> Hidden => hidden.OrderBy(k => k);
        public int HiddenCount => hidden.Count;
        /// <summary>"2 hidden", or nothing.</summary>
        public string HiddenText => hidden.Count == 0 ? "" : $"{hidden.Count} hidden";

        /// <summary>Whether anything on that side (left: the map; right: servers, lists) is still on show.</summary>
        public bool AnyShown(bool left) => Parts.Any(p => p.IsLeft == left && p.Key != "timer" && p.IsShown);

        /// <summary>From settings: the hidden parts, the view and the size, without counting as a change.</summary>
        public void Load(IEnumerable<string> hiddenKeys, bool simpleView, double size = 1)
        {
            hidden.Clear();
            foreach (var key in hiddenKeys.Select(k => k.Trim()).Where(k => Parts.Any(p => p.Key.Equals(k, StringComparison.OrdinalIgnoreCase))))
                hidden.Add(key);
            simple = simpleView;
            scale = Math.Round(Math.Max(0.8, Math.Min(1.5, size)), 2);
            Raise();
        }

        public void ShowAll()
        {
            if (hidden.Count == 0) return;
            hidden.Clear();
            Raise();
            Changed?.Invoke();
        }

        void Raise()
        {
            // "Item[]" updates every Layout[key] binding.
            foreach (var name in new[] { "Item[]", nameof(IsSimple), nameof(IsAdvanced), nameof(HiddenCount), nameof(HiddenText), nameof(Scale), nameof(ScaleText) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            foreach (var part in Parts) part.Raise();
        }
    }
}
