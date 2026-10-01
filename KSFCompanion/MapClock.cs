using System;

namespace KsfCompanion
{
    /// <summary>
    /// How long the map you're playing has left: its time limit minus how long it has been running. The two are
    /// learned separately, because each source is good at one of them:
    /// <list type="bullet">
    /// <item>the limit: mp_timelimit, read in the game's console (it only prints there). Extending the map raises it,
    /// so it's read again whenever the map is extended.</item>
    /// <item>when the map started: ksf.surf's sample of the server (time limit and time left as of the sample - often
    /// over a minute old, as the list is cached, but the start doesn't move when the map is extended), the timer's
    /// "2 minutes remaining" in chat (exact), and the timer's "Timeleft: 8 minutes" panel read from the demo (whole
    /// minutes, but the moment it ticks down is a minute mark).</item>
    /// </list>
    /// </summary>
    sealed class MapClock
    {
        // How far behind the panel text can be by the time it's read (the game writes its demo every ~5.5 s).
        const double PanelLag = 7;

        // How sure we are of the start: a guess from the panel's whole minutes, a minute mark on the panel,
        // ksf.surf's sample, or the timer's own countdown message.
        enum Source { None, PanelRange, PanelMark, Ksf, Countdown }

        double? limitMinutes;
        DateTime limitAt = DateTime.MinValue;
        bool limitFromConsole;
        DateTime start;
        Source startFrom;
        // "N minutes remaining" while the limit isn't known yet.
        double? plainLeft;
        DateTime plainAt;
        // Extended by an amount that isn't known until mp_timelimit is read again.
        bool extending;
        DateTime extendingSince;
        int? panelMinutes;
        // How often the map has been extended while we watched, the limit when we first read it, and extension
        // messages the console hasn't shown a higher limit for yet (so a higher limit alone counts as one more).
        int extensions, unconfirmedExtensions;
        double? firstLimit, lastConsoleLimit;
        DateTime firstLimitAt;

        /// <summary>A new map (or none): nothing is known about it yet.</summary>
        public void Reset()
        {
            limitMinutes = null;
            limitAt = DateTime.MinValue;
            limitFromConsole = false;
            startFrom = Source.None;
            plainLeft = null;
            extending = false;
            panelMinutes = null;
            extensions = unconfirmedExtensions = 0;
            firstLimit = lastConsoleLimit = null;
        }

        /// <summary>The time limit in minutes (extensions included), if known.</summary>
        public double? LimitMinutes => limitMinutes;

        /// <summary>How many times the map has been extended while we watched.</summary>
        public int Extensions => extensions;

        /// <summary>Minutes added to the time limit while we watched.</summary>
        public double ExtendedMinutes => limitMinutes is double limit && firstLimit is double first && limit > first ? limit - first : 0;

        /// <summary>We've watched this map from (about) its start, so the extensions counted are all of them.</summary>
        public bool SeenFromStart => firstLimit != null && startFrom != Source.None && (firstLimitAt - start).TotalMinutes < 4;

        /// <summary>The map was just extended and the new time limit isn't known yet.</summary>
        public bool Extending => extending;

        /// <summary>Seconds left, or null while that isn't known (or the map has no time limit).</summary>
        public double? LeftAt(DateTime now)
        {
            if (extending) return null;
            if (limitMinutes is double limit && startFrom != Source.None)
                return limit > 0 ? Math.Max(0, limit * 60 - (now - start).TotalSeconds) : (double?)null;
            return plainLeft is double left ? Math.Max(0, left - (now - plainAt).TotalSeconds) : (double?)null;
        }

        void SetStart(DateTime value, Source from)
        {
            start = value;
            startFrom = from;
        }

        /// <summary>mp_timelimit as the console printed it (0: no time limit).</summary>
        public void FromConsole(double minutes, DateTime now)
        {
            if (firstLimit == null)
            {
                firstLimit = minutes;
                firstLimitAt = now;
            }
            else if (lastConsoleLimit is double before && minutes > before + 0.01)
            {
                // Raised: an extension message we saw, or one we didn't recognise.
                if (unconfirmedExtensions > 0) unconfirmedExtensions = 0;
                else extensions++;
            }
            lastConsoleLimit = minutes;
            limitMinutes = minutes;
            limitAt = now;
            limitFromConsole = true;
            extending = false;
        }

