using System;
using System.ComponentModel.Composition;
using System.Windows.Forms;
using vatsys;
using vatsys.Plugin;
using WeatherPlugin.Services;
using WeatherPlugin.UI;

namespace WeatherPlugin
{
    [Export(typeof(IPlugin))]
    public class Plugin : IPlugin, IDisposable
    {
        public string Name => "AU Weather";
        private static readonly string DisplayName = "AU Weather";

        private static WeatherCache _cache;
        private static WeatherWindow _window;
        private static CustomToolStripMenuItem _menuItem;
        private System.Timers.Timer _metarTimer;
        private System.Timers.Timer _tafTimer;

        public Plugin()
        {
            try
            {
                _cache = new WeatherCache();

                // Add "AU Weather" under the Windows menu
                _menuItem = new CustomToolStripMenuItem(
                    CustomToolStripMenuItemWindowType.Main,
                    CustomToolStripMenuItemCategory.Windows,
                    new ToolStripMenuItem(DisplayName));
                _menuItem.Item.Click += (s, e) => ShowWindow();
                MMI.AddCustomMenuItem(_menuItem);

                // Immediate bulk fetch on load
                _ = WeatherService.FetchBulkAsync(_cache)
                    .ContinueWith(_ => _window?.UpdateStatus());

                // METAR refresh every 10 minutes
                _metarTimer = new System.Timers.Timer(10 * 60 * 1000) { AutoReset = true };
                _metarTimer.Elapsed += OnMetarRefresh;
                _metarTimer.Start();

                // TAF refresh every 30 minutes (TAFs are slower-changing)
                _tafTimer = new System.Timers.Timer(30 * 60 * 1000) { AutoReset = true };
                _tafTimer.Elapsed += OnTafRefresh;
                _tafTimer.Start();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("AU Weather failed to start: " + ex.Message), DisplayName);
            }
        }

        private void OnMetarRefresh(object sender, System.Timers.ElapsedEventArgs e)
        {
            _ = WeatherService.FetchBulkAsync(_cache)
                .ContinueWith(_ => _window?.UpdateStatus());
        }

        private void OnTafRefresh(object sender, System.Timers.ElapsedEventArgs e)
        {
            _ = WeatherService.FetchBulkAsync(_cache)
                .ContinueWith(_ => _window?.UpdateStatus());
        }

        private static void ShowWindow()
        {
            var mainForm = Application.OpenForms["MainForm"];
            if (mainForm == null) return;

            try
            {
                MMI.InvokeOnGUI(delegate ()
                {
                    if (_window == null || _window.IsDisposed)
                        _window = new WeatherWindow(_cache);
                    _window.Show(mainForm);
                });
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("AU Weather window error: " + ex.Message), DisplayName);
            }
        }

        public void OnFDRUpdate(FDP2.FDR updated) { }
        public void OnRadarTrackUpdate(RDP.RadarTrack updated) { }

        public void Dispose()
        {
            _metarTimer?.Dispose();
            _tafTimer?.Dispose();
        }
    }
}
