using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using vatsys;
using WeatherPlugin.Models;

namespace WeatherPlugin.Services
{
    public static class WeatherService
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private const string DisplayName = "AU Weather";

        // BoM aviation text product URLs (Australian METAR/TAF bulletins).
        // These follow WMO GTS product naming. Fall through to AWC if unavailable.
        private static readonly string[] BomMetarCandidates = {
            "http://www.bom.gov.au/fwo/IDY30300.txt",
            "http://www.bom.gov.au/fwo/IDY30301.txt",
        };
        private static readonly string[] BomTafCandidates = {
            "http://www.bom.gov.au/fwo/IDY40000.txt",
            "http://www.bom.gov.au/fwo/IDY40001.txt",
        };

        // AWC (Aviation Weather Center) bulk bbox covering Australia
        // bbox = minLat,minLon,maxLat,maxLon
        private const string AwcMetarUrl =
            "https://aviationweather.gov/api/data/metar?bbox=-44,112,-10,155&format=raw&hours=2";
        private const string AwcTafUrl =
            "https://aviationweather.gov/api/data/taf?bbox=-44,112,-10,155&format=raw&hours=12";

        // VATSIM METAR — single-station fallback (real-world data, no auth)
        private const string VatsimMetarBase = "https://metar.vatsim.net/metar.php?id=";

        static WeatherService()
        {
            Http.DefaultRequestHeaders.Add("User-Agent", "vatSys-WeatherPlugin/1.0");
        }

        public static async Task FetchBulkAsync(WeatherCache cache)
        {
            await Task.WhenAll(FetchMetarsAsync(cache), FetchTafsAsync(cache));
        }

        private static async Task FetchMetarsAsync(WeatherCache cache)
        {
            // Try BoM first
            foreach (var url in BomMetarCandidates)
            {
                try
                {
                    var text = await Http.GetStringAsync(url);
                    if (!string.IsNullOrWhiteSpace(text) && ContainsIcao(text))
                    {
                        ParseAndCacheMetars(text, cache);
                        return;
                    }
                }
                catch { }
            }

            // Fall back to AWC
            try
            {
                var text = await Http.GetStringAsync(AwcMetarUrl);
                ParseAndCacheMetars(text, cache);
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("AU Weather: METAR fetch failed — " + ex.Message), DisplayName);
            }
        }

        private static async Task FetchTafsAsync(WeatherCache cache)
        {
            // Try BoM first
            foreach (var url in BomTafCandidates)
            {
                try
                {
                    var text = await Http.GetStringAsync(url);
                    if (!string.IsNullOrWhiteSpace(text) && ContainsIcao(text))
                    {
                        ParseAndCacheTafs(text, cache);
                        return;
                    }
                }
                catch { }
            }

            // Fall back to AWC
            try
            {
                var text = await Http.GetStringAsync(AwcTafUrl);
                ParseAndCacheTafs(text, cache);
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("AU Weather: TAF fetch failed — " + ex.Message), DisplayName);
            }
        }

        // Single-station fetch for on-demand lookups (uses VATSIM METAR as fast fallback)
        public static async Task FetchStationAsync(string icao, WeatherCache cache)
        {
            icao = icao.ToUpper().Trim();

            // METAR via VATSIM if not cached or stale
            var entry = cache.Get(icao);
            if (entry == null || entry.IsMetarStale(15))
            {
                try
                {
                    var raw = await Http.GetStringAsync(VatsimMetarBase + icao);
                    raw = raw.Trim();
                    if (!string.IsNullOrEmpty(raw) && raw.Length > 10 && !raw.StartsWith("No"))
                        cache.SetMetar(icao, raw);
                }
                catch { }
            }

            // TAF via AWC single-station if not cached or stale
            entry = cache.Get(icao);
            if (entry == null || entry.IsTafStale(1))
            {
                try
                {
                    var url = $"https://aviationweather.gov/api/data/taf?ids={icao}&format=raw&hours=12";
                    var text = await Http.GetStringAsync(url);
                    var tafs = ParseTafs(text);
                    if (tafs.TryGetValue(icao, out var taf))
                        cache.SetTaf(icao, taf);
                }
                catch { }
            }
        }

        // ── Parsers ──────────────────────────────────────────────────────────────

        private static void ParseAndCacheMetars(string raw, WeatherCache cache)
        {
            foreach (var kvp in ParseMetars(raw))
            {
                if (kvp.Value.StartsWith("SPECI "))
                    cache.SetSpeci(kvp.Key, kvp.Value);
                else
                    cache.SetMetar(kvp.Key, kvp.Value);
            }
        }

        private static void ParseAndCacheTafs(string raw, WeatherCache cache)
        {
            foreach (var kvp in ParseTafs(raw))
                cache.SetTaf(kvp.Key, kvp.Value);
        }

        private static Dictionary<string, string> ParseMetars(string raw)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in raw.Split('\n'))
            {
                var clean = line.Trim();
                if (clean.Length < 8) continue;

                // Strip optional METAR/SPECI prefix to extract ICAO, then keep original
                var icaoPart = Regex.Replace(clean, @"^(METAR|SPECI)\s+", "");
                if (icaoPart.Length < 4) continue;

                var icao = icaoPart.Substring(0, 4).ToUpper();
                if (!IsIcao(icao)) continue;

                result[icao] = clean;
            }
            return result;
        }

        public static Dictionary<string, string> ParseTafs(string raw)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            string currentIcao = null;

            foreach (var rawLine in raw.Split('\n'))
            {
                var line = rawLine.TrimEnd();
                var trimmed = line.TrimStart();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                // New TAF starts when line begins with "TAF" (optionally "TAF AMD")
                if (Regex.IsMatch(trimmed, @"^TAF(\s+AMD)?\s+[A-Z]{4}", RegexOptions.IgnoreCase))
                {
                    SaveCurrentTaf(result, currentIcao, sb);

                    var afterTaf = Regex.Replace(trimmed, @"^TAF(\s+AMD)?\s+", "", RegexOptions.IgnoreCase);
                    currentIcao = afterTaf.Length >= 4 ? afterTaf.Substring(0, 4).ToUpper() : null;
                    sb.Clear();
                    if (currentIcao != null)
                        sb.AppendLine(trimmed);
                }
                else if (currentIcao != null)
                {
                    // Continuation line
                    sb.AppendLine("  " + trimmed.TrimEnd('='));
                }
            }

            SaveCurrentTaf(result, currentIcao, sb);
            return result;
        }

        private static void SaveCurrentTaf(Dictionary<string, string> result, string icao, StringBuilder sb)
        {
            if (icao == null || sb.Length == 0) return;
            var text = sb.ToString().TrimEnd().TrimEnd('=');
            if (!string.IsNullOrWhiteSpace(text))
                result[icao] = text;
        }

        private static bool ContainsIcao(string text) =>
            Regex.IsMatch(text, @"\b[A-Z]{4}\b");

        private static bool IsIcao(string s) =>
            s.Length == 4 && s.All(char.IsLetter);
    }
}
