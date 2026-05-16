using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using WeatherPlugin.Models;

namespace WeatherPlugin.Services
{
    public class WeatherCache
    {
        private readonly ConcurrentDictionary<string, WeatherEntry> _store =
            new ConcurrentDictionary<string, WeatherEntry>(StringComparer.OrdinalIgnoreCase);

        public DateTime LastMetarRefresh { get; private set; }
        public DateTime LastTafRefresh { get; private set; }
        public int StationCount => _store.Count;

        public void SetMetar(string icao, string raw, string source = null)
        {
            var entry = _store.GetOrAdd(icao, k => new WeatherEntry { Icao = k.ToUpper() });
            entry.RawMetar        = raw;
            entry.MetarSource     = source;
            entry.MetarTimestamp  = DateTime.UtcNow;
            LastMetarRefresh      = DateTime.UtcNow;
        }

        public void SetSpeci(string icao, string raw, string source = null)
        {
            var entry = _store.GetOrAdd(icao, k => new WeatherEntry { Icao = k.ToUpper() });
            entry.RawSpeci       = raw;
            entry.MetarSource    = source;
            entry.MetarTimestamp = DateTime.UtcNow;
        }

        public void SetTaf(string icao, string raw, string source = null)
        {
            var entry = _store.GetOrAdd(icao, k => new WeatherEntry { Icao = k.ToUpper() });
            entry.RawTaf         = raw;
            entry.TafSource      = source;
            entry.TafTimestamp   = DateTime.UtcNow;
            LastTafRefresh       = DateTime.UtcNow;
        }

        public void SetAtis(string icao, string raw, string source = null)
        {
            var entry = _store.GetOrAdd(icao, k => new WeatherEntry { Icao = k.ToUpper() });
            entry.RawAtis        = raw;
            entry.AtisSource     = source;
            entry.AtisTimestamp  = DateTime.UtcNow;
        }

        public WeatherEntry Get(string icao) =>
            _store.TryGetValue(icao, out var e) ? e : null;

        public IEnumerable<WeatherEntry> GetAll() => _store.Values;

        public void Clear() => _store.Clear();
    }
}
