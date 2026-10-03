using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text.Json;

namespace KsfCompanion
{
    sealed class MapInfo
    {
        public string Name;
        public int Tier;
        public bool IsLinear;
        public int StageCount;
        public int BonusCount;
        public string Mappers;
        public double? Rating;
        public int RatingCount;
        public DateTime? Added;
        /// <summary>ksf.surf's popularity score (how much it's played lately).</summary>
        public double Popularity;
    }

    /// <summary>One row of a map leaderboard; rank 1 is the world record.</summary>
    sealed class WorldRecord
    {
        public int Rank;
        public string Name;
        public string SteamId;
        public string Country;
        public double Time;
        public DateTime? Date;
    }

    sealed class KsfServer
    {
        public string Game;
        public string Name;
        public string Address;
        public string Map;
        public int Tier;
        public bool IsLinear;
        public int StageCount;
        public int BonusCount;
        public int PlayerCount;
        public int TimeLeftSeconds;
        public int TimeLimitMinutes;
        public DateTime FetchedAt = DateTime.Now;
        public List<KsfServerPlayer> Players = new List<KsfServerPlayer>();
    }

    sealed class KsfServerPlayer
    {
        public string SteamId;
        public string Name;
        public int? Rank;
        public int? Points;
        /// <summary>0 = start zone, 1.. = stage or checkpoint, 31.. = bonus.</summary>
        public int? Zone;
        public int? ConnectedSeconds;
    }

    sealed class RecentRecord
    {
        public string Game;
        public string Map;
        public int ZoneId;
        public double? Time;
        public string Type;
        public string Server;
        public int? Rank;
        public DateTime? Date;
    }

    /// <summary>Where a player stands on one tick rate (KSF ranks 66 and 100 tick separately), from their ksf.surf profile.</summary>
    sealed class PlayerStanding
    {
        /// <summary>css or css100t</summary>
        public string Game;
        /// <summary>"66T" or "100T"</summary>
        public string Tick;
        /// <summary>KSF's title for them ("PROFICIENT"), when the profile shows one.</summary>
        public string Title;
        public int? Rank;
        public int? CountryRank;
        public string Country;
        public int Points;
        public int? MapsDone;
        public DateTime At = DateTime.Now;
        /// <summary>Read off the profile page (the server list's rank and points can be from when you joined).</summary>
        public bool FromProfile;
    }

    /// <summary>A map you've finished: your best time there, and your group on it (1-6; null when you're not in one).</summary>
    sealed class FinishedMap
    {
        public string Map;
        public double Time;
        public int? Group;
        public int? Points;
    }

    /// <summary>The player's result in one zone: 0 is the map itself, 1-30 are stages (staged maps), 31 and up are bonuses.</summary>
    sealed class ZoneRecord
    {
        public int ZoneId;
        public double? Time;
        public int? Rank;
        public int? TotalRanks;
        public int? Completions;
        public int? Attempts;
        public double? PlaytimeSeconds;
        public int? Group;
        /// <summary>A time you just set in game that ksf.surf doesn't have yet (so no rank for it yet).</summary>
        public bool Unsynced;
    }

    sealed class MapReport
    {
        public const int FirstBonusZone = 31;

        /// <summary>css (66 tick) or css100t: KSF keeps separate records for each.</summary>
        public string Game;
        public string Map;
        public string SteamId;
        public string PlayerName;
        public string PlayerCountry;
        public MapInfo Info;
        public WorldRecord Wr;
        public List<WorldRecord> Top = new List<WorldRecord>();
        public List<ZoneRecord> Zones = new List<ZoneRecord>();
        /// <summary>The record on each stage and bonus, by zone (1-30 stages, 31 and up bonuses).</summary>
        public Dictionary<int, WorldRecord> ZoneWrs = new Dictionary<int, WorldRecord>();
        public List<string> Suggestions = new List<string>();
        public string Error;
        public string PersonalError;
        public DateTime FetchedAt;

        public ZoneRecord Main => Zones.FirstOrDefault(z => z.ZoneId == 0);
        public int BonusesDone => Zones.Count(z => z.ZoneId >= FirstBonusZone && z.Time != null);
        public bool IsOnKsf => Info != null;
        /// <summary>A map split into stages that are timed on their own (linear maps only have checkpoints).</summary>
        public bool IsStaged => Info != null && IsStagedMap(Info);
        public ZoneRecord Zone(int zone) => Zones.FirstOrDefault(z => z.ZoneId == zone);

