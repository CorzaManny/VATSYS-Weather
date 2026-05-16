using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeatherPlugin.Models;
using WeatherPlugin.Services;

namespace WeatherPlugin.UI
{
    public class ResultWindow : Form
    {
        // vatSys chrome
        private static readonly Color ColBg       = Color.FromArgb(160, 170, 170);
        private static readonly Color ColPanel    = Color.FromArgb(148, 158, 158);
        private static readonly Color ColBtn      = Color.FromArgb(140, 152, 152);
        private static readonly Color ColBtnBdr   = Color.FromArgb(90,  102, 102);
        private static readonly Color ColDetailBg = Color.FromArgb(148, 158, 158);
        private static readonly Color ColYellow   = Color.FromArgb(255, 200,  60);
        private static readonly Color ColBlue     = Color.FromArgb(100, 160, 255);
        private static readonly Color ColGrey     = Color.FromArgb(200, 210, 210);
        private static readonly Color ColStale    = Color.FromArgb(255, 140,  40);
        private static readonly Color ColSep      = Color.FromArgb(110, 120, 120);
        private static readonly Color ColDetailTxt= Color.White;

        // NATO MIL flight category colours
        private static readonly Color ColVfr  = Color.FromArgb(0,   210,   0);
        private static readonly Color ColMvfr = Color.FromArgb(80,  140, 255);
        private static readonly Color ColIfr  = Color.FromArgb(220,  50,  50);
        private static readonly Color ColLifr = Color.FromArgb(220,  60, 220);

        private static readonly Font MonoFont = new Font("Courier New", 8f, FontStyle.Regular);
        private static readonly Font MonoBold = new Font("Courier New", 8f, FontStyle.Bold);
        private static readonly Font UiFont   = new Font("Arial",       8f, FontStyle.Regular);
        private static readonly Font UiBold   = new Font("Arial",       8f, FontStyle.Bold);

        private const int RefreshIntervalMs = 60_000;

        // ── State ─────────────────────────────────────────────────────────────
        private readonly WeatherCache        _cache;
        private readonly List<MonitorEntry>  _entries = new List<MonitorEntry>();
        private readonly List<EntrySpan>     _spans   = new List<EntrySpan>();

        private System.Timers.Timer        _refreshTimer;
        private System.Windows.Forms.Timer _tickTimer;
        private DateTime _nextRefresh;
        private bool     _fetching;
        private bool     _flashOn;

        // ── Controls ──────────────────────────────────────────────────────────
        private Label       _statusLabel;
        private Label       _countdownLabel;
        private Button      _refreshNowBtn;
        private RichTextBox _display;

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

            var toolbar = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = ColPanel };

            int x = 6, y = 4;

            _refreshNowBtn = Btn("Refresh All", x, y);
            _refreshNowBtn.Click += async (s, e) => await RefreshAll();
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

            _display = new RichTextBox
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
            _display.MouseClick += OnDisplayClick;

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
            _nextRefresh = DateTime.UtcNow.AddMilliseconds(RefreshIntervalMs);

            _refreshTimer = new System.Timers.Timer(RefreshIntervalMs) { AutoReset = true };
            _refreshTimer.Elapsed += (s, e) => _ = RefreshAll();
            _refreshTimer.Start();

