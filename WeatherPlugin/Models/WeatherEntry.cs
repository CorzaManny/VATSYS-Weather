using System;

namespace WeatherPlugin.Models
{
    public class WeatherEntry
    {
        public string Icao { get; set; }
        public string RawMetar    { get; set; }
        public string RawSpeci    { get; set; }
        public string RawTaf      { get; set; }
        public string RawAtis     { get; set; }
        public string MetarSource { get; set; }
        public string TafSource   { get; set; }
        public string AtisSource  { get; set; }
        public DateTime MetarTimestamp { get; set; }
        public DateTime TafTimestamp   { get; set; }
        public DateTime AtisTimestamp  { get; set; }

        public bool HasMetar => !string.IsNullOrWhiteSpace(RawMetar);
        public bool HasSpeci => !string.IsNullOrWhiteSpace(RawSpeci);
        public bool HasTaf   => !string.IsNullOrWhiteSpace(RawTaf);
        public bool HasAtis  => !string.IsNullOrWhiteSpace(RawAtis);

        public bool IsMetarStale(int minutes = 70) =>
            MetarTimestamp == default || (DateTime.UtcNow - MetarTimestamp).TotalMinutes > minutes;

        public bool IsTafStale(int hours = 6) =>
            TafTimestamp == default || (DateTime.UtcNow - TafTimestamp).TotalHours > hours;

        public bool IsAtisStale(int minutes = 30) =>
            AtisTimestamp == default || (DateTime.UtcNow - AtisTimestamp).TotalMinutes > minutes;
    }
}
