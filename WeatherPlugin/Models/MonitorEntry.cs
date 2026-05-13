namespace WeatherPlugin.Models
{
    public enum EntryState { Yellow, Blue }

    public class MonitorEntry
    {
        public string Icao       { get; set; }
        public bool   WatchMetar { get; set; }
        public bool   WatchTaf   { get; set; }
        public bool   WatchAtis  { get; set; }

        // Live text (read from WeatherCache on each render)
        public string MetarRaw { get; set; }
        public string TafRaw   { get; set; }
        public string AtisRaw  { get; set; }

        // Snapshot from just before the last refresh — used for change detection display
        public string PrevMetarRaw { get; set; }
        public string PrevTafRaw   { get; set; }
        public string PrevAtisRaw  { get; set; }

        public EntryState MetarState { get; set; } = EntryState.Yellow;
        public EntryState TafState   { get; set; } = EntryState.Yellow;
        public EntryState AtisState  { get; set; } = EntryState.Yellow;
    }
}
