using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeatherPlugin.Models;
using WeatherPlugin.Services;

namespace WeatherPlugin.UI
{
    public class ResultWindow : Form
    {
        // vatSys chrome: #A0AAAA and relatives
        private static readonly Color ColBg       = Color.FromArgb(160, 170, 170);
        private static readonly Color ColPanel    = Color.FromArgb(148, 158, 158);
        private static readonly Color ColBtn      = Color.FromArgb(140, 152, 152);
        private static readonly Color ColBtnBdr   = Color.FromArgb(90,  102, 102);
        private static readonly Color ColDetailBg = Color.FromArgb(148, 158, 158);
        private static readonly Color ColYellow   = Color.FromArgb(180, 120,  0);
        private static readonly Color ColBlue     = Color.FromArgb(20,  80,  180);
        private static readonly Color ColGrey     = Color.FromArgb(70,  85,  85);
        private static readonly Color ColStale    = Color.FromArgb(150, 60,   0);
        private static readonly Color ColSep      = Color.FromArgb(105, 115, 115);
        private static readonly Color ColDetailTxt= Color.FromArgb(15,  20,  20);

        // NATO MIL flight category colours (VFR/MVFR/IFR/LIFR — NOAA standard)
        private static readonly Color ColVfr  = Color.FromArgb(0,   210, 0);    // green
        private static readonly Color ColMvfr = Color.FromArgb(80,  140, 255);  // blue
        private static readonly Color ColIfr  = Color.FromArgb(220, 50,  50);   // red
        private static readonly Color ColLifr = Color.FromArgb(220, 60,  220);  // magenta

        private static readonly Font MonoFont = new Font("Courier New", 8f,  FontStyle.Regular);
        private static readonly Font MonoBold = new Font("Courier New", 8f,  FontStyle.Bold);
        private static readonly Font UiFont   = new Font("Arial",       8f,  FontStyle.Regular);
        private static readonly Font UiBold   = new Font("Arial",       8f,  FontStyle.Bold);

        private const int MetarIntervalMs = 60_000;   // 1 minute
        private const int TafIntervalMs   = 300_000;  // 5 minutes

        // ── State ─────────────────────────────────────────────────────────────
        private readonly WeatherCache _cache;
        private readonly List<MonitorEntry> _entries = new List<MonitorEntry>();

        private System.Timers.Timer        _metarTimer;
        private System.Timers.Timer        _tafTimer;
        private System.Windows.Forms.Timer _countdownTimer;
        private DateTime _nextMetarRefresh;
        private DateTime _nextTafRefresh;
        private bool     _fetching;

        // ── Controls ──────────────────────────────────────────────────────────
        private Label          _statusLabel;
        private Label          _countdownLabel;
        private Button         _refreshNowBtn;
        private ClickableRtb   _display;

        // Span tracking: each row's character range so middle-click can hit-test
        private readonly List<EntrySpan> _spans = new List<EntrySpan>();

        public ResultWindow(WeatherCache cache)
        {
            _cache = cache;
            BuildUi();
            StartTimers();
        }

        // ── UI ────────────────────────────────────────────────────────────────

        private void BuildUi()
        {
            Text            = "AIS Monitor";
            Size            = new Size(640, 380);
            MinimumSize     = new Size(420, 200);
            BackColor       = ColBg;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition   = FormStartPosition.Manual;
            Font            = UiFont;

            var toolbar = new Panel
            {
                Dock = DockStyle.Top, Height = 26, BackColor = ColPanel,
            };

            int x = 6, y = 4;

            _refreshNowBtn = Btn("Refresh All", x, y);
            _refreshNowBtn.Click += async (s, e) => await RefreshAll(metar: true, taf: true);
            x += _refreshNowBtn.Width + 10;

            _statusLabel = new Label
            {
                Left = x, Top = y + 4, AutoSize = true,
                ForeColor = Color.FromArgb(28, 30, 30), Font = UiFont,
                Text = "No stations",
            };
            x += 220;

            _countdownLabel = new Label
            {
                Left = x, Top = y + 4, AutoSize = true,
                ForeColor = Color.FromArgb(0, 0, 90), Font = UiBold, Text = "",
            };

            toolbar.Controls.AddRange(new Control[] { _refreshNowBtn, _statusLabel, _countdownLabel });

            _display = new ClickableRtb
            {
                Dock        = DockStyle.Fill,
                ReadOnly    = true,
                BackColor   = ColDetailBg,
                ForeColor   = ColDetailTxt,
                Font        = MonoFont,
                WordWrap    = false,
                BorderStyle = BorderStyle.None,
                ScrollBars  = RichTextBoxScrollBars.Both,
            };
            _display.MiddleClick += OnDisplayMiddleClick;

            Controls.Add(_display);
            Controls.Add(toolbar);

            Render();
        }