        /// <summary>The stages (staged maps) and bonuses, each of which has its own leaderboard.</summary>
        public List<int> RecordZones => ZonesOf(Info);

        /// <summary>A map's stages (staged maps) and bonuses.</summary>
        public static List<int> ZonesOf(MapInfo info) => info == null ? new List<int>()
            : (IsStagedMap(info) ? Enumerable.Range(1, info.StageCount) : Enumerable.Empty<int>())
                .Concat(Enumerable.Range(FirstBonusZone, Math.Max(0, info.BonusCount))).ToList();

        public static bool IsStagedMap(MapInfo info) => !info.IsLinear && info.StageCount > 1 && info.StageCount < FirstBonusZone;
        public static bool IsBonus(int zone) => zone >= FirstBonusZone;
        /// <summary>"Map", "Stage 3", "Bonus 1".</summary>
        public static string ZoneName(int zone) => zone == 0 ? "Map" : IsBonus(zone) ? "Bonus " + (zone - FirstBonusZone + 1) : "Stage " + zone;
        /// <summary>"MAP", "S3", "B1".</summary>
        public static string ZoneLabel(int zone) => zone == 0 ? "MAP" : IsBonus(zone) ? "B" + (zone - FirstBonusZone + 1) : "S" + zone;
    }

    /// <summary>
    /// Reads the public JSON endpoints that ksf.surf's own website uses. Only a handful of requests per map change.
    /// </summary>
    sealed class KsfApi : IDisposable
    {
        public const string Site = "https://ksf.surf";
        static readonly TimeSpan MapInfoLifetime = TimeSpan.FromMinutes(30), ZoneWrLifetime = TimeSpan.FromHours(24);
        // ksf.surf answers "429 Too Many Requests" when asked too much too fast, so zone records trickle in one at a time.
        // Brisk normally; after a "too many requests" it slows right down for a few minutes.
        static readonly TimeSpan QuickSpacing = TimeSpan.FromMilliseconds(120), CarefulSpacing = TimeSpan.FromMilliseconds(1200),
            CarefulFor = TimeSpan.FromMinutes(5), BusyPause = TimeSpan.FromSeconds(30);
        DateTime carefulUntil;
        TimeSpan ZoneRequestSpacing => DateTime.Now < carefulUntil ? CarefulSpacing : QuickSpacing;
        // ksf.surf lets one address make about a request a second, plus short bursts, before it answers "too many
        // requests". Every request here draws on a matching allowance (a little under theirs) so it never has to
        // refuse us, and the bulk lookups (stage records, the map lists) leave some over for the dashboard's own.
        const double AllowanceSize = 16, AllowancePerSecond = 0.9;
        // What each kind of bulk lookup leaves of the allowance for the rest: the records of the map you're on a
        // little, background lists (the map list, your finished maps, the next map's records) more.
        const double KeepForOthersNow = 3, KeepForOthersLater = 6;
        // Records of the map you're on still coming in: background lists wait.
        int urgentLookups;
        double allowance = AllowanceSize;
        DateTime allowanceAt = DateTime.Now;
        readonly object allowanceLock = new object();
        readonly HttpClient http;
        // Map details hardly ever change, so the frequent refreshes while you play don't ask for them again.
        readonly Dictionary<string, (DateTime At, Dictionary<string, object> Map)> mapCache = new Dictionary<string, (DateTime, Dictionary<string, object>)>(StringComparer.OrdinalIgnoreCase);
        readonly ZoneRecordStore zoneRecords;
        readonly SemaphoreSlim zoneLane = new SemaphoreSlim(1, 1);
        DateTime nextZoneRequest;

        public KsfApi()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            // .NET Framework allows only 2 connections per site by default: map pictures downloading at the same
            // time would make every data request wait behind them (and time out).
            if (ServicePointManager.DefaultConnectionLimit < 8) ServicePointManager.DefaultConnectionLimit = 8;
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KSFCompanion/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            zoneRecords = new ZoneRecordStore(Path.Combine(Program.CacheDir, "zone-records.txt"));
        }

        /// <summary>Set when ksf.surf said "too many requests": the zone records and regular polls wait until then.</summary>
        public DateTime BusyUntil { get; private set; }

