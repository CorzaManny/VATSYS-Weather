using System;
using System.Drawing;
using System.Windows.Forms;

namespace WeatherPlugin.UI
{
    public class WeatherWindow : Form
    {
        private static readonly Color ColBg     = Color.FromArgb(160, 170, 170);
        private static readonly Color ColPanel  = Color.FromArgb(130, 146, 146);
        private static readonly Color ColBtn    = Color.FromArgb(130, 146, 146);
        private static readonly Color ColBtnBdr = Color.FromArgb(80, 90, 90);

        private static readonly Font MonoBold = new Font("Courier New", 9f, FontStyle.Bold);
        private static readonly Font UiFont   = new Font("Arial", 8.5f, FontStyle.Regular);

        private TextBox  _icaoBox;
        private ComboBox _typeBox;
        private Button   _requestBtn;
        private Label    _statusLabel;

        private readonly ResultWindow _resultWindow;

        public WeatherWindow(ResultWindow resultWindow)
        {
            _resultWindow = resultWindow;
            BuildUi();
        }

        private void BuildUi()
        {
            Text            = "AU Weather";
            ClientSize      = new Size(340, 60);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition   = FormStartPosition.Manual;
            BackColor       = ColPanel;
            Font            = UiFont;

            int x = 6, y = 8;

            var lblIcao = new Label
            {
                Text = "ICAO:", Left = x, Top = y + 4,
                AutoSize = true, ForeColor = Color.FromArgb(30, 30, 30), Font = UiFont,
            };
            x += lblIcao.PreferredWidth + 4;

            _icaoBox = new TextBox
            {
                Left = x, Top = y, Width = 52, MaxLength = 4,
                CharacterCasing = CharacterCasing.Upper,
                Font            = MonoBold,
                BackColor       = Color.FromArgb(200, 208, 208),
                BorderStyle     = BorderStyle.FixedSingle,
            };
            _icaoBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) DoRequest(); };
            x += _icaoBox.Width + 6;

            _typeBox = new ComboBox
            {
                Left = x, Top = y, Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font          = UiFont,
                BackColor     = Color.FromArgb(200, 208, 208),
            };
            _typeBox.Items.AddRange(new object[] { "METAR + TAF", "METAR only", "TAF only" });
            _typeBox.SelectedIndex = 0;
            x += _typeBox.Width + 6;

            _requestBtn = new Button
            {
                Text = "Request", Left = x, Top = y, Height = 22, AutoSize = true,
                BackColor = ColBtn, ForeColor = Color.FromArgb(20, 20, 20),
                FlatStyle = FlatStyle.Flat, Font = UiFont,
            };
            _requestBtn.FlatAppearance.BorderColor = ColBtnBdr;
            _requestBtn.Click += (s, e) => DoRequest();
            x += _requestBtn.Width + 8;

            _statusLabel = new Label
            {
                Left = 6, Top = 40, AutoSize = false, Width = 320, Height = 16,
                ForeColor = Color.FromArgb(30, 30, 30), Font = UiFont, Text = "",
            };

            Controls.AddRange(new Control[]
                { lblIcao, _icaoBox, _typeBox, _requestBtn, _statusLabel });
        }

        private void DoRequest()
        {
            var icao = _icaoBox.Text.Trim().ToUpper();
            if (icao.Length != 4) { _statusLabel.Text = "Enter a 4-letter ICAO code."; return; }

            bool metar = _typeBox.SelectedIndex != 2; // not "TAF only"
            bool taf   = _typeBox.SelectedIndex != 1; // not "METAR only"

            _statusLabel.Text = $"Requesting {icao}…";

            if (!_resultWindow.Visible)
                _resultWindow.Show(this);
            _resultWindow.BringToFront();
            _resultWindow.AddStation(icao, metar, taf);

            _statusLabel.Text = $"{icao} sent to monitor.";
            _icaoBox.Clear();
            _icaoBox.Focus();
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
    }
}