        private Button Btn(string text, int left, int top)
        {
            var b = new Button
            {
                Text = text, Left = left, Top = top, Height = 22, AutoSize = true,
                BackColor = ColBtn, ForeColor = Color.FromArgb(20, 20, 20),
                FlatStyle = FlatStyle.Flat, Font = UiFont,
            };
            b.FlatAppearance.BorderColor = ColBtnBdr;
            return b;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void AddStation(string icao, bool watchMetar, bool watchTaf, bool watchAtis = false)
        {
            icao = icao.ToUpper();

            var entry = _entries.FirstOrDefault(e => e.Icao == icao);
            if (entry == null)
            {
                entry = new MonitorEntry
                {
                    Icao       = icao,
                    WatchMetar = watchMetar,
                    WatchTaf   = watchTaf,
                    WatchAtis  = watchAtis,
                    MetarState = EntryState.Yellow,
                    TafState   = EntryState.Yellow,
                    AtisState  = EntryState.Yellow,
                };
                _entries.Add(entry);
            }
            else
            {
                if (watchMetar) entry.WatchMetar = true;
                if (watchTaf)   entry.WatchTaf   = true;
                if (watchAtis)  entry.WatchAtis  = true;
            }

            SetStatus($"Fetching {icao}…");

            Task.Run(async () =>
            {
                await WeatherService.FetchStationAsync(icao, _cache);
                SafeUi(() =>
                {
                    var we = _cache.Get(icao);
                    if (we != null)
                    {
                        entry.MetarRaw = we.RawMetar;
                        entry.TafRaw   = we.RawTaf;
                        entry.AtisRaw  = we.RawAtis;
                    }
                    Render();
                    SetStatus($"{_entries.Count} station(s)");
                });
            });
        }

        // ── Timers ────────────────────────────────────────────────────────────

        private void StartTimers()
        {
            _nextMetarRefresh = DateTime.UtcNow.AddMilliseconds(MetarIntervalMs);
            _nextTafRefresh   = DateTime.UtcNow.AddMilliseconds(TafIntervalMs);

            _metarTimer = new System.Timers.Timer(MetarIntervalMs) { AutoReset = true };
            _metarTimer.Elapsed += (s, e) => _ = RefreshAll(metar: true, taf: false);
            _metarTimer.Start();

            _tafTimer = new System.Timers.Timer(TafIntervalMs) { AutoReset = true };
            _tafTimer.Elapsed += (s, e) => _ = RefreshAll(metar: false, taf: true);
            _tafTimer.Start();

            _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _countdownTimer.Tick += (s, e) => TickCountdown();
            _countdownTimer.Start();
        }

        private async Task RefreshAll(bool metar, bool taf)
        {
            if (_fetching) return;
            var stations = _entries.Where(e => (metar && e.WatchMetar) || (taf && e.WatchTaf) || e.WatchAtis)
                                   .Select(e => e.Icao).ToList();
            if (stations.Count == 0) return;

            _fetching = true;
            if (metar) _nextMetarRefresh = DateTime.UtcNow.AddMilliseconds(MetarIntervalMs);
            if (taf)   _nextTafRefresh   = DateTime.UtcNow.AddMilliseconds(TafIntervalMs);

            SetStatus("Refreshing…");
            try
            {
                await Task.Run(async () =>
                {
                    foreach (var icao in stations)
                        await WeatherService.FetchStationAsync(icao, _cache);
                });

                SafeUi(() =>
                {
                    foreach (var entry in _entries)
                    {
                        var we = _cache.Get(entry.Icao);
                        if (we == null) continue;

                        if (metar && entry.WatchMetar)
                        {
                            var nowMetar = we.RawMetar ?? "";
                            if (entry.MetarRaw != null && entry.MetarRaw != nowMetar && nowMetar != "")
                            {
                                entry.PrevMetarRaw = entry.MetarRaw;
                                entry.MetarState   = EntryState.Yellow;
                            }
                            entry.MetarRaw = nowMetar;
                        }

                        if (taf && entry.WatchTaf)
                        {
                            var nowTaf = we.RawTaf ?? "";
                            if (entry.TafRaw != null && entry.TafRaw != nowTaf && nowTaf != "")
                            {
                                entry.PrevTafRaw = entry.TafRaw;
                                entry.TafState   = EntryState.Yellow;
                            }
                            entry.TafRaw = nowTaf;
                        }

                        if (entry.WatchAtis)
                        {
                            var nowAtis = we.RawAtis ?? "";
                            if (entry.AtisRaw != null && entry.AtisRaw != nowAtis && nowAtis != "")
                            {
                                entry.PrevAtisRaw = entry.AtisRaw;
                                entry.AtisState   = EntryState.Yellow;
                            }
                            entry.AtisRaw = nowAtis;
                        }
                    }
                    Render();
                    SetStatus($"Updated {DateTime.UtcNow:HH:mm}Z  |  {_entries.Count} station(s)");
                });
            }
            finally
            {
                _fetching = false;
            }
        }

        private void TickCountdown()
        {
            if (_entries.Count == 0) return;
            var rm = _nextMetarRefresh - DateTime.UtcNow;
            var rt = _nextTafRefresh   - DateTime.UtcNow;
            if (rm < TimeSpan.Zero) rm = TimeSpan.Zero;
            if (rt < TimeSpan.Zero) rt = TimeSpan.Zero;
            SafeUi(() => _countdownLabel.Text =
                $"METAR {rm.Minutes}:{rm.Seconds:D2}  TAF {rt.Minutes}:{rt.Seconds:D2}");
        }

        // ── Middle-click handling ─────────────────────────────────────────────

        private void OnDisplayMiddleClick(Point clientPt)
        {
            int charIdx;
            try { charIdx = _display.GetCharIndexFromPosition(clientPt); }
            catch { return; }

            foreach (var span in _spans)
            {
                if (charIdx < span.Start || charIdx > span.End) continue;

                if (span.IsAtis)
                    CycleAtisState(span.Entry);
                else if (span.IsMetar)
                    CycleMetarState(span.Entry);
                else
                    CycleTafState(span.Entry);

                Render();
                return;
            }
        }

        private void CycleMetarState(MonitorEntry entry)
        {
            if (entry.MetarState == EntryState.Yellow)
            {
                entry.MetarState = EntryState.Blue;
            }
            else
            {
                entry.WatchMetar = false;
                if (!entry.WatchTaf && !entry.WatchAtis)
                    _entries.Remove(entry);
            }
        }

        private void CycleTafState(MonitorEntry entry)
        {
            if (entry.TafState == EntryState.Yellow)
            {
                entry.TafState = EntryState.Blue;
            }
            else
            {
                entry.WatchTaf = false;
                if (!entry.WatchMetar && !entry.WatchAtis)
                    _entries.Remove(entry);
            }
        }

        private void CycleAtisState(MonitorEntry entry)
        {
            if (entry.AtisState == EntryState.Yellow)
            {
                entry.AtisState = EntryState.Blue;
            }
            else
            {
                entry.WatchAtis = false;
                if (!entry.WatchMetar && !entry.WatchTaf)
                    _entries.Remove(entry);
            }
        }

        // ── Flight category helpers ───────────────────────────────────────────

        // Badge colour — VFR still shows green in the header badge.
        private static Color CategoryColor(FlightCategory cat)
        {
            switch (cat)
            {
                case FlightCategory.VFR:  return ColVfr;
                case FlightCategory.MVFR: return ColMvfr;
                case FlightCategory.IFR:  return ColIfr;
                case FlightCategory.LIFR: return ColLifr;
                default:                  return ColGrey;
            }
        }

        // Per-element colour — VFR elements render in the standard text colour ("white when okay").
        private static Color ElementColor(FlightCategory? cat)
        {
            if (cat == null) return ColDetailTxt;
            switch (cat)
            {
                case FlightCategory.VFR:  return ColDetailTxt;
                case FlightCategory.MVFR: return ColMvfr;
                case FlightCategory.IFR:  return ColIfr;
                case FlightCategory.LIFR: return ColLifr;
                default:                  return ColGrey;
            }
        }

        private static string CategoryLabel(FlightCategory cat)
        {
            switch (cat)
            {
                case FlightCategory.VFR:  return "VFR";
                case FlightCategory.MVFR: return "MVFR";
                case FlightCategory.IFR:  return "IFR";
                case FlightCategory.LIFR: return "LIFR";
                default:                  return "";
            }
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        private void Render()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke((Action)Render); return; }

            _spans.Clear();
            _display.Clear();

            if (_entries.Count == 0)
            {
                Write("No stations monitored.\r\nUse the AU Weather window to request a station.\r\n",
                      ColGrey, UiFont);
                return;
            }

            foreach (var entry in _entries.ToList())
            {
                if (entry.WatchMetar) RenderMetar(entry);
                if (entry.WatchTaf)   RenderTaf(entry);
                if (entry.WatchAtis)  RenderAtis(entry);

                Write("\r\n" + new string('─', 72) + "\r\n", ColSep);
            }

            _display.SelectionStart = 0;
            _display.ScrollToCaret();
        }