        /// <summary>How far ksf.surf's clock is ahead of this PC's (from its replies' Date header), to put its timestamps on our clock.</summary>
        public TimeSpan ClockOffset { get; private set; }

        public void Dispose() => http.Dispose();

        public static string MapPage(string map) => $"{Site}/maps/{Uri.EscapeDataString(map)}";
        public static string MapImage(string map) => $"{Site}/images/{Uri.EscapeDataString(map)}.jpg";

        public HttpClient Http => http;

        public async Task<MapReport> GetReportAsync(string map, string steamId, string game, int style)
        {
            var report = new MapReport { Game = game, Map = map, SteamId = steamId, FetchedAt = DateTime.Now };
            try
            {
                var exact = CachedMap(map);
                if (exact == null)
                {
                    var found = Json.Objects(await GetJsonAsync($"/api/maps/search/{Uri.EscapeDataString(map)}").ConfigureAwait(false)).ToList();
                    exact = found.FirstOrDefault(m => string.Equals(Json.Str(m, "name"), map, StringComparison.OrdinalIgnoreCase));
                    if (exact == null)
                    {
                        report.Suggestions = await SuggestAsync(map, found).ConfigureAwait(false);
                        return report;
                    }
                    lock (mapCache) mapCache[map] = (DateTime.Now, exact);
                }

                report.Info = ParseMap(exact);
                var name = Uri.EscapeDataString(report.Info.Name);
                var query = $"?game={Uri.EscapeDataString(game)}&mode={style}";
                var wrTask = GetJsonAsync($"/api/maps/{name}/records/zone/0/1{query}");
                var personalTask = steamId == null
                    ? Task.FromResult<object>(null)
                    : GetJsonAsync($"/api/players/{Uri.EscapeDataString(steamId)}/records/map/{name}{query}");
                // Stage and bonus records that aren't saved yet come later, from FetchZoneWrsAsync.
                report.ZoneWrs = zoneRecords.Get(game, style, report.Info.Name, report.RecordZones, ZoneWrLifetime);

                try
                {
                    report.Top = ParseLeaderboard(await wrTask.ConfigureAwait(false)).Take(10).ToList();
                    report.Wr = report.Top.FirstOrDefault();
                }
                catch (Exception ex) { report.Error = Describe(ex); }

                try { ParsePersonal(await personalTask.ConfigureAwait(false), report); }
                catch (Exception ex) { report.PersonalError = Describe(ex); }
            }
            catch (Exception ex)
            {
                report.Error = Describe(ex);
            }
            return report;
        }

        Dictionary<string, object> CachedMap(string map)
        {
            lock (mapCache)
                return mapCache.TryGetValue(map, out var hit) && DateTime.Now - hit.At < MapInfoLifetime ? hit.Map : null;
        }

        /// <summary>
        /// Looks up the record on each stage/bonus that isn't saved from an earlier visit. KSF only has a leaderboard
        /// per zone, so that's one request per zone: they go one at a time, spaced out, and pause if ksf.surf says it's
        /// busy. <paramref name="found"/> gets each one as it arrives (on the caller's thread).
        /// </summary>
        public async Task FetchZoneWrsAsync(string map, IEnumerable<int> zones, string game, int style, Action<int, WorldRecord> found, CancellationToken cancel,
            bool background = false)
        {
            var query = $"?game={Uri.EscapeDataString(game)}&mode={style}";
            var fetched = false;
            // The map you're on goes first: background lists wait until its records are in.
            if (!background) Interlocked.Increment(ref urgentLookups);
            try
            {
                foreach (var zone in zones)
                {
                    // Saved meanwhile (the next map's records are fetched ahead of time): hand it over as it is.
                    if (zoneRecords.IsFresh(game, style, map, zone, ZoneWrLifetime))
                    {
                        if (zoneRecords.Get(game, style, map, new[] { zone }, ZoneWrLifetime).TryGetValue(zone, out var saved)) found(zone, saved);
                        continue;
                    }
                    var json = await GetPacedAsync($"/api/maps/{Uri.EscapeDataString(map)}/records/zone/{zone}/1{query}", cancel, background);
                    var wr = ParseLeaderboard(json).FirstOrDefault();
                    zoneRecords.Put(game, style, map, zone, wr);
                    fetched = true;
                    found(zone, wr);
                }
            }
            finally
            {
                if (!background) Interlocked.Decrement(ref urgentLookups);
                if (fetched) zoneRecords.Save();
            }
        }