            _tickTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _tickTimer.Tick += (s, e) => Tick();
            _tickTimer.Start();
        }

        private async Task RefreshAll()
        {
            if (_fetching) return;
            var stations = _entries.Select(e => e.Icao).ToList();
            if (stations.Count == 0) return;

            _fetching = true;
            _nextRefresh = DateTime.UtcNow.AddMilliseconds(RefreshIntervalMs);
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

                        if (entry.WatchMetar)
                        {
                            var now = we.RawMetar ?? "";
                            if (entry.MetarRaw != null && entry.MetarRaw != now && now != "")
                            {
                                entry.PrevMetarRaw = entry.MetarRaw;
                                entry.MetarState   = EntryState.Yellow;
                            }
                            entry.MetarRaw = now;
                        }

                        if (entry.WatchTaf)
                        {
                            var now = we.RawTaf ?? "";
                            if (entry.TafRaw != null && entry.TafRaw != now && now != "")
                            {
                                entry.PrevTafRaw = entry.TafRaw;
                                entry.TafState   = EntryState.Yellow;
                            }
                            entry.TafRaw = now;
                        }

                        if (entry.WatchAtis)
                        {
                            var now = we.RawAtis ?? "";
                            if (entry.AtisRaw != null && entry.AtisRaw != now && now != "")
                            {
                                entry.PrevAtisRaw = entry.AtisRaw;
                                entry.AtisState   = EntryState.Yellow;
                            }
                            entry.AtisRaw = now;
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

        private void Tick()
        {
            _flashOn = !_flashOn;

            var rem = _nextRefresh - DateTime.UtcNow;
            if (rem < TimeSpan.Zero) rem = TimeSpan.Zero;

            SafeUi(() =>
            {
                _countdownLabel.Text = $"Next: {(int)rem.TotalMinutes}:{rem.Seconds:D2}";

                bool anyFlashing = _entries.Any(e =>
                    (e.WatchMetar && e.MetarState == EntryState.Yellow) ||
                    (e.WatchTaf   && e.TafState   == EntryState.Yellow) ||
                    (e.WatchAtis  && e.AtisState  == EntryState.Yellow));

                if (anyFlashing) Render();
            });
        }

        // ── Click to acknowledge ──────────────────────────────────────────────

        private void OnDisplayClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            int charIdx;
            try { charIdx = _display.GetCharIndexFromPosition(e.Location); }
            catch { return; }

            foreach (var span in _spans)
            {
                if (charIdx < span.Start || charIdx > span.End) continue;

                if (span.IsAtis)        span.Entry.AtisState  = EntryState.Blue;
                else if (span.IsMetar)  span.Entry.MetarState = EntryState.Blue;
                else                    span.Entry.TafState   = EntryState.Blue;

                Render();
                return;
            }
        }

        // ── Flight category helpers ───────────────────────────────────────────

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

        // Returns the colour for a section header — yellow flashes, blue is static.
        private Color StateColor(EntryState state) =>
            state == EntryState.Blue ? ColBlue
                                     : (_flashOn ? ColYellow : ColDetailTxt);

        // ── Rendering ─────────────────────────────────────────────────────────

        private void Render()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke((Action)Render); return; }

            _spans.Clear();
            _display.Clear();

            if (_entries.Count == 0)
            {
                Write("No stations monitored.\r\nUse the AU Weather window to request a station.\r\n", ColGrey, UiFont);
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
            var stateCol = StateColor(entry.MetarState);
            var cat      = MetarDecoder.Classify(entry.MetarRaw ?? "");
            var catLabel = CategoryLabel(cat);

            int spanStart = _display.TextLength;

            Write("METAR  " + entry.Icao, stateCol, MonoBold);
            if (entry.MetarState == EntryState.Yellow)
                Write("  ●", _flashOn ? ColYellow : ColDetailTxt, MonoBold);
            if (catLabel != "") Write($"  [{catLabel}]", CategoryColor(cat), UiBold);
            var obsTime = MetarDecoder.ParseObsTime(entry.MetarRaw ?? "");
            if (obsTime != null) Write($"  {obsTime}", ColGrey, UiFont);
            var src = _cache.Get(entry.Icao)?.MetarSource;
            if (src != null) Write($"  {src}", ColGrey, UiFont);
            Write("\r\n", stateCol);

            if (!string.IsNullOrEmpty(entry.MetarRaw))
            {
                var tokens = MetarDecoder.TokenizeForDisplay(entry.MetarRaw);
                for (int t = 0; t < tokens.Count; t++)
                    Write(tokens[t].Text + (t < tokens.Count - 1 ? " " : ""), ElementColor(tokens[t].Cat));
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
            var stateCol = StateColor(entry.TafState);

            int spanStart = _display.TextLength;
            Write("TAF    " + entry.Icao, stateCol, MonoBold);
            if (entry.TafState == EntryState.Yellow)
                Write("  ●", _flashOn ? ColYellow : ColDetailTxt, MonoBold);
            var src = _cache.Get(entry.Icao)?.TafSource;
            if (src != null) Write($"  {src}", ColGrey, UiFont);
            Write("\r\n", stateCol);

            if (!string.IsNullOrEmpty(entry.TafRaw))
            {
                foreach (var line in entry.TafRaw.Split('\n'))
                {
                    var trimmed = line.TrimEnd();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    var tokens = MetarDecoder.TokenizeForDisplay(trimmed);
                    for (int t = 0; t < tokens.Count; t++)
                        Write(tokens[t].Text + (t < tokens.Count - 1 ? " " : ""), ElementColor(tokens[t].Cat));
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
            var stateCol = StateColor(entry.AtisState);

            int spanStart = _display.TextLength;
            Write("ATIS   " + entry.Icao, stateCol, MonoBold);
            if (entry.AtisState == EntryState.Yellow)
                Write("  ●", _flashOn ? ColYellow : ColDetailTxt, MonoBold);
            var we = _cache.Get(entry.Icao);
            if (we?.AtisSource != null) Write($"  {we.AtisSource}", ColGrey, UiFont);
            if (we != null && we.AtisTimestamp != default(DateTime))
                Write($"  {we.AtisTimestamp:HH:mm}Z", ColGrey, UiFont);
            Write("\r\n", stateCol);

            if (!string.IsNullOrEmpty(entry.AtisRaw))
            {
                foreach (var line in entry.AtisRaw.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    Write(trimmed + "\r\n", ColDetailTxt, MonoFont);
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
            _refreshTimer?.Dispose();
            _tickTimer?.Stop();
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
    }
}