        private void RenderMetar(MonitorEntry entry)
        {
            bool yellow   = entry.MetarState == EntryState.Yellow;
            var  stateCol = yellow ? ColYellow : ColBlue;

            var cat      = MetarDecoder.Classify(entry.MetarRaw ?? "");
            var catCol   = CategoryColor(cat);
            var catLabel = CategoryLabel(cat);

            int spanStart = _display.TextLength;

            // Header: ICAO  ●  [CAT]  time  source
            Write("METAR  " + entry.Icao, stateCol, MonoBold);
            if (yellow) Write("  ●", ColYellow, MonoBold);
            if (catLabel != "") Write($"  [{catLabel}]", catCol, UiBold);
            var obsTime    = MetarDecoder.ParseObsTime(entry.MetarRaw ?? "");
            if (obsTime != null) Write($"  {obsTime}", ColGrey, UiFont);
            var metarSrc   = _cache.Get(entry.Icao)?.MetarSource;
            if (metarSrc  != null) Write($"  {metarSrc}", ColGrey, UiFont);
            Write("\r\n", stateCol);

            // Body — individual elements coloured by their own NOAA category
            if (!string.IsNullOrEmpty(entry.MetarRaw))
            {
                var tokens = MetarDecoder.TokenizeForDisplay(entry.MetarRaw);
                for (int t = 0; t < tokens.Count; t++)
                {
                    var text    = tokens[t].Text;
                    var elemCat = tokens[t].Cat;
                    Write(text + (t < tokens.Count - 1 ? " " : ""), ElementColor(elemCat));
                }
                Write("\r\n", ColDetailTxt);

                if (!string.IsNullOrEmpty(entry.PrevMetarRaw))
                    Write("  Prev: " + entry.PrevMetarRaw + "\r\n", ColGrey, UiFont);
            }
            else
            {
                Write("  (No METAR)\r\n", ColStale);
            }

            _spans.Add(new EntrySpan(entry, spanStart, _display.TextLength - 1, isMetar: true));
        }

