using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace KsfCompanion.Ui
{
    abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }

        protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    sealed class RelayCommand : ICommand
    {
        readonly Action<object> run;

        public RelayCommand(Action<object> run) => this.run = run;

        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => run(parameter);
    }

    enum Connection { Offline, Waiting, Connected, Limited }

    /// <summary>A map on the nominate page, as a list row or a tile.</summary>
    sealed class MapResultRow : Observable
    {
        ImageSource thumb;
        bool isSaved, isDone;
        string doneText = "", doneTip;

        public string Map { get; set; }
        public int Tier { get; set; }
        public string TierText { get; set; }
        public Brush TierBrush { get; set; }
        public Brush TierSoftBrush { get; set; }
        /// <summary>"4 stages  ·  7 bonuses"</summary>
        public string Details { get; set; }
        public string Rating { get; set; }
        public bool HasRating { get; set; }
        public string Mappers { get; set; }
        /// <summary>The map you're playing right now.</summary>
        public bool IsCurrent { get; set; }
        public ImageSource Thumb { get => thumb; set => Set(ref thumb, value); }
        /// <summary>In your play-later list.</summary>
        public bool IsSaved { get => isSaved; set => Set(ref isSaved, value); }
        /// <summary>You've finished it (on the tick and style on show).</summary>
        public bool IsDone { get => isDone; set => Set(ref isDone, value); }
        /// <summary>Your time on it: "1:02.144".</summary>
        public string DoneText { get => doneText; set => Set(ref doneText, value); }
        public string DoneTip { get => doneTip; set => Set(ref doneTip, value); }
    }

    /// <summary>Your KSF title on one tick rate (66 and 100 tick are ranked separately) and what the next title takes.</summary>
    sealed class LevelRow
    {
        /// <summary>"66T" / "100T"</summary>
        public string Tick { get; set; }
        /// <summary>"PROFICIENT"</summary>
        public string Title { get; set; }
        /// <summary>"#4,962  ·  7,835 pts"</summary>
        public string Standing { get; set; }
        /// <summary>"#242 in Canada  ·  211 maps done"</summary>
        public string Extra { get; set; }
        /// <summary>"1,165 pts to ADEPT"</summary>
        public string Next { get; set; }
        /// <summary>"ADEPT from 9,000 pts" / "EXCEPTIONAL is the top 500  ·  #500 has 65,700 pts"</summary>
        public string NextDetail { get; set; }
        /// <summary>From this title's start to the next one (0..1).</summary>
        public double Progress { get; set; }
        /// <summary>The tick rate you're playing on.</summary>
        public bool IsCurrent { get; set; }
    }

    /// <summary>One of the leaderboards you can switch to: MAP, S1, S2... B1, B2...</summary>
    sealed class LeaderChip : Observable
    {
        bool isSelected;
        public int Zone { get; set; }
        public string Label { get; set; }
        public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }
    }

    sealed class LeaderRow
    {
        public string Rank { get; set; }
        public Brush RankBrush { get; set; }
        public string Name { get; set; }
        public string Time { get; set; }
        public string Gap { get; set; }
        public bool IsYou { get; set; }
        public bool IsGap { get; set; }
    }

    /// <summary>
    /// Your result on one of the map's leaderboards - the map itself, a stage or a bonus: your time, how far off the
    /// record it is and where you are on that leaderboard.
    /// </summary>
    sealed class ZoneRow : Observable
    {
        bool isCurrent, isFresh, done, isWorst, isNew;
        string time, gap, rank, rankTotal, tip;
        Brush gapBrush, rankBrush;
        double closeness;

        public int Zone { get; set; }
        public string Label { get; set; }
        // These change in place as records arrive and as you play.
        public string Time { get => time; set => Set(ref time, value); }
        public bool Done { get => done; set => Set(ref done, value); }
        public string Gap { get => gap; set => Set(ref gap, value); }
        public Brush GapBrush { get => gapBrush; set => Set(ref gapBrush, value); }
        /// <summary>"#4,570": your place on this leaderboard.</summary>
        public string Rank { get => rank; set => Set(ref rank, value); }
        /// <summary>"/ 31,958": how many are on it.</summary>
        public string RankTotal { get => rankTotal; set => Set(ref rankTotal, value); }
        public Brush RankBrush { get => rankBrush; set => Set(ref rankBrush, value); }
        /// <summary>How close your time is to the record, 0..1 (1 = the record; empty at twice its time). Drives the bar.</summary>
        public double Closeness { get => closeness; set => Set(ref closeness, value); }
        /// <summary>The stage you lose the most time on.</summary>
        public bool IsWorst { get => isWorst; set => Set(ref isWorst, value); }
        /// <summary>Set in game moments ago; ksf.surf doesn't have it yet, so there's no rank for it yet.</summary>
        public bool IsNew { get => isNew; set => Set(ref isNew, value); }
        public string Tip { get => tip; set => Set(ref tip, value); }
        /// <summary>You're on this stage/bonus right now.</summary>
        public bool IsCurrent { get => isCurrent; set => Set(ref isCurrent, value); }
        /// <summary>Just finished or improved: the row lights up for a few seconds.</summary>
        public bool IsFresh { get => isFresh; set => Set(ref isFresh, value); }
    }

    sealed class LaterRow : Observable
    {
        ImageSource thumb;
        string saved;

        public string Map { get; set; }
        public string Tier { get; set; }
        public Brush TierBrush { get; set; }
        public Brush TierSoftBrush { get; set; }
        public DateTime SavedAt { get; set; }
        public string Saved { get => saved; set => Set(ref saved, value); }
        public bool IsCurrent { get; set; }
        public bool IsLive { get; set; }
        public string Live { get; set; }
        public string JoinAddress { get; set; }
        public ImageSource Thumb { get => thumb; set => Set(ref thumb, value); }

        public void Update() => Saved = SavedAt > DateTime.MinValue ? "saved " + DashboardViewModel.Ago(SavedAt) : "";
    }

    /// <summary>A KSF server; the time left counts down every second between refreshes.</summary>
    sealed class ServerRow : Observable
    {
        string timeLeft;

        public string Name { get; set; }
        public string Map { get; set; }
        public string Tier { get; set; }
        public Brush TierBrush { get; set; }
        public Brush TierSoftBrush { get; set; }
        public string Players { get; set; }
        public string Address { get; set; }
        public bool IsYours { get; set; }
        public bool IsSaved { get; set; }
        public int SecondsLeft { get; set; }
        public DateTime FetchedAt { get; set; }
        public string TimeLeft { get => timeLeft; set => Set(ref timeLeft, value); }

        public void Update(DateTime now) => TimeLeft = DashboardViewModel.Countdown(SecondsLeft - (now - FetchedAt).TotalSeconds);
    }

    /// <summary>Someone on your server: where they are on the map and how long they've been on.</summary>
    sealed class LivePlayerRow : Observable
    {
        string connected;

        public string Name { get; set; }
        public string Zone { get; set; }
        public bool HasZone => !string.IsNullOrEmpty(Zone);
        public Brush ZoneBrush { get; set; }
        public Brush ZoneSoftBrush { get; set; }
        public string Rank { get; set; }
        public bool IsYou { get; set; }
        public int ConnectedAtFetch { get; set; }
        public DateTime FetchedAt { get; set; }
        public string Connected { get => connected; set => Set(ref connected, value); }

        public void Update(DateTime now) => Connected = Format.Duration(ConnectedAtFetch + (now - FetchedAt).TotalSeconds);
    }

    sealed class RecentRow
    {
        public string Kind { get; set; }
        public Brush KindBrush { get; set; }
        public Brush KindSoftBrush { get; set; }
        public string Map { get; set; }
        public string Time { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>Everything the dashboard shows. Companion pushes data in; the XAML binds to it.</summary>
    sealed class DashboardViewModel : Observable
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Brush Gold = Frozen("#F5C542"), Silver = Frozen("#C9CED6"), Bronze = Frozen("#D08A55"), Plain = Frozen("#6F6F7C");
        static readonly Brush GoodBrush = Frozen("#3DDC97"), WaitBrush = Frozen("#6F6F7C"), WarnBrush = Frozen("#F5B83D");
        static readonly string[] TierColors = { "#8A8A96", "#34D399", "#A3E635", "#FACC15", "#FB923C", "#F87171", "#F472B6", "#C084FC", "#818CF8" };

        // header
        const string DefaultEmptyText = "Join a surf map in CS:S and everything about it shows up here.";
        string playerName = "", playerDetail = "", statusText = "Starting...";
        ImageSource avatar;
        Brush statusBrush = WaitBrush;

        // hero
        bool hasMap, isOnKsf, loading, isSaved, hasRating, hasServerLine, is100t;
        string mapName = "No map yet", mapBadge = "WAITING FOR A MAP", tierText, typeText, bonusText, mapperText, ratingText, serverLine,
            emptyText = DefaultEmptyText;
        Brush tierBrush = Plain, tierSoftBrush = Plain;
        ImageSource mapImage;

        // tiles
        bool hasWr, hasPb;
        string wrTime = "--", wrHolder = "", wrDate = "", pbTime = "--", pbRank = "", pbTop = "", gapTime = "--", gapDetail = "",
            groupText = "", finishes = "--", attempts = "", playtime = "";
        double pbBar;

        // lists
        string leaderTitle = "TOP 10", finishersText = "", laterCount = "", serversUpdated = "", toast;
        bool hasLater, hasServers, hasRecent;

        public ObservableCollection<LeaderRow> Leaders { get; } = new ObservableCollection<LeaderRow>();
        public ObservableCollection<LaterRow> Later { get; } = new ObservableCollection<LaterRow>();
        public ObservableCollection<ServerRow> Servers { get; } = new ObservableCollection<ServerRow>();
        public ObservableCollection<RecentRow> Recent { get; } = new ObservableCollection<RecentRow>();
        public ObservableCollection<LivePlayerRow> LivePlayers { get; } = new ObservableCollection<LivePlayerRow>();

        // ----- your times: the map, every stage and every bonus - time, gap to the record, place on the leaderboard.
        // Stages and bonuses are split in two halves: left and right column, or one below the other when narrow.
        static readonly Brush GapBrushNormal = Frozen("#B3B3BE"), GapBrushWorst = Frozen("#FF8A5C"), NotDoneBrush = Frozen("#6F6F7C"),
            RankBrushNormal = Frozen("#B3B3BE"), NewBrush = Frozen("#FF9A45");
        bool hasTimes, hasStages, hasBonuses;
        string stagesSummary = "", bonusesSummary = "", timesKey;
        int? currentZone;

        public ZoneRow MapRow { get; } = new ZoneRow { Zone = 0, Label = "MAP" };
        public ObservableCollection<ZoneRow> StagesA { get; } = new ObservableCollection<ZoneRow>();
        public ObservableCollection<ZoneRow> StagesB { get; } = new ObservableCollection<ZoneRow>();
        public ObservableCollection<ZoneRow> BonusesA { get; } = new ObservableCollection<ZoneRow>();
        public ObservableCollection<ZoneRow> BonusesB { get; } = new ObservableCollection<ZoneRow>();
        public bool HasTimes { get => hasTimes; set => Set(ref hasTimes, value); }
        public bool HasStages { get => hasStages; set => Set(ref hasStages, value); }
        public bool HasBonuses { get => hasBonuses; set => Set(ref hasBonuses, value); }
        public bool HasStagesB => StagesB.Count > 0;
        public bool HasBonusesB => BonusesB.Count > 0;
        public string StagesSummary { get => stagesSummary; set => Set(ref stagesSummary, value); }
        public string BonusesSummary { get => bonusesSummary; set => Set(ref bonusesSummary, value); }

        IEnumerable<ZoneRow> ZoneRows => new[] { MapRow }.Concat(StagesA).Concat(StagesB).Concat(BonusesA).Concat(BonusesB);

        /// <summary>Fills "your times" from the report; called again as records come in and as you finish zones.</summary>
        public void ShowTimes(MapReport r)
        {
            var info = r.Info;
            var stages = r.IsStaged ? info.StageCount : 0;
            var bonuses = Math.Max(0, info?.BonusCount ?? 0);
            // A linear map without bonuses has nothing the tiles above don't already say.
            HasTimes = info != null && stages + bonuses > 0;
            if (!HasTimes)
            {
                ClearTimes();
                return;
            }

            // Same map as before: update the rows in place so nothing flickers.
            var key = r.Game + "|" + r.Map;
            if (key != timesKey || StagesA.Count + StagesB.Count != stages || BonusesA.Count + BonusesB.Count != bonuses)
            {
                timesKey = key;
                Fill(StagesA, StagesB, Enumerable.Range(1, stages));
                Fill(BonusesA, BonusesB, Enumerable.Range(MapReport.FirstBonusZone, bonuses));
                Raise(nameof(HasStagesB));
                Raise(nameof(HasBonusesB));
            }
            HasStages = stages > 0;
            HasBonuses = bonuses > 0;

            ShowZone(MapRow, r, r.Wr);
            var stageRows = StagesA.Concat(StagesB).ToList();
            var bonusRows = BonusesA.Concat(BonusesB).ToList();
            foreach (var row in stageRows.Concat(bonusRows))
            {
                r.ZoneWrs.TryGetValue(row.Zone, out var wr);
                ShowZone(row, r, wr);
            }

            // The stage you lose the most time on stands out.
            var worst = stageRows.Where(x => x.Done && r.ZoneWrs.ContainsKey(x.Zone) && x.Gap != "WR")
                .OrderByDescending(x => r.Zone(x.Zone).Time.Value - r.ZoneWrs[x.Zone].Time).FirstOrDefault();
            if (worst != null && stageRows.Count(x => x.Done) > 1)
            {
                worst.IsWorst = true;
                worst.GapBrush = GapBrushWorst;
            }

            var stagesDone = stageRows.Count(x => x.Done);
            if (stagesDone < stageRows.Count)
                StagesSummary = $"{stagesDone} of {stageRows.Count} done";
            else
            {
                StagesSummary = "sum of best " + Format.Time(stageRows.Sum(x => r.Zone(x.Zone).Time.Value));
                if (stageRows.All(x => r.ZoneWrs.ContainsKey(x.Zone))) StagesSummary += "  ·  records " + Format.Time(stageRows.Sum(x => r.ZoneWrs[x.Zone].Time));
            }
            BonusesSummary = $"{bonusRows.Count(x => x.Done)} of {bonusRows.Count} done";
        }

        void Fill(ObservableCollection<ZoneRow> first, ObservableCollection<ZoneRow> second, IEnumerable<int> zones)
        {
            first.Clear();
            second.Clear();
            var rows = zones.Select(z => new ZoneRow { Zone = z, Label = MapReport.ZoneLabel(z), IsCurrent = currentZone == z }).ToList();
            var firstHalf = rows.Count > 1 ? (rows.Count + 1) / 2 : rows.Count;
            foreach (var row in rows.Take(firstHalf)) first.Add(row);
            foreach (var row in rows.Skip(firstHalf)) second.Add(row);
        }

        void ShowZone(ZoneRow row, MapReport r, WorldRecord wr)
        {
            var zone = row.Zone;
            var mine = r.Zone(zone);
            string Show(double seconds) => zone == 0 ? Format.Time(seconds) : Format.Short(seconds);
            row.IsWorst = false;
            row.GapBrush = NotDoneBrush;
            row.IsNew = mine?.Unsynced == true;
            if (mine?.Time is double time)
            {
                row.Done = true;
                row.Time = Show(time);
                var yours = wr != null && (string.Equals(wr.SteamId, r.SteamId, StringComparison.OrdinalIgnoreCase) || time <= wr.Time + 0.0005);
                row.Gap = wr == null ? "" : yours ? "WR" : Format.Diff(time - wr.Time);
                row.GapBrush = yours ? Gold : GapBrushNormal;
                // The bar (it fills smoothly): how close to the record - full at record pace, empty at twice its time.
                row.Closeness = wr == null ? 0 : yours ? 1 : Math.Max(0.02, Math.Min(1, (wr.Time / time - 0.5) / 0.5));
                if (mine.Unsynced)
                {
                    // Your new rank comes with ksf.surf's next update.
                    row.Rank = "new best";
                    row.RankTotal = "";
                    row.RankBrush = NewBrush;
                }
                else if (mine.Rank > 0 && mine.TotalRanks > 0)
                {
                    row.Rank = $"#{mine.Rank:N0}";
                    row.RankTotal = $" / {mine.TotalRanks:N0}";
                    row.RankBrush = mine.Rank <= 10 ? Gold : RankBrushNormal;
                }
                else
                {
                    row.Rank = row.RankTotal = "";
                }
            }
            else
            {
                row.Done = false;
                row.Time = "--";
                row.Gap = wr != null ? "WR " + Show(wr.Time) : "";
                row.Rank = row.RankTotal = "";
                row.Closeness = 0;
            }

            var tip = new List<string> { $"Click: show the {(zone == 0 ? "map" : MapReport.ZoneName(zone).ToLowerInvariant())} leaderboard  ·  arrow: teleport there" };
            if (wr != null) tip.Add($"{MapReport.ZoneName(zone)} record {Show(wr.Time)} by {wr.Name}");
            if (wr != null && mine?.Time is double yourTime) tip.Add(string.Format(Inv, "you're at {0:0.0}% of record pace", wr.Time / yourTime * 100));
            if (mine?.Time != null && !mine.Unsynced && mine.Rank > 0 && mine.TotalRanks > 0)
                tip.Add($"you're #{mine.Rank:N0} of {mine.TotalRanks:N0} ({TopPercent(mine.Rank.Value, mine.TotalRanks.Value)})");
            if (mine?.Unsynced == true) tip.Add("just set in game - ksf.surf will have your new rank shortly");
            if (mine?.Completions > 0) tip.Add(mine.Completions == 1 ? "done once" : $"done {mine.Completions:N0} times");
            row.Tip = tip.Count > 0 ? string.Join("  ·  ", tip) : null;
        }

        void ClearTimes()
        {
            StagesA.Clear();
            StagesB.Clear();
            BonusesA.Clear();
            BonusesB.Clear();
            timesKey = null;
            HasTimes = HasStages = HasBonuses = false;
            StagesSummary = BonusesSummary = "";
            Raise(nameof(HasStagesB));
            Raise(nameof(HasBonusesB));
        }

        /// <summary>Highlights the stage or bonus you're on (null: none, e.g. in the start zone or spectating).</summary>
        public void SetCurrentZone(int? zone)
        {
            currentZone = zone;
            foreach (var row in ZoneRows) row.IsCurrent = zone != null && zone != 0 && row.Zone == zone;
        }

        /// <summary>Lights up the rows of zones you just finished.</summary>
        public void FlashZones(ICollection<int> zones)
        {
            foreach (var row in ZoneRows.Where(row => zones.Contains(row.Zone)))
            {
                // Off and on again, so a zone finished twice in a row lights up both times.
                row.IsFresh = false;
                row.IsFresh = true;
            }
        }

        // ----- live: your server, the session, the next map, PB celebrations -----
        static readonly Brush StartBrush = Frozen("#9A9AA6"), StartSoftBrush = Frozen("#229A9AA6");
        static readonly Brush ZoneBrush = Frozen("#FF9A45"), ZoneSoftBrush = Frozen("#26FF7A1A");
        static readonly Brush BonusBrush = Frozen("#C084FC"), BonusSoftBrush = Frozen("#26C084FC");

        static readonly Brush TimeBrush = Frozen("#F4F4F6"), TimeSoonBrush = Frozen("#FF8A3D"), TimeUpBrush = Frozen("#FF5C5C");
        string heroTimeBig = "", heroTimeLabel = "TIME LEFT", extendInfo = "";
        Brush heroTimeBrush = TimeBrush;
        bool hasExtendInfo;
        KsfServer liveServer;
        bool hasLiveServer, hasLiveTime, hasHeroTime, hasNextMap, hasSession;
        string liveTitle, liveSubtitle, liveTimeLeft, liveSpectators, nextMap, sessionTitle, sessionTime, sessionMaps, sessionFinishes, sessionPbs,
            celebrationTitle, celebrationDetail;
        double liveTimeFraction, heroTimeFraction;
        int celebrationId;
        DateTime? sessionStart;
        DateTime lastRelativeUpdate = DateTime.MinValue;
        ImageSource ambientImage;

        public bool HasLiveServer { get => hasLiveServer; set => Set(ref hasLiveServer, value); }
        public string LiveTitle { get => liveTitle; set => Set(ref liveTitle, value); }
        public string LiveSubtitle { get => liveSubtitle; set => Set(ref liveSubtitle, value); }
        /// <summary>The time left on your map (see Clock) is known, or the map was just extended.</summary>
        public bool HasLiveTime { get => hasLiveTime; set => Set(ref hasLiveTime, value); }
        /// <summary>Companion's clock for the time left on the map you're playing.</summary>
        public MapClock Clock { get; set; }
        public string LiveTimeLeft { get => liveTimeLeft; set => Set(ref liveTimeLeft, value); }
        /// <summary>The big countdown on the map picture: "9:16", and above it "TIME LEFT" (or "EXTENDED", "MAP ENDING").</summary>
        public string HeroTimeBig { get => heroTimeBig; set => Set(ref heroTimeBig, value); }
        public string HeroTimeLabel { get => heroTimeLabel; set => Set(ref heroTimeLabel, value); }
        /// <summary>Orange in the last two minutes, red in the last 30 seconds.</summary>
        public Brush HeroTimeBrush
        {
            get => heroTimeBrush;
            set
            {
                if (!Set(ref heroTimeBrush, value)) return;
                Raise(nameof(HeroAccentBrush));
            }
        }
        /// <summary>The timer's dot and line: the accent while there's time, orange then red near the end.</summary>
        public Brush HeroAccentBrush => heroTimeBrush == TimeBrush ? HeroAccent : heroTimeBrush;
        static readonly Brush HeroAccent = Frozen("#FF7A1A");
        /// <summary>"80 min limit  ·  extended 2× (+20 min)"</summary>
        public string ExtendInfo { get => extendInfo; set => Set(ref extendInfo, value); }
        public bool HasExtendInfo { get => hasExtendInfo; set => Set(ref hasExtendInfo, value); }
        public double LiveTimeFraction { get => liveTimeFraction; set => Set(ref liveTimeFraction, value); }
        public string LiveSpectators { get => liveSpectators; set => Set(ref liveSpectators, value); }
        public bool HasHeroTime { get => hasHeroTime; set => Set(ref hasHeroTime, value); }
        public double HeroTimeFraction { get => heroTimeFraction; set => Set(ref heroTimeFraction, value); }
        public bool HasNextMap { get => hasNextMap; set => Set(ref hasNextMap, value); }
        public string NextMap { get => nextMap; set => Set(ref nextMap, value); }
        public bool HasSession { get => hasSession; set => Set(ref hasSession, value); }
        public string SessionTitle { get => sessionTitle; set => Set(ref sessionTitle, value); }
        public string SessionTime { get => sessionTime; set => Set(ref sessionTime, value); }
        public string SessionMaps { get => sessionMaps; set => Set(ref sessionMaps, value); }
        public string SessionFinishes { get => sessionFinishes; set => Set(ref sessionFinishes, value); }
        public string SessionPbs { get => sessionPbs; set => Set(ref sessionPbs, value); }
        public string CelebrationTitle { get => celebrationTitle; set => Set(ref celebrationTitle, value); }
        public string CelebrationDetail { get => celebrationDetail; set => Set(ref celebrationDetail, value); }
        /// <summary>Goes up by one each time there's something to celebrate; the window plays its animation on change.</summary>
        public int CelebrationId { get => celebrationId; set => Set(ref celebrationId, value); }
        /// <summary>A tiny version of the map picture, stretched and faded behind the whole dashboard.</summary>
        public ImageSource AmbientImage { get => ambientImage; set => Set(ref ambientImage, value); }

        /// <summary>Everyone on your KSF server and where they are on the map (KSF's own labels: start, cp/stage N, bonus N).</summary>
        public void SetLiveServer(KsfServer server, string yourSteamId, string heroMap)
        {
            liveServer = server;
            HasLiveServer = server != null;
            LivePlayers.Clear();
            if (server == null) return;

            // Right after a map change ksf.surf still reports the old map for a little while.
            var fresh = heroMap == null || string.Equals(server.Map, heroMap, StringComparison.OrdinalIgnoreCase);
            LiveTitle = server.Name;
            var active = server.Players.Where(p => p.Zone != -1).ToList();
            var spectating = server.Players.Count - active.Count;
            LiveSubtitle = fresh ? $"{server.Map}  ·  {active.Count} surfing" : $"{heroMap}  ·  new map, updating...";
            // Busy servers have 40+ people; you and the furthest along are enough.
            const int shown = 12;
            var more = Math.Max(0, active.Count - shown);
            var youSpectate = server.Players.Any(p => p.Zone == -1 && string.Equals(p.SteamId, yourSteamId, StringComparison.OrdinalIgnoreCase));
            var spectators = spectating == 0 ? null
                : youSpectate ? (spectating == 1 ? "you're spectating" : $"{spectating} spectating, including you")
                : $"{spectating} spectating";
            LiveSpectators = string.Join("  ·  ", new[] { more > 0 ? $"+ {more} more surfing" : null, spectators }.Where(s => s != null));
            foreach (var p in active
                .OrderByDescending(p => string.Equals(p.SteamId, yourSteamId, StringComparison.OrdinalIgnoreCase))
                .ThenBy(p => (p.Zone ?? 0) >= 30)
                .ThenByDescending(p => p.Zone ?? 0)
                .ThenByDescending(p => p.ConnectedSeconds ?? 0)
                .Take(shown))
            {
                var zone = p.Zone ?? 0;
                var bonus = zone >= 30;
                var start = zone < 1;
                var row = new LivePlayerRow
                {
                    Name = p.Name,
                    Zone = !fresh ? "" : start ? "START" : bonus ? $"BONUS {zone - 30}" : (server.IsLinear ? "CP " : "STAGE ") + zone,
                    ZoneBrush = start ? StartBrush : bonus ? BonusBrush : ZoneBrush,
                    ZoneSoftBrush = start ? StartSoftBrush : bonus ? BonusSoftBrush : ZoneSoftBrush,
                    Rank = p.Rank > 0 ? $"#{p.Rank:N0}" : "",
                    IsYou = string.Equals(p.SteamId, yourSteamId, StringComparison.OrdinalIgnoreCase),
                    ConnectedAtFetch = p.ConnectedSeconds ?? 0,
                    FetchedAt = server.FetchedAt,
                };
                row.Update(DateTime.Now);
                LivePlayers.Add(row);
            }
            Tick(DateTime.Now);
        }

        public void SetNextMap(string map, int? tier)
        {
            HasNextMap = !string.IsNullOrEmpty(map);
            NextMap = map == null ? "" : tier > 0 ? $"{map}  ·  T{tier}" : map;
        }

        public void SetSession(DateTime start, int maps, int finishes, int pbs)
        {
            sessionStart = start;
            HasSession = true;
            SessionTitle = "THIS SESSION";
            SessionMaps = maps.ToString(Inv);
            SessionFinishes = finishes.ToString(Inv);
            SessionPbs = pbs.ToString(Inv);
            SessionMapsLabel = maps == 1 ? "map" : "maps";
            SessionFinishesLabel = finishes == 1 ? "finish" : "finishes";
            SessionPbsLabel = pbs == 1 ? "new PB" : "new PBs";
            Tick(DateTime.Now);
        }

        string sessionMapsLabel = "maps", sessionFinishesLabel = "finishes", sessionPbsLabel = "new PBs";
        public string SessionMapsLabel { get => sessionMapsLabel; set => Set(ref sessionMapsLabel, value); }
        public string SessionFinishesLabel { get => sessionFinishesLabel; set => Set(ref sessionFinishesLabel, value); }
        public string SessionPbsLabel { get => sessionPbsLabel; set => Set(ref sessionPbsLabel, value); }

        /// <summary>The game closed: keep the numbers up, stop the clock.</summary>
        public void EndSession()
        {
            if (!HasSession) return;
            Tick(DateTime.Now);
            sessionStart = null;
            SessionTitle = "LAST SESSION";
        }

        public void Celebrate(string title, string detail)
        {
            CelebrationTitle = title;
            CelebrationDetail = detail;
            CelebrationId++;
        }

        /// <summary>Called every second: countdowns, time on server, session clock, "saved 3h ago".</summary>
        public void Tick(DateTime now)
        {
            foreach (var player in LivePlayers) player.Update(now);
            var left = Clock?.LeftAt(now);
            var extending = Clock?.Extending == true;
            HasLiveTime = HasHeroTime = left != null || extending;
            if (left is double seconds)
            {
                LiveTimeLeft = Countdown(seconds);
                var limit = Clock.LimitMinutes ?? liveServer?.TimeLimitMinutes;
                var fraction = limit > 0 ? Math.Max(0, Math.Min(1, seconds / (limit.Value * 60))) : 0;
                LiveTimeFraction = fraction;
                HeroTimeFraction = fraction;
                HeroTimeBig = ClockFace(seconds);
                HeroTimeLabel = seconds <= 0 ? "MAP ENDING" : "TIME LEFT";
                HeroTimeBrush = seconds < 30 ? TimeUpBrush : seconds < 120 ? TimeSoonBrush : TimeBrush;
            }
            else if (extending)
            {
                LiveTimeLeft = "extended";
                HeroTimeBig = "+ ...";
                HeroTimeLabel = "EXTENDED";
                HeroTimeBrush = TimeSoonBrush;
            }
            ExtendInfo = ExtendText(Clock);
            HasExtendInfo = HasLiveTime && ExtendInfo.Length > 0;
            // Your own server's row shows the same countdown as the live card.
            foreach (var server in Servers)
            {
                if (server.IsYours && HasLiveTime) server.TimeLeft = LiveTimeLeft;
                else server.Update(now);
            }
            if (sessionStart is DateTime start)
            {
                var t = now - start;
                SessionTime = t.TotalHours >= 1 ? string.Format(Inv, "{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                                                : string.Format(Inv, "{0}:{1:00}", t.Minutes, t.Seconds);
            }
            if ((now - lastRelativeUpdate).TotalSeconds >= 30)
            {
                lastRelativeUpdate = now;
                foreach (var row in Later) row.Update();
            }
        }

        /// <summary>"9:16" or "1:05:12" (no "left").</summary>
        static string ClockFace(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, seconds)));
            return t.TotalHours >= 1 ? string.Format(Inv, "{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                                     : string.Format(Inv, "{0}:{1:00}", t.Minutes, t.Seconds);
        }

        /// <summary>"80 min limit  ·  extended 2× (+20 min)" - or "since you joined" when we didn't see the map start.</summary>
        static string ExtendText(MapClock clock)
        {
            if (!(clock?.LimitMinutes is double limit) || limit <= 0) return "";
            var text = string.Format(Inv, "{0:0} min limit", limit);
            if (clock.Extensions > 0)
            {
                text += string.Format(Inv, "\nextended {0}×", clock.Extensions);
                if (clock.ExtendedMinutes > 0) text += string.Format(Inv, "  ·  +{0:0} min", clock.ExtendedMinutes);
                if (!clock.SeenFromStart) text += " since you joined";
            }
            else if (clock.SeenFromStart) text += "\nnot extended yet";
            return text;
        }

        public static string Countdown(double seconds)
        {
            if (seconds <= 0) return "map ending";
            var t = TimeSpan.FromSeconds(Math.Ceiling(seconds));
            return t.TotalHours >= 1 ? string.Format(Inv, "{0}:{1:00}:{2:00} left", (int)t.TotalHours, t.Minutes, t.Seconds)
                                     : string.Format(Inv, "{0}:{1:00} left", t.Minutes, t.Seconds);
        }

        public string PlayerName
        {
            get => playerName;
            set
            {
                if (!Set(ref playerName, value)) return;
                Raise(nameof(HasPlayer));
                Raise(nameof(PlayerLabel));
            }
        }
        public bool HasPlayer => !string.IsNullOrEmpty(playerName);
        public string PlayerLabel => HasPlayer ? playerName : "Not connected yet";

        string notice;
        /// <summary>Something that needs you to act, e.g. restarting the game once after setup. Null when all is well.</summary>
        public string Notice
        {
            get => notice;
            set { if (Set(ref notice, value)) Raise(nameof(HasNotice)); }
        }
        public bool HasNotice => !string.IsNullOrEmpty(notice);
        public string PlayerDetail { get => playerDetail; set => Set(ref playerDetail, value); }
        public ImageSource Avatar { get => avatar; set => Set(ref avatar, value); }
        public string StatusText { get => statusText; set => Set(ref statusText, value); }
        public Brush StatusBrush { get => statusBrush; set => Set(ref statusBrush, value); }

        public bool HasMap { get => hasMap; set => Set(ref hasMap, value); }
        /// <summary>Which KSF records the map section shows: 100 tick (true) or 66 tick.</summary>
        public bool Is100t { get => is100t; set => Set(ref is100t, value); }
        public bool IsOnKsf { get => isOnKsf; set => Set(ref isOnKsf, value); }
        public bool Loading { get => loading; set => Set(ref loading, value); }
        public bool IsSaved
        {
            get => isSaved;
            set { if (Set(ref isSaved, value)) Raise(nameof(SaveLabel)); }
        }
        public string SaveLabel => isSaved ? "Saved for later" : "Save for later";
        public string MapName { get => mapName; set => Set(ref mapName, value); }
        public string MapBadge { get => mapBadge; set => Set(ref mapBadge, value); }
        public ImageSource MapImage { get => mapImage; set => Set(ref mapImage, value); }
        public string TierText { get => tierText; set => Set(ref tierText, value); }
        public Brush TierBrush { get => tierBrush; set => Set(ref tierBrush, value); }
        public Brush TierSoftBrush { get => tierSoftBrush; set => Set(ref tierSoftBrush, value); }
        public string TypeText { get => typeText; set => Set(ref typeText, value); }
        public string BonusText { get => bonusText; set => Set(ref bonusText, value); }
        public string MapperText { get => mapperText; set => Set(ref mapperText, value); }
        public bool HasRating { get => hasRating; set => Set(ref hasRating, value); }
        public string RatingText { get => ratingText; set => Set(ref ratingText, value); }
        public bool HasServerLine { get => hasServerLine; set => Set(ref hasServerLine, value); }
        public string ServerLine { get => serverLine; set => Set(ref serverLine, value); }
        public string EmptyText { get => emptyText; set => Set(ref emptyText, value); }

        public bool HasWr { get => hasWr; set => Set(ref hasWr, value); }
        public string WrTime { get => wrTime; set => Set(ref wrTime, value); }
        public string WrHolder { get => wrHolder; set => Set(ref wrHolder, value); }
        public string WrDate { get => wrDate; set => Set(ref wrDate, value); }
        public bool HasPb { get => hasPb; set => Set(ref hasPb, value); }
        public string PbTime { get => pbTime; set => Set(ref pbTime, value); }
        public string PbRank { get => pbRank; set => Set(ref pbRank, value); }
        public string PbTop { get => pbTop; set => Set(ref pbTop, value); }
        public double PbBar { get => pbBar; set => Set(ref pbBar, value); }
        public string GapTime { get => gapTime; set => Set(ref gapTime, value); }
        public string GapDetail { get => gapDetail; set => Set(ref gapDetail, value); }
        public string GroupText
        {
            get => groupText;
            set { if (Set(ref groupText, value)) Raise(nameof(HasGroup)); }
        }
        public bool HasGroup => !string.IsNullOrEmpty(groupText);
        public string Finishes { get => finishes; set => Set(ref finishes, value); }
        public string Attempts { get => attempts; set => Set(ref attempts, value); }
        public string Playtime { get => playtime; set => Set(ref playtime, value); }

        public string LeaderTitle { get => leaderTitle; set => Set(ref leaderTitle, value); }
        public string FinishersText { get => finishersText; set => Set(ref finishersText, value); }
        public bool HasLater { get => hasLater; set => Set(ref hasLater, value); }
        public string LaterCount { get => laterCount; set => Set(ref laterCount, value); }
        public bool HasServers { get => hasServers; set => Set(ref hasServers, value); }
        public string ServersUpdated { get => serversUpdated; set => Set(ref serversUpdated, value); }
        public bool HasRecent { get => hasRecent; set => Set(ref hasRecent, value); }
        public string Toast { get => toast; set => Set(ref toast, value); }

        public ICommand SaveCommand { get; set; }
        public ICommand OpenMapCommand { get; set; }
        public ICommand RefreshCommand { get; set; }
        public ICommand OpenLaterCommand { get; set; }
        public ICommand RemoveLaterCommand { get; set; }
        public ICommand NominateCommand { get; set; }
        /// <summary>Parameter: the zone (0 = map start, 1-30 stage, 31+ bonus).</summary>
        public ICommand TeleportCommand { get; set; }
        /// <summary>Parameter: the zone whose leaderboard to show (0 = the map).</summary>
        public ICommand SelectLeaderboardCommand { get; set; }
        public ICommand FollowLeaderboardCommand { get; set; }
        public ICommand JoinCommand { get; set; }
        public ICommand OpenFolderCommand { get; set; }
        public ICommand TickCommand { get; set; }

        public void SetStatus(string text, Connection connection)
        {
            StatusText = text;
            StatusBrush = connection == Connection.Connected ? GoodBrush : connection == Connection.Limited ? WarnBrush : WaitBrush;
        }

        public void ShowNoMap()
        {
            HasMap = false;
            Loading = false;
            MapName = "No map yet";
            MapBadge = "WAITING FOR A MAP";
            MapImage = null;
            HasServerLine = false;
            IsOnKsf = false;
            EmptyText = DefaultEmptyText;
            ClearMapDetails();
        }

        public void ShowLoading(string map, bool live)
        {
            HasMap = true;
            Loading = true;
            IsOnKsf = true;
            EmptyText = DefaultEmptyText;
            MapName = map;
            MapBadge = live ? "NOW PLAYING" : "LAST MAP";
            MapImage = null;
            ClearMapDetails();
        }

        public void SetLive(bool live)
        {
            if (HasMap) MapBadge = live ? "NOW PLAYING" : "LAST MAP";
        }

        void ClearMapDetails()
        {
            TierText = TypeText = BonusText = MapperText = null;
            TierBrush = TierSoftBrush = Plain;
            HasRating = false;
            HasWr = HasPb = false;
            WrTime = PbTime = GapTime = Finishes = "--";
            WrHolder = WrDate = PbRank = PbTop = GapDetail = GroupText = Attempts = Playtime = FinishersText = "";
            PbBar = 0;
            Leaders.Clear();
            LeaderChips.Clear();
            zoneTops.Clear();
            leaderReport = null;
            leaderKey = null;
            leaderZone = 0;
            HasLeaderChoices = LeaderPinned = LeaderLoading = false;
            LeaderHeading = "";
            ClearTimes();
        }

        /// <summary>
        /// Shows a run the moment the game prints it, before KSF's numbers arrive a second later.
        /// </summary>
        public void ShowFreshFinish(double time, WorldRecord wr, bool improved)
        {
            if (!improved) return;
            HasPb = true;
            PbTime = Format.Time(time);
            PbRank = "saving to KSF...";
            PbTop = "just now";
            if (wr != null)
            {
                GapTime = Format.Diff(time - wr.Time);
                GapDetail = string.Format(Inv, "{0:0.0}% slower than the WR", (time / wr.Time - 1) * 100);
            }
        }

        public void ShowReport(MapReport r, bool saved)
        {
            Loading = false;
            IsSaved = saved;
            Is100t = r.Game == "css100t";
            MapName = r.Map;
            IsOnKsf = r.IsOnKsf;
            if (!r.IsOnKsf)
            {
                ClearMapDetails();
                EmptyText = r.Error ?? ("Not on KSF's map list" + (r.Suggestions.Count > 0 ? " - KSF has " + string.Join(", ", r.Suggestions) : ""));
                return;
            }

            var info = r.Info;
            TierText = "TIER " + info.Tier;
            TierBrush = TierColor(info.Tier);
            TierSoftBrush = TierSoft(info.Tier);
            TypeText = info.IsLinear ? "LINEAR" : $"{info.StageCount} STAGES";
            BonusText = info.BonusCount == 0 ? "NO BONUSES" : info.BonusCount == 1 ? "1 BONUS" : $"{info.BonusCount} BONUSES";
            MapperText = string.IsNullOrEmpty(info.Mappers) ? "" : "by " + info.Mappers;
            HasRating = info.Rating != null && info.RatingCount > 0;
            RatingText = HasRating ? string.Format(Inv, "{0:0.0}  ({1:N0})", info.Rating, info.RatingCount) : "";

            HasWr = r.Wr != null;
            WrTime = r.Wr != null ? Format.Time(r.Wr.Time) : "--";
            WrHolder = r.Wr?.Name ?? (r.Error ?? "nobody has finished it yet");
            WrDate = r.Wr?.Date is DateTime wrDate ? "set " + wrDate.ToString("MMM d, yyyy", Inv) : "";

            var me = r.Main;
            var total = me?.TotalRanks ?? 0;
            HasPb = me?.Time != null;
            if (me?.Time is double newPb && me.Unsynced)
            {
                // Just set in game: ksf.surf hasn't ranked it yet.
                PbTime = Format.Time(newPb);
                PbRank = "new PB - rank updates soon";
                PbTop = "just now";
                if (r.Wr != null)
                {
                    GapTime = Format.Diff(newPb - r.Wr.Time);
                    GapDetail = string.Format(Inv, "{0:0.0}% slower than the WR", (newPb / r.Wr.Time - 1) * 100);
                }
            }
            else if (me?.Time is double pb)
            {
                PbTime = Format.Time(pb);
                PbRank = total > 0 ? $"#{me.Rank:N0} of {total:N0}" : "";
                var fraction = total > 1 && me.Rank > 0 ? (me.Rank.Value - 1) / (double)(total - 1) : 0;
                PbBar = 1 - fraction;
                PbTop = total > 0 && me.Rank > 0 ? TopPercent(me.Rank.Value, total) : "";
                if (r.Wr != null)
                {
                    var mine = string.Equals(r.Wr.SteamId, r.SteamId, StringComparison.OrdinalIgnoreCase);
                    GapTime = mine ? "WR" : Format.Diff(pb - r.Wr.Time);
                    GapDetail = mine ? "you hold the world record" : string.Format(Inv, "{0:0.0}% slower than the WR", (pb / r.Wr.Time - 1) * 100);
                }
                GroupText = me.Group > 0 ? "GROUP " + me.Group : "";
            }
            else
            {
                PbTime = "--";
                PbRank = r.PersonalError ?? "not finished yet";
                PbTop = total > 0 ? $"{total:N0} players have" : "";
                PbBar = 0;
                GapTime = "--";
                GapDetail = "finish it to get a time";
                GroupText = "";
            }
            Finishes = me?.Completions > 0 ? me.Completions.Value.ToString("N0", Inv) : "0";
            Attempts = me?.Attempts == 1 ? "1 attempt" : me?.Attempts > 0 ? $"{me.Attempts:N0} attempts" : "no attempts yet";
            Playtime = me?.PlaytimeSeconds > 0 ? Format.Duration(me.PlaytimeSeconds.Value) + " on this map" : "";

            ShowTimes(r);
            ShowLeaderboardOf(r);
        }

        // ----- nominate page: all of KSF's maps to search, filter and nominate, plus rock the vote -----
        const int MapsPerPage = 60, ThumbsKept = 300;
        string page = "dashboard", mapSearch = "", mapSort = "popular", mapView = "tiles", mapDone = "all", mapKind = "all", mapStatus = "";
        double tileSize = 280;
        int mapTier, mapsShown = MapsPerPage, mapsMatching;
        bool hasMoreMaps, catalogLoading, finishedLoading, onlyCloseMatches;
        List<MapInfo> catalogMaps = new List<MapInfo>();
        HashSet<string> savedMapSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The maps you've finished (on the tick and style on show), by name.
        IReadOnlyDictionary<string, FinishedMap> finishedMaps = new Dictionary<string, FinishedMap>();
        string playingMap;
        // Pictures already loaded, so searching and filtering don't load them again (the oldest go first).
        readonly Dictionary<string, ImageSource> mapThumbs = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        readonly Queue<string> mapThumbOrder = new Queue<string>();

        public DashboardViewModel()
        {
            ShowPageCommand = new RelayCommand(p => Page = p as string == "nominate" || p as string == "binds" ? (string)p : "dashboard");
            SetMapTierCommand = new RelayCommand(p => MapTier = int.TryParse(p as string, out var tier) ? tier : 0);
            SetMapSortCommand = new RelayCommand(p => MapSort = p as string ?? "popular");
            SetMapViewCommand = new RelayCommand(p => MapView = p as string == "list" ? "list" : "tiles");
            SetMapDoneCommand = new RelayCommand(p => MapDone = p as string == "todo" || p as string == "done" ? (string)p : "all");
            SetMapKindCommand = new RelayCommand(p => MapKind = p as string == "linear" || p as string == "staged" ? (string)p : "all");
            ShowMoreMapsCommand = new RelayCommand(_ =>
            {
                mapsShown += MapsPerPage;
                FilterMaps();
            });
            ClearMapSearchCommand = new RelayCommand(_ => MapSearch = "");
            HidePartCommand = new RelayCommand(p => { if (p is string key) Layout[key] = false; });
            ShowAllPartsCommand = new RelayCommand(_ => Layout.ShowAll());
            SetViewCommand = new RelayCommand(p => Layout.IsSimple = p as string == "simple");
            Layout.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(DashboardLayout.IsSimple)) return;
                foreach (var name in new[] { nameof(LeaderLimit), nameof(LiveLimit), nameof(ServerLimit), nameof(RecentLimit) }) Raise(name);
                RenderLeaderboard();
            };
        }

        // ----- what's on the dashboard: parts you've hidden, and Simple or Advanced -----
        public DashboardLayout Layout { get; } = new DashboardLayout();
        /// <summary>Parameter: the part to hide ("times", "servers"...).</summary>
        public ICommand HidePartCommand { get; }
        public ICommand ShowAllPartsCommand { get; }
        /// <summary>Parameter: "simple" or "advanced".</summary>
        public ICommand SetViewCommand { get; }
        // How many rows the lists show: the Simple view keeps them short.
        public int LeaderLimit => Layout.IsSimple ? 5 : 10;
        public int LiveLimit => Layout.IsSimple ? 6 : 12;
        public int ServerLimit => Layout.IsSimple ? 6 : 99;
        public int RecentLimit => Layout.IsSimple ? 3 : 99;

        /// <summary>Rows on show that have no picture yet: Companion loads them.</summary>
        public event Action<List<MapResultRow>> ThumbsNeeded;
        /// <summary>The search changed (while the map list is still coming in, Companion asks ksf.surf's search too).</summary>
        public event Action<string> MapSearchChanged;

        public ObservableCollection<MapResultRow> MapResults { get; } = new ObservableCollection<MapResultRow>();
        public string Page
        {
            get => page;
            set
            {
                if (!Set(ref page, value)) return;
                Raise(nameof(IsDashboardPage));
                Raise(nameof(IsNominatePage));
                Raise(nameof(IsBindsPage));
                if (page != "binds") Binds.CancelCapture();
            }
        }
        public bool IsDashboardPage => page != "nominate" && page != "binds";
        public bool IsNominatePage => page == "nominate";
        public bool IsBindsPage => page == "binds";
        /// <summary>The binds page: KSF commands and turning on keys.</summary>
        public BindsViewModel Binds { get; } = new BindsViewModel();
        public string MapSearch
        {
            get => mapSearch;
            set
            {
                if (!Set(ref mapSearch, value ?? "")) return;
                Raise(nameof(HasMapSearch));
                mapsShown = MapsPerPage;
                FilterMaps();
                MapSearchChanged?.Invoke(mapSearch.Trim());
            }
        }
        public bool HasMapSearch => mapSearch.Length > 0;
        public int MapTier
        {
            get => mapTier;
            set
            {
                if (!Set(ref mapTier, value)) return;
                mapsShown = MapsPerPage;
                FilterMaps();
            }
        }
        public string MapSort
        {
            get => mapSort;
            set
            {
                if (!Set(ref mapSort, value)) return;
                mapsShown = MapsPerPage;
                FilterMaps();
            }
        }
        public string MapView
        {
            get => mapView;
            set
            {
                if (!Set(ref mapView, value)) return;
                Raise(nameof(IsTileView));
                Raise(nameof(IsListView));
            }
        }
        public bool IsTileView => mapView != "list";
        public bool IsListView => mapView == "list";
        /// <summary>"all", "todo" (maps you haven't finished) or "done".</summary>
        public string MapDone
        {
            get => mapDone;
            set
            {
                if (!Set(ref mapDone, value)) return;
                mapsShown = MapsPerPage;
                FilterMaps();
            }
        }
        /// <summary>"all", "linear" or "staged".</summary>
        public string MapKind
        {
            get => mapKind;
            set
            {
                if (!Set(ref mapKind, value)) return;
                mapsShown = MapsPerPage;
                FilterMaps();
            }
        }
        /// <summary>How wide the map tiles are at least (the size slider); the list's pictures follow it too.</summary>
        public double TileSize
        {
            get => tileSize;
            set
            {
                if (!Set(ref tileSize, Math.Max(200, Math.Min(460, value)))) return;
                Raise(nameof(ListThumbWidth));
                Raise(nameof(ListThumbHeight));
                TileSizeChanged?.Invoke(tileSize);
            }
        }
        public double ListThumbWidth => Math.Round(tileSize * 0.4);
        public double ListThumbHeight => Math.Round(tileSize * 0.4 * 0.5625);
        /// <summary>The size slider moved (to save it).</summary>
        public event Action<double> TileSizeChanged;
        public ICommand SetMapKindCommand { get; }
        public string MapStatus { get => mapStatus; set => Set(ref mapStatus, value); }
        public bool HasMoreMaps { get => hasMoreMaps; set => Set(ref hasMoreMaps, value); }

        public ICommand ShowPageCommand { get; }
        public ICommand SetMapTierCommand { get; }
        public ICommand SetMapSortCommand { get; }
        public ICommand SetMapViewCommand { get; }
        public ICommand SetMapDoneCommand { get; }
        public ICommand ShowMoreMapsCommand { get; }
        public ICommand ClearMapSearchCommand { get; }
        /// <summary>Rock the vote on the server you're on (sm_rtv).</summary>
        public ICommand RtvCommand { get; set; }
        /// <summary>Parameter: the map to add to / take off your play-later list.</summary>
        public ICommand ToggleSavedCommand { get; set; }

        /// <summary>The map list (it grows while it's being fetched).</summary>
        public void SetMapCatalog(List<MapInfo> maps, bool loading)
        {
            catalogMaps = maps;
            catalogLoading = loading;
            FilterMaps();
        }

        /// <summary>Your play-later maps (listed first, with a filled star) and the map you're on.</summary>
        public void SetMapsContext(ICollection<string> saved, string currentMap)
        {
            savedMapSet = new HashSet<string>(saved, StringComparer.OrdinalIgnoreCase);
            if (!string.Equals(playingMap, currentMap, StringComparison.OrdinalIgnoreCase))
            {
                playingMap = currentMap;
                FilterMaps();
                return;
            }
            foreach (var row in MapResults) row.IsSaved = savedMapSet.Contains(row.Map);
        }

        /// <summary>
        /// The maps you've finished (on the tick and style on show); <paramref name="loading"/> while ksf.surf's list of
        /// them is still being read. Without a done/not-done filter the rows on show are just marked, so nothing jumps.
        /// </summary>
        public void SetFinishedMaps(IReadOnlyDictionary<string, FinishedMap> maps, bool loading)
        {
            finishedMaps = maps;
            finishedLoading = loading;
            if (mapDone != "all")
            {
                FilterMaps();
                return;
            }
            foreach (var row in MapResults) ShowDone(row);
            UpdateMapStatus();
        }

        public void SetMapThumb(MapResultRow row, ImageSource thumb)
        {
            if (thumb == null) return;
            if (!mapThumbs.ContainsKey(row.Map))
            {
                mapThumbOrder.Enqueue(row.Map);
                while (mapThumbOrder.Count > ThumbsKept) mapThumbs.Remove(mapThumbOrder.Dequeue());
            }
            mapThumbs[row.Map] = thumb;
            row.Thumb = thumb;
            // The list may have been rebuilt (search, filter) while the picture loaded.
            foreach (var shown in MapResults.Where(r => r != row && r.Thumb == null && string.Equals(r.Map, row.Map, StringComparison.OrdinalIgnoreCase)))
                shown.Thumb = thumb;
        }

        void FilterMaps()
        {
            var words = MapMatch.Words(mapSearch);
            IEnumerable<MapInfo> maps = catalogMaps;
            if (mapTier > 0) maps = maps.Where(m => m.Tier == mapTier);
            if (mapDone == "done") maps = maps.Where(m => finishedMaps.ContainsKey(m.Name));
            else if (mapDone == "todo") maps = maps.Where(m => !finishedMaps.ContainsKey(m.Name));
            if (mapKind == "linear") maps = maps.Where(m => !MapReport.IsStagedMap(m));
            else if (mapKind == "staged") maps = maps.Where(m => MapReport.IsStagedMap(m));
            var scores = new Dictionary<MapInfo, int>();
            if (words.Length > 0)
            {
                foreach (var m in maps)
                    if (MapMatch.Score(m.Name, m.Mappers, words) is int score) scores[m] = score;
                // Maps that match exactly hide the ones that are only close (a letter off).
                if (scores.Values.Any(s => s < MapMatch.Close))
                    foreach (var close in scores.Where(s => s.Value >= MapMatch.Close).Select(s => s.Key).ToList()) scores.Remove(close);
                maps = maps.Where(scores.ContainsKey);
            }
            onlyCloseMatches = scores.Count > 0 && scores.Values.All(s => s >= MapMatch.Close);
            IEnumerable<MapInfo> sorted = mapSort switch
            {
                "newest" => maps.OrderByDescending(m => m.Added ?? DateTime.MinValue),
                "tier" => maps.OrderBy(m => m.Tier).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase),
                "name" => maps.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase),
                "rating" => maps.OrderByDescending(m => m.RatingCount >= 3 ? m.Rating ?? 0 : 0).ThenByDescending(m => m.RatingCount),
                _ => maps.OrderByDescending(m => m.Popularity),
            };
            List<MapInfo> list;
            if (words.Length > 0)
            {
                // Best name matches first ("x" finds surf_x before surf_xenon), otherwise in the chosen order.
                var text = string.Join("", words);
                list = sorted.OrderBy(m => MatchRank(m.Name, text)).ThenBy(m => scores[m]).ToList();
            }
            else
            {
                // Nothing searched: your play-later maps first.
                var all = sorted.ToList();
                list = all.Where(m => savedMapSet.Contains(m.Name)).Concat(all.Where(m => !savedMapSet.Contains(m.Name))).ToList();
            }

            MapResults.Clear();
            foreach (var map in list.Take(mapsShown))
            {
                var row = ToResultRow(map);
                ShowDone(row);
                MapResults.Add(row);
            }
            HasMoreMaps = list.Count > mapsShown;
            mapsMatching = list.Count;
            UpdateMapStatus();
            var missing = MapResults.Where(r => r.Thumb == null).ToList();
            if (missing.Count > 0) ThumbsNeeded?.Invoke(missing);
        }

        /// <summary>"86 of 823 maps  ·  211 done", and what's still loading.</summary>
        void UpdateMapStatus()
        {
            if (catalogMaps.Count == 0)
            {
                MapStatus = catalogLoading ? "Getting KSF's map list..." : "Search for a map";
                return;
            }
            var filtered = mapSearch.Trim().Length > 0 || mapTier > 0 || mapDone != "all" || mapKind != "all";
            var done = finishedMaps.Count == 0 ? 0 : catalogMaps.Count(m => finishedMaps.ContainsKey(m.Name));
            MapStatus = (onlyCloseMatches ? $"No exact match - {mapsMatching:N0} close" : filtered ? $"{mapsMatching:N0} of {catalogMaps.Count:N0} maps" : $"{catalogMaps.Count:N0} maps")
                        + (done > 0 ? $"  ·  {done:N0} done" : "")
                        + (catalogLoading ? "  ·  still getting the rest of KSF's maps..."
                           : finishedLoading ? "  ·  checking which ones you've done..." : "");
        }

        void ShowDone(MapResultRow row)
        {
            if (finishedMaps.TryGetValue(row.Map, out var done))
            {
                row.IsDone = true;
                row.DoneText = done.Time > 0 ? Format.Time(done.Time) : "done";
                row.DoneTip = "You've finished this map" + (done.Time > 0 ? ": " + Format.Time(done.Time) : "")
                              + (done.Group is int group ? $"  ·  group {group}" : "")
                              + (done.Points > 0 ? $"  ·  {done.Points} pts" : "");
            }
            else
            {
                row.IsDone = false;
                row.DoneText = "";
                row.DoneTip = null;
            }
        }

        static int MatchRank(string name, string text)
        {
            // Compared without surf_ and the separators, like the search itself ("utopianjv" is surf_utopia_njv).
            var lower = name.ToLowerInvariant();
            var bare = new string((lower.StartsWith("surf_", StringComparison.Ordinal) ? lower.Substring(5) : lower).Where(char.IsLetterOrDigit).ToArray());
            return bare == text ? 0 : bare.StartsWith(text, StringComparison.Ordinal) ? 1 : 2;
        }

        MapResultRow ToResultRow(MapInfo m) => new MapResultRow
        {
            Map = m.Name,
            Tier = m.Tier,
            TierText = m.Tier > 0 ? "T" + m.Tier.ToString(Inv) : "T?",
            TierBrush = TierColor(m.Tier),
            TierSoftBrush = TierSoft(m.Tier),
            Details = (m.IsLinear ? "linear" : m.StageCount == 1 ? "1 stage" : $"{m.StageCount} stages") + "  ·  "
                      + (m.BonusCount == 0 ? "no bonuses" : m.BonusCount == 1 ? "1 bonus" : $"{m.BonusCount} bonuses"),
            HasRating = m.Rating != null && m.RatingCount > 0,
            Rating = m.Rating is double rating ? rating.ToString("0.0", Inv) : "",
            Mappers = string.IsNullOrEmpty(m.Mappers) ? "" : "by " + m.Mappers,
            IsCurrent = string.Equals(m.Name, playingMap, StringComparison.OrdinalIgnoreCase),
            IsSaved = savedMapSet.Contains(m.Name),
            Thumb = mapThumbs.TryGetValue(m.Name, out var thumb) ? thumb : null,
        };

        // ----- leaderboard: the map's, or one stage's / bonus's. Which one is Companion's call (where you are, or your pick). -----
        MapReport leaderReport;
        string leaderKey;
        int leaderZone;
        bool leaderPinned, leaderLoading, hasLeaderChoices;
        string leaderHeading = "";
        readonly Dictionary<int, List<WorldRecord>> zoneTops = new Dictionary<int, List<WorldRecord>>();

        public ObservableCollection<LeaderChip> LeaderChips { get; } = new ObservableCollection<LeaderChip>();
        public string LeaderHeading { get => leaderHeading; set => Set(ref leaderHeading, value); }
        /// <summary>You picked this leaderboard; otherwise it follows where you are.</summary>
        public bool LeaderPinned { get => leaderPinned; set => Set(ref leaderPinned, value); }
        public bool LeaderLoading { get => leaderLoading; set => Set(ref leaderLoading, value); }
        public bool HasLeaderChoices { get => hasLeaderChoices; set => Set(ref hasLeaderChoices, value); }
        public int LeaderZone => leaderZone;

        void ShowLeaderboardOf(MapReport r)
        {
            leaderReport = r;
            var key = r.Game + "|" + r.Map;
            if (key != leaderKey)
            {
                // Another map (or tick rate): start over on its map leaderboard.
                leaderKey = key;
                zoneTops.Clear();
                leaderZone = 0;
                LeaderPinned = false;
                LeaderChips.Clear();
                foreach (var zone in new[] { 0 }.Concat(r.RecordZones))
                    LeaderChips.Add(new LeaderChip { Zone = zone, Label = MapReport.ZoneLabel(zone) });
                HasLeaderChoices = LeaderChips.Count > 1;
            }
            RenderLeaderboard();
        }

        /// <summary>Shows this zone's leaderboard (0 = the map). A stage or bonus shows "loading" until SetZoneTop brings its rows.</summary>
        public void SetLeaderZone(int zone, bool pinned)
        {
            leaderZone = zone;
            LeaderPinned = pinned;
            RenderLeaderboard();
        }

        public void SetZoneTop(int zone, List<WorldRecord> top)
        {
            zoneTops[zone] = top;
            if (zone == leaderZone) RenderLeaderboard();
        }

        void RenderLeaderboard()
        {
            var r = leaderReport;
            foreach (var chip in LeaderChips) chip.IsSelected = chip.Zone == leaderZone;
            LeaderHeading = leaderZone == 0 ? "MAP" : MapReport.ZoneName(leaderZone).ToUpperInvariant();
            Leaders.Clear();
            if (r == null || !r.IsOnKsf) return;

            List<WorldRecord> top;
            if (leaderZone == 0) top = r.Top;
            else if (!zoneTops.TryGetValue(leaderZone, out top)) top = null;
            LeaderLoading = top == null;
            var mine = r.Zone(leaderZone);
            FinishersText = mine?.TotalRanks > 0 ? $"{mine.TotalRanks:N0} finishers" : "";
            if (top == null) return;

            var wr = top.FirstOrDefault(t => t.Rank == 1) ?? top.FirstOrDefault();
            string Show(double seconds) => leaderZone == 0 ? Format.Time(seconds) : Format.Short(seconds);
            // The Simple view shows the top 5 (and you).
            top = top.Take(LeaderLimit).ToList();
            foreach (var row in top)
            {
                Leaders.Add(new LeaderRow
                {
                    Rank = row.Rank.ToString(Inv),
                    RankBrush = row.Rank == 1 ? Gold : row.Rank == 2 ? Silver : row.Rank == 3 ? Bronze : Plain,
                    Name = row.Name,
                    Time = Show(row.Time),
                    Gap = row.Rank == 1 || wr == null ? "" : Format.Diff(row.Time - wr.Time),
                    IsYou = string.Equals(row.SteamId, r.SteamId, StringComparison.OrdinalIgnoreCase),
                });
            }
            // You, below the top 10 (or with a time ksf.surf hasn't ranked yet).
            var youListed = top.Any(row => string.Equals(row.SteamId, r.SteamId, StringComparison.OrdinalIgnoreCase));
            if (mine?.Time is double myTime && top.Count > 0 && (mine.Unsynced || (!youListed && mine.Rank > top.Count)))
            {
                Leaders.Add(new LeaderRow { IsGap = true });
                Leaders.Add(new LeaderRow
                {
                    Rank = mine.Unsynced ? "new" : mine.Rank.Value.ToString("N0", Inv),
                    RankBrush = Plain,
                    Name = r.PlayerName ?? "you",
                    Time = Show(myTime),
                    Gap = wr != null ? Format.Diff(myTime - wr.Time) : "",
                    IsYou = true,
                });
            }
        }

        /// <param name="tick">"66T" or "100T": KSF ranks the two separately.</param>
        public void SetPlayer(string name, string country, int? rank, int? points, string tick)
        {
            if (!string.IsNullOrEmpty(name)) PlayerName = name;
            if (!string.IsNullOrEmpty(country)) playerCountry = country;
            // Once both tick rates are known, the header shows both ranks (SetLevels).
            if (HasLevels) return;
            var bits = new List<string>();
            if (rank > 0) bits.Add((string.IsNullOrEmpty(tick) ? "" : tick + " ") + $"rank #{rank:N0}");
            if (points > 0) bits.Add($"{points:N0} pts");
            if (!string.IsNullOrEmpty(country)) bits.Add(country);
            PlayerDetail = string.Join("  ·  ", bits);
        }

        // ----- your KSF titles (the tag in front of your name in chat) on both tick rates, and what the next ones take -----
        bool hasLevels;
        string playerCountry;

        public bool HasLevels { get => hasLevels; set => Set(ref hasLevels, value); }
        public ObservableCollection<LevelRow> Levels { get; } = new ObservableCollection<LevelRow>();

        /// <summary>
        /// Your standing on each tick rate, with - when its next title goes by rank - the points of whoever holds that
        /// title's last spot now (what you need to pass). The header then shows both ranks.
        /// </summary>
        public void SetLevels(IList<(PlayerStanding Standing, int? NextRankPoints)> standings, string currentGame)
        {
            Levels.Clear();
            foreach (var (s, nextRankPoints) in standings.OrderBy(x => x.Standing.Game == "css100t"))
                Levels.Add(Describe(s, nextRankPoints, s.Game == currentGame));
            HasLevels = Levels.Count > 0;
            if (!HasLevels) return;
            var bits = standings.OrderBy(x => x.Standing.Game == "css100t").Where(x => x.Standing.Rank > 0)
                .Select(x => string.Format(Inv, "{0} #{1:N0}", x.Standing.Tick, x.Standing.Rank)).ToList();
            if (!string.IsNullOrEmpty(playerCountry)) bits.Add(playerCountry);
            PlayerDetail = string.Join("  ·  ", bits);
        }

        static LevelRow Describe(PlayerStanding s, int? nextRankPoints, bool current)
        {
            // ksf.surf's own title is the one shown (it's what's in front of your name in game); the next step goes from
            // whichever is better, it or what your rank and points make you.
            var index = KsfLevels.IndexOf(s);
            var level = KsfLevels.Ladder[index];
            var row = new LevelRow
            {
                Tick = s.Tick,
                Title = string.IsNullOrEmpty(s.Title) ? level.Title : s.Title.ToUpperInvariant(),
                Standing = (s.Rank > 0 ? string.Format(Inv, "#{0:N0}  ·  ", s.Rank) : "") + string.Format(Inv, "{0:N0} pts", s.Points),
                Extra = string.Join("  ·  ", new[]
                {
                    s.CountryRank > 0 ? string.Format(Inv, "#{0:N0} in {1}", s.CountryRank, string.IsNullOrEmpty(s.Country) ? "your country" : s.Country) : null,
                    s.MapsDone > 0 ? string.Format(Inv, "{0:N0} maps done", s.MapsDone) : null,
                }.Where(x => x != null)),
                IsCurrent = current,
            };
            if (index == 0)
            {
                row.Next = "The top title on KSF";
                row.NextDetail = string.Format(Inv, "{0} is the top {1}", level.Title, level.TopRank);
                row.Progress = 1;
                return row;
            }
            var next = KsfLevels.Ladder[index - 1];
            if (next.TopRank is int top)
            {
                // Goes by rank: pass whoever holds its last spot.
                row.Next = nextRankPoints is int needed
                    ? string.Format(Inv, "{0:N0} pts to {1}", Math.Max(1, needed - s.Points + 1), next.Title)
                    : string.Format(Inv, "Top {0:N0} for {1}", top, next.Title);
                row.NextDetail = string.Format(Inv, "{0} is the top {1:N0}", next.Title, top)
                                 + (nextRankPoints is int at ? string.Format(Inv, "  ·  #{0:N0} has {1:N0} pts", top, at) : "");
                row.Progress = level.TopRank is int from && s.Rank is int rank
                    ? Math.Max(0, Math.Min(1, (double)(from - rank) / (from - top)))
                    : nextRankPoints is int goal && goal > level.MinPoints
                        ? Math.Max(0, Math.Min(1, (double)(s.Points - level.MinPoints) / (goal - level.MinPoints)))
                        : 0;
            }
            else
            {
                row.Next = string.Format(Inv, "{0:N0} pts to {1}", Math.Max(1, next.MinPoints - s.Points), next.Title);
                row.NextDetail = string.Format(Inv, "{0} from {1:N0} pts", next.Title, next.MinPoints);
                var floor = Math.Max(0, level.MinPoints);
                row.Progress = Math.Max(0, Math.Min(1, (double)(s.Points - floor) / (next.MinPoints - floor)));
            }
            return row;
        }

        public void SetServerLine(KsfServer yours)
        {
            HasServerLine = yours != null;
            ServerLine = yours == null ? "" : $"{yours.Name}  ·  {yours.PlayerCount} players";
        }

        public List<LaterRow> SetPlayLater(IReadOnlyList<PlayLaterEntry> items, string currentMap, IList<KsfServer> servers)
        {
            var thumbs = Later.Where(r => r.Thumb != null).ToDictionary(r => r.Map, r => r.Thumb);
            Later.Clear();
            foreach (var e in items)
            {
                var live = servers?.FirstOrDefault(s => string.Equals(s.Map, e.Map, StringComparison.OrdinalIgnoreCase));
                var tier = e.Tier ?? live?.Tier;
                Later.Add(new LaterRow
                {
                    Map = e.Map,
                    Tier = tier is int t ? "T" + t : "T?",
                    TierBrush = TierColor(tier ?? 0),
                    TierSoftBrush = TierSoft(tier ?? 0),
                    SavedAt = e.Saved,
                    Saved = e.Saved > DateTime.MinValue ? "saved " + Ago(e.Saved) : "",
                    IsCurrent = string.Equals(e.Map, currentMap, StringComparison.OrdinalIgnoreCase),
                    IsLive = live != null,
                    Live = live == null ? "" : $"LIVE on {live.Name}  ·  {live.PlayerCount} playing",
                    JoinAddress = live?.Address,
                    Thumb = thumbs.TryGetValue(e.Map, out var thumb) ? thumb : null,
                });
            }
            HasLater = Later.Count > 0;
            LaterCount = Later.Count == 0 ? "" : Later.Count.ToString(Inv);
            return Later.Where(r => r.Thumb == null).ToList();
        }

        public void SetServers(IList<KsfServer> servers, string yourServerAddress, ICollection<string> savedMaps)
        {
            Servers.Clear();
            foreach (var s in servers.OrderByDescending(s => s.Address == yourServerAddress).ThenByDescending(s => s.PlayerCount))
            {
                Servers.Add(new ServerRow
                {
                    Name = s.Name,
                    Map = s.Map,
                    Tier = "T" + s.Tier,
                    TierBrush = TierColor(s.Tier),
                    TierSoftBrush = TierSoft(s.Tier),
                    Players = s.PlayerCount.ToString(Inv),
                    Address = s.Address,
                    IsYours = s.Address == yourServerAddress,
                    IsSaved = savedMaps.Contains(s.Map),
                    SecondsLeft = s.TimeLeftSeconds,
                    FetchedAt = s.FetchedAt,
                });
                Servers[Servers.Count - 1].Update(DateTime.Now);
            }
            HasServers = Servers.Count > 0;
            ServersUpdated = "updated " + DateTime.Now.ToString("HH:mm", Inv);
        }

        public void SetRecent(IEnumerable<RecentRecord> records)
        {
            Recent.Clear();
            foreach (var r in records.Take(8))
            {
                var kind = r.Type ?? "Record";
                var color = kind.IndexOf("WR", StringComparison.OrdinalIgnoreCase) >= 0 ? "#F5C542"
                    : kind.IndexOf("Top", StringComparison.OrdinalIgnoreCase) >= 0 ? "#C084FC"
                    : kind.IndexOf("Group", StringComparison.OrdinalIgnoreCase) >= 0 ? "#3DDC97"
                    : "#9A9AA6";
                var zone = r.ZoneId >= MapReport.FirstBonusZone ? $"  (bonus {r.ZoneId - MapReport.FirstBonusZone + 1})" : r.ZoneId > 0 ? $"  (stage {r.ZoneId})" : "";
                Recent.Add(new RecentRow
                {
                    Kind = kind.ToUpperInvariant(),
                    KindBrush = Frozen(color),
                    KindSoftBrush = Frozen("#22" + color.Substring(1)),
                    Map = r.Map + zone,
                    Time = r.Time is double t ? Format.Time(t) : "",
                    Detail = string.Join("  ·  ", new[] { r.Rank > 0 ? $"#{r.Rank:N0}" : null, r.Server, r.Date is DateTime d ? Ago(d) : null }.Where(x => !string.IsNullOrEmpty(x))),
                });
            }
            HasRecent = Recent.Count > 0;
        }

        public static Brush TierColor(int tier) => Frozen(TierColors[Math.Max(0, Math.Min(TierColors.Length - 1, tier))]);

        static Brush TierSoft(int tier) => Frozen("#24" + TierColors[Math.Max(0, Math.Min(TierColors.Length - 1, tier))].Substring(1));

        static string TopPercent(int rank, int total)
        {
            var pct = rank * 100.0 / total;
            return pct < 1 ? string.Format(Inv, "top {0:0.0}%", Math.Max(pct, 0.1)) : string.Format(Inv, "top {0:0}%", Math.Ceiling(pct));
        }

        public static string Ago(DateTime when)
        {
            var span = DateTime.Now - when;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return when.ToString("MMM d", Inv);
        }

        static Brush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }
}
