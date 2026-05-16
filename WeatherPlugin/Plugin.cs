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
    public class Plugin : IPlugin
    {
        public string Name => "AU Weather";
        private static readonly string DisplayName = "AU Weather";

        private static WeatherCache _cache;
        private static CustomToolStripMenuItem _menuItem;

        public Plugin()
        {
            try
            {
                _cache = new WeatherCache();

                _menuItem = new CustomToolStripMenuItem(
                    CustomToolStripMenuItemWindowType.Main,
                    CustomToolStripMenuItemCategory.Tools,
                    new ToolStripMenuItem(DisplayName));
                _menuItem.Item.Click += (s, e) => ShowWindow();
                MMI.AddCustomMenuItem(_menuItem);
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
    }
}