        /// <summary>ksf.surf's sample of the server: its time limit and time left at <paramref name="sampledAt"/>.</summary>
        public void FromKsf(double limit, double left, DateTime sampledAt)
        {
            if (limit <= 0) return;
            // The start holds even if the map was extended since the sample; only the limit may be out of date.
            if (startFrom <= Source.Ksf) SetStart(sampledAt.AddSeconds(-(limit * 60 - left)), Source.Ksf);
            if (!limitFromConsole && sampledAt > limitAt)
            {
                limitMinutes = limit;
                limitAt = sampledAt;
            }
        }

        /// <summary>The timer's "N minutes remaining" / "N seconds remaining" (0 when the map ends).</summary>
        public void Countdown(double seconds, DateTime now)
        {
            plainLeft = seconds;
            plainAt = now;
            if (limitMinutes is double limit && limit > 0 && !extending) SetStart(now.AddSeconds(-(limit * 60 - seconds)), Source.Countdown);
        }

        /// <summary>
        /// The map was extended by <paramref name="minutes"/>, or by an amount the message doesn't say (null). Either
        /// way mp_timelimit should be read again to be sure.
        /// </summary>
        public void Extended(int? minutes, DateTime now)
        {
            extensions++;
            unconfirmedExtensions++;
            if (minutes is int added && limitMinutes is double limit && !extending)
            {
                limitMinutes = limit + added;
                limitAt = now;
                limitFromConsole = false;
                if (plainLeft is double left) plainLeft = left + added * 60;
                return;
            }
            extending = true;
            extendingSince = now;
            limitFromConsole = false;
        }

        /// <summary>
        /// The timer's panel shows "Timeleft: N minutes" - between N and N+1 minutes left (0: "Less than 1 minute").
        /// <paramref name="previous"/> is what it showed before and <paramref name="changedAgo"/> about how many seconds
        /// ago it changed, when it has only just changed (both null otherwise). True when mp_timelimit should be read
        /// again, because the panel doesn't add up with what we know (the map was extended, say).
        /// </summary>
        public bool FromPanel(int minutes, int? previous, double? changedAgo, DateTime now)
        {
            var jumped = previous is int from && changedAgo != null && minutes > from;
            // Just extended: panel text that doesn't show it yet is from before.
            if (extending && !jumped && (now - extendingSince).TotalSeconds < 15) return false;
            panelMinutes = minutes;
            if (!(limitMinutes is double limit) || limit <= 0) return !limitFromConsole;

            double low = minutes * 60 - PanelLag, high = (minutes + 1) * 60;
            var elapsedLimit = limit * 60;
            if (previous is int before && changedAgo is double ago && before == minutes + 1 && !extending)
            {
                // Ticked down: a minute mark just went by, which says when the map started (given the limit).
                var mark = high - ago;
                var markStart = now.AddSeconds(-(elapsedLimit - mark));
                var offBy = startFrom == Source.None ? double.MaxValue : Math.Abs((markStart - start).TotalSeconds);
                if (startFrom < Source.PanelMark || offBy > 4)
                {
                    // Far off with a limit that isn't the console's own: more likely the limit changed than the start.
                    if (offBy > 45 && !limitFromConsole && startFrom >= Source.PanelMark) return true;
                    SetStart(markStart, Source.PanelMark);
                }
                return false;
            }

            var left = LeftAt(now);
            if (left is double l && l >= low - 1 && l <= high + 1) return false;
            // Doesn't fit (or nothing yet): a jump up means extended, which the console will confirm; otherwise the
            // start is off, so move it to fit.
            if (jumped || extending || !limitFromConsole)
            {
                if (startFrom == Source.None) SetStart(now.AddSeconds(-(elapsedLimit - (minutes * 60 + 30))), Source.PanelRange);
                return true;
            }
            var fit = left == null ? minutes * 60 + 30 : Math.Max(minutes * 60, Math.Min(high, left.Value));
            SetStart(now.AddSeconds(-(elapsedLimit - fit)), Source.PanelRange);
            return false;
        }
    }
}
