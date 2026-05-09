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
    public class WeatherWindow : Form
    {
        // vatSys colours
        private static readonly Color ColBg      = Color.FromArgb(160, 170, 170);
        private static readonly Color ColPanel   = Color.FromArgb(130, 146, 146);
        private static readonly Color ColBtn     = Color.FromArgb(130, 146, 146);
        private static readonly Color ColBtnBdr  = Color.FromArgb(80, 90, 90);
        private static readonly Color ColListSel = Color.FromArgb(0, 0, 96);
        private static readonly Color ColSelTxt  = Color.FromArgb(220, 220, 220);
        private static readonly Color ColIcao    = Color.FromArgb(180, 210, 255);
        private static readonly Color ColStale   = Color.FromArgb(160, 100, 40);

        private static readonly Font MonoFont = new Font("Courier New", 9f, FontStyle.Regular);
        private static readonly Font MonoBold = new Font("Courier New", 9f, FontStyle.Bold);
        private static readonly Font UiFont   = new Font("Arial", 8.5f, FontStyle.Regular);
        private static readonly Font UiBold   = new Font("Arial", 8.5f, FontStyle.Bold);

        private ListBox _stationList;
        private Label   _countLabel;
        private Label   _statusLabel;
        private Button  _refreshBtn;

        private readonly WeatherCache _cache;
        private RequestWindow _requestWindow;

        public WeatherWindow(WeatherCache cache)
        {
            _cache = cache;
            BuildUi();
        }

        private void BuildUi()
        {
            Text            = "AU Weather — Stations";
            Size            = new Size(420, 620);
            MinimumSize     = new Size(300, 300);
            BackColor       = ColPanel;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            StartPosition   = FormStartPosition.Manual;
            Font            = UiFont;

            // ── Top bar ───────────────────────────────────────────────────────
            var topBar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 30,
                BackColor = ColBg,
            };

            _countLabel = new Label
            {
                Text      = "Stations",
                Left      = 6, Top = 7,
                AutoSize  = true,
                Font      = UiBold,
                ForeColor = Color.FromArgb(30, 30, 30),
            };

            _refreshBtn = new Button
            {
                Text      = "Refresh All",
                Left      = 280, Top = 4,
                Height    = 22, AutoSize = true,
                Anchor    = AnchorStyles.Right | AnchorStyles.Top,
                BackColor = ColBtn,
                ForeColor = Color.FromArgb(20, 20, 20),
                FlatStyle = FlatStyle.Flat,
                Font      = UiFont,
            };
            _refreshBtn.FlatAppearance.BorderColor = ColBtnBdr;
            _refreshBtn.Click += async (s, e) => await DoRefreshAll();

            _statusLabel = new Label
            {
                Text      = "Not yet refreshed",
                Left      = 6, Top = 34,
                AutoSize  = false, Width = 360, Height = 16,
                ForeColor = Color.FromArgb(30, 30, 30),
                Font      = UiFont,
            };

            topBar.Controls.Add(_countLabel);
            topBar.Controls.Add(_refreshBtn);

            var statusBar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 20,
                BackColor = ColBg,
            };
            statusBar.Controls.Add(_statusLabel);

            // ── Station list ──────────────────────────────────────────────────
            _stationList = new ListBox
            {
                Dock               = DockStyle.Fill,
                BackColor          = ColPanel,
                ForeColor          = Color.FromArgb(20, 20, 20),
                BorderStyle        = BorderStyle.None,
                Font               = MonoFont,
                IntegralHeight     = false,
                DrawMode           = DrawMode.OwnerDrawFixed,
                ItemHeight         = 16,
                ScrollAlwaysVisible= true,
            };
            _stationList.DrawItem             += DrawItem;
            _stationList.SelectedIndexChanged += OnSelectionChanged;
            _stationList.DoubleClick          += OnDoubleClick;

            // Add Fill first so Top bars sit above it
            Controls.Add(_stationList);
            Controls.Add(statusBar);
            Controls.Add(topBar);
        }

        // ── Drawing ───────────────────────────────────────────────────────────

        private void DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _stationList.Items.Count) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var bg   = sel ? ColListSel : (e.Index % 2 == 0 ? ColPanel : Color.FromArgb(120, 136, 136));
            e.Graphics.FillRectangle(new SolidBrush(bg), e.Bounds);

            var item = _stationList.Items[e.Index] as StationListItem;
            if (item == null) return;

            var fgIcao = sel ? ColSelTxt : ColIcao;
            var fgTxt  = sel ? ColSelTxt : Color.FromArgb(30, 30, 30);
            var stale  = item.IsStale;

            var icaoRect = new Rectangle(e.Bounds.Left + 2, e.Bounds.Top, 42, e.Bounds.Height);
            var sf = new StringFormat { LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString(item.Icao, MonoBold,
                new SolidBrush(stale ? ColStale : fgIcao), icaoRect, sf);

            var txtRect = new Rectangle(e.Bounds.Left + 46, e.Bounds.Top, e.Bounds.Width - 48, e.Bounds.Height);
            var sf2 = new StringFormat
            {
                Trimming    = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
                LineAlignment = StringAlignment.Center,
            };
            e.Graphics.DrawString(item.Summary, MonoFont,
                new SolidBrush(stale ? ColStale : fgTxt), txtRect, sf2);
        }

        private void OnSelectionChanged(object sender, EventArgs e) { }

        private void OnDoubleClick(object sender, EventArgs e)
        {
            var item = _stationList.SelectedItem as StationListItem;
            if (item == null) return;
            OpenRequestWindow(item.Icao);
        }

        // ── Data ──────────────────────────────────────────────────────────────

        private async Task DoRefreshAll()
        {
            _refreshBtn.Enabled = false;
            SetStatus("Refreshing all Australian stations…");
            try
            {
                await Task.Run(() => WeatherService.FetchBulkAsync(_cache));
                RebuildList();
                UpdateStatus();
            }
            finally
            {
                _refreshBtn.Enabled = true;
            }
        }

        public void RebuildList()
        {
            SafeUi(() =>
            {
                var prevSel = (_stationList.SelectedItem as StationListItem)?.Icao;
                _stationList.BeginUpdate();
                _stationList.Items.Clear();

                foreach (var entry in _cache.GetAll().OrderBy(e => e.Icao))
                    _stationList.Items.Add(new StationListItem(entry));

                _countLabel.Text = $"Stations  ({_stationList.Items.Count})";
                _stationList.EndUpdate();

                if (prevSel != null)
                {
                    for (int i = 0; i < _stationList.Items.Count; i++)
                    {
                        if ((_stationList.Items[i] as StationListItem)?.Icao == prevSel)
                        { _stationList.SelectedIndex = i; break; }
                    }
                }
            });
        }

        public void UpdateStatus()
        {
            SafeUi(() =>
            {
                var last = _cache.LastMetarRefresh == default
                    ? "--:--" : _cache.LastMetarRefresh.ToString("HH:mm") + "Z";
                SetStatus($"Last refresh: {last}  |  {_cache.StationCount} stations");
                RebuildList();
            });
        }

        private void OpenRequestWindow(string icao)
        {
            if (_requestWindow == null || _requestWindow.IsDisposed)
                _requestWindow = new RequestWindow(_cache);
            if (!_requestWindow.Visible)
                _requestWindow.Show();
            _requestWindow.BringToFront();
            _requestWindow.AddStation(icao);
        }

        private void SetStatus(string msg) => SafeUi(() => _statusLabel.Text = msg);

        private void SafeUi(Action a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(a);
            else a();
        }

        // ── List item model ───────────────────────────────────────────────────

        private class StationListItem
        {
            public string Icao    { get; }
            public string Summary { get; }
            public bool   IsStale { get; }

            public StationListItem(WeatherEntry e)
            {
                Icao    = e.Icao;
                IsStale = e.IsMetarStale();
                var raw = e.RawMetar ?? e.RawTaf ?? "";
                var parts = raw.TrimStart().Split(new[] { ' ' }, 2);
                Summary = parts.Length == 2 ? parts[1] : raw;
            }

            public override string ToString() => Icao;
        }
    }
}
