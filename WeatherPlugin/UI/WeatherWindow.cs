using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using vatsys;
using WeatherPlugin.Models;
using WeatherPlugin.Services;

namespace WeatherPlugin.UI
{
    public class WeatherWindow : BaseForm
    {
        private enum WeatherTab { Metar, Taf, Atis }

        // ── Fonts ─────────────────────────────────────────────────────────
        private static readonly Font TermFont  = new Font("Terminus (TTF)", 18F, FontStyle.Bold, GraphicsUnit.Pixel);
        private static readonly Font TermSmall = new Font("Terminus (TTF)", 16F, FontStyle.Bold, GraphicsUnit.Pixel);
        private static readonly Font MonoFont  = new Font("Terminus (TTF)", 18F, FontStyle.Bold, GraphicsUnit.Pixel);
        private static readonly Font MonoBold  = new Font("Terminus (TTF)", 20F, FontStyle.Bold, GraphicsUnit.Pixel);

        // ── Flight-category colours (NOAA standard) ───────────────────────
        private static readonly Color ColVfr  = Color.FromArgb(0,   130, 0);
        private static readonly Color ColMvfr = Color.FromArgb(0,   60,  200);
        private static readonly Color ColIfr  = Color.FromArgb(180, 0,   0);
        private static readonly Color ColLifr = Color.FromArgb(160, 0,   160);

        // ── Misc colours ──────────────────────────────────────────────────
        private static readonly Color ColStale    = Color.FromArgb(140, 50,  0);
        private static readonly Color ColTabOn    = Color.FromArgb(0, 0, 96);
        private static readonly Color ColDisplayBg = Color.FromArgb(160, 170, 170);
        private static readonly Color ColDisplayFg = Color.FromArgb(0, 0, 96);
        private static readonly Color ColInputBg   = Color.FromArgb(160, 170, 170);
        private static readonly Color ColInputFg   = Color.FromArgb(0, 0, 96);
        private static readonly Color ColDimText   = Color.FromArgb(60, 60, 90);
        private static readonly Color ColListSelBg = Color.FromArgb(120, 130, 135);

        // ── Timing ────────────────────────────────────────────────────────
        private const int MetarIntervalMs = 60_000;
        private const int TafIntervalMs   = 300_000;

        // ── Controls ──────────────────────────────────────────────────────
        private TextBox       _icaoBox;
        private GenericButton _searchBtn;
        private GenericButton _refreshBtn;
        private GenericButton _removeBtn;
        private GenericButton _metarBtn;
        private GenericButton _tafBtn;
        private GenericButton _atisBtn;
        private ListBox       _stationList;
        private RichTextBox   _display;
        private TextLabel     _statusLabel;
        private TextLabel     _countdownLabel;

        // ── State ─────────────────────────────────────────────────────────
        private readonly WeatherCache       _cache;
        private readonly List<MonitorEntry> _entries = new List<MonitorEntry>();
        private WeatherTab _activeTab    = WeatherTab.Metar;
        private string     _selectedIcao;

        // ── Timers ────────────────────────────────────────────────────────
        private System.Timers.Timer        _metarTimer;
        private System.Timers.Timer        _tafTimer;
        private System.Windows.Forms.Timer _countdownTimer;
        private DateTime _nextMetarRefresh;
        private DateTime _nextTafRefresh;
        private bool     _fetching;

        // ── Singleton ─────────────────────────────────────────────────────
        private static WeatherWindow _instance;

        internal static void ShowWindow(WeatherCache cache)
        {
            if (_instance == null || _instance.IsDisposed)
                _instance = new WeatherWindow(cache);

            _instance.Show();
            _instance.TopMost = true;
            _instance.BringToFront();
            _instance.Activate();
        }

        public WeatherWindow(WeatherCache cache)
        {
            _cache = cache;
            BuildUi();
            StartTimers();
        }

        // ── UI Construction ───────────────────────────────────────────────

