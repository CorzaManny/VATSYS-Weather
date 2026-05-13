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

        private static WeatherCache  _cache;
        private static CustomToolStripMenuItem _menuItem;

        private System.Timers.Timer _metarTimer;
        private System.Timers.Timer _tafTimer;

        public Plugin()
        {
            try
            {
                _cache = new WeatherCache();

                _menuItem = new CustomToolStripMenuItem(
                    CustomToolStripMenuItemWindowType.Main,
                    CustomToolStripMenuItemCategory.Windows,
                    new ToolStripMenuItem(DisplayName));
                _menuItem.Item.Click += (s, e) => ShowWindow();
                MMI.AddCustomMenuItem(_menuItem);

                // Background bulk fetch so the cache is warm on first use
                _ = WeatherService.FetchBulkAsync(_cache);

                // Periodic bulk refresh (keeps cache warm for any new requests)
                _metarTimer = new System.Timers.Timer(10 * 60_000) { AutoReset = true };
                _metarTimer.Elapsed += (s, e) => _ = WeatherService.FetchBulkAsync(_cache);
                _metarTimer.Start();

                _tafTimer = new System.Timers.Timer(30 * 60_000) { AutoReset = true };
                _tafTimer.Elapsed += (s, e) => _ = WeatherService.FetchBulkAsync(_cache);
                _tafTimer.Start();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("AU Weather failed to start: " + ex.Message), DisplayName);
            }
        }

        private static void ShowWindow()
        {
            try
            {
                MMI.InvokeOnGUI(delegate ()
                {
                    WeatherWindow.ShowWindow(_cache);
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
