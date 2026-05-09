using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using WeatherPlugin.Services;

namespace WeatherPlugin.UI
{
    public class WeatherWindow : Form
    {
        // vatSys colour scheme (from Colours.xml)
        private static readonly Color ColBg      = Color.FromArgb(160, 170, 170);
        private static readonly Color ColPanel   = Color.FromArgb(130, 146, 146);
        private static readonly Color ColBtn     = Color.FromArgb(0, 0, 96);
        private static readonly Color ColBtnText = Color.FromArgb(220, 220, 220);
        private static readonly Color ColText    = Color.FromArgb(30, 30, 30);
        private static readonly Color ColStale   = Color.FromArgb(160, 80, 0);
        private static readonly Font  MonoFont   = new Font("Courier New", 9.5f, FontStyle.Regular);
        private static readonly Font  UiFont     = new Font("Arial", 9f, FontStyle.Regular);

        private TextBox _icaoBox;
        private Button  _lookupBtn;
        private Button  _refreshBtn;
        private Label   _statusLabel;
        private RichTextBox _metarBox;
        private RichTextBox _tafBox;

        private readonly WeatherCache _cache;

        public WeatherWindow(WeatherCache cache)
        {
            _cache = cache;
            BuildUi();
        }

        private void BuildUi()
        {
            Text            = "AU Weather";
            MinimumSize     = new Size(520, 360);
            Size            = new Size(660, 480);
            BackColor       = ColBg;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition   = FormStartPosition.Manual;
            Font            = UiFont;

            // ── Top toolbar ──────────────────────────────────────────────────
            var toolbar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 36,
                BackColor = ColPanel,
                Padding   = new Padding(6, 0, 6, 0),
            };

            var icaoLabel = new Label
            {
                Text      = "ICAO:",
                AutoSize  = true,
                ForeColor = ColBtnText,
                Top       = 10,
                Left      = 6,
                Font      = UiFont,
            };

            _icaoBox = new TextBox
            {
                Width     = 60,
                MaxLength = 4,
                Left      = icaoLabel.Right + 4,
                Top       = 7,
                CharacterCasing = CharacterCasing.Upper,
                Font      = UiFont,
            };
            _icaoBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) LookupStation(); };

            _lookupBtn = MakeButton("Look Up", 130, 5);
            _lookupBtn.Click += (s, e) => LookupStation();

            _refreshBtn = MakeButton("Refresh All", 215, 5);
            _refreshBtn.Click += async (s, e) => await RefreshAll();

            _statusLabel = new Label
            {
                Text      = "Not refreshed yet",
                AutoSize  = true,
                ForeColor = ColBtnText,
                Top       = 11,
                Left      = 315,
                Font      = UiFont,
            };

            toolbar.Controls.AddRange(new Control[]
                { icaoLabel, _icaoBox, _lookupBtn, _refreshBtn, _statusLabel });

            // ── Tab control ──────────────────────────────────────────────────
            var tabs = new TabControl
            {
                Dock      = DockStyle.Fill,
                Font      = UiFont,
            };

            _metarBox = MakeWeatherBox();
            _tafBox   = MakeWeatherBox();

            tabs.TabPages.Add(new TabPage("METAR / SPECI") { Controls = { _metarBox } });
            tabs.TabPages.Add(new TabPage("TAF")           { Controls = { _tafBox   } });

            Controls.Add(tabs);
            Controls.Add(toolbar);  // Add after fill-docked tabs so it sits on top
        }

        private Button MakeButton(string text, int left, int top)
        {
            return new Button
            {
                Text      = text,
                Left      = left,
                Top       = top,
                Height    = 26,
                AutoSize  = true,
                BackColor = ColBtn,
                ForeColor = ColBtnText,
                FlatStyle = FlatStyle.Flat,
                Font      = UiFont,
            };
        }

        private RichTextBox MakeWeatherBox() => new RichTextBox
        {
            Dock      = DockStyle.Fill,
            ReadOnly  = true,
            BackColor = Color.FromArgb(20, 22, 28),
            ForeColor = Color.FromArgb(200, 220, 200),
            Font      = MonoFont,
            WordWrap  = false,
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Both,
        };

        // ── Data operations ──────────────────────────────────────────────────

        private void LookupStation()
        {
            var icao = _icaoBox.Text.Trim().ToUpper();
            if (icao.Length != 4) return;

            var entry = _cache.Get(icao);
            if (entry == null || entry.IsMetarStale(15))
            {
                SetStatus("Fetching " + icao + "…");
                Task.Run(async () =>
                {
                    await WeatherService.FetchStationAsync(icao, _cache);
                    InvokeUi(() => PopulateStation(icao));
                });
            }
            else
            {
                PopulateStation(icao);
            }
        }

        private async Task RefreshAll()
        {
            _refreshBtn.Enabled = false;
            SetStatus("Refreshing all Australian stations…");
            try
            {
                await Task.Run(() => WeatherService.FetchBulkAsync(_cache));
            }
            finally
            {
                _refreshBtn.Enabled = true;
                UpdateStatus();
                // If a station is already displayed, refresh its data
                var icao = _icaoBox.Text.Trim().ToUpper();
                if (icao.Length == 4 && _cache.Get(icao) != null)
                    PopulateStation(icao);
            }
        }

        private void PopulateStation(string icao)
        {
            var entry = _cache.Get(icao);

            _metarBox.Clear();
            _tafBox.Clear();

            if (entry == null)
            {
                _metarBox.Text = $"No data for {icao}.";
                _tafBox.Text   = $"No TAF for {icao}.";
                SetStatus("Not found: " + icao);
                return;
            }

            // METAR / SPECI tab
            if (entry.HasMetar)
                _metarBox.AppendText(entry.RawMetar + "\r\n");
            else
                _metarBox.AppendText("(No METAR on file)\r\n");

            if (entry.HasSpeci)
            {
                _metarBox.AppendText("\r\n─── SPECI ───\r\n");
                _metarBox.AppendText(entry.RawSpeci + "\r\n");
            }

            if (entry.IsMetarStale())
                ColorizeStale(_metarBox);

            // TAF tab
            if (entry.HasTaf)
                _tafBox.Text = entry.RawTaf;
            else
                _tafBox.Text = "(No TAF on file)";

            SetStatus($"{icao} — updated {entry.MetarTimestamp:HH:mm}Z | {_cache.StationCount} stations cached");
        }

        public void UpdateStatus()
        {
            InvokeUi(() =>
            {
                var last = _cache.LastMetarRefresh == default
                    ? "--:--"
                    : _cache.LastMetarRefresh.ToString("HH:mm") + "Z";
                SetStatus($"Last refresh: {last} | {_cache.StationCount} stations cached");
            });
        }

        // Grey-out stale data in the METAR box
        private void ColorizeStale(RichTextBox box)
        {
            box.SelectAll();
            box.SelectionColor = ColStale;
            box.SelectionLength = 0;
        }

        private void SetStatus(string msg) =>
            InvokeUi(() => _statusLabel.Text = msg);

        private void InvokeUi(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) Invoke(a);
            else a();
        }
    }
}