        private void BuildUi()
        {
            SuspendLayout();

            var bg = Colours.GetColour(Colours.Identities.WindowBackground);
            var fg = Colours.GetColour(Colours.Identities.InteractiveText);

            Text            = "AU Weather";
            ClientSize      = new Size(620, 420);
            MinimumSize     = new Size(480, 280);
            HasMinimizeButton = false;
            Resizeable      = true;
            TopMost         = true;
            StartPosition   = FormStartPosition.CenterScreen;
            BackColor       = bg;

            // ── Top bar ─────────────────────────────────────────────────
            var topBar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = bg };

            _icaoBox = new TextBox
            {
                Location        = new Point(8, 5),
                Width           = 62,
                Height          = 24,
                MaxLength       = 4,
                CharacterCasing = CharacterCasing.Upper,
                Font            = TermFont,
                BackColor       = ColInputBg,
                ForeColor       = ColInputFg,
                BorderStyle     = BorderStyle.FixedSingle,
            };
            _icaoBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { DoSearch(); e.SuppressKeyPress = true; }
            };

            _searchBtn = MakeButton("Search", new Point(78, 4), new Size(80, 26));
            _searchBtn.Click += (s, e) => DoSearch();

            _refreshBtn = MakeButton("Refresh", new Point(166, 4), new Size(84, 26));
            _refreshBtn.Click += async (s, e) => await RefreshAll();

            _countdownLabel = new TextLabel
            {
                Location        = new Point(260, 8),
                AutoSize        = true,
                Font            = TermSmall,
                ForeColor       = fg,
                HasBorder       = false,
                InteractiveText = false,
                Text            = "",
            };

            topBar.Controls.AddRange(new Control[] { _icaoBox, _searchBtn, _refreshBtn, _countdownLabel });

