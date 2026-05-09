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
        // vatSys colours
        private static readonly Color ColBg       = Color.FromArgb(160, 170, 170);
        private static readonly Color ColPanel    = Color.FromArgb(130, 146, 146);
        private static readonly Color ColBtn      = Color.FromArgb(130, 146, 146);
        private static readonly Color ColBtnBdr   = Color.FromArgb(80, 90, 90);
        private static readonly Color ColDetailBg = Color.FromArgb(20, 22, 28);
        private static readonly Color ColYellow   = Color.FromArgb(255, 200, 60);
        private static readonly Color ColBlue     = Color.FromArgb(100, 160, 255);
        private static readonly Color ColGrey     = Color.FromArgb(120, 130, 120);
        private static readonly Color ColStale    = Color.FromArgb(160, 100, 40);
        private static readonly Color ColSep      = Color.FromArgb(55, 65, 55);
        private static readonly Color ColDetailTxt= Color.FromArgb(200, 215, 200);

        // Flight category colours (standard aviation weather map palette)
        private static readonly Color ColVfr  = Color.FromArgb(60,  200, 80);
        private static readonly Color ColMvfr = Color.FromArgb(90,  140, 230);
        private static readonly Color ColSvfr = Color.FromArgb(255, 140, 0);
        private static readonly Color ColIfr  = Color.FromArgb(220, 50,  50);
        private static readonly Color ColLifr = Color.FromArgb(210, 60,  210);

        private static readonly Font MonoFont = new Font("Courier New", 9f,  FontStyle.Regular);
        private static readonly Font MonoBold = new Font("Courier New", 9f,  FontStyle.Bold);
        private static readonly Font UiFont   = new Font("Arial",       8.5f, FontStyle.Regular);
        private static readonly Font UiBold   = new Font("Arial",       8.5f, FontStyle.Bold);

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
            Size            = new Size(760, 520);
            MinimumSize     = new Size(500, 280);
            BackColor       = ColBg;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition   = FormStartPosition.Manual;
            Font            = UiFont;

            var toolbar = new Panel
            {
                Dock = DockStyle.Top, Height = 30, BackColor = ColPanel,
            };

            int x = 6, y = 4;

            _refreshNowBtn = Btn("Refresh All", x, y);
            _refreshNowBtn.Click += async (s, e) => await RefreshAll(metar: true, taf: true);
            x += _refreshNowBtn.Width + 10;

            _statusLabel = new Label
            {
                Left = x, Top = y + 4, AutoSize = true,
                ForeColor = Color.FromArgb(30, 30, 30), Font = UiFont,
                Text = "No stations",
            };
            x += 220;

            _countdownLabel = new Label
            {
                Left = x, Top = y + 4, AutoSize = true,
                ForeColor = Color.FromArgb(0, 0, 96), Font = UiBold, Text = "",
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

        public void AddStation(string icao, bool watchMetar, bool watchTaf)
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
                    MetarState = EntryState.Yellow,
                    TafState   = EntryState.Yellow,
                };
                _entries.Add(entry);
            }
            else
            {
                if (watchMetar) entry.WatchMetar = true;
                if (watchTaf)   entry.WatchTaf   = true;
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
            var stations = _entries.Where(e => (metar && e.WatchMetar) || (taf && e.WatchTaf))
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

                if (span.IsMetar)
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
                // Blue → remove METAR watch; if also not watching TAF, remove entry
                entry.WatchMetar = false;
                if (!entry.WatchTaf)
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
                if (!entry.WatchMetar)
                    _entries.Remove(entry);
            }
        }

        // ── Flight category helpers ───────────────────────────────────────────

        private static Color CategoryColor(FlightCategory cat)
        {
            switch (cat)
            {
                case FlightCategory.VFR:  return ColVfr;
                case FlightCategory.MVFR: return ColMvfr;
                case FlightCategory.SVFR: return ColSvfr;
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
                case FlightCategory.SVFR: return "SVFR";
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

                Write("\r\n" + new string('─', 100) + "\r\n\r\n", ColSep);
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

            // Body uses category colour when acknowledged (Blue), Yellow when unread
            var bodyCol = yellow ? ColYellow : (cat != FlightCategory.Unknown ? catCol : ColBlue);

            int spanStart = _display.TextLength;

            // Header: ICAO + update dot (state colour) + [CATEGORY] badge (category colour) + obs time
            Write("METAR  " + entry.Icao, stateCol, MonoBold);
            if (yellow) Write("  ●", ColYellow, MonoBold);
            if (catLabel != "") Write($"  [{catLabel}]", catCol, UiBold);
            var obsTime = MetarDecoder.ParseObsTime(entry.MetarRaw ?? "");
            if (obsTime != null) Write($"  {obsTime}", ColGrey, UiFont);
            Write("\r\n", stateCol);

            // Body
            if (!string.IsNullOrEmpty(entry.MetarRaw))
            {
                Write(entry.MetarRaw + "\r\n", bodyCol);
                if (!string.IsNullOrEmpty(entry.PrevMetarRaw))
                    Write("  Previous: " + entry.PrevMetarRaw + "\r\n", ColGrey, UiFont);
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
            Write("\r\n", col);

            if (!string.IsNullOrEmpty(entry.TafRaw))
            {
                foreach (var line in entry.TafRaw.Split('\n'))
                    Write(line.TrimEnd() + "\r\n", col);
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

            public EntrySpan(MonitorEntry entry, int start, int end, bool isMetar)
            {
                Entry = entry; Start = start; End = end; IsMetar = isMetar;
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
