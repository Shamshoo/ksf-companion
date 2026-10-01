using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using KsfCompanion.Ui;
using Forms = System.Windows.Forms;

namespace KsfCompanion
{
    enum LinkState { NoGame, Waiting, Testing, Ready, Unavailable }

    /// <summary>
    /// The brain of the app: follows the game's console log, fetches KSF data when the map changes, keeps the
    /// in-game card and the dashboard up to date and, on KSF servers, asks the server to print map info and rank.
    /// </summary>
    sealed class Companion
    {
        const long MaxLogBytes = 32L * 1024 * 1024;
        const int MaxCatchUpBytes = 8 * 1024 * 1024;
        const string Tick66 = "css", Tick100 = "css100t";
        static readonly Regex HostnameLine = new Regex(@"^hostname\s*:\s*(.*)$", RegexOptions.IgnoreCase);
        // Also from "status": the server's address, which is how KSF's server list names it too.
        static readonly Regex AddressLine = new Regex(@"^udp/ip\s*:\s*(?<address>\d{1,3}(?:\.\d{1,3}){3}:\d+)", RegexOptions.IgnoreCase);
        // Joining a server, before its map even starts loading: "Connected to 137.74.205.6:27018"
        static readonly Regex ConnectedLine = new Regex(@"^Connected to (?<address>\d{1,3}(?:\.\d{1,3}){3}:\d+)", RegexOptions.Compiled);
        // "status" lists players as: #  67 "SomePlayer"  [U:1:123456789]  3:53:52  99  0 active
        static readonly Regex StatusPlayerLine = new Regex(@"^#\s*\d+\s+(?:\d+\s+)?""(?<name>.+)""\s+\[U:1:(?<account>\d+)\]", RegexOptions.Compiled);
        // KSF's timer announces runs in chat: [Surf Timer] - SomePlayer finished in 08:23:89 (WR +07:16:94)
        static readonly Regex FinishLine = new Regex(@"^\[Surf Timer\] - (?<name>.+?) finished (?<zone>.*?)in (?<time>\d+(?:[:.]\d{1,3}){1,3})", RegexOptions.Compiled);
        // Comes just before your own finish line: "... You finished the map for the first time . You have received [47] points"
        static readonly Regex PointsLine = new Regex(@"^\[Surf Timer\] - Congratulations! .*received \[(?<points>\d+)\] points", RegexOptions.Compiled);
        // "[Surf Timer] - Nextmap: surf_x" or "[SM] Map voting has finished. The next map will be surf_x. (Received ...)"
        static readonly Regex NextMapLine = new Regex(@"^\[(?:Surf Timer\] - Nextmap: |SM\] .*?The next map will be )(?<map>[\w\-]+)", RegexOptions.Compiled);
        // The timer's warnings near the end of a map: "[Surf Timer] - 2 minutes remaining", "... 30 seconds remaining"
        static readonly Regex RemainingLine = new Regex(@"^\[Surf Timer\] - (?<n>\d+) (?<unit>minute|second)s? remaining", RegexOptions.Compiled);
        // The game starting a demo: "Recording to ksfc_live.dem..."
        static readonly Regex RecordingLine = new Regex(@"^Recording to (?<file>[^\s\\/]+?\.dem)\.\.\.", RegexOptions.Compiled);
        // After a respawn: "[Surf Timer] - Your timer has been resumed at 'Stage 2 - Hyttekos'"
        static readonly Regex ResumedLine = new Regex(@"^\[Surf Timer\] - Your timer has been resumed at 'Stage (?<n>\d+)", RegexOptions.Compiled);
        // The ways KSF extends a map: "The Map has Been extended for 10 minutes" (!cvote), "Extending map by 20 mins due to
        // players vote." (votemap), and the end-of-map vote's "[SM] The current map has been extended. (Received 80% ...)",
        // which doesn't say by how much.
        static readonly Regex ExtendedLine = new Regex(
            @"^(?:The Map has Been extended for (?<n>\d+) min|Extending map by (?<n>\d+) min|\[SM\] The current map has been extended)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Typing mp_timelimit in the console prints (only there): "mp_timelimit" = "80" ( def. "0" )
        static readonly Regex TimeLimitLine = new Regex(@"^""mp_timelimit"" = ""(?<minutes>\d+(?:\.\d+)?)""", RegexOptions.Compiled);

        readonly Settings settings;
        KeyNames keys;
        // The binds page: your keys for KSF commands, the keys KSF Companion has taken over in the game (to give back
        // what they did when they're taken off), and the turn speed.
        BindSet binds;
        HashSet<string> boundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool bindsToApply;
        int turnSpeed;
        int? turnSpeedToSave;
        readonly PlayLaterList later;
        readonly KsfApi api = new KsfApi();
        readonly ImageCache images;
        readonly DashboardViewModel vm = new DashboardViewModel();
        readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        readonly Forms.NotifyIcon tray;
        readonly Forms.ToolStripMenuItem statusItem, keepOnTopItem;
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        readonly Dictionary<string, MapReport> cache = new Dictionary<string, MapReport>(StringComparer.OrdinalIgnoreCase);
        readonly LogParser parser = new LogParser();
        readonly RegisteredWaitHandle showWait;

        GameConfig config;
        LogWatcher watcher;
        string setupError;
        DashboardWindow window;
        bool placementDirty;

        int gamePid;
        IntPtr gameWindow;
        DateTime gameSeenAt, gameStartedAt, nextGameCheck, nextCheckpoint;
        LinkState link = LinkState.NoGame;
        LinkFormat linkFormat;
        DateTime nextLinkTest;
        int linkAttempts;
        string awaitedNonce;
        TaskCompletionSource<bool> nonceSeen;
        TaskCompletionSource<string> hostnameSeen;

        string currentMap;
        DateTime mapSeenAt;
        DateTime? inGameAt;
        MapReport report;
        int fetchId;
        bool fetching;
        bool announcePending, cardEchoPending, announcing;
        DateTime nextAnnounceTry, lastKsfChatAt = DateTime.MinValue;

        List<KsfServer> servers = new List<KsfServer>();
        KsfServer yourServer;
        int? lastRank, lastPoints;
        DateTime nextServerPoll, nextRecentPoll;
        bool pollingServers, pollingRecent;
        string avatarFor;

        // Which KSF records to show (66 or 100 tick) and who "you" are in the game's chat.
        string detectedGame, manualGame, ourName, configName;
        readonly List<DateTime> refreshAt = new List<DateTime>();

        // Live bits: the server you're on, what's next, and how this session is going.
        bool onKsfServer;
        string connectedAddress, nextMapName;
        int? pointsJustEarned;
        // The stage or bonus you're on, when we last had it live (timer text or chat), and when moving on last made us
        // re-check your times on ksf.surf.
        int? currentZone;
        DateTime lastLiveZoneAt, lastZoneRefresh;
        // Stage and bonus records still coming in for this map (game|map).
        CancellationTokenSource zoneFetch;
        string zoneFetchKey;
        // The timer's on-screen text, read from a demo the game records (see LiveHud), and which map we asked it to record.
        LiveHud hud;
        string hudRequestedFor;
        bool demoBusyNoted;
        DateTime nextDemoCheck;
        TimeSpan demoRetry = TimeSpan.FromSeconds(30);
        int failedFetches;
        // ksf.surf's server list has you as a spectator (zone -1): used until the demo itself shows it.
        bool listedAsSpectating;
        // Where you stand on each tick rate (css / css100t), when to read your ksf.surf profile again, and what it takes
        // to reach a rank (for the next rank title): game|style|rank -> when looked up, points of whoever is there.
        readonly Dictionary<string, PlayerStanding> standings = new Dictionary<string, PlayerStanding>();
        DateTime nextStandingsCheck = DateTime.MinValue;
        readonly Dictionary<string, (DateTime At, int? Points)> pointsAtRank = new Dictionary<string, (DateTime, int?)>();
        bool levelLoading;
        double? tileSizeToSave;
        bool layoutToSave;
        // The map's time left, and when to read mp_timelimit in the console next (it only prints there).
        readonly MapClock clock = new MapClock();
        DateTime? timeLimitCheckAt;
        DateTime lastTimeLimitCheck = DateTime.MinValue;
        // Times you set in game that ksf.surf may not have yet: game|map -> zone -> best time.
        readonly Dictionary<string, Dictionary<int, double>> localBests = new Dictionary<string, Dictionary<int, double>>(StringComparer.OrdinalIgnoreCase);
        // Map finishes seen live that ksf.surf may not count yet: game|map -> (its count when we started counting, finishes since).
        readonly Dictionary<string, (int OnRecord, int Since)> liveFinishes = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        // The leaderboard on show follows where you are - the map (its stages are part of the run) or the bonus you're
        // on - unless you picked one, which lasts until you move on to another map or bonus.
        int? pinnedLeaderZone;
        int followedLeaderZone;
        // The nominate page: every KSF map (read once, kept on disk), and what's loading for it.
        readonly MapCatalog catalog;
        bool catalogLoading;
        int mapSearchId;
        readonly HashSet<string> thumbsLoading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The maps you've finished (per player, tick and style) that the nominate page marks, which of those lists is
        // on show, and which is being read from ksf.surf right now.
        readonly FinishedMaps finishedMaps;
        string finishedShown, finishedReading;
        readonly CancellationTokenSource shutdown = new CancellationTokenSource();
        readonly Dictionary<string, (DateTime At, List<WorldRecord> Top)> zoneTops = new Dictionary<string, (DateTime, List<WorldRecord>)>(StringComparer.OrdinalIgnoreCase);
        string loadingZoneTop;
        string lastLocalFinish;
        DateTime lastLocalFinishAt;
        readonly DateTime companionStartedAt = DateTime.Now;
        DateTime sessionStart = DateTime.Now;
        int sessionMaps, sessionFinishes, sessionPbs;

        public Companion(Settings settings, EventWaitHandle showSignal, bool startHidden)
        {
            this.settings = settings;
            keys = KeyNames.From(settings);
            later = new PlayLaterList(Path.Combine(Program.DataDir, "play-later.txt"));
            images = new ImageCache(api.Http);
            linkFormat = settings.Get("link_format") == "commandline" ? LinkFormat.CommandLine : LinkFormat.Raw;
            if (int.TryParse(settings.Get("last_rank"), out var rank)) lastRank = rank;
            if (int.TryParse(settings.Get("last_points"), out var points)) lastPoints = points;

            vm.SaveCommand = new RelayCommand(_ => SaveCurrentMap());
            vm.OpenMapCommand = new RelayCommand(_ => OpenMapPage(currentMap));
            vm.RefreshCommand = new RelayCommand(_ => RefreshEverything());
            vm.OpenLaterCommand = new RelayCommand(p => OpenMapPage(p as string));
            vm.RemoveLaterCommand = new RelayCommand(p => { if (p is string map && later.Remove(map)) { ListChanged(); vm.Toast = "Removed " + map; } });
            vm.NominateCommand = new RelayCommand(p => Nominate(p as string));
            vm.TeleportCommand = new RelayCommand(Teleport);
            vm.RtvCommand = new RelayCommand(_ => RockTheVote());
            vm.ToggleSavedCommand = new RelayCommand(p => ToggleSaved(p as string));
            catalog = new MapCatalog(Path.Combine(Program.CacheDir, "maps.txt"));
            vm.SetMapCatalog(catalog.Maps, loading: false);
            finishedMaps = new FinishedMaps(Path.Combine(Program.CacheDir, "finished-maps.txt"));
            ShowFinishedMaps();
            vm.ThumbsNeeded += rows => _ = LoadMapThumbsAsync(rows);
            vm.MapSearchChanged += text => _ = SearchKsfAsync(text);
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(DashboardViewModel.Page) || !vm.IsNominatePage) return;
                EnsureMapCatalog();
                EnsureFinishedMaps();
            };
            vm.SelectLeaderboardCommand = new RelayCommand(SelectLeaderboard);
            vm.FollowLeaderboardCommand = new RelayCommand(_ =>
            {
                pinnedLeaderZone = null;
                UpdateLeaderboard();
            });
            vm.JoinCommand = new RelayCommand(p => Join(p as string));
            vm.OpenFolderCommand = new RelayCommand(_ => OpenDataFolder());
            vm.TickCommand = new RelayCommand(p =>
            {
                manualGame = p as string == Tick100 ? Tick100 : Tick66;
                if (currentMap != null) _ = FetchAsync(currentMap);
            });
            vm.SetPlayer(settings.Get("last_name"), settings.Get("last_country"), lastRank, lastPoints, settings.Get("last_rank_tick"));
            // Simple or Advanced, and the parts you've hidden: kept in settings.ini.
            vm.Layout.Load(settings.Get("hidden").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries), settings.Get("view") == "simple",
                settings.GetInt("size", 80, 150) / 100.0);
            // Saved once the mouse is let go (the Size slider changes it on every step of a drag).
            vm.Layout.Changed += () => layoutToSave = true;
            // The nominate page's tile size (saved a moment after the slider stops, not on every step of a drag).
            if (double.TryParse(settings.Get("tile_size"), NumberStyles.Float, CultureInfo.InvariantCulture, out var tileSize)) vm.TileSize = tileSize;
            vm.TileSizeChanged += size => tileSizeToSave = size;
            binds = BindSet.Parse(settings.Get("binds"));
            turnSpeed = settings.GetInt("turn_speed", 50, 600);
            vm.Binds.Message += text => vm.Toast = text;
            vm.Binds.Changed += OnBindChanged;
            vm.Binds.TurnSpeedChanged += speed => turnSpeedToSave = speed;
            vm.PropertyChanged += (s, e) =>
            {
                // What the keys do in the game may have changed since (it saves config.cfg when it closes).
                if (e.PropertyName == nameof(DashboardViewModel.Page) && vm.IsBindsPage) vm.Binds.SetGameBinds(config?.CurrentBinds());
            };
            vm.Binds.GameKeyRemoved += RemoveGameBind;