            // ── Tab bar ─────────────────────────────────────────────────
            var tabBar = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = bg };

            _metarBtn = MakeButton("METAR", new Point(8, 2), new Size(80, 24));
            _metarBtn.Click += (s, e) => SetActiveTab(WeatherTab.Metar);

            _tafBtn = MakeButton("TAF", new Point(94, 2), new Size(70, 24));
            _tafBtn.Click += (s, e) => SetActiveTab(WeatherTab.Taf);

            _atisBtn = MakeButton("ATIS", new Point(170, 2), new Size(70, 24));
            _atisBtn.Click += (s, e) => SetActiveTab(WeatherTab.Atis);

            _removeBtn = MakeButton("Remove", new Point(256, 2), new Size(80, 24));
            _removeBtn.Font = TermSmall;
            _removeBtn.Click += (s, e) => RemoveSelected();

            tabBar.Controls.AddRange(new Control[] { _metarBtn, _tafBtn, _atisBtn, _removeBtn });

            // ── Station list (left side) ────────────────────────────────
            _stationList = new ListBox
            {
                Dock           = DockStyle.Left,
                Width          = 80,
                Font           = TermFont,
                BackColor      = ColDisplayBg,
                ForeColor      = ColDisplayFg,
                BorderStyle    = BorderStyle.FixedSingle,
                IntegralHeight = false,
                DrawMode       = DrawMode.OwnerDrawFixed,
                ItemHeight     = 22,
            };
            _stationList.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                var isSelected = (e.State & DrawItemState.Selected) != 0;
                var bgc = isSelected ? ColListSelBg : ColDisplayBg;
                var fgc = isSelected ? Color.White : ColDisplayFg;
                e.Graphics.FillRectangle(new SolidBrush(bgc), e.Bounds);
                TextRenderer.DrawText(e.Graphics, _stationList.Items[e.Index].ToString(),
                    _stationList.Font, e.Bounds, fgc, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            _stationList.SelectedIndexChanged += (s, e) =>
            {
                if (_stationList.SelectedItem is string icao)
                {
                    _selectedIcao = icao;
                    RenderContent();
                }
            };

            // ── Display area ────────────────────────────────────────────
            _display = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                ReadOnly    = true,
                BackColor   = ColDisplayBg,
                ForeColor   = ColDisplayFg,
                Font        = MonoFont,
                WordWrap    = true,
                BorderStyle = BorderStyle.None,
                ScrollBars  = RichTextBoxScrollBars.Vertical,
            };

            // ── Status bar ──────────────────────────────────────────────
            var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 24, BackColor = bg };

            _statusLabel = new TextLabel
            {
                Location        = new Point(8, 4),
                AutoSize        = true,
                Font            = TermSmall,
                ForeColor       = fg,
                HasBorder       = false,
                InteractiveText = false,
                Text            = "Enter an ICAO code to search",
            };

            statusBar.Controls.Add(_statusLabel);

            // ── Assembly order (last-added docks first) ─────────────────
            Controls.Add(_display);
            Controls.Add(_stationList);
            Controls.Add(tabBar);
            Controls.Add(topBar);
            Controls.Add(statusBar);

            UpdateTabHighlight();

            ResumeLayout(false);
            PerformLayout();
        }

        private GenericButton MakeButton(string text, Point location, Size size)
        {
            var bg = Colours.GetColour(Colours.Identities.WindowBackground);
            var fg = Colours.GetColour(Colours.Identities.InteractiveText);
            return new GenericButton
            {
                Text      = text,
                Location  = location,
                Size      = size,
                Font      = TermFont,
                SubText   = "",
                SubFont   = new Font("Microsoft Sans Serif", 8.25F),
                BackColor = bg,
                ForeColor = fg,
            };
        }

        // ── Tab management ────────────────────────────────────────────────

        private void SetActiveTab(WeatherTab tab)
        {
            _activeTab = tab;
            UpdateTabHighlight();
            RenderContent();
        }

        private void UpdateTabHighlight()
        {
            var fg = Colours.GetColour(Colours.Identities.InteractiveText);
            _metarBtn.ForeColor = _activeTab == WeatherTab.Metar ? ColTabOn : fg;
            _tafBtn.ForeColor   = _activeTab == WeatherTab.Taf   ? ColTabOn : fg;
            _atisBtn.ForeColor  = _activeTab == WeatherTab.Atis  ? ColTabOn : fg;
        }

        // ── Search ────────────────────────────────────────────────────────

        private void DoSearch()
        {
            var icao = _icaoBox.Text.Trim().ToUpper();
            if (icao.Length != 4)
            {
                SetStatus("Enter a 4-letter ICAO code.");
                return;
            }

            var entry = _entries.FirstOrDefault(e => e.Icao == icao);
            if (entry == null)
            {
                entry = new MonitorEntry
                {
                    Icao       = icao,
                    WatchMetar = true,
                    WatchTaf   = true,
                    WatchAtis  = true,
                    MetarState = EntryState.Yellow,
                    TafState   = EntryState.Yellow,
                    AtisState  = EntryState.Yellow,
                };
                _entries.Add(entry);
                _stationList.Items.Add(icao);
            }

            _stationList.SelectedItem = icao;
            _selectedIcao = icao;
            _icaoBox.Clear();
            _icaoBox.Focus();

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
                    RenderContent();
                    SetStatus($"{_entries.Count} station(s) | Updated {DateTime.UtcNow:HH:mm}Z");
                });
            });
        }

        // ── Remove station ────────────────────────────────────────────────

        private void RemoveSelected()
        {
            if (_selectedIcao == null) return;

            var entry = _entries.FirstOrDefault(e => e.Icao == _selectedIcao);
            if (entry != null) _entries.Remove(entry);

            _stationList.Items.Remove(_selectedIcao);
            _selectedIcao = _stationList.Items.Count > 0
                ? _stationList.Items[0] as string
                : null;

            if (_selectedIcao != null)
                _stationList.SelectedItem = _selectedIcao;

            RenderContent();
            SetStatus($"{_entries.Count} station(s)");
        }

        // ── Rendering ─────────────────────────────────────────────────────

        private void RenderContent()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke((Action)RenderContent); return; }

            _display.Clear();

            if (_selectedIcao == null || _entries.Count == 0)
            {
                Write("No station selected.\nSearch for an ICAO code above.", ColDimText);
                return;
            }

            var entry = _entries.FirstOrDefault(e => e.Icao == _selectedIcao);
            if (entry == null) return;

            switch (_activeTab)
            {
                case WeatherTab.Metar: RenderMetar(entry); break;
                case WeatherTab.Taf:   RenderTaf(entry);   break;
                case WeatherTab.Atis:  RenderAtis(entry);  break;
            }

            _display.SelectionStart = 0;
            _display.ScrollToCaret();
        }

        private void RenderMetar(MonitorEntry entry)
        {
            var cat      = MetarDecoder.Classify(entry.MetarRaw ?? "");
            var catCol   = CategoryColor(cat);
            var catLabel = CategoryLabel(cat);

            Write($"METAR  {entry.Icao}", ColDisplayFg, MonoBold);
            if (catLabel != "") Write($"  [{catLabel}]", catCol, MonoBold);
            var obsTime  = MetarDecoder.ParseObsTime(entry.MetarRaw ?? "");
            if (obsTime  != null) Write($"  {obsTime}", ColDimText);
            var metarSrc = _cache.Get(entry.Icao)?.MetarSource;
            if (metarSrc != null) Write($"  ({metarSrc})", ColDimText);
            Write("\n\n", ColDisplayFg);

            if (!string.IsNullOrEmpty(entry.MetarRaw))
            {
                var tokens = MetarDecoder.TokenizeForDisplay(entry.MetarRaw);
                foreach (var token in tokens)
                    Write(token.Text + " ", ElementColor(token.Cat, ColDisplayFg));

                if (!string.IsNullOrEmpty(entry.PrevMetarRaw))
                {
                    Write("\n\nPrevious:\n", ColDimText);
                    Write(entry.PrevMetarRaw, ColDimText);
                }
            }
            else
            {
                Write("No METAR available.", ColStale);
            }
        }

        private void RenderTaf(MonitorEntry entry)
        {
            var tafSrc = _cache.Get(entry.Icao)?.TafSource;

            Write($"TAF  {entry.Icao}", ColDisplayFg, MonoBold);
            if (tafSrc != null) Write($"  ({tafSrc})", ColDimText);
            Write("\n\n", ColDisplayFg);

            if (!string.IsNullOrEmpty(entry.TafRaw))
            {
                foreach (var line in entry.TafRaw.Split('\n'))
                {
                    var trimmed = line.TrimEnd();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    var tokens = MetarDecoder.TokenizeForDisplay(trimmed);
                    foreach (var token in tokens)
                        Write(token.Text + " ", ElementColor(token.Cat, ColDisplayFg));
                    Write("\n", ColDisplayFg);
                }

                if (!string.IsNullOrEmpty(entry.PrevTafRaw))
                {
                    Write("\nPrevious TAF:\n", ColDimText);
                    foreach (var line in entry.PrevTafRaw.Split('\n'))
                        Write(line.TrimEnd() + "\n", ColDimText);
                }
            }
            else
            {
                Write("No TAF available.", ColStale);
            }
        }

        private void RenderAtis(MonitorEntry entry)
        {
            var we = _cache.Get(entry.Icao);

            Write($"ATIS  {entry.Icao}", ColDisplayFg, MonoBold);
            if (we?.AtisSource != null) Write($"  ({we.AtisSource})", ColDimText);
            if (we?.AtisTimestamp != default(DateTime))
                Write($"  {we.AtisTimestamp:HH:mm}Z", ColDimText);
            Write("\n\n", ColDisplayFg);

            if (!string.IsNullOrEmpty(entry.AtisRaw))
            {
                foreach (var line in entry.AtisRaw.Split('\n'))
                {
                    var trimmed = line.TrimEnd();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    Write(trimmed + "\n", ColDisplayFg, MonoFont);
                }

                if (!string.IsNullOrEmpty(entry.PrevAtisRaw))
                {
                    Write("\nPrevious ATIS:\n", ColDimText);
                    foreach (var line in entry.PrevAtisRaw.Split('\n'))
                        Write(line.TrimEnd() + "\n", ColDimText);
                }
            }
            else
            {
                Write("No ATIS available.\nNAIPS credentials may be required.", ColStale);
            }
        }

        // ── Rich text helpers ─────────────────────────────────────────────

        private void Write(string text, Color color, Font font = null)
        {
            _display.SelectionStart  = _display.TextLength;
            _display.SelectionLength = 0;
            _display.SelectionColor  = color;
            _display.SelectionFont   = font ?? MonoFont;
            _display.AppendText(text);
        }

        // ── Category helpers ──────────────────────────────────────────────

        private static Color CategoryColor(FlightCategory cat)
        {
            switch (cat)
            {
                case FlightCategory.VFR:  return ColVfr;
                case FlightCategory.MVFR: return ColMvfr;
                case FlightCategory.IFR:  return ColIfr;
                case FlightCategory.LIFR: return ColLifr;
                default:                  return Color.Gray;
            }
        }

        private static Color ElementColor(FlightCategory? cat, Color defaultColor)
        {
            if (cat == null)               return defaultColor;
            if (cat == FlightCategory.VFR) return defaultColor;
            return CategoryColor(cat.Value);
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

        // ── Timers ────────────────────────────────────────────────────────

        private void StartTimers()
        {
            _nextMetarRefresh = DateTime.UtcNow.AddMilliseconds(MetarIntervalMs);
            _nextTafRefresh   = DateTime.UtcNow.AddMilliseconds(TafIntervalMs);

            _metarTimer = new System.Timers.Timer(MetarIntervalMs) { AutoReset = true };
            _metarTimer.Elapsed += (s, e) => _ = RefreshAll();
            _metarTimer.Start();

            _tafTimer = new System.Timers.Timer(TafIntervalMs) { AutoReset = true };
            _tafTimer.Elapsed += (s, e) => _ = RefreshAll();
            _tafTimer.Start();

            _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _countdownTimer.Tick += (s, e) => TickCountdown();
            _countdownTimer.Start();
        }

        private async Task RefreshAll()
        {
            if (_fetching) return;
            var stations = _entries.Select(e => e.Icao).ToList();
            if (stations.Count == 0) return;

            _fetching = true;
            _nextMetarRefresh = DateTime.UtcNow.AddMilliseconds(MetarIntervalMs);
            _nextTafRefresh   = DateTime.UtcNow.AddMilliseconds(TafIntervalMs);

            SafeUi(() => SetStatus("Refreshing…"));

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

                        var nowMetar = we.RawMetar ?? "";
                        if (entry.MetarRaw != null && entry.MetarRaw != nowMetar && nowMetar != "")
                        {
                            entry.PrevMetarRaw = entry.MetarRaw;
                            entry.MetarState   = EntryState.Yellow;
                        }
                        entry.MetarRaw = nowMetar;

                        var nowTaf = we.RawTaf ?? "";
                        if (entry.TafRaw != null && entry.TafRaw != nowTaf && nowTaf != "")
                        {
                            entry.PrevTafRaw = entry.TafRaw;
                            entry.TafState   = EntryState.Yellow;
                        }
                        entry.TafRaw = nowTaf;

                        var nowAtis = we.RawAtis ?? "";
                        if (entry.AtisRaw != null && entry.AtisRaw != nowAtis && nowAtis != "")
                        {
                            entry.PrevAtisRaw = entry.AtisRaw;
                            entry.AtisState   = EntryState.Yellow;
                        }
                        entry.AtisRaw = nowAtis;
                    }

                    RenderContent();
                    SetStatus($"{_entries.Count} station(s) | Updated {DateTime.UtcNow:HH:mm}Z");
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
                $"M {rm.Minutes}:{rm.Seconds:D2}  T {rt.Minutes}:{rt.Seconds:D2}");
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private void SetStatus(string msg)
        {
            if (IsDisposed || !IsHandleCreated) return;
            _statusLabel.Text = msg;
        }

        private void SafeUi(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(a);
            else a();
        }

        // ── Form lifecycle ────────────────────────────────────────────────

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
    }
}
