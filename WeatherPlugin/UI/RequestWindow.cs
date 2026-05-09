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
    public class RequestWindow : Form
    {
        // vatSys colours
        private static readonly Color ColBg       = Color.FromArgb(160, 170, 170);
        private static readonly Color ColPanel    = Color.FromArgb(130, 146, 146);
        private static readonly Color ColBtn      = Color.FromArgb(130, 146, 146);
        private static readonly Color ColBtnBdr   = Color.FromArgb(80, 90, 90);
        private static readonly Color ColDetailBg = Color.FromArgb(20, 22, 28);
        private static readonly Color ColDetailTxt= Color.FromArgb(200, 215, 200);
        private static readonly Color ColIcao     = Color.FromArgb(180, 210, 255);
        private static readonly Color ColUpdated  = Color.FromArgb(255, 200, 60);
        private static readonly Color ColStale    = Color.FromArgb(160, 100, 40);
        private static readonly Color ColSep      = Color.FromArgb(55, 65, 55);
        private static readonly Color ColGrey     = Color.FromArgb(100, 110, 100);

        private static readonly Font MonoFont = new Font("Courier New", 9f,  FontStyle.Regular);
        private static readonly Font MonoBold = new Font("Courier New", 9f,  FontStyle.Bold);
        private static readonly Font UiFont   = new Font("Arial",       8.5f, FontStyle.Regular);
        private static readonly Font UiBold   = new Font("Arial",       8.5f, FontStyle.Bold);

        private const int RefreshMinutes = 5;

        // ── State ─────────────────────────────────────────────────────────────
        private readonly WeatherCache _cache;
        private readonly List<string> _watchlist = new List<string>();

        // Previous raw text per ICAO for change detection
        private readonly Dictionary<string, string> _prevMetar =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _prevTaf =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _metarChanged =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _tafChanged =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private System.Timers.Timer         _refreshTimer;
        private System.Windows.Forms.Timer  _countdownTimer;
        private DateTime _nextRefresh;
        private bool     _fetching;

        // ── Controls ──────────────────────────────────────────────────────────
        private TextBox     _addBox;
        private Button      _addBtn;
        private Button      _refreshNowBtn;
        private Label       _statusLabel;
        private Label       _countdownLabel;
        private RichTextBox _display;

        public RequestWindow(WeatherCache cache)
        {
            _cache = cache;
            BuildUi();
            StartTimers();
        }

        // ── UI ────────────────────────────────────────────────────────────────

        private void BuildUi()
        {
            Text            = "AIS Request";
            Size            = new Size(820, 580);
            MinimumSize     = new Size(520, 320);
            BackColor       = ColBg;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition   = FormStartPosition.Manual;
            Font            = UiFont;

            // ── Toolbar ───────────────────────────────────────────────────────
            var toolbar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 34,
                BackColor = ColPanel,
            };

            int x = 6, y = 5;

            var lbl = Label("ICAO:", x, y + 3);
            x += lbl.PreferredWidth + 4;

            _addBox = new TextBox
            {
                Left = x, Top = y, Width = 55, MaxLength = 4,
                CharacterCasing = CharacterCasing.Upper,
                Font            = MonoBold,
                BackColor       = Color.FromArgb(200, 208, 208),
                BorderStyle     = BorderStyle.FixedSingle,
            };
            _addBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) AddStation(_addBox.Text); };
            x += _addBox.Width + 6;

            _addBtn = Btn("Request", x, y);
            _addBtn.Click += (s, e) => AddStation(_addBox.Text);
            x += _addBtn.Width + 12;

            _refreshNowBtn = Btn("Refresh All", x, y);
            _refreshNowBtn.Click += async (s, e) => await RefreshAll(forced: true);
            x += _refreshNowBtn.Width + 12;

            _statusLabel = Label("No stations requested yet", x, y + 3);
            x += 260;

            _countdownLabel = new Label
            {
                Left      = x, Top = y + 3,
                AutoSize  = true,
                ForeColor = Color.FromArgb(0, 0, 96),
                Font      = UiBold,
                Text      = "",
            };

            toolbar.Controls.AddRange(new Control[]
                { lbl, _addBox, _addBtn, _refreshNowBtn, _statusLabel, _countdownLabel });

            // ── Display ───────────────────────────────────────────────────────
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

            Controls.Add(_display);
            Controls.Add(toolbar);
        }

        private Label Label(string text, int left, int top) => new Label
        {
            Text = text, Left = left, Top = top, AutoSize = true,
            ForeColor = Color.FromArgb(30, 30, 30), Font = UiFont,
        };

        private Button Btn(string text, int left, int top)
        {
            var b = new Button
            {
                Text = text, Left = left, Top = top, Height = 24, AutoSize = true,
                BackColor = ColBtn, ForeColor = Color.FromArgb(20, 20, 20),
                FlatStyle = FlatStyle.Flat, Font = UiFont,
            };
            b.FlatAppearance.BorderColor = ColBtnBdr;
            return b;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void AddStation(string icao)
        {
            icao = icao?.Trim().ToUpper() ?? "";
            if (icao.Length != 4) return;

            if (!_watchlist.Contains(icao))
                _watchlist.Add(icao);

            _addBox.Text = icao;
            SetStatus($"Fetching {icao}…");

            Task.Run(async () =>
            {
                await WeatherService.FetchStationAsync(icao, _cache);
                Render();
                SafeUi(() => SetStatus($"{icao} loaded  |  {_watchlist.Count} station(s)"));
            });
        }

        // ── Timers ────────────────────────────────────────────────────────────

        private void StartTimers()
        {
            _nextRefresh = DateTime.UtcNow.AddMinutes(RefreshMinutes);

            _refreshTimer = new System.Timers.Timer(RefreshMinutes * 60_000) { AutoReset = true };
            _refreshTimer.Elapsed += (s, e) => _ = RefreshAll(forced: false);
            _refreshTimer.Start();

            _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _countdownTimer.Tick += (s, e) => TickCountdown();
            _countdownTimer.Start();
        }

        private async Task RefreshAll(bool forced)
        {
            if (_fetching || _watchlist.Count == 0) return;
            _fetching    = true;
            _nextRefresh = DateTime.UtcNow.AddMinutes(RefreshMinutes);
            SetStatus("Refreshing…");

            try
            {
                // Snapshot before fetch
                var snapMetar = _watchlist.ToDictionary(
                    i => i, i => _cache.Get(i)?.RawMetar ?? "",
                    StringComparer.OrdinalIgnoreCase);
                var snapTaf = _watchlist.ToDictionary(
                    i => i, i => _cache.Get(i)?.RawTaf ?? "",
                    StringComparer.OrdinalIgnoreCase);

                await Task.Run(async () =>
                {
                    foreach (var icao in _watchlist.ToList())
                        await WeatherService.FetchStationAsync(icao, _cache);
                });

                // Detect changes
                foreach (var icao in _watchlist)
                {
                    var nowMetar = _cache.Get(icao)?.RawMetar ?? "";
                    var nowTaf   = _cache.Get(icao)?.RawTaf   ?? "";
                    _metarChanged[icao] = snapMetar[icao] != "" && snapMetar[icao] != nowMetar;
                    _tafChanged[icao]   = snapTaf[icao]   != "" && snapTaf[icao]   != nowTaf;
                    _prevMetar[icao]    = snapMetar[icao];
                    _prevTaf[icao]      = snapTaf[icao];
                }

                Render();
                SetStatus($"Updated {DateTime.UtcNow:HH:mm}Z  |  {_watchlist.Count} station(s)");
            }
            finally
            {
                _fetching = false;
            }
        }

        private void TickCountdown()
        {
            if (_watchlist.Count == 0) return;
            var r = _nextRefresh - DateTime.UtcNow;
            if (r < TimeSpan.Zero) r = TimeSpan.Zero;
            SafeUi(() => _countdownLabel.Text = $"Next: {r.Minutes}m {r.Seconds:D2}s");
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        private void Render()
        {
            SafeUi(() =>
            {
                _display.Clear();

                if (_watchlist.Count == 0)
                {
                    Write("Type an ICAO above and click Request.\r\n", ColDetailTxt, UiFont);
                    return;
                }

                foreach (var icao in _watchlist)
                {
                    var entry = _cache.Get(icao);

                    RenderMetar(icao, entry);
                    Write("\r\n", ColDetailTxt);
                    RenderTaf(icao, entry);

                    Write("\r\n" + new string('─', 90) + "\r\n\r\n", ColSep);
                }

                _display.SelectionStart = 0;
                _display.ScrollToCaret();
            });
        }

        private void RenderMetar(string icao, WeatherEntry entry)
        {
            bool changed = _metarChanged.ContainsKey(icao) && _metarChanged[icao];
            bool stale   = entry?.IsMetarStale() ?? true;
            var  hdrCol  = changed ? ColUpdated : ColIcao;

            // Header line
            Write("METAR  ", hdrCol, MonoBold);
            Write(icao, hdrCol, MonoBold);
            if (changed) Write("  ● UPDATED", ColUpdated, UiBold);
            else if (stale && entry != null) Write("  (stale)", ColStale, UiFont);
            if (entry?.HasMetar == true)
                Write($"  {entry.MetarTimestamp:HH:mm}Z", ColGrey, UiFont);
            Write("\r\n", ColDetailTxt);

            // Body
            if (entry?.HasMetar == true)
            {
                Write(entry.RawMetar + "\r\n", changed ? ColUpdated : stale ? ColStale : ColDetailTxt);

                // Previous if changed
                if (changed && _prevMetar.TryGetValue(icao, out var prev) && !string.IsNullOrEmpty(prev))
                {
                    Write("  Previous: " + prev + "\r\n", ColGrey, UiFont);
                }
            }
            else
            {
                Write("  (No METAR)\r\n", ColStale);
            }

            // SPECI if present
            if (entry?.HasSpeci == true)
            {
                Write("SPECI  ", ColIcao, MonoBold);
                Write(entry.RawSpeci + "\r\n", ColDetailTxt);
            }
        }

        private void RenderTaf(string icao, WeatherEntry entry)
        {
            bool changed = _tafChanged.ContainsKey(icao) && _tafChanged[icao];
            bool stale   = entry?.IsTafStale() ?? true;
            var  hdrCol  = changed ? ColUpdated : ColIcao;

            // Header line
            Write("TAF    ", hdrCol, MonoBold);
            Write(icao, hdrCol, MonoBold);
            if (changed) Write("  ● AMENDED", ColUpdated, UiBold);
            else if (stale && entry != null) Write("  (stale)", ColStale, UiFont);
            if (entry?.HasTaf == true)
                Write($"  {entry.TafTimestamp:HH:mm}Z", ColGrey, UiFont);
            Write("\r\n", ColDetailTxt);

            // Body
            if (entry?.HasTaf == true)
            {
                foreach (var line in entry.RawTaf.Split('\n'))
                    Write(line.TrimEnd() + "\r\n",
                          changed ? ColUpdated : stale ? ColStale : ColDetailTxt);

                // Previous if changed
                if (changed && _prevTaf.TryGetValue(icao, out var prev) && !string.IsNullOrEmpty(prev))
                {
                    Write("\r\n  Previous TAF:\r\n", ColGrey, UiFont);
                    foreach (var line in prev.Split('\n'))
                        Write("  " + line.TrimEnd() + "\r\n", ColGrey);
                }
            }
            else
            {
                Write("  (No TAF)\r\n", ColStale);
            }
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

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _refreshTimer?.Dispose();
            _countdownTimer?.Stop();
            base.OnFormClosed(e);
        }
    }
}