        /// <summary>The stage and bonus records of a map that are saved and fresh (the rest need FetchZoneWrsAsync).</summary>
        public int FreshZoneCount(string map, IEnumerable<int> zones, string game, int style) =>
            zones.Count(zone => zoneRecords.IsFresh(game, style, map, zone, ZoneWrLifetime));

        /// <summary>
        /// The slow lane for bulk lookups (stage/bonus records, the map list): one request at a time, spaced out,
        /// and waiting out a "too many requests" from ksf.surf. Continues on the caller's thread.
        /// </summary>
        async Task<object> GetPacedAsync(string path, CancellationToken cancel, bool background = false)
        {
            while (background && Volatile.Read(ref urgentLookups) > 0) await Task.Delay(300, cancel);
            await zoneLane.WaitAsync(cancel);
            try
            {
                var wait = (BusyUntil > nextZoneRequest ? BusyUntil : nextZoneRequest) - DateTime.Now;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, cancel);
                return await GetJsonAsync(path, background ? KeepForOthersLater : KeepForOthersNow);
            }
            finally
            {
                nextZoneRequest = DateTime.Now + ZoneRequestSpacing;
                zoneLane.Release();
            }
        }

        /// <summary>
        /// Ten maps of ksf.surf's full list, newest first, starting at <paramref name="start"/> (1 = the newest).
        /// Paging through all of them builds the map list for the nominate page.
        /// </summary>
        public async Task<List<MapInfo>> GetMapsPageAsync(int start, CancellationToken cancel)
        {
            var json = await GetPacedAsync($"/api/maps/new?offset={start}", cancel, background: true);
            return Json.Objects(json).Select(ParseMap).Where(m => !string.IsNullOrEmpty(m.Name)).ToList();
        }

        /// <summary>
        /// Every map you've finished, from the "best records" on your ksf.surf profile. They come 5 at a time (most
        /// points first), so this pages to the end, paced like the other bulk lookups; <paramref name="page"/> gets
        /// each batch as it arrives (on the caller's thread).
        /// </summary>
        public async Task GetFinishedMapsAsync(string steamId, string game, int style, Action<List<FinishedMap>> page, CancellationToken cancel)
        {
            const int pageSize = 5;
            // The number in this URL is where the page starts (1 = the first), not a page number.
            for (var start = 1; start < 20000; start += pageSize)
            {
                var root = await GetPacedAsync($"/api/players/{Uri.EscapeDataString(steamId)}/bestrecords/{start}?game={Uri.EscapeDataString(game)}&mode={style}", cancel, background: true)
                    as Dictionary<string, object>;
                var records = Json.Objects(Json.Get(root, "records")).ToList();
                page(records.Select(r => new FinishedMap
                {
                    Map = Json.Str(r, "mapName"),
                    Time = Json.Num(r, "time") ?? 0,
                    // "g4" is group 4; "0" means no group.
                    Group = Json.Str(r, "rank") is string rank && rank.StartsWith("g", StringComparison.OrdinalIgnoreCase)
                            && int.TryParse(rank.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var group) ? group : (int?)null,
                    Points = Json.Int(r, "points"),
                }).Where(m => !string.IsNullOrEmpty(m.Map)).ToList());
                if (records.Count < pageSize) return;
            }
        }

        /// <summary>
        /// A player's title, rank, country rank, points and maps done on one tick rate. There's no API for those (the
        /// in-game !rank answers in chat), so they're read off the player's ksf.surf profile page (?game=66T / 100T).
        /// Null when the page doesn't have them.
        /// </summary>
        public async Task<PlayerStanding> GetStandingAsync(string steamId, string game)
        {
            var tick = game == "css100t" ? "100T" : "66T";
            var html = await GetTextAsync($"/players/{Uri.EscapeDataString(steamId)}?game={tick}").ConfigureAwait(false);
            if (html == null) return null;
            // The page's text, a piece a line: "... | Canada | PROFICIENT | 211 | maps | ... | global rank | # | 4962 |
            // country rank | # | 242 | points | 7835 | total playtime | ..."
            var text = Regex.Replace(html, @"<(script|style)\b.*?</\1>", "\n", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var lines = Regex.Replace(text, "<[^>]+>", "\n").Split('\n')
                .Select(l => WebUtility.HtmlDecode(l).Trim()).Where(l => l.Length > 0).ToList();
            int? NumberAfter(string label)
            {
                var at = lines.FindIndex(l => l.Equals(label, StringComparison.OrdinalIgnoreCase));
                for (var i = at + 1; at >= 0 && i < lines.Count && i <= at + 3; i++)
                    if (int.TryParse(lines[i].Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
                return null;
            }
            var standing = new PlayerStanding
            {
                Game = game,
                Tick = tick,
                FromProfile = true,
                Rank = NumberAfter("global rank"),
                CountryRank = NumberAfter("country rank"),
                Points = NumberAfter("points") ?? 0,
            };
            // "PROFICIENT | 211 | maps", with the country just before it (the page's menu has a "maps" too, without a number).
            var done = 0;
            var maps = Enumerable.Range(2, Math.Max(0, lines.Count - 2))
                .FirstOrDefault(i => lines[i] == "maps" && int.TryParse(lines[i - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out done));
            if (maps >= 2)
            {
                standing.MapsDone = done;
                if (Regex.IsMatch(lines[maps - 2], "^[A-Z][A-Z ]{2,24}$"))
                {
                    standing.Title = lines[maps - 2];
                    if (maps >= 3) standing.Country = lines[maps - 3];
                }
            }
            return standing.Rank == null && standing.Points == 0 ? null : standing;
        }

        /// <summary>The points of whoever is at <paramref name="rank"/> in KSF's ranking - what it takes to reach that rank.</summary>
        public async Task<int?> GetPointsAtRankAsync(int rank, string game, int style)
        {
            // The number in this URL is the rank the list starts at (100 players from there).
            var rows = Json.Objects(await GetJsonAsync($"/api/players/detailed-top/points/{rank}?game={Uri.EscapeDataString(game)}&mode={style}").ConfigureAwait(false)).ToList();
            var row = rows.FirstOrDefault(r => Json.Int(r, "rank") == rank) ?? rows.FirstOrDefault();
            return row == null ? null : Json.Int(row, "points");
        }

        /// <summary>ksf.surf's own map search (it returns up to 5 maps).</summary>
        public async Task<List<MapInfo>> SearchMapsAsync(string text)
        {
            var json = await GetJsonAsync($"/api/maps/search/{Uri.EscapeDataString(text)}").ConfigureAwait(false);
            return Json.Objects(json).Select(ParseMap).Where(m => !string.IsNullOrEmpty(m.Name)).ToList();
        }

        /// <summary>The top 10 of one stage's or bonus's leaderboard. Its first row is that zone's record, which is saved for next time too.</summary>
        public async Task<List<WorldRecord>> GetZoneTopAsync(string map, int zone, string game, int style)
        {
            var json = await GetJsonAsync($"/api/maps/{Uri.EscapeDataString(map)}/records/zone/{zone}/1?game={Uri.EscapeDataString(game)}&mode={style}").ConfigureAwait(false);
            var top = ParseLeaderboard(json).Take(10).ToList();
            zoneRecords.Put(game, style, map, zone, top.FirstOrDefault());
            zoneRecords.Save();
            return top;
        }

        /// <summary>Every KSF server with its current map and players (what ksf.surf/connect shows).</summary>
        public async Task<List<KsfServer>> GetServersAsync(string game)
        {
            var servers = new List<KsfServer>();
            foreach (var s in Json.Objects(await GetJsonAsync($"/api/servers?game={Uri.EscapeDataString(game)}").ConfigureAwait(false)))
            {
                if (Json.Bool(s, "private") || Json.Bool(s, "archived")) continue;
                var server = new KsfServer
                {
                    Game = game,
                    Name = Json.Str(s, "name") ?? Json.Str(s, "hostname"),
                    Address = Json.Str(s, "IP") ?? $"{Json.Str(s, "ip")}:{Json.Str(s, "port")}",
                    Map = Json.Str(s, "map"),
                    Tier = Json.Int(s, "tier") ?? 0,
                    IsLinear = Json.Bool(s, "isLinear"),
                    StageCount = Json.Int(s, "cp_count") ?? 0,
                    BonusCount = Json.Int(s, "b_count") ?? 0,
                    PlayerCount = Json.Int(s, "playerCount") ?? 0,
                    TimeLeftSeconds = Json.Int(s, "time_left") ?? 0,
                    TimeLimitMinutes = Json.Int(s, "time_limit") ?? 0,
                    // KSF samples each server every so often, and this list is cached for about a minute on top: time
                    // left and time on server are as of the sample ("updated", Unix time), often over a minute ago.
                    FetchedAt = Json.Num(s, "updated") is double updated && updated > 0
                        ? Earliest(DateTime.Now, DateTimeOffset.FromUnixTimeSeconds((long)updated).LocalDateTime - ClockOffset)
                        : DateTime.Now.AddSeconds(-Math.Max(0, Math.Min(600, Json.Int(s, "last_updated") ?? 0))),
                };
                foreach (var p in Json.Objects(Json.Get(s, "players")))
                    server.Players.Add(new KsfServerPlayer
                    {
                        SteamId = Json.Str(p, "steamid"),
                        Name = Json.Str(p, "playername"),
                        Rank = Json.Int(p, "rank"),
                        Points = Json.Int(p, "points"),
                        Zone = Json.Int(p, "zone"),
                        ConnectedSeconds = Json.Int(p, "timeconnected"),
                    });
                if (!string.IsNullOrEmpty(server.Map)) servers.Add(server);
            }
            return servers;
        }

        /// <summary>The player's latest records (new PRs, group changes, WRs), newest first.</summary>
        public async Task<List<RecentRecord>> GetRecentAsync(string steamId, string game, int style, int pages = 2)
        {
            const int pageSize = 5;
            var records = new List<RecentRecord>();
            var seen = new HashSet<string>();
            // The number in this URL is where the page starts (1 = newest), not a page number.
            for (var start = 1; start <= pages * pageSize; start += pageSize)
            {
                var root = await GetJsonAsync($"/api/players/{Uri.EscapeDataString(steamId)}/recentlyplayed/{start}?game={Uri.EscapeDataString(game)}&mode={style}").ConfigureAwait(false)
                    as Dictionary<string, object>;
                var batch = Json.Objects(Json.Get(root, "recentRecords")).ToList();
                foreach (var r in batch.Where(r => seen.Add(Json.Str(r, "id") ?? Guid.NewGuid().ToString())))
                    records.Add(new RecentRecord
                    {
                        Game = game,
                        Map = Json.Str(r, "mapName"),
                        ZoneId = Json.Int(r, "zoneID") ?? 0,
                        Time = Json.Num(r, "surfTime"),
                        Type = Json.Str(r, "recordType"),
                        Server = Json.Str(r, "server"),
                        Rank = Json.Int(r, "newRank"),
                        Date = Json.Date(r, "date_at"),
                    });
                if (batch.Count < pageSize) break;
            }
            return records;
        }

        public async Task<string> GetAvatarUrlAsync(string steamId)
        {
            var root = await GetJsonAsync($"/api/players/{Uri.EscapeDataString(steamId)}/avatar").ConfigureAwait(false) as Dictionary<string, object>;
            return Json.Str(root, "avatar_url");
        }

        public async Task<int?> GetTierAsync(string map)
        {
            try
            {
                var found = Json.Objects(await GetJsonAsync($"/api/maps/search/{Uri.EscapeDataString(map)}").ConfigureAwait(false));
                var exact = found.FirstOrDefault(m => string.Equals(Json.Str(m, "name"), map, StringComparison.OrdinalIgnoreCase));
                return exact == null ? null : Json.Int(exact, "tier");
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is ArgumentException || ex is InvalidOperationException)
            {
                return null;
            }
        }

        async Task<List<string>> SuggestAsync(string map, List<Dictionary<string, object>> found)
        {
            // Servers often run a renamed version (surf_x_v2, surf_x_fix...), so look the base name up instead.
            if (found.Count == 0)
            {
                var keyword = Keyword(map);
                if (keyword.Length >= 3)
                    found = Json.Objects(await GetJsonAsync($"/api/maps/search/{Uri.EscapeDataString(keyword)}").ConfigureAwait(false)).ToList();
            }
            return found.Select(m => Json.Str(m, "name")).Where(n => !string.IsNullOrEmpty(n)).Take(3).ToList();
        }

        static readonly HashSet<string> VersionWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "fix", "fixed", "refix", "final", "ksf", "njv", "remake", "remix", "new", "css", "csgo", "go", "beta", "alpha", "hd" };

        static string Keyword(string map)
        {
            var words = map.ToLowerInvariant().Split('_')
                .Where(w => w != "surf" && w.Length > 0 && !VersionWords.Contains(w) && !IsVersionTag(w))
                .ToList();
            return words.OrderByDescending(w => w.Length).FirstOrDefault() ?? "";
        }

        static bool IsVersionTag(string word) =>
            word.All(char.IsDigit) ||
            (word.Length >= 2 && (word[0] == 'v' || word[0] == 'b' || word[0] == 'a' || word[0] == 'r') && word.Skip(1).All(c => char.IsDigit(c) || c == '.'));

        /// <summary>A JSON reply (null for "not found"). <paramref name="keep"/>: how much of the allowance to leave for others.</summary>
        async Task<object> GetJsonAsync(string path, double keep = 0)
        {
            var text = await GetTextAsync(path, keep).ConfigureAwait(false);
            return text == null ? null : Json.Parse(text);
        }

        async Task<string> GetTextAsync(string path, double keep = 0)
        {
            for (var attempt = 0; ; attempt++)
            {
                await WaitForAllowanceAsync(keep).ConfigureAwait(false);
                HttpResponseMessage response;
                try
                {
                    response = await http.GetAsync(Site + path).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    Program.Trace($"GET {path} failed: {ex.GetBaseException().Message}");
                    throw;
                }
                using (response)
                {
                    if ((int)response.StatusCode == 429)
                    {
                        // Too many requests: everything optional waits a while; this request tries once more if the wait is short.
                        var retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                        var pause = retry > BusyPause ? retry : BusyPause;
                        BusyUntil = DateTime.Now + pause;
                        carefulUntil = DateTime.Now + CarefulFor;
                        Program.Trace($"GET {path}: 429, retry after {retry.TotalSeconds}s");
                        if (attempt == 0 && retry <= TimeSpan.FromSeconds(5))
                        {
                            await Task.Delay(retry + TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);
                            continue;
                        }
                        throw new HttpRequestException(BusyMessage);
                    }
                    if (response.StatusCode == HttpStatusCode.NotFound) return null;
                    if (!response.IsSuccessStatusCode)
                    {
                        Program.Trace($"GET {path}: {(int)response.StatusCode}");
                        throw new HttpRequestException($"ksf.surf answered {(int)response.StatusCode}");
                    }
                    // Only a clock that's clearly off counts (the header is in whole seconds and arrives a moment late).
                    if (response.Headers.Date is DateTimeOffset serverNow)
                    {
                        var offset = serverNow.UtcDateTime - DateTime.UtcNow;
                        ClockOffset = Math.Abs(offset.TotalSeconds) < 2 ? TimeSpan.Zero : offset;
                    }
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>Waits until a request can go without going over ksf.surf's limit, leaving <paramref name="keep"/> of the allowance for others.</summary>
        async Task WaitForAllowanceAsync(double keep)
        {
            while (true)
            {
                TimeSpan wait;
                lock (allowanceLock)
                {
                    var now = DateTime.Now;
                    allowance = Math.Min(AllowanceSize, allowance + (now - allowanceAt).TotalSeconds * AllowancePerSecond);
                    allowanceAt = now;
                    if (allowance >= 1 + keep)
                    {
                        allowance -= 1;
                        return;
                    }
                    wait = TimeSpan.FromSeconds((1 + keep - allowance) / AllowancePerSecond);
                }
                await Task.Delay(wait).ConfigureAwait(false);
            }
        }

        const string BusyMessage = "ksf.surf is busy (too many requests)";

        static DateTime Earliest(DateTime a, DateTime b) => a < b ? a : b;

        static MapInfo ParseMap(Dictionary<string, object> m)
        {
            var rating = Json.Obj(m, "rating");
            return new MapInfo
            {
                Name = Json.Str(m, "name"),
                Tier = Json.Int(m, "tier") ?? 0,
                IsLinear = Json.Bool(m, "isLinear"),
                StageCount = Json.Int(m, "cp_count") ?? 0,
                BonusCount = Json.Int(m, "b_count") ?? 0,
                Mappers = string.Join(", ", Json.Objects(Json.Get(m, "mappers")).Select(x => Json.Str(x, "name")).Where(n => !string.IsNullOrEmpty(n))),
                Rating = Json.Num(rating, "average"),
                RatingCount = Json.Int(rating, "count") ?? 0,
                Added = Json.Date(m, "created_at"),
                Popularity = Json.Num(m, "popularity") ?? 0,
            };
        }

        static IEnumerable<WorldRecord> ParseLeaderboard(object json)
        {
            foreach (var row in Json.Objects(json))
            {
                var time = Json.Num(row, "time");
                if (time == null) continue;
                yield return new WorldRecord
                {
                    Rank = Json.Int(row, "rank") ?? 0,
                    Name = Json.Str(row, "name"),
                    SteamId = Json.Str(row, "steamID"),
                    Country = Json.Str(row, "country"),
                    Time = time.Value,
                    Date = Json.Date(row, "date_at"),
                };
            }
        }

        static void ParsePersonal(object json, MapReport report)
        {
            if (!(json is Dictionary<string, object> root)) return;
            var basic = Json.Obj(root, "basicInfo");
            report.PlayerName = Json.Str(basic, "name");
            report.PlayerCountry = Json.Str(basic, "country");
            foreach (var z in Json.Objects(Json.Get(root, "records")))
            {
                var time = Json.Num(z, "surfTime");
                report.Zones.Add(new ZoneRecord
                {
                    ZoneId = Json.Int(z, "zoneId") ?? -1,
                    Time = time > 0 ? time : null,
                    Rank = Json.Int(z, "rank"),
                    TotalRanks = Json.Int(z, "totalRanks"),
                    Completions = Json.Int(z, "completions"),
                    Attempts = Json.Int(z, "attempts"),
                    PlaytimeSeconds = Json.Num(z, "totalSurfTime"),
                    Group = Json.Int(z, "group"),
                });
            }
        }

        static string Describe(Exception ex)
        {
            while (ex is AggregateException agg && agg.InnerException != null) ex = agg.InnerException;
            return ex switch
            {
                TaskCanceledException _ => "ksf.surf took too long to answer",
                HttpRequestException h when h.Message == BusyMessage => "ksf.surf is busy - trying again in a moment",
                HttpRequestException h when h.Message.StartsWith("ksf.surf answered", StringComparison.Ordinal) => h.Message + " - trying again in a moment",
                HttpRequestException _ => "couldn't reach ksf.surf",
                _ => "ksf.surf error: " + ex.Message,
            };
        }
    }

    static class Json
    {
        /// <summary>
        /// JSON as plain objects, the same shapes the Windows build's JavaScriptSerializer gives: objects as
        /// Dictionary&lt;string, object&gt;, arrays as object[], whole numbers as int or long, other numbers as decimal.
        /// </summary>
        public static object Parse(string text)
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true });
            return Convert(document.RootElement);
        }

        static object Convert(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.Object => e.EnumerateObject().Aggregate(new Dictionary<string, object>(), (d, p) => { d[p.Name] = Convert(p.Value); return d; }),
            JsonValueKind.Array => e.EnumerateArray().Select(Convert).ToArray(),
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetInt32(out var i) ? i : e.TryGetInt64(out var l) ? l : e.TryGetDecimal(out var m) ? m : (object)e.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };

        public static IEnumerable<Dictionary<string, object>> Objects(object value) =>
            (value as object[])?.OfType<Dictionary<string, object>>() ?? Enumerable.Empty<Dictionary<string, object>>();

        public static object Get(Dictionary<string, object> obj, string key) =>
            obj != null && obj.TryGetValue(key, out var value) ? value : null;

        public static Dictionary<string, object> Obj(Dictionary<string, object> obj, string key) => Get(obj, key) as Dictionary<string, object>;

        public static string Str(Dictionary<string, object> obj, string key) => Get(obj, key) switch
        {
            null => null,
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString(),
        };

        public static double? Num(Dictionary<string, object> obj, string key) => Get(obj, key) switch
        {
            int i => i,
            long l => l,
            decimal m => (double)m,
            double d => d,
            float f => f,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };

        public static int? Int(Dictionary<string, object> obj, string key) => Num(obj, key) is double d ? (int)Math.Round(d) : (int?)null;

        public static bool Bool(Dictionary<string, object> obj, string key) => Get(obj, key) is bool b && b;

        public static DateTime? Date(Dictionary<string, object> obj, string key) =>
            DateTime.TryParse(Str(obj, key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc)
                ? utc.ToLocalTime()
                : (DateTime?)null;
    }
}