        private void RenderTaf(MonitorEntry entry)
        {
            bool yellow = entry.TafState == EntryState.Yellow;
            var  col    = yellow ? ColYellow : ColBlue;

            int spanStart = _display.TextLength;
            Write("TAF    " + entry.Icao, col, MonoBold);
            if (yellow) Write("  ●", ColYellow, MonoBold);
            var tafSrc = _cache.Get(entry.Icao)?.TafSource;
            if (tafSrc != null) Write($"  {tafSrc}", ColGrey, UiFont);
            Write("\r\n", col);

            if (!string.IsNullOrEmpty(entry.TafRaw))
            {
                foreach (var line in entry.TafRaw.Split('\n'))
                {
                    var trimmed = line.TrimEnd();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    var tokens = MetarDecoder.TokenizeForDisplay(trimmed);
                    for (int t = 0; t < tokens.Count; t++)
                    {
                        var elemCat = tokens[t].Cat;
                        Write(tokens[t].Text + (t < tokens.Count - 1 ? " " : ""),
                              ElementColor(elemCat));
                    }
                    Write("\r\n", ColDetailTxt);
                }

                if (!string.IsNullOrEmpty(entry.PrevTafRaw))
                {
                    Write("  Previous TAF:\r\n", ColGrey, UiFont);
                    foreach (var line in entry.PrevTafRaw.Split('\n'))
                        Write("  " + line.TrimEnd() + "\r\n", ColGrey);
                }
            }
            else
            {
                Write("  (No TAF)\r\n", ColStale);
            }

            _spans.Add(new EntrySpan(entry, spanStart, _display.TextLength - 1, isMetar: false));
        }

