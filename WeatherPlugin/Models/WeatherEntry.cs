using System;

namespace WeatherPlugin.Models
{
    public class WeatherEntry
    {
        public string Icao { get; set; }
        public string RawMetar { get; set; }
        public string RawSpeci { get; set; }
        public string RawTaf { get; set; }
        public DateTime MetarTimestamp { get; set; }
        public DateTime TafTimestamp { get; set; }

        public bool HasMetar => !string.IsNullOrWhiteSpace(RawMetar);
        public bool HasSpeci => !string.IsNullOrWhiteSpace(RawSpeci);
        public bool HasTaf => !string.IsNullOrWhiteSpace(RawTaf);

        public bool IsMetarStale(int minutes = 70) =>
            MetarTimestamp == default || (DateTime.UtcNow - MetarTimestamp).TotalMinutes > minutes;

        public bool IsTafStale(int hours = 6) =>
            TafTimestamp == default || (DateTime.UtcNow - TafTimestamp).TotalHours > hours;
    }
}