            var menu = new Forms.ContextMenuStrip();
            statusItem = new Forms.ToolStripMenuItem("Starting...") { Enabled = false };
            var openItem = new Forms.ToolStripMenuItem("Open dashboard", null, (s, e) => ShowDashboard(activate: true));
            openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
            var announce = new Forms.ToolStripMenuItem("Run !m and !mrank on map load (KSF answers in chat)") { CheckOnClick = true, Checked = settings.GetBool("run_server_commands") };
            announce.CheckedChanged += (s, e) => settings.Set("run_server_commands", announce.Checked ? "1" : "0");
            var autoOpen = new Forms.ToolStripMenuItem("Open the dashboard when CS:S starts") { CheckOnClick = true, Checked = settings.GetBool("dashboard_on_game_start") };
            autoOpen.CheckedChanged += (s, e) => settings.Set("dashboard_on_game_start", autoOpen.Checked ? "1" : "0");
            var startWithWindows = new Forms.ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = Startup.IsEnabled };
            startWithWindows.CheckedChanged += (s, e) => Startup.Set(startWithWindows.Checked);
            keepOnTopItem = new Forms.ToolStripMenuItem("Keep the dashboard on top") { CheckOnClick = true, Checked = settings.GetBool("window_topmost") };
            keepOnTopItem.CheckedChanged += (s, e) =>
            {
                EnsureWindow();
                window.SetTopmost(keepOnTopItem.Checked);
            };
            menu.Items.Add(statusItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(openItem);
            menu.Items.Add("Open this map on ksf.surf", null, (s, e) => OpenMapPage(currentMap));
            menu.Items.Add("Refresh now", null, (s, e) => RefreshEverything());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(keepOnTopItem);
            menu.Items.Add(announce);
            menu.Items.Add(autoOpen);
            menu.Items.Add(startWithWindows);
            menu.Items.Add("Open settings folder", null, (s, e) => OpenDataFolder());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Remove from CS:S...", null, (s, e) => Uninstall());
            menu.Items.Add("Exit", null, (s, e) => ExitApp());

            tray = new Forms.NotifyIcon { Icon = AppIcon.Get(), Text = Program.AppName, ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == Forms.MouseButtons.Left) ShowDashboard(activate: true); };

            showWait = ThreadPool.RegisterWaitForSingleObject(showSignal,
                (state, timedOut) => dispatcher.BeginInvoke(new Action(() => ShowDashboard(activate: true))), null, Timeout.Infinite, false);

            parser.MapChanged += map => OnMapChanged(map, justJoined: true);
            parser.InGame += () =>
            {
                if (currentMap == null || inGameAt != null) return;
                inGameAt = DateTime.Now;
                // Still on a KSF server: start the live timer recording now, not once "status" has answered - a stage
                // finished in the first seconds of the map would be missed otherwise.
                if (onKsfServer) EnsureLiveDemo();
                // The new map's time limit has arrived by now.
                CheckTimeLimitSoon(2);
            };
            vm.Clock = clock;
            parser.SaveRequested += SaveCurrentMap;
            parser.GameStarted += OnGameStarted;
            parser.LineParsed += OnLine;

            SetUpGame();
            ListChanged();
            UpdateStatus();

            timer.Tick += (s, e) => OnTick();
            timer.Start();
            if (!startHidden) ShowDashboard(activate: true);
        }

        /// <summary>
        /// css (66 tick) or css100t. Follows the server you're on unless you picked one on the dashboard or in settings.
        /// </summary>
        string Game => manualGame ?? settings.FixedGame ?? detectedGame ?? (settings.Get("last_game") == Tick100 ? Tick100 : Tick66);
        int KsfStyle => settings.GetInt("ksf_style", 0, 3);

        void SetDetectedGame(string game)
        {
            if (game == detectedGame) return;
            Program.Trace($"server tick detected: {game}");
            var before = Game;
            detectedGame = game;
            if (settings.Get("last_game") != game) settings.Set("last_game", game);
            if (Game != before && currentMap != null) _ = FetchAsync(currentMap);
        }

        /// <summary>
        /// Just connected to a KSF server that's in KSF's list: use its tick rate from the start. The map line comes
        /// right after and loads the right records; a map already on screen is reloaded if the tick changed.
        /// </summary>
        void ChooseTickEarly(KsfServer server)
        {
            onKsfServer = true;
            if (server.Game == detectedGame) return;
            Program.Trace($"joining {server.Name}: {server.Game}");
            detectedGame = server.Game;
            if (settings.Get("last_game") != server.Game) settings.Set("last_game", server.Game);
        }

        /// <summary>A server we haven't seen in KSF's list yet: look it up before the map finishes loading.</summary>
        async Task LookUpServerAsync(string address)
        {
            var lists = await Task.WhenAll(ServersOrEmpty(Tick66), ServersOrEmpty(Tick100));
            var all = lists[0].Concat(lists[1]).ToList();
            if (all.Count > 0) servers = all;
            var server = all.FirstOrDefault(s => s.Address == address);
            if (server == null || address != connectedAddress) return;
            var before = Game;
            ChooseTickEarly(server);
            // The map may already be loading with the old tick rate's records.
            if (Game != before && currentMap != null) _ = FetchAsync(currentMap);
        }

        static bool Is100Tick(string hostname) =>
            hostname.IndexOf("100 tick", StringComparison.OrdinalIgnoreCase) >= 0 || hostname.IndexOf("100t", StringComparison.OrdinalIgnoreCase) >= 0;
        bool DashboardVisible => window != null && window.IsVisible && window.WindowState != WindowState.Minimized;

        void SetUpGame()
        {
            var dir = SteamLocator.FindCstrikeDir(settings.Get("game_dir"));
            if (dir == null) return;

            config = new GameConfig(dir);
            configName = config.PlayerName();
            var firstTime = !config.IsInstalled;
            try
            {
                config.Install(settings);
                config.RememberOriginals(settings, binds.Keys.Values);
                config.WriteBinds(BoundActions(), turnSpeed);
                boundKeys = WantedKeys();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                setupError = ex.Message;
            }
            LoadBindsPage();

            var gameRunning = GameBridge.FindGameProcessId() != 0;
            hud = new LiveHud(config.CstrikeDir);
            hud.ZoneChanged += OnHudZone;
            hud.ZoneFinished += OnHudFinished;
            hud.TimeLeftShown += (minutes, previous, changedAgo) =>
            {
                if (currentMap == null) return;
                if (clock.FromPanel(minutes, previous, changedAgo, DateTime.Now)) CheckTimeLimitSoon(0.5);
                if (previous != minutes) TraceClock($"panel {minutes} min" + (previous is int p ? $", was {p}" : ""));
                vm.Tick(DateTime.Now);
            };
            // Left over from a game that has closed since (it's only needed while playing).
            if (!gameRunning) hud.Delete();
            var log = config.LogCandidates.First();
            var start = CatchUpOnMissedSaves(log, out var lastMap);
            if (!gameRunning && start > MaxLogBytes && TryDelete(log)) start = 0;

            watcher = new LogWatcher(config.LogCandidates, new Dictionary<string, long> { [log] = start });
            watcher.LineRead += parser.Feed;
            SaveCheckpoint();

            if (firstTime && setupError == null)
            {
                tray.ShowBalloonTip(10000, "KSF Companion is set up",
                    $"In-game: {keys.Save} saves the map for later, hold {keys.Card} for the KSF card, hold {keys.List} for your list.",
                    Forms.ToolTipIcon.Info);
            }

            if (gameRunning)
            {
                // Already on a map: get everything ready, but don't announce anything mid-map.
                var map = lastMap ?? LogWatcher.FindCurrentMap(config.LogCandidates);
                if (map != null) OnMapChanged(map, justJoined: false);
            }
            _ = FillInMissingTiersAsync();
        }

        void OnTick()
        {
            try { watcher?.Poll(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            // The live timer text (stage you're on, stage/bonus finishes) from the demo the game is recording.
            if (gamePid != 0) hud?.Poll();
            CheckLiveDemo(DateTime.Now);

            var now = DateTime.Now;
            if (now >= nextGameCheck)
            {
                nextGameCheck = now.AddSeconds(2);
                CheckGame(now);
                RefreshIfStale(now);
                UpdateStatus();
                if (placementDirty) SavePlacement();
            }
            if (now >= nextCheckpoint)
            {
                nextCheckpoint = now.AddSeconds(30);
                SaveCheckpoint();
            }
            if (bindsToApply) ApplyBinds();
            if (System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Released)
            {
                if (turnSpeedToSave is int speed)
                {
                    turnSpeedToSave = null;
                    turnSpeed = speed;
                    settings.Set("turn_speed", speed.ToString(CultureInfo.InvariantCulture));
                    ApplyBinds(onlySpeed: true);
                }
                if (tileSizeToSave is double size)
                {
                    tileSizeToSave = null;
                    settings.Set("tile_size", Math.Round(size).ToString(CultureInfo.InvariantCulture));
                }
                if (layoutToSave)
                {
                    layoutToSave = false;
                    settings.Set("view", vm.Layout.IsSimple ? "simple" : "advanced");
                    settings.Set("hidden", string.Join(", ", vm.Layout.Hidden));
                    settings.Set("size", Math.Round(vm.Layout.Scale * 100).ToString(CultureInfo.InvariantCulture));
                }
            }
            if (refreshAt.Count > 0 && refreshAt.Min() <= now)
            {
                refreshAt.RemoveAll(t => t <= now);
                ReloadFromKsf();
            }
            if (DashboardVisible)
            {
                vm.Tick(now);
                // After a "too many requests" from ksf.surf the regular polls sit out for a bit.
                var ksfBusy = now < api.BusyUntil;
                if (now >= nextServerPoll && !pollingServers && !ksfBusy) _ = PollServersAsync();
                if (now >= nextRecentPoll && !pollingRecent && !ksfBusy) _ = PollRecentAsync();
                if (now >= nextStandingsCheck && !ksfBusy) _ = RefreshStandingsAsync();
            }
            if (link == LinkState.Waiting && now >= nextLinkTest) _ = TestLinkAsync();
            // Now and then anyway, in case the map was extended in a way that said nothing we recognise.
            if (onKsfServer && currentMap != null && timeLimitCheckAt == null && now - lastTimeLimitCheck > TimeSpan.FromMinutes(5)) CheckTimeLimitSoon(0);
            if (timeLimitCheckAt is DateTime due && now >= due && link == LinkState.Ready && currentMap != null && now - lastTimeLimitCheck >= TimeSpan.FromSeconds(3))
            {
                timeLimitCheckAt = null;
                lastTimeLimitCheck = now;
                // Prints only in the console: "mp_timelimit" = "80" ( def. "0" )
                _ = PushAsync("mp_timelimit");
            }
            if ((announcePending || cardEchoPending) && !announcing && link == LinkState.Ready && now >= nextAnnounceTry)
            {
                if (now > mapSeenAt.AddMinutes(2))
                {
                    announcePending = cardEchoPending = false;
                }
                else if (now >= (inGameAt?.AddSeconds(2) ?? mapSeenAt.AddSeconds(30)))
                {
                    _ = AnnounceAsync();
                }
            }
        }

        void CheckGame(DateTime now)
        {
            var pid = GameBridge.FindGameProcessId();
            if (pid == 0)
            {
                if (gamePid != 0)
                {
                    gamePid = 0;
                    gameWindow = IntPtr.Zero;
                    link = LinkState.NoGame;
                    yourServer = null;
                    onKsfServer = false;
                    connectedAddress = nextMapName = hudRequestedFor = null;
                    currentZone = null;
                    vm.SetCurrentZone(null);
                    // The game let go of its demo when it closed; it was only there for the live timer text.
                    hud?.Delete();
                    vm.SetServerLine(null);
                    vm.SetLive(false);
                    vm.SetLiveServer(null, null, null);
                    vm.SetNextMap(null, null);
                    vm.EndSession();
                }
                return;
            }

            if (pid != gamePid)
            {
                gamePid = pid;
                gameSeenAt = now;
                gameStartedAt = ProcessStartTime(pid) ?? now;
                gameWindow = IntPtr.Zero;
                linkAttempts = 0;
                nextLinkTest = now.AddSeconds(8);
                link = LinkState.Waiting;
                vm.SetLive(currentMap != null);
                StartSession(gameStartedAt);
                OpenDashboardForGame();
            }
            if (gameWindow == IntPtr.Zero || !NativeMethods.IsWindow(gameWindow))
                gameWindow = GameBridge.FindGameWindow(pid);
        }

        /// <summary>
        /// When CS:S starts, pop the dashboard up on the second monitor without taking focus from the game.
        /// </summary>
        void OpenDashboardForGame()
        {
            if (!settings.GetBool("dashboard_on_game_start") || Forms.Screen.AllScreens.Length < 2 || DashboardVisible) return;
            EnsureWindow();
            if (window.IsOnSecondaryScreen) ShowDashboard(activate: false);
        }

        /// <summary>
        /// Works out once per game launch whether the game runs commands sent over WM_COPYDATA: we send an
        /// echo with a random tag and wait for it to show up in the console log.
        /// </summary>
        async Task TestLinkAsync()
        {
            if (link != LinkState.Waiting) return;
            if (gameWindow == IntPtr.Zero)
            {
                nextLinkTest = DateTime.Now.AddSeconds(3);
                return;
            }

            link = LinkState.Testing;
            var formats = linkFormat == LinkFormat.CommandLine
                ? new[] { LinkFormat.CommandLine, LinkFormat.Raw }
                : new[] { LinkFormat.Raw, LinkFormat.CommandLine };

            foreach (var format in formats)
            {
                var nonce = "#" + Guid.NewGuid().ToString("N").Substring(0, 8);
                var seen = new TaskCompletionSource<bool>();
                awaitedNonce = nonce;
                nonceSeen = seen;

                // Turning con_logfile on here also covers a game that was started before we were installed.
                var result = await SendAsync($"con_logfile {GameConfig.LogFileName}; echo \"{GameConfig.LinkMarker} {nonce}\"", format);
                if (result == SendResult.Timeout || result == SendResult.NoWindow)
                {
                    awaitedNonce = null;
                    if (link != LinkState.Testing) return;
                    // Busy loading - try again in a moment.
                    link = ++linkAttempts > 20 ? LinkState.Unavailable : LinkState.Waiting;
                    nextLinkTest = DateTime.Now.AddSeconds(4);
                    return;
                }

                var winner = await Task.WhenAny(seen.Task, Task.Delay(4000));
                awaitedNonce = null;
                if (link != LinkState.Testing) return;
                if (winner == seen.Task)
                {
                    linkFormat = format;
                    settings.Set("link_format", format == LinkFormat.Raw ? "raw" : "commandline");
                    link = LinkState.Ready;
                    UpdateStatus();
                    // Makes the keys work right away, even in a game started before KSF Companion was installed.
                    await PushAsync("exec ksf_companion");
                    // Already on a map: "status" tells us the server (66 or 100 tick) and your in-game name.
                    if (currentMap != null) await PushAsync("status");
                    return;
                }
            }

            link = LinkState.Unavailable;
            UpdateStatus();
        }

        void OnLine(string line)
        {
            if (awaitedNonce != null && line.StartsWith(GameConfig.LinkMarker, StringComparison.Ordinal) && line.EndsWith(awaitedNonce, StringComparison.Ordinal))
                nonceSeen?.TrySetResult(true);

            var host = HostnameLine.Match(line);
            if (host.Success)
            {
                var hostname = host.Groups[1].Value.Trim();
                hostnameSeen?.TrySetResult(hostname);
                onKsfServer = hostname.IndexOf("ksf", StringComparison.OrdinalIgnoreCase) >= 0;
                if (onKsfServer)
                {
                    SetDetectedGame(Is100Tick(hostname) ? Tick100 : Tick66);
                    EnsureLiveDemo();
                }
                return;
            }

            // The game says which file it's recording to (it picks ksfc_live_2.dem etc. if the name is taken).
            var recordingTo = RecordingLine.Match(line);
            if (recordingTo.Success)
            {
                var file = recordingTo.Groups["file"].Value;
                if (hud != null && config != null && file.StartsWith(LiveHud.DemoName, StringComparison.OrdinalIgnoreCase))
                {
                    Program.Trace("live hud: reading " + file);
                    hud.Watch(Path.Combine(config.CstrikeDir, file), alreadyRunning: false);
                    demoRetry = TimeSpan.FromSeconds(30);
                }
                return;
            }

            // The game's answer to "record" while you're already recording a demo of your own.
            if (line.StartsWith("Already recording", StringComparison.Ordinal))
            {
                // Most likely our own demo of this map, from before KSF Companion restarted: follow that one.
                if (FollowOwnDemo()) return;
                if (!demoBusyNoted)
                {
                    demoBusyNoted = true;
                    vm.Toast = "You're recording a demo, so live stage times wait until it's stopped";
                }
                return;
            }

            // A new server: its address says which KSF server it is - and so 66 or 100 tick - before the map loads,
            // so the first thing shown for the new map is already the right records.
            var connected = ConnectedLine.Match(line);
            if (connected.Success)
            {
                connectedAddress = connected.Groups["address"].Value;
                yourServer = null;
                var known = servers.FirstOrDefault(s => s.Address == connectedAddress);
                if (known != null) ChooseTickEarly(known);
                else _ = LookUpServerAsync(connectedAddress);
                nextServerPoll = DateTime.MinValue;
                return;
            }

            var extended = ExtendedLine.Match(line);
            if (extended.Success)
            {
                int? minutes = extended.Groups["n"].Success ? int.Parse(extended.Groups["n"].Value, CultureInfo.InvariantCulture) : (int?)null;
                clock.Extended(minutes, DateTime.Now);
                TraceClock($"extended by {minutes?.ToString(CultureInfo.InvariantCulture) ?? "?"} min");
                // Extending raises mp_timelimit: read it again once the new value has reached the game.
                CheckTimeLimitSoon(1.5);
                vm.Tick(DateTime.Now);
                vm.Toast = minutes is int m ? $"{currentMap ?? "The map"} was extended by {m} minutes" : $"{currentMap ?? "The map"} was extended";
                return;
            }

            var timeLimit = TimeLimitLine.Match(line);
            if (timeLimit.Success)
            {
                var minutes = double.Parse(timeLimit.Groups["minutes"].Value, CultureInfo.InvariantCulture);
                clock.FromConsole(minutes, DateTime.Now);
                TraceClock("mp_timelimit " + minutes.ToString(CultureInfo.InvariantCulture));
                vm.Tick(DateTime.Now);
                return;
            }

            var address = AddressLine.Match(line);
            if (address.Success)
            {
                var value = address.Groups["address"].Value;
                if (value != connectedAddress)
                {
                    connectedAddress = value;
                    Program.Trace($"server address: {value}");
                    // Look the server up in KSF's list right away rather than waiting for the next poll.
                    nextServerPoll = DateTime.MinValue;
                }
                return;
            }

            var player = StatusPlayerLine.Match(line);
            if (player.Success)
            {
                if (uint.TryParse(player.Groups["account"].Value, out var account) && account == SteamLocator.AccountId(CurrentSteamId()))
                {
                    ourName = player.Groups["name"].Value;
                    Program.Trace($"in-game name: {ourName}");
                }
                return;
            }

            if (!line.StartsWith("[", StringComparison.Ordinal)) return;

            // KSF servers advertise in chat every few minutes; a fallback if "status" gets no answer.
            if (line.StartsWith("[KSF Clan]", StringComparison.Ordinal)) lastKsfChatAt = DateTime.Now;

            var next = NextMapLine.Match(line);
            if (next.Success)
            {
                _ = ShowNextMapAsync(next.Groups["map"].Value.ToLowerInvariant());
                return;
            }

            var resumed = ResumedLine.Match(line);
            if (resumed.Success)
            {
                SetCurrentZone(int.Parse(resumed.Groups["n"].Value, CultureInfo.InvariantCulture), ZoneSource.Live);
                return;
            }

            // The timer's own countdown is exact.
            var remaining = RemainingLine.Match(line);
            if (remaining.Success)
            {
                var n = int.Parse(remaining.Groups["n"].Value, CultureInfo.InvariantCulture);
                var seconds = remaining.Groups["unit"].Value == "minute" ? n * 60 : n;
                clock.Countdown(seconds, DateTime.Now);
                TraceClock($"timer says {seconds}s");
                vm.Tick(DateTime.Now);
                return;
            }
            if (line.StartsWith("[Surf Timer] - ---- MAP END", StringComparison.Ordinal))
            {
                clock.Countdown(0, DateTime.Now);
                vm.Tick(DateTime.Now);
                return;
            }

            var zoneFinish = ChatZoneFinishLine.Match(line);
            if (zoneFinish.Success)
            {
                OnOwnFinish(zoneFinish.Groups["zone"].Value, zoneFinish.Groups["time"].Value);
                return;
            }

            var finish = FinishLine.Match(line);
            if (finish.Success && IsMe(finish.Groups["name"].Value))
            {
                OnOwnFinish(finish.Groups["zone"].Value.Trim(), finish.Groups["time"].Value);
            }
            else if (line.StartsWith("[Surf Timer] - Congratulations! You finished", StringComparison.Ordinal))
            {
                var points = PointsLine.Match(line);
                pointsJustEarned = points.Success ? int.Parse(points.Groups["points"].Value, CultureInfo.InvariantCulture) : (int?)null;
                OnOwnFinish(null, null);
            }
        }

        bool IsMe(string name) => name == "You" ||
            new[] { ourName, configName, report?.PlayerName, settings.Get("last_name") }
                .Any(n => !string.IsNullOrEmpty(n) && string.Equals(n, name, StringComparison.Ordinal));

        // "bonus [Bonus 4 - Watti] " in "... finished bonus [Bonus 4 - Watti] in 00:18:26" (the same for stages, when the
        // timer's chat messages print them)
        static readonly Regex ZoneText = new Regex(@"\b(?<kind>Stage|Bonus) (?<n>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // The timer's own-finish chat line in the style of its on-screen text: "[Surf Timer] - Finished [Stage 3]: 00:17:06 ..."
        static readonly Regex ChatZoneFinishLine = new Regex(
            @"^\[Surf Timer\] - (?:You )?[Ff]inished (?<zone>\[?(?:Stage|Bonus) \d+[^\]:]*\]?)\s*:?\s*(?:in )?(?<time>\d+(?::\d{2}){1,2}(?:[:.]\d{1,3})?)",
            RegexOptions.Compiled);

        /// <summary>
        /// You just finished (the timer said so in chat). Show the time right away, then get the new rank,
        /// group and leaderboard from KSF once it has the run.
        /// </summary>
        void OnOwnFinish(string zone, string timeText)
        {
            if (currentMap == null) return;
            Program.Trace($"own finish on {currentMap}: zone='{zone}' time='{timeText}'");
            // A stage or bonus finish in chat: that's instant (the on-screen text only reaches us with the demo's next write).
            var zoneRef = zone == null ? null : ZoneText.Match(zone);
            if (timeText != null && zoneRef?.Success == true && TryParseTimerTime(timeText, out var zoneTime))
            {
                var n = int.Parse(zoneRef.Groups["n"].Value, CultureInfo.InvariantCulture);
                var bonus = zoneRef.Groups["kind"].Value.StartsWith("b", StringComparison.OrdinalIgnoreCase);
                var id = bonus ? MapReport.FirstBonusZone - 1 + n : n;
                RecordLocalBest(id, zoneTime, announce: true);
                // Finishing a stage puts you at the start of the next one.
                if (!bonus && report?.IsStaged == true && n < report.Info.StageCount) SetCurrentZone(n + 1, ZoneSource.Live);
                return;
            }

            var mainMap = string.IsNullOrEmpty(zone) || zone.Equals("the map", StringComparison.OrdinalIgnoreCase);
            if (timeText != null && mainMap && TryParseTimerTime(timeText, out var time))
            {
                sessionFinishes++;
                var points = pointsJustEarned;
                pointsJustEarned = null;
                if (report != null && report.Game == Game)
                {
                    var previous = report.Main?.Time;
                    // Chat only shows hundredths, so compare at that precision.
                    var improved = previous == null || time < Math.Floor(previous.Value * 100) / 100 - 0.0001;
                    vm.ShowFreshFinish(time, report.Wr, improved);
                    Program.Trace($"showing finish right away: {Format.Time(time)} (improved={improved})");
                    if (improved)
                    {
                        sessionPbs++;
                        var beatWr = report.Wr != null && time < report.Wr.Time;
                        var detail = previous == null ? Format.Time(time) : $"{Format.Time(time)}   {Format.Diff(time - previous.Value)}";
                        if (points > 0) detail += $"   +{points} pts";
                        var title = beatWr ? "WORLD RECORD" : previous == null ? "FIRST FINISH" : "NEW PERSONAL BEST";
                        Program.Trace($"celebrate: {title} / {detail}");
                        vm.Celebrate(title, detail);
                    }
                    else
                    {
                        vm.Toast = $"Finished in {Format.Time(time)}  -  your PB is {Format.Time(previous.Value)}";
                    }
                }
                else
                {
                    vm.Toast = $"Finished {currentMap} in {Format.Time(time)}";
                }
                // The finish counts right away; ksf.surf's own count takes over once it has caught up.
                var key = CacheKey(Game, currentMap);
                liveFinishes[key] = liveFinishes.TryGetValue(key, out var counted) ? (counted.OnRecord, counted.Since + 1)
                    : (report?.Game == Game ? report.Main?.Completions ?? 0 : 0, 1);
                // After the PB check above: this makes it the time on record until ksf.surf has it.
                RecordLocalBest(0, time, announce: true);
                if (report != null && ApplyLocalBests(report)) WriteCard();
                MarkFinished(currentMap, time, Game);
                UpdateSession();
                // New points (and maybe a new title): look at your profile again once ksf.surf has the run.
                if (nextStandingsCheck > DateTime.Now.AddSeconds(90)) nextStandingsCheck = DateTime.Now.AddSeconds(90);
            }
            refreshAt.Add(DateTime.Now.AddSeconds(1.5));
            refreshAt.Add(DateTime.Now.AddSeconds(12));
        }

        /// <summary>KSF announces the next map in chat near the end of the current one.</summary>
        async Task ShowNextMapAsync(string map)
        {
            if (map == nextMapName) return;
            nextMapName = map;
            vm.SetNextMap(map, null);
            var tier = await api.GetTierAsync(map);
            Program.Trace($"next map: {map} (tier {tier?.ToString(CultureInfo.InvariantCulture) ?? "?"})");
            if (nextMapName == map) vm.SetNextMap(map, tier);
            await PrefetchMapAsync(map);
        }

        /// <summary>
        /// The next map, as soon as KSF names it: its stage and bonus records are fetched in the background (and kept
        /// for a day), so your times on it - with the gaps to the records - are there the moment it starts.
        /// </summary>
        async Task PrefetchMapAsync(string map)
        {
            var game = Game;
            var info = catalog.Find(map);
            // Its picture too, so the map's picture is there the moment it starts.
            _ = images.MapAsync(map, 40);
            try
            {
                if (info == null)
                {
                    var next = await api.GetReportAsync(map, CurrentSteamId(), game, KsfStyle);
                    info = next.Info;
                    if (next.Error == null && next.PersonalError == null) cache[CacheKey(game, map)] = next;
                }
                var zones = MapReport.ZonesOf(info);
                if (info == null || zones.Count == 0 || api.FreshZoneCount(info.Name, zones, game, KsfStyle) == zones.Count) return;
                Program.Trace($"getting {info.Name}'s stage/bonus records ahead of time ({zones.Count})");
                await api.FetchZoneWrsAsync(info.Name, zones, game, KsfStyle, (zone, wr) => { }, shutdown.Token, background: true);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                Program.Trace($"next map records: {ex.GetBaseException().Message}");
            }
        }

        enum ZoneSource { Live, ServerList }

        /// <summary>
        /// The stage or bonus you're on. The timer's own text and chat are live; ksf.surf's server list is a minute or
        /// so behind, so it only counts when nothing live has been heard for a while - and then moving on to the next
        /// stage is the hint that one was finished, so your times get checked on ksf.surf again.
        /// </summary>
        void SetCurrentZone(int? zone, ZoneSource source)
        {
            if (source == ZoneSource.Live) lastLiveZoneAt = DateTime.Now;
            // The server list is a minute or so behind: it must never override what the timer's text shows now -
            // including when you've simply stayed on the same stage/bonus for a while.
            else if (DateTime.Now - lastLiveZoneAt < TimeSpan.FromSeconds(90)
                     || (hud != null && DateTime.Now - hud.LastZoneSeenAt < TimeSpan.FromSeconds(60))
                     || hud?.Spectating == false && hud.FindRecordingDemo() != null) return;
            if (zone == currentZone) return;
            var previous = currentZone;
            currentZone = zone;
            vm.SetCurrentZone(zone);
            UpdateLeaderboard();
            Program.Trace($"on: {(previous is int p ? MapReport.ZoneName(p) : "-")} -> {(zone is int z ? MapReport.ZoneName(z) : "-")} ({source})");
            if (source == ZoneSource.ServerList && previous >= 1 && zone > previous && report?.IsStaged == true && DateTime.Now - lastZoneRefresh > TimeSpan.FromSeconds(20))
            {
                lastZoneRefresh = DateTime.Now;
                refreshAt.Add(DateTime.Now.AddSeconds(1));
            }
        }

        /// <summary>The timer's text says you finished a stage or bonus (read live from the demo).</summary>
        void OnHudFinished(int zone, double time, bool justNow)
        {
            if (MaybeSpectating) return;
            Program.Trace($"hud: finished {MapReport.ZoneName(zone)} in {Format.Short(time)}{(justNow ? "" : " (from before)")}");
            lastLiveZoneAt = DateTime.Now;
            RecordLocalBest(zone, time, announce: justNow);
        }

        void OnHudZone(int zone)
        {
            if (!MaybeSpectating) SetCurrentZone(zone, ZoneSource.Live);
        }

        /// <summary>The demo hasn't shown yet whether you're spectating, but ksf.surf's list says you are.</summary>
        bool MaybeSpectating => hud?.Spectating == null && listedAsSpectating;

        /// <summary>Puts the right leaderboard on show: your pick, or else where you are (a bonus, or the map for everything else).</summary>
        void UpdateLeaderboard()
        {
            var follow = currentZone is int z && MapReport.IsBonus(z) ? z : 0;
            if (follow != followedLeaderZone)
            {
                // You moved on to another bonus or back to the map: follow again.
                followedLeaderZone = follow;
                pinnedLeaderZone = null;
            }
            if (report == null || !report.IsOnKsf) return;
            var zone = pinnedLeaderZone ?? followedLeaderZone;
            if (zone != 0 && !report.RecordZones.Contains(zone)) zone = 0;
            vm.SetLeaderZone(zone, pinnedLeaderZone != null);
            if (zone != 0) _ = LoadZoneTopAsync(report, zone);
        }

        /// <summary>Clicking a leaderboard chip or a row in "your times": show that leaderboard (the one you're on = follow again).</summary>
        void SelectLeaderboard(object parameter)
        {
            if (!(parameter is int zone)) return;
            pinnedLeaderZone = zone == followedLeaderZone ? (int?)null : zone;
            UpdateLeaderboard();
        }

        /// <summary>A stage's or bonus's top 10, fetched when it's first shown and kept a few minutes.</summary>
        async Task LoadZoneTopAsync(MapReport r, int zone)
        {
            var key = $"{r.Game}|{r.Map}|{zone}";
            if (zoneTops.TryGetValue(key, out var known))
            {
                vm.SetZoneTop(zone, known.Top);
                if (DateTime.Now - known.At < TimeSpan.FromMinutes(3)) return;
            }
            if (loadingZoneTop == key || DateTime.Now < api.BusyUntil) return;
            loadingZoneTop = key;
            try
            {
                var top = await api.GetZoneTopAsync(r.Info.Name, zone, r.Game, KsfStyle);
                zoneTops[key] = (DateTime.Now, top);
                if (report == null || report.Game != r.Game || report.Map != r.Map) return;
                // Its first row is the record: keep the gaps in "your times" up to date too.
                if (top.Count > 0)
                {
                    report.ZoneWrs[zone] = top[0];
                    vm.ShowTimes(report);
                }
                vm.SetZoneTop(zone, top);
            }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                Program.Trace($"top of {MapReport.ZoneName(zone)}: {ex.GetBaseException().Message}");
            }
            finally
            {
                if (loadingZoneTop == key) loadingZoneTop = null;
            }
        }

        /// <summary>
        /// The nominate page needs every KSF map: read ksf.surf's whole list once (about 90 small requests, paced),
        /// then only top it up with the newest maps now and then. Shown as it comes in.
        /// </summary>
        async void EnsureMapCatalog()
        {
            if (catalogLoading) return;
            var full = catalog.Count == 0 || DateTime.Now - catalog.CompleteAt > TimeSpan.FromDays(7);
            if (!full && DateTime.Now - catalog.ToppedUpAt < TimeSpan.FromHours(12)) return;
            catalogLoading = true;
            vm.SetMapCatalog(catalog.Maps, loading: true);
            var finished = false;
            try
            {
                for (int start = 1, pages = 0; start < 3000; start += 10, pages++)
                {
                    var page = await api.GetMapsPageAsync(start, shutdown.Token);
                    if (page.Count == 0) break;
                    var anythingNew = page.Any(m => !catalog.Has(m.Name));
                    catalog.Add(page);
                    // Topping up: the list is newest first, so a page of known maps means we're done.
                    if (!full && !anythingNew) break;
                    if (pages % 5 == 4) vm.SetMapCatalog(catalog.Maps, loading: true);
                    if (pages % 20 == 19) catalog.Save();
                }
                finished = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                Program.Trace("map list: " + ex.GetBaseException().Message);
            }
            finally
            {
                if (finished)
                {
                    if (full) catalog.MarkComplete();
                    else catalog.MarkToppedUp();
                }
                catalog.Save();
                catalogLoading = false;
                vm.SetMapCatalog(catalog.Maps, loading: false);
                Program.Trace($"map list: {catalog.Count} maps (finished: {finished})");
            }
        }

        string FinishedKey() => FinishedMaps.Key(CurrentSteamId(), Game, KsfStyle);

        /// <summary>The nominate page marks the maps you've finished on the tick rate you're playing.</summary>
        void ShowFinishedMaps()
        {
            var key = FinishedKey();
            finishedShown = key;
            vm.SetFinishedMaps(finishedMaps.Of(key), loading: key != null && key == finishedReading);
        }

        /// <summary>
        /// Which maps you've finished, for the nominate page: the "best records" on your ksf.surf profile read to the
        /// end - 5 maps a request, paced, so about 40 requests for 200 maps - at most about once a day. Maps you finish
        /// in between are added as they happen (MarkFinished).
        /// </summary>
        async void EnsureFinishedMaps()
        {
            var steamId = CurrentSteamId();
            var game = Game;
            var style = KsfStyle;
            var key = FinishedMaps.Key(steamId, game, style);
            if (key == null || finishedReading != null || DateTime.Now - finishedMaps.ReadAt(key) < TimeSpan.FromHours(20))
            {
                ShowFinishedMaps();
                return;
            }
            finishedReading = key;
            ShowFinishedMaps();
            int read = 0;
            var complete = false;
            try
            {
                await api.GetFinishedMapsAsync(steamId, game, style, page =>
                {
                    finishedMaps.Merge(key, page);
                    read += page.Count;
                    // Shown as it comes in.
                    if (page.Count > 0 && read % 25 == 0 && key == finishedShown) vm.SetFinishedMaps(finishedMaps.Of(key), loading: true);
                }, shutdown.Token);
                complete = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                Program.Trace("finished maps: " + ex.GetBaseException().Message);
            }
            finally
            {
                if (complete) finishedMaps.MarkRead(key);
                finishedMaps.Save();
                finishedReading = null;
                ShowFinishedMaps();
                Program.Trace($"finished maps ({game}): {read} read, complete: {complete}");
            }
            // You moved to the other tick rate while this was reading: that one's list next.
            if (complete && vm.IsNominatePage && FinishedKey() != key) EnsureFinishedMaps();
        }

        /// <summary>A map finish of yours (seen in game, or your time on the dashboard): marked done straight away.</summary>
        void MarkFinished(string map, double time, string game)
        {
            var key = FinishedMaps.Key(CurrentSteamId(), game, KsfStyle);
            if (!finishedMaps.Add(key, map, time)) return;
            finishedMaps.Save();
            if (key == FinishedKey()) ShowFinishedMaps();
        }

        /// <summary>While the full map list is still coming in, ksf.surf's own search fills in what you look for (up to 5 maps).</summary>
        async Task SearchKsfAsync(string text)
        {
            if (catalog.CompleteAt != DateTime.MinValue || text.Length < 2) return;
            var id = ++mapSearchId;
            await Task.Delay(400);
            if (id != mapSearchId) return;
            try
            {
                var found = await api.SearchMapsAsync(text);
                if (found.Count == 0 || id != mapSearchId) return;
                catalog.Add(found);
                vm.SetMapCatalog(catalog.Maps, loading: catalogLoading);
            }
            catch (Exception ex) when (IsNetworkError(ex)) { }
        }

        /// <summary>Pictures for the maps on show (downloaded once, then read from disk).</summary>
        async Task LoadMapThumbsAsync(List<MapResultRow> rows)
        {
            await Task.WhenAll(rows.Where(row => thumbsLoading.Add(row.Map)).Select(async row =>
            {
                try { vm.SetMapThumb(row, await images.MapAsync(row.Map, 240)); }
                finally { thumbsLoading.Remove(row.Map); }
            }));
        }

        // ----- binds page -----

        void LoadBindsPage()
        {
            var appKeys = new Dictionary<string, string> { ["app_save"] = keys.Save, ["app_card"] = keys.Card, ["app_list"] = keys.List };
            vm.Binds.Load(binds, appKeys, turnSpeed, key => config?.OriginalBind(settings, key));
            vm.Binds.SetGameBinds(config?.CurrentBinds());
        }

        /// <summary>One of your own binds taken off on the binds page: the key does nothing now.</summary>
        async void RemoveGameBind(string key)
        {
            if (config == null) return;
            string command;
            try { command = config.RemoveBind(key); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                vm.Toast = "Couldn't change CS:S's config.cfg: " + ex.Message;
                return;
            }
            Program.Trace("binds: took your bind off " + key);
            if (link == LinkState.Ready) await PushAsync(command);
        }

        /// <summary>The binds page's keys with what they do (KSF Companion's own keys are in ksf_companion.cfg instead).</summary>
        List<(BindAction Action, string Key)> BoundActions() => binds.Keys
            .Select(b => (Action: b.Key.StartsWith(BindCatalog.CustomPrefix, StringComparison.Ordinal)
                ? BindCatalog.OwnAction(b.Key.Substring(BindCatalog.CustomPrefix.Length)) : BindCatalog.Find(b.Key), Key: b.Value))
            .Where(b => b.Action != null)
            .ToList();

        HashSet<string> WantedKeys() =>
            new HashSet<string>(binds.Keys.Values.Concat(new[] { keys.Save, keys.Card, keys.List }), StringComparer.OrdinalIgnoreCase);

        /// <summary>A key set, moved or taken off on the binds page: noted now, put in the game on the next tick (a move is two changes).</summary>
        void OnBindChanged(BindChange change)
        {
            if (change.Action.AppSetting != null)
            {
                if (change.NewKey == null) return;
                settings.Set(change.Action.AppSetting, change.NewKey);
                keys = KeyNames.From(settings);
                window?.SetKeys(keys);
            }
            else if (change.NewKey == null) binds.Keys.Remove(change.Action.Id);
            else binds.Keys[change.Action.Id] = change.NewKey;
            settings.Set("binds", binds.ToString());
            bindsToApply = true;
        }

        /// <summary>
        /// Writes the binds into the game's cfg and, while it runs, loads them there. Keys that aren't ours any more
        /// get back what they did before. All of it from the console - nothing is typed in chat.
        /// </summary>
        async void ApplyBinds(bool onlySpeed = false)
        {
            bindsToApply = false;
            if (config == null) return;
            var commands = new List<string>();
            try
            {
                if (!onlySpeed)
                {
                    var wanted = WantedKeys();
                    foreach (var key in boundKeys.Where(k => !wanted.Contains(k)).ToList()) commands.Add(config.GiveBack(settings, key));
                    config.RememberOriginals(settings, wanted);
                    // Rewrites ksf_companion.cfg too (KSF Companion's own keys), which runs ksf_binds.
                    config.Install(settings);
                    boundKeys = wanted;
                }
                config.WriteBinds(BoundActions(), turnSpeed);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                vm.Toast = "Couldn't write the binds into CS:S's cfg folder: " + ex.Message;
                return;
            }
            Program.Trace($"binds: {binds} (turn speed {turnSpeed})" + (commands.Count > 0 ? " - given back: " + string.Join("; ", commands) : ""));
            vm.Binds.RefreshReplaces();
            WriteCard();
            if (link != LinkState.Ready) return;
            foreach (var command in commands) await PushAsync(command);
            if (!onlySpeed) await PushAsync("exec ksf_companion");
            else if (BoundActions().Any(b => b.Action.IsTurn)) await PushAsync("cl_yawspeed " + turnSpeed.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Rock the vote on the KSF server you're on - sent from the console (sm_rtv), not typed in chat.</summary>
        async void RockTheVote()
        {
            if (link != LinkState.Ready || !(onKsfServer || yourServer != null))
            {
                vm.Toast = "Join a KSF server to rock the vote";
                return;
            }
            if (await PushAsync("sm_rtv")) vm.Toast = "Rock the vote sent - the server shows the votes in chat";
        }

        void ToggleSaved(string map)
        {
            if (string.IsNullOrEmpty(map)) return;
            if (later.Contains(map))
            {
                later.Remove(map);
                vm.Toast = "Took " + map + " off your play-later list";
            }
            else
            {
                later.Add(map, catalog.Find(map)?.Tier);
                vm.Toast = "Saved " + map + " for later";
            }
            ListChanged();
        }

        /// <summary>Clicking a row's arrow: teleport to that stage or bonus (KSF's !stage / !bonus), or back to the start for the map.</summary>
        async void Teleport(object parameter)
        {
            if (!(parameter is int zone)) return;
            if (link != LinkState.Ready || !(onKsfServer || yourServer != null) || currentMap == null)
            {
                vm.Toast = "Join a KSF server to teleport";
                return;
            }
            var command = zone == 0 ? "sm_restart"
                : MapReport.IsBonus(zone) ? "sm_bonus " + (zone - MapReport.FirstBonusZone + 1).ToString(CultureInfo.InvariantCulture)
                : "sm_stage " + zone.ToString(CultureInfo.InvariantCulture);
            if (await PushAsync(command))
                vm.Toast = zone == 0 ? "Back to the start" : "Teleporting to " + MapReport.ZoneName(zone);
        }

        /// <summary>
        /// A time you just set in game on the map, a stage or a bonus. It shows right away - as your new best if it
        /// beats the one on record - long before ksf.surf has it (and your new rank).
        /// </summary>
        void RecordLocalBest(int zone, double time, bool announce)
        {
            if (currentMap == null) return;
            // The timer text and chat can both report the same finish.
            var finishKey = $"{currentMap}|{zone}|{time:0.00}";
            if (finishKey == lastLocalFinish && DateTime.Now - lastLocalFinishAt < TimeSpan.FromMinutes(1)) return;
            lastLocalFinish = finishKey;
            lastLocalFinishAt = DateTime.Now;

            var key = CacheKey(Game, currentMap);
            if (!localBests.TryGetValue(key, out var bests)) localBests[key] = bests = new Dictionary<int, double>();
            var onRecord = report != null && report.Game == Game ? report.Zone(zone)?.Time : null;
            var best = bests.TryGetValue(zone, out var mine) && (onRecord == null || mine < onRecord) ? mine : onRecord;
            // The timer shows hundredths, so compare at that precision.
            var improved = best == null || time < Math.Floor(best.Value * 100) / 100 - 0.0001;
            if (improved)
            {
                bests[zone] = time;
                if (report != null && ApplyLocalBests(report)) WriteCard();
            }
            if (!announce) return;

            vm.FlashZones(new[] { zone });
            if (zone == 0) return; // the map itself gets the big PB banner instead
            var name = MapReport.ZoneName(zone);
            vm.Toast = best == null ? $"{name} done  {Format.Short(time)}"
                : improved ? $"{name} new best  {Format.Short(time)}  ({Format.Diff(time - best.Value)})"
                : $"{name}  {Format.Short(time)}   best {Format.Short(best.Value)}  ({Format.Diff(time - best.Value)})";
        }

        /// <summary>Puts the times (and finishes) you set in game into a report from ksf.surf that doesn't have them yet. True if it changed.</summary>
        bool ApplyLocalBests(MapReport r)
        {
            var changed = false;
            var key = CacheKey(r.Game, r.Map);
            if (liveFinishes.TryGetValue(key, out var counted))
            {
                var main = r.Zone(0);
                if (main == null) r.Zones.Add(main = new ZoneRecord { ZoneId = 0 });
                var shown = counted.OnRecord + counted.Since;
                // ksf.surf's count has caught up: from now on it's the one to show.
                if ((main.Completions ?? 0) >= shown) liveFinishes.Remove(key);
                else if (main.Completions != shown)
                {
                    main.Completions = shown;
                    changed = true;
                }
            }
            if (!localBests.TryGetValue(key, out var bests)) return changed;
            foreach (var pair in bests)
            {
                var zone = r.Zone(pair.Key);
                if (zone == null) r.Zones.Add(zone = new ZoneRecord { ZoneId = pair.Key });
                // ksf.surf has it now (its time has more decimals than the timer's hundredths).
                if (zone.Time is double onRecord && onRecord <= pair.Value + 0.005) continue;
                if (zone.Unsynced && zone.Time == pair.Value) continue;
                zone.Time = pair.Value;
                zone.Rank = null;
                zone.Unsynced = true;
                changed = true;
            }
            return changed;
        }

        /// <summary>
        /// Has the game record a demo of this map with its own "record" command, so the timer's on-screen text
        /// (the stage you're on, stage and bonus finishes) can be read live. Once per map, only on KSF, and it
        /// never touches a demo you're recording yourself.
        /// </summary>
        async void EnsureLiveDemo()
        {
            if (hud == null || !settings.GetBool("live_hud") || currentMap == null || hudRequestedFor == currentMap || link != LinkState.Ready) return;
            var map = hudRequestedFor = currentMap;

            var recording = hud.FindRecordingDemo();
            if (recording != null)
            {
                // Already recording (e.g. KSF Companion was restarted): keep reading it if it's this map's. A header
                // that isn't on disk yet means the recording has only just started - also this map's.
                hud.Watch(recording, alreadyRunning: true);
                var of = LiveHud.MapOf(recording);
                if (of == null || string.Equals(of, map, StringComparison.OrdinalIgnoreCase)) return;
                // Still the last map's recording: finish it before starting this map's.
                Program.Trace($"live hud: stop ({of})");
                await PushAsync("stop");
                await Task.Delay(700);
                if (map != currentMap) return;
            }
            // The game won't overwrite a demo (it would pick ksfc_live_2.dem), so clear out old ones first.
            hud.DeleteFinishedDemos();
            Program.Trace("live hud: record " + LiveHud.DemoName);
            await PushAsync("record " + LiveHud.DemoName);
        }

        /// <summary>Our demo of the map you're on, if the game is still writing one: read that. True if there is one.</summary>
        bool FollowOwnDemo()
        {
            var recording = hud?.FindRecordingDemo();
            if (recording == null) return false;
            var of = LiveHud.MapOf(recording);
            if (of != null && !string.Equals(of, currentMap, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(recording, hud.DemoPath, StringComparison.OrdinalIgnoreCase))
            {
                Program.Trace("live hud: following " + Path.GetFileName(recording));
                hud.Watch(recording, alreadyRunning: true);
            }
            return true;
        }

        /// <summary>
        /// Keeps the live timer text coming: if nothing new has come from the demo for a while on a KSF map, find the
        /// one the game is writing (it may have a new name) - or have it record one again if it isn't recording.
        /// </summary>
        void CheckLiveDemo(DateTime now)
        {
            if (hud == null || gamePid == 0 || !onKsfServer || currentMap == null || !settings.GetBool("live_hud") || link != LinkState.Ready) return;
            if (now - hud.LastDataAt < TimeSpan.FromSeconds(40) || now - mapSeenAt < TimeSpan.FromSeconds(40) || now < nextDemoCheck) return;
            if (FollowOwnDemo())
            {
                nextDemoCheck = now.AddSeconds(30);
                return;
            }
            // Asking again when that doesn't start one (your own demo is recording, you're in the menu) waits longer
            // each time, so the console gets at most a "record" every few minutes.
            nextDemoCheck = now + demoRetry;
            demoRetry = TimeSpan.FromSeconds(Math.Min(demoRetry.TotalSeconds * 2, 300));
            Program.Trace("live hud: nothing recording - asking again");
            hudRequestedFor = null;
            EnsureLiveDemo();
        }

        void UpdateSession()
        {
            if (gamePid == 0) return;
            Program.Trace($"session: {sessionMaps} maps, {sessionFinishes} finishes, {sessionPbs} PBs since {sessionStart:HH:mm:ss}");
            vm.SetSession(sessionStart, sessionMaps, sessionFinishes, sessionPbs);
        }

        /// <summary>A session is one run of the game: its clock starts when CS:S does.</summary>
        void StartSession(DateTime start)
        {
            sessionStart = start;
            // Started after the game and it's already on a map: that one counts. A fresh game hasn't joined anything yet.
            sessionMaps = currentMap != null && start < companionStartedAt ? 1 : 0;
            sessionFinishes = sessionPbs = 0;
            UpdateSession();
        }

        /// <summary>The timer's "08:23:89" is minutes:seconds:hundredths (hours come first on very long runs).</summary>
        static bool TryParseTimerTime(string text, out double seconds)
        {
            seconds = 0;
            var parts = text.Split(':', '.');
            if (parts.Length < 2) return false;
            var fraction = parts[parts.Length - 1];
            if (!int.TryParse(fraction, out var hundredths)) return false;
            seconds = hundredths / Math.Pow(10, fraction.Length);
            var unit = 1;
            for (var i = parts.Length - 2; i >= 0; i--, unit *= 60)
            {
                if (!int.TryParse(parts[i], out var value)) return false;
                seconds += value * unit;
            }
            return true;
        }

        /// <summary>Pulls fresh numbers for the current map and your recent records, ignoring the cache.</summary>
        void ReloadFromKsf()
        {
            if (currentMap != null)
            {
                cache.Remove(CacheKey(Game, currentMap));
                _ = FetchAsync(currentMap);
            }
            nextRecentPoll = DateTime.MinValue;
        }

        static string CacheKey(string game, string map) => game + "|" + map;

        /// <summary>The game just (re)started and is sitting in the main menu.</summary>
        void OnGameStarted()
        {
            currentMap = null;
            report = null;
            inGameAt = null;
            announcePending = cardEchoPending = false;
            yourServer = null;
            onKsfServer = false;
            connectedAddress = nextMapName = null;
            currentZone = null;
            vm.SetCurrentZone(null);
            zoneFetch?.Cancel();
            vm.ShowNoMap();
            vm.SetNextMap(null, null);
            vm.SetLiveServer(null, null, null);
            clock.Reset();
            timeLimitCheckAt = null;
            vm.AmbientImage = null;
            ListChanged();
            UpdateStatus();
        }

        /// <summary>
        /// Your title, rank and points on 66 and 100 tick, from your ksf.surf profile (read every quarter of an hour
        /// while the dashboard is up; the server list keeps the tick rate you're playing current in between).
        /// </summary>
        async Task RefreshStandingsAsync()
        {
            var steamId = CurrentSteamId();
            if (steamId == null || levelLoading || DateTime.Now < api.BusyUntil) return;
            levelLoading = true;
            nextStandingsCheck = DateTime.Now.AddMinutes(15);
            try
            {
                foreach (var game in new[] { Tick66, Tick100 })
                {
                    try
                    {
                        var standing = await api.GetStandingAsync(steamId, game);
                        if (standing != null) standings[game] = standing;
                    }
                    catch (Exception ex) when (IsNetworkError(ex))
                    {
                        Program.Trace($"standing ({game}): {ex.GetBaseException().Message}");
                    }
                }
                Program.Trace("standings: " + string.Join(", ", standings.Values.Select(s => $"{s.Tick} {s.Title} #{s.Rank} {s.Points} pts")));
            }
            finally
            {
                levelLoading = false;
            }
            await ShowLevelsAsync();
        }

        /// <summary>The level card: each tick rate's title and what the next one takes. Rank titles take passing whoever holds the last spot.</summary>
        async Task ShowLevelsAsync()
        {
            var rows = new List<(PlayerStanding, int?)>();
            foreach (var standing in standings.Values.OrderBy(s => s.Game).ToList())
            {
                int? needed = null;
                var index = KsfLevels.IndexOf(standing);
                if (index > 0 && KsfLevels.Ladder[index - 1].TopRank is int top)
                {
                    var key = $"{standing.Game}|{KsfStyle}|{top}";
                    if (pointsAtRank.TryGetValue(key, out var known) && DateTime.Now - known.At < TimeSpan.FromMinutes(15)) needed = known.Points;
                    else if (DateTime.Now >= api.BusyUntil)
                    {
                        try
                        {
                            needed = await api.GetPointsAtRankAsync(top, standing.Game, KsfStyle);
                            pointsAtRank[key] = (DateTime.Now, needed);
                        }
                        catch (Exception ex) when (IsNetworkError(ex)) { }
                    }
                }
                rows.Add((standing, needed));
            }
            if (rows.Count > 0) vm.SetLevels(rows, Game);
        }

        /// <summary>
        /// The server list has your rank and points on the tick rate you're playing - but as the game server had them,
        /// which can be from when you joined. Only used while your profile hasn't been read lately.
        /// </summary>
        void UpdateStanding(string game, int? rank, int? points)
        {
            if (!(points is int p)) return;
            if (!standings.TryGetValue(game, out var standing))
                standings[game] = standing = new PlayerStanding { Game = game, Tick = game == Tick100 ? "100T" : "66T" };
            if (standing.FromProfile && DateTime.Now - standing.At < TimeSpan.FromMinutes(20)) return;
            if (standing.Rank == rank && standing.Points == p) return;
            standing.Rank = rank;
            standing.Points = p;
            _ = ShowLevelsAsync();
        }

        void TraceClock(string why)
        {
            var left = clock.LeftAt(DateTime.Now);
            Program.Trace($"time left ({why}): " + (clock.Extending ? "extended, reading mp_timelimit"
                : left is double seconds ? DashboardViewModel.Countdown(seconds) : "unknown")
                + (clock.LimitMinutes is double limit ? $", limit {limit.ToString(CultureInfo.InvariantCulture)} min" : ""));
        }

        /// <summary>Reads mp_timelimit in the console in a moment (at most every few seconds, and only while on a map).</summary>
        void CheckTimeLimitSoon(double seconds)
        {
            var at = DateTime.Now.AddSeconds(seconds);
            if (timeLimitCheckAt == null || at < timeLimitCheckAt) timeLimitCheckAt = at;
        }

        async void OnMapChanged(string map, bool justJoined)
        {
            currentMap = map;
            mapSeenAt = DateTime.Now;
            demoRetry = TimeSpan.FromSeconds(30);
            failedFetches = 0;
            inGameAt =justJoined ? (DateTime?)null : DateTime.Now;
            announcePending = justJoined && settings.GetBool("run_server_commands");
            cardEchoPending = justJoined;
            nextAnnounceTry = DateTime.MinValue;
            manualGame = null;
            refreshAt.Clear();
            report = null;
            nextMapName = null;
            pointsJustEarned = null;
            currentZone = null;
            lastLiveZoneAt = DateTime.MinValue;
            vm.SetCurrentZone(null);
            zoneFetch?.Cancel();
            pinnedLeaderZone = null;
            followedLeaderZone = 0;
            if (justJoined) sessionMaps++;
            UpdateSession();
            vm.ShowLoading(map, live: gamePid != 0 || justJoined);
            vm.SetNextMap(null, null);
            clock.Reset();
            // Joining: read it once in the game (see InGame); already on the map when the app started: now.
            CheckTimeLimitSoon(justJoined ? 30 : 1);
            if (yourServer != null) vm.SetLiveServer(yourServer, CurrentSteamId(), map);
            vm.SetServerLine(yourServer != null && yourServer.Map == map ? yourServer : null);
            ListChanged();
            UpdateStatus();
            // The server list lags a little behind map changes; look again shortly.
            nextServerPoll = DateTime.Now.AddSeconds(justJoined ? 12 : 0);
            try
            {
                var image = LoadHeroImageAsync(map);
                var ambient = LoadAmbientAsync(map);
                await FetchAsync(map);
                await image;
                await ambient;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        async Task LoadHeroImageAsync(string map)
        {
            var image = await images.MapAsync(map, 1600, urgent: true);
            if (map == currentMap) vm.MapImage = image;
        }

        /// <summary>A tiny blurred copy of the map picture; stretched behind the whole dashboard it tints it with the map's colours.</summary>
        async Task LoadAmbientAsync(string map)
        {
            var image = await images.AmbientAsync(map);
            if (map == currentMap) vm.AmbientImage = image;
        }

        async Task FetchAsync(string map)
        {
            var id = ++fetchId;
            var game = Game;
            var key = CacheKey(game, map);
            fetching = true;
            vm.Is100t = game == Tick100;
            MapReport fresh;
            try
            {
                if (cache.TryGetValue(key, out var cached) && DateTime.Now - cached.FetchedAt < TimeSpan.FromMinutes(2))
                {
                    fresh = cached;
                }
                else
                {
                    fresh = await api.GetReportAsync(map, CurrentSteamId(), game, KsfStyle);
                    // Kept only when complete: a missing part is asked for again soon.
                    if (fresh.Error == null && fresh.PersonalError == null) cache[key] = fresh;
                }
            }
            finally
            {
                if (id == fetchId) fetching = false;
            }

            if (id != fetchId || map != currentMap) return;
            if (game != Game)
            {
                // Found out we're on the other tick rate while this was loading.
                _ = FetchAsync(map);
                return;
            }
            var before = report;
            report = fresh;
            // Times you've set in game that ksf.surf doesn't have yet stay on show.
            ApplyLocalBests(fresh);
            if (fresh.Main?.Time is double best) MarkFinished(fresh.Info?.Name ?? map, best, game);
            // The nominate page follows the tick rate you're on.
            if (FinishedKey() != finishedShown)
            {
                ShowFinishedMaps();
                if (vm.IsNominatePage) EnsureFinishedMaps();
            }
            var zones = fresh.RecordZones;
            Program.Trace($"loaded {map} ({game}): pb={(fresh.Main?.Time is double pb ? Format.Time(pb) : "none")} rank={fresh.Main?.Rank}/{fresh.Main?.TotalRanks} error={fresh.Error ?? "-"}" +
                (zones.Count > 0 ? $" zones done={zones.Count(z => fresh.Zone(z)?.Time != null)}/{zones.Count} records={fresh.ZoneWrs.Count}" : ""));
            if (report.PlayerName != null)
            {
                if (settings.Get("last_name") != report.PlayerName) settings.Set("last_name", report.PlayerName);
                if (report.PlayerCountry != null && settings.Get("last_country") != report.PlayerCountry) settings.Set("last_country", report.PlayerCountry);
                vm.SetPlayer(report.PlayerName, report.PlayerCountry, lastRank, lastPoints, settings.Get("last_rank_tick"));
            }
            WriteCard();
            ShowNewZoneBests(before, fresh);
            LoadZoneRecords(fresh);
            UpdateLeaderboard();
            UpdateStatus();
        }

        /// <summary>
        /// The record on each stage and bonus, for "your times": whatever wasn't saved from an earlier visit comes in
        /// one at a time after everything else (ksf.surf limits how fast it can be asked).
        /// </summary>
        async void LoadZoneRecords(MapReport r)
        {
            var key = r.Game + "|" + r.Map;
            // The zone you're on and the ones you've done come first: they get a gap and a bar, the rest only the record.
            var zones = r.RecordZones.Where(z => !r.ZoneWrs.ContainsKey(z))
                .OrderBy(z => z == currentZone ? 0 : r.Zone(z)?.Time != null ? 1 : 2).ThenBy(z => z).ToList();
            if (zones.Count == 0 || key == zoneFetchKey) return;
            zoneFetch?.Cancel();
            var cancel = new CancellationTokenSource();
            zoneFetch = cancel;
            zoneFetchKey = key;
            try
            {
                // A timeout or a hiccup shouldn't leave gaps until the next refresh: try again a little later.
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await api.FetchZoneWrsAsync(r.Info.Name, zones, r.Game, KsfStyle, (zone, wr) =>
                        {
                            if (wr == null || report == null || report.Game + "|" + report.Map != key) return;
                            report.ZoneWrs[zone] = wr;
                            vm.ShowTimes(report);
                        }, cancel.Token);
                        break;
                    }
                    catch (Exception ex) when (!cancel.IsCancellationRequested && IsNetworkError(ex) && attempt < 4)
                    {
                        Program.Trace($"zone records for {r.Map} (try {attempt}): {ex.GetBaseException().Message}");
                        await Task.Delay(TimeSpan.FromSeconds(15 * attempt), cancel.Token);
                    }
                }
                Program.Trace($"zone records for {r.Map}: {report?.ZoneWrs.Count}/{r.RecordZones.Count}");
                // The in-game card shows the gaps too.
                if (report != null && report.Game + "|" + report.Map == key) WriteCard();
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            catch (Exception ex) when (IsNetworkError(ex))
            {
                Program.Trace("zone records: " + ex.GetBaseException().Message);
            }
            finally
            {
                if (zoneFetch == cancel)
                {
                    zoneFetch = null;
                    zoneFetchKey = null;
                }
                cancel.Dispose();
            }
        }

        /// <summary>
        /// Compares your stage and bonus times with the previous load of the same map and points out the ones you
        /// improved (for times the live timer text didn't already show).
        /// </summary>
        void ShowNewZoneBests(MapReport before, MapReport after)
        {
            if (before == null || !after.IsOnKsf || before.Map != after.Map || before.Game != after.Game || before.PersonalError != null || after.PersonalError != null
                || before.Zones.Count == 0) return;
            var better = new List<(int Zone, double Time, double? Old)>();
            foreach (var zone in after.RecordZones)
            {
                if (!(after.Zone(zone)?.Time is double time)) continue;
                var old = before.Zone(zone)?.Time;
                if (old == null || time < old.Value - 0.0005) better.Add((zone, time, old));
            }
            if (better.Count == 0) return;

            Program.Trace("new bests: " + string.Join(", ", better.Select(b => $"{MapReport.ZoneLabel(b.Zone)} {Format.Short(b.Time)}")));
            vm.FlashZones(better.Select(b => b.Zone).ToList());
            var one = better[0];
            vm.Toast = better.Count > 1 ? "New bests on " + string.Join(", ", better.Select(b => MapReport.ZoneLabel(b.Zone)))
                : one.Old is double was ? $"{MapReport.ZoneName(one.Zone)} new best  {Format.Short(one.Time)}  ({Format.Diff(one.Time - was)})"
                : $"{MapReport.ZoneName(one.Zone)} done  {Format.Short(one.Time)}";
        }

        /// <summary>
        /// Once the player is in the game: print the card into the console and, on a KSF server, run the
        /// server_commands so KSF itself shows the map info and the player's rank in chat.
        /// </summary>
        async Task AnnounceAsync()
        {
            announcing = true;
            try
            {
                var map = currentMap;
                var announce = announcePending;
                var echoCard = cardEchoPending && report != null;
                var commands = new List<string>();
                if (echoCard) commands.Add("exec ksf_card");
                // "status" only prints to your console: the server (66/100 tick) and its address for the live panel.
                if (echoCard || announce) commands.Add("status");
                if (commands.Count == 0) return;

                var hostname = new TaskCompletionSource<string>();
                if (announce) hostnameSeen = hostname;

                if (!await PushAsync(string.Join("; ", commands)))
                {
                    hostnameSeen = null;
                    nextAnnounceTry = DateTime.Now.AddSeconds(2);
                    return;
                }
                // A newer map may have started meanwhile; it gets its own announcement.
                if (map != currentMap) return;
                if (echoCard) cardEchoPending = false;
                if (!announce) return;

                announcePending = false;
                var winner = await Task.WhenAny(hostname.Task, Task.Delay(3000));
                hostnameSeen = null;
                var onKsf = winner == hostname.Task
                    ? hostname.Task.Result.IndexOf("ksf", StringComparison.OrdinalIgnoreCase) >= 0
                    : DateTime.Now - lastKsfChatAt < TimeSpan.FromMinutes(15);
                // One at a time: KSF answers a second command within a second with "You must wait 1.0 seconds".
                var serverCommands = GameConfig.ServerCommands(settings).Split(';').Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
                for (var i = 0; i < serverCommands.Count && onKsf && map == currentMap; i++)
                {
                    if (i > 0) await Task.Delay(1300);
                    await PushAsync(serverCommands[i]);
                }
            }
            finally
            {
                announcing = false;
            }
        }

        /// <summary>Keeps your times current while you grind the map.</summary>
        void RefreshIfStale(DateTime now)
        {
            if (gamePid == 0 || currentMap == null || report == null || fetching) return;
            var age = now - report.FetchedAt;
            // Your times or the map's records didn't come (ksf.surf timed out or was busy): ask again in seconds, not
            // minutes - a little later each time it keeps failing.
            var failed = report.Error != null || report.PersonalError != null;
            if (!failed) failedFetches = 0;
            var retry = TimeSpan.FromSeconds(Math.Min(180, 8 << Math.Min(failedFetches, 5)));
            if (age > TimeSpan.FromMinutes(3) || (failed && age > retry))
            {
                if (failed)
                {
                    failedFetches++;
                    Program.Trace($"asking ksf.surf again for {currentMap} (try {failedFetches + 1}): {report.Error ?? report.PersonalError}");
                }
                _ = FetchAsync(currentMap);
            }
        }

        void RefreshEverything()
        {
            ReloadFromKsf();
            nextServerPoll = DateTime.MinValue;
            vm.Toast = "Refreshing from ksf.surf...";
        }

        async Task<List<KsfServer>> ServersOrEmpty(string game)
        {
            try { return await api.GetServersAsync(game); }
            catch (Exception ex) when (IsNetworkError(ex)) { return new List<KsfServer>(); }
        }

        async Task<List<RecentRecord>> RecentOrEmpty(string steamId, string game)
        {
            try { return await api.GetRecentAsync(steamId, game, KsfStyle); }
            catch (Exception ex) when (IsNetworkError(ex)) { return new List<RecentRecord>(); }
        }

        async Task PollServersAsync()
        {
            pollingServers = true;
            try
            {
                // 66 and 100 tick servers together, so you see every KSF server and where you are.
                var lists = await Task.WhenAll(ServersOrEmpty(Tick66), ServersOrEmpty(Tick100));
                var list = lists[0].Concat(lists[1]).ToList();
                if (list.Count == 0) return;
                servers = list;
                var steamId = CurrentSteamId();
                KsfServerPlayer you = null;
                KsfServer listedOn = null;
                if (gamePid != 0 && steamId != null)
                {
                    foreach (var server in list)
                    {
                        you = server.Players.FirstOrDefault(p => string.Equals(p.SteamId, steamId, StringComparison.OrdinalIgnoreCase));
                        if (you == null) continue;
                        listedOn = server;
                        break;
                    }
                }
                // The address from "status" is exact; ksf.surf's player lists can lag a minute behind a server switch.
                yourServer = gamePid == 0 ? null
                    : connectedAddress != null ? list.FirstOrDefault(s => s.Address == connectedAddress)
                    : listedOn;
                var rankTick = listedOn?.Game == Tick100 ? "100T" : "66T";
                if (you != null && (you.Rank != lastRank || you.Points != lastPoints || settings.Get("last_rank_tick") != rankTick))
                {
                    lastRank = you.Rank;
                    lastPoints = you.Points;
                    settings.Set("last_rank", lastRank?.ToString(CultureInfo.InvariantCulture) ?? "");
                    settings.Set("last_points", lastPoints?.ToString(CultureInfo.InvariantCulture) ?? "");
                    settings.Set("last_rank_tick", rankTick);
                    vm.SetPlayer(report?.PlayerName ?? settings.Get("last_name"), report?.PlayerCountry ?? settings.Get("last_country"), lastRank, lastPoints, rankTick);
                }
                if (you != null && listedOn != null) UpdateStanding(listedOn.Game, you.Rank, you.Points);
                // Only trust it for the tick rate when it's clearly up to date (the hostname from "status" usually settles this first).
                if (yourServer != null && (connectedAddress != null || string.Equals(yourServer.Map, currentMap, StringComparison.OrdinalIgnoreCase)))
                    SetDetectedGame(yourServer.Game);
                vm.SetServers(list, yourServer?.Address, SavedMaps());
                vm.SetServerLine(yourServer != null && string.Equals(yourServer.Map, currentMap, StringComparison.OrdinalIgnoreCase) ? yourServer : null);
                // ksf.surf's sample says when the map started (even from before an extension) and its time limit then.
                if (yourServer != null && string.Equals(yourServer.Map, currentMap, StringComparison.OrdinalIgnoreCase))
                    clock.FromKsf(yourServer.TimeLimitMinutes, yourServer.TimeLeftSeconds, yourServer.FetchedAt);
                vm.SetLiveServer(yourServer, steamId, currentMap);
                // Your stage or bonus, when KSF's list is up to date with the map you're on (1-30 stages, 31+ bonuses;
                // 0 is the start zone, -1 spectating). Only used while the live timer text isn't coming in.
                listedAsSpectating = yourServer != null && listedOn == yourServer && you.Zone == -1;
                if (yourServer != null && listedOn == yourServer && string.Equals(yourServer.Map, currentMap, StringComparison.OrdinalIgnoreCase))
                    SetCurrentZone(you.Zone >= 1 ? you.Zone : null, ZoneSource.ServerList);
                RefreshLaterView();
            }
            finally
            {
                pollingServers = false;
                // Faster while you're on a KSF server, so the players and their stages stay current.
                nextServerPoll = DateTime.Now.AddSeconds(yourServer != null ? 15 : 30);
            }
        }

        async Task PollRecentAsync()
        {
            pollingRecent = true;
            try
            {
                var steamId = CurrentSteamId();
                if (steamId == null) return;
                if (avatarFor != steamId)
                {
                    avatarFor = steamId;
                    var url = await api.GetAvatarUrlAsync(steamId);
                    if (url != null) vm.Avatar = await images.AvatarAsync(url);
                }
                var both = await Task.WhenAll(RecentOrEmpty(steamId, Tick66), RecentOrEmpty(steamId, Tick100));
                vm.SetRecent(both[0].Concat(both[1]).OrderByDescending(r => r.Date ?? DateTime.MinValue));
            }
            catch (Exception ex) when (IsNetworkError(ex)) { }
            finally
            {
                pollingRecent = false;
                nextRecentPoll = DateTime.Now.AddMinutes(3);
            }
        }

        static bool IsNetworkError(Exception ex) =>
            ex is HttpRequestException || ex is TaskCanceledException || ex is ArgumentException || ex is InvalidOperationException;

        string CurrentSteamId()
        {
            var id = SteamLocator.FindSteamId(settings.Get("steamid"));
            if (id != null)
            {
                if (settings.Get("last_steamid") != id) settings.Set("last_steamid", id);
                return id;
            }
            var last = settings.Get("last_steamid");
            return last.Length > 0 ? last : null;
        }

        void SaveCurrentMap()
        {
            string message;
            if (currentMap == null)
                message = "no map detected yet - join a map first";
            else if (later.Contains(currentMap))
                message = $"{currentMap} is already in your play-later list ({later.Items.Count} maps)";
            else
            {
                later.Add(currentMap, report?.Info?.Tier);
                var count = later.Items.Count;
                message = $"saved {currentMap} for later - {count} map{(count == 1 ? "" : "s")} in your list";
                if (report?.Info == null) _ = FillInMissingTiersAsync();
            }
            ListChanged();
            vm.Toast = char.ToUpperInvariant(message[0]) + message.Substring(1);

            try { config?.WriteMessage(new[] { message }); }
            catch (IOException) { }
            _ = PushAsync("exec ksf_msg");
        }

        /// <summary>
        /// On a KSF server with the game connected, nominates straight away (the server answers in chat).
        /// Otherwise copies "!nominate map" to paste in chat.
        /// </summary>
        async void Nominate(string map)
        {
            if (string.IsNullOrEmpty(map)) return;
            if (link == LinkState.Ready && (yourServer != null || onKsfServer) && await PushAsync("sm_nominate " + map))
            {
                vm.Toast = $"Nominated {map} - the server replies in chat";
                return;
            }
            try
            {
                Clipboard.SetText("!nominate " + map);
                vm.Toast = $"Copied  !nominate {map}  - paste it in KSF chat";
            }
            catch (COMException)
            {
                vm.Toast = "Couldn't reach the clipboard - try again";
            }
        }

        void Join(string address)
        {
            if (string.IsNullOrEmpty(address)) return;
            Process.Start("steam://connect/" + address);
            vm.Toast = "Joining " + address + "...";
        }

        void ListChanged()
        {
            var items = later.Items;
            try { config?.WriteList(CardBuilder.BuildList(items, currentMap, keys)); }
            catch (IOException) { }
            RefreshLaterView();
            if (servers.Count > 0) vm.SetServers(servers, yourServer?.Address, SavedMaps());
            vm.SetMapsContext(SavedMaps(), currentMap);
            WriteCard();
        }

        void RefreshLaterView()
        {
            var needThumbs = vm.SetPlayLater(later.Items, currentMap, servers);
            _ = LoadThumbsAsync(needThumbs);
        }

        async Task LoadThumbsAsync(List<LaterRow> rows)
        {
            foreach (var row in rows)
            {
                var thumb = await images.MapAsync(row.Map, 240);
                if (thumb != null) row.Thumb = thumb;
            }
        }

        HashSet<string> SavedMaps() => new HashSet<string>(later.Items.Select(e => e.Map), StringComparer.OrdinalIgnoreCase);

        void WriteCard()
        {
            if (report == null) return;
            var saved = later.Contains(report.Map);
            try { config?.WriteCard(CardBuilder.Build(report, saved, keys)); }
            catch (IOException) { }
            vm.ShowReport(report, saved);
        }

        /// <summary>
        /// The save key keeps working while KSF Companion is closed, because the game still logs the press.
        /// Read what was logged since we last looked and save those maps now.
        /// </summary>
        long CatchUpOnMissedSaves(string path, out string lastMap)
        {
            lastMap = null;
            long length;
            try { length = File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch (IOException) { return 0; }

            if (!long.TryParse(settings.Get("log_offset"), NumberStyles.None, CultureInfo.InvariantCulture, out var offset)) return length;
            if (offset > length) offset = 0;
            offset = Math.Max(offset, length - MaxCatchUpBytes);
            if (offset >= length) return length;

            string text;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(offset, SeekOrigin.Begin);
                var buffer = new byte[length - offset];
                var total = 0;
                while (total < buffer.Length)
                {
                    var n = stream.Read(buffer, total, buffer.Length - total);
                    if (n <= 0) break;
                    total += n;
                }
                // Stop at the last whole line; the watcher picks up from there.
                var end = total > 0 ? Array.LastIndexOf(buffer, (byte)'\n', total - 1) : -1;
                if (end < 0) return offset;
                text = Encoding.UTF8.GetString(buffer, 0, end + 1);
                length = offset + end + 1;
            }
            catch (IOException) { return length; }

            var map = settings.Get("log_map");
            if (map.Length == 0) map = null;
            var added = new List<string>();
            var catchUp = new LogParser();
            catchUp.MapChanged += m => map = m;
            catchUp.GameStarted += () => map = null;
            catchUp.SaveRequested += () => { if (map != null && later.Add(map, null)) added.Add(map); };
            foreach (var line in text.Split('\n')) catchUp.Feed(line);

            lastMap = map;
            if (added.Count > 0)
                tray.ShowBalloonTip(8000, Program.AppName, $"Saved {string.Join(", ", added)} - you pressed {keys.Save} while KSF Companion was closed.", Forms.ToolTipIcon.Info);
            return length;
        }

        void SaveCheckpoint()
        {
            if (config == null || watcher == null || !watcher.TryGetCheckpoint(config.LogCandidates.First(), out var position)) return;
            var offset = position.ToString(CultureInfo.InvariantCulture);
            if (settings.Get("log_offset") != offset) settings.Set("log_offset", offset);
            if (settings.Get("log_map") != (currentMap ?? "")) settings.Set("log_map", currentMap ?? "");
        }

        async Task FillInMissingTiersAsync()
        {
            var changed = false;
            foreach (var entry in later.Items.Where(e => e.Tier == null).Take(25).ToList())
            {
                var tier = await api.GetTierAsync(entry.Map);
                if (tier == null) continue;
                later.SetTier(entry.Map, tier.Value);
                changed = true;
            }
            if (changed) ListChanged();
        }

        async Task<bool> PushAsync(string command)
        {
            if (link != LinkState.Ready) return false;
            var result = await SendAsync(command, linkFormat);
            return result == SendResult.Accepted || result == SendResult.Declined;
        }

        async Task<SendResult> SendAsync(string command, LinkFormat format)
        {
            var hwnd = gameWindow;
            if (hwnd == IntPtr.Zero) return SendResult.NoWindow;
            var text = GameBridge.Wrap(command, format);
            await sendLock.WaitAsync();
            try
            {
                return await Task.Run(() => GameBridge.Send(hwnd, text));
            }
            finally
            {
                sendLock.Release();
            }
        }

        void UpdateStatus()
        {
            string text;
            var connection = Connection.Waiting;
            if (config == null)
            {
                text = "Couldn't find Counter-Strike: Source - set game_dir in settings.ini";
                connection = Connection.Limited;
            }
            else if (setupError != null)
            {
                text = "Couldn't write to the CS:S cfg folder: " + setupError;
                connection = Connection.Limited;
            }
            else if (gamePid == 0)
                text = "Waiting for CS:S";
            else if (link == LinkState.Ready)
            {
                text = "Connected to CS:S";
                connection = Connection.Connected;
            }
            else if (!LogIsActive())
            {
                text = "CS:S is running - restart it once to finish setup";
                connection = Connection.Limited;
            }
            else if (link == LinkState.Unavailable)
            {
                text = $"Following your maps (hold {keys.Card} in-game for the card)";
                connection = Connection.Limited;
            }
            else
                text = "Connecting to CS:S...";

            vm.SetStatus(text, connection);
            // Only the states you can do something about get a notice under the title bar.
            vm.Notice = config == null ? @"Couldn't find Counter-Strike: Source. Put your ...\Counter-Strike Source\cstrike folder in settings.ini (game_dir), then restart KSF Companion."
                : setupError != null ? "Couldn't write to the CS:S cfg folder: " + setupError
                : connection == Connection.Limited && gamePid != 0 && !LogIsActive() ? "CS:S is running but KSF Companion can't follow it yet. Restart the game once to finish setup."
                : link == LinkState.Unavailable ? $"The game isn't taking commands from KSF Companion, so map info won't be posted in chat. The dashboard and {keys.Save} / {keys.Card} / {keys.List} still work."
                : null;
            statusItem.Text = text.Length > 90 ? text.Substring(0, 87) + "..." : text;
            var tip = Program.AppName + (currentMap != null ? " - " + currentMap : "");
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        bool LogIsActive()
        {
            if (watcher != null && watcher.LastLineAt >= gameSeenAt) return true;
            foreach (var path in config.LogCandidates)
            {
                try
                {
                    if (File.Exists(path) && File.GetLastWriteTime(path) >= gameStartedAt) return true;
                }
                catch (IOException) { }
            }
            return false;
        }

        static DateTime? ProcessStartTime(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return process.StartTime;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }

        static bool TryDelete(string path)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        void EnsureWindow()
        {
            if (window != null) return;
            window = new DashboardWindow(vm, keys);
            window.ApplyPlacement(settings.Get("window"));
            window.SetTopmost(settings.GetBool("window_topmost"));
            window.TopmostChanged += on =>
            {
                settings.Set("window_topmost", on ? "1" : "0");
                if (keepOnTopItem.Checked != on) keepOnTopItem.Checked = on;
            };
            window.PlacementChanged += () => placementDirty = true;
            window.IsVisibleChanged += (s, e) =>
            {
                if (!window.IsVisible) return;
                nextServerPoll = nextRecentPoll = DateTime.MinValue;
            };
        }

        void ShowDashboard(bool activate)
        {
            EnsureWindow();
            if (!activate)
            {
                // Next to a running game: never take focus.
                if (!window.IsVisible) window.ShowWithoutFocus();
                return;
            }
            window.ShowActivated = true;
            if (!window.IsVisible) window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        void SavePlacement()
        {
            placementDirty = false;
            var placement = window?.Placement;
            if (placement != null && settings.Get("window") != placement) settings.Set("window", placement);
        }

        void OpenMapPage(string map)
        {
            if (!string.IsNullOrEmpty(map)) Process.Start(KsfApi.MapPage(map));
        }

        void OpenDataFolder() => Process.Start("explorer.exe", "\"" + Program.DataDir + "\"");

        void Uninstall()
        {
            if (config == null) return;
            if (GameBridge.FindGameProcessId() != 0)
            {
                MessageBox.Show("Close Counter-Strike: Source first, then choose Remove again.", Program.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var question =
                "Remove KSF Companion from Counter-Strike: Source?\n\n" +
                $"This deletes its ksf_*.cfg files and its block in autoexec.cfg, and puts your old {keys.Save} / {keys.Card} / {keys.List} binds back.\n\n" +
                "Your play-later list stays in Documents\\KSF Companion.";
            if (MessageBox.Show(question, Program.AppName, MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

            try
            {
                config.Uninstall(settings);
                Startup.Set(false);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show("Couldn't remove everything: " + ex.Message, Program.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            timer.Stop();
            config = null;
            MessageBox.Show("Removed from CS:S. KSF Companion will close now.", Program.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            ExitApp();
        }

        void ExitApp()
        {
            shutdown.Cancel();
            timer.Stop();
            showWait.Unregister(null);
            SavePlacement();
            if (config != null)
            {
                SaveCheckpoint();
                // Without the companion the card would go stale, so say so instead of showing an old map.
                try { config.WriteCard(new[] { "KSF Companion isn't running - start it to see KSF info for your map" }); }
                catch (IOException) { }
            }
            tray.Visible = false;
            tray.Dispose();
            if (window != null)
            {
                window.AllowClose = true;
                window.Close();
            }
            api.Dispose();
            Application.Current.Shutdown();
        }
    }
}