        private void RenderAtis(MonitorEntry entry)
        {
            bool yellow = entry.AtisState == EntryState.Yellow;
            var  col    = yellow ? ColYellow : ColBlue;

            int spanStart = _display.TextLength;
            Write("ATIS   " + entry.Icao, col, MonoBold);
            if (yellow) Write("  ●", ColYellow, MonoBold);
            var we = _cache.Get(entry.Icao);
            if (we?.AtisSource != null) Write($"  {we.AtisSource}", ColGrey, UiFont);
            if (we?.AtisTimestamp != default(DateTime))
                Write($"  {we.AtisTimestamp:HH:mm}Z", ColGrey, UiFont);
            Write("\r\n", col);

            if (!string.IsNullOrEmpty(entry.AtisRaw))
            {
                foreach (var line in entry.AtisRaw.Split('\n'))
                {
                    var trimmed = line.TrimEnd();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    Write("  " + trimmed + "\r\n", ColDetailTxt, MonoFont);
                }

                if (!string.IsNullOrEmpty(entry.PrevAtisRaw))
                {
                    Write("  Previous ATIS:\r\n", ColGrey, UiFont);
                    foreach (var line in entry.PrevAtisRaw.Split('\n'))
                        Write("  " + line.TrimEnd() + "\r\n", ColGrey);
                }
            }
            else
            {
                Write("  (No ATIS — NAIPS credentials required)\r\n", ColStale);
            }

            _spans.Add(new EntrySpan(entry, spanStart, _display.TextLength - 1, isMetar: false, isAtis: true));
        }

        private void Write(string text, Color color, Font font = null)
        {
            _display.SelectionStart  = _display.TextLength;
            _display.SelectionLength = 0;
            _display.SelectionColor  = color;
            _display.SelectionFont   = font ?? MonoFont;
            _display.AppendText(text);
        }

        private void SetStatus(string msg) => SafeUi(() => _statusLabel.Text = msg);

        private void SafeUi(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(a);
            else a();
        }

        // Hide instead of close so state is preserved
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _metarTimer?.Dispose();
            _tafTimer?.Dispose();
            _countdownTimer?.Stop();
            base.OnFormClosed(e);
        }

        // ── Helper types ──────────────────────────────────────────────────────

        private class EntrySpan
        {
            public MonitorEntry Entry   { get; }
            public int          Start   { get; }
            public int          End     { get; }
            public bool         IsMetar { get; }
            public bool         IsAtis  { get; }

            public EntrySpan(MonitorEntry entry, int start, int end, bool isMetar, bool isAtis = false)
            {
                Entry = entry; Start = start; End = end; IsMetar = isMetar; IsAtis = isAtis;
            }
        }

        // RichTextBox subclass that exposes middle-click via an event
        private class ClickableRtb : RichTextBox
        {
            public event Action<Point> MiddleClick;

            private const int WM_MBUTTONDOWN = 0x0207;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_MBUTTONDOWN)
                {
                    int x = m.LParam.ToInt32() & 0xFFFF;
                    int y = (m.LParam.ToInt32() >> 16) & 0xFFFF;
                    MiddleClick?.Invoke(new Point(x, y));
                    return; // swallow so base doesn't scroll
                }
                base.WndProc(ref m);
            }
        }
    }
}
