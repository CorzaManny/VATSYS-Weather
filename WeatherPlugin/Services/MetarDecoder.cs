using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WeatherPlugin.Models;

namespace WeatherPlugin.Services
{
    public static class MetarDecoder
    {
        // ── Overall classification ────────────────────────────────────────────

        public static FlightCategory Classify(string rawMetar)
        {
            if (string.IsNullOrWhiteSpace(rawMetar)) return FlightCategory.Unknown;
            var upper = rawMetar.ToUpper();

            if (upper.Contains("CAVOK")) return FlightCategory.VFR;

            int vis     = ParseVisibilityMeters(upper);
            int ceiling = ParseCeilingFeet(upper);

            return DetermineCategory(vis, ceiling);
        }

        // Returns "HH:MMZ" extracted from the METAR observation time, or null.
        public static string ParseObsTime(string rawMetar)
        {
            if (string.IsNullOrWhiteSpace(rawMetar)) return null;
            var m = Regex.Match(rawMetar, @"\b\d{6}Z\b");
            if (!m.Success || m.Value.Length < 7) return null;
            return m.Value.Substring(2, 2) + ":" + m.Value.Substring(4, 2) + "Z";
        }

        // ── Per-element classification (NOAA thresholds) ──────────────────────

        public static FlightCategory ClassifyVisibility(int visM)
        {
            if (visM < 0)    return FlightCategory.Unknown;
            if (visM < 1600) return FlightCategory.LIFR;   // < 1 SM
            if (visM < 4800) return FlightCategory.IFR;    // 1–3 SM
            if (visM < 8000) return FlightCategory.MVFR;   // 3–5 SM
            return FlightCategory.VFR;
        }

        public static FlightCategory ClassifyCeiling(int ceilFt)
        {
            if (ceilFt < 0)    return FlightCategory.Unknown;
            if (ceilFt < 500)  return FlightCategory.LIFR;   // < 500 ft
            if (ceilFt < 1000) return FlightCategory.IFR;    // 500–999 ft
            if (ceilFt < 3000) return FlightCategory.MVFR;   // 1000–2999 ft
            return FlightCategory.VFR;
        }

        // ── Token colorizer ───────────────────────────────────────────────────
        // Returns each whitespace-delimited token paired with its flight category.
        // null category = render in default/standard colour.

        public static List<(string Text, FlightCategory? Cat)> TokenizeForDisplay(string rawMetar)
        {
            var result = new List<(string, FlightCategory?)>();
            if (string.IsNullOrEmpty(rawMetar))
            {
                result.Add((rawMetar, null));
                return result;
            }

            var origTokens  = rawMetar.Split(' ');
            var upperTokens = rawMetar.ToUpper().Split(' ');

            bool pastWind = false;
            bool visSeen  = false;

            for (int i = 0; i < origTokens.Length; i++)
            {
                var tok  = upperTokens[i];
                var orig = origTokens[i];

                if (tok == "METAR" || tok == "SPECI" ||
                    tok == "AUTO"  || tok == "COR"   || tok == "NIL")
                { result.Add((orig, null)); continue; }

                // ICAO identifier (4 letters, before wind)
                if (!pastWind && tok.Length == 4 && tok.All(char.IsLetter))
                { result.Add((orig, null)); continue; }

                // Observation time DDHHMMz
                if (!pastWind && tok.EndsWith("Z") && tok.Length >= 6 &&
                    tok.Substring(0, tok.Length - 1).All(char.IsDigit))
                { result.Add((orig, null)); continue; }

                // Wind group
                if (!pastWind && (tok.EndsWith("KT") || tok.EndsWith("MPS") ||
                    (tok.StartsWith("VRB") && tok.Length >= 5)))
                { pastWind = true; result.Add((orig, null)); continue; }

                if (!pastWind) { result.Add((orig, null)); continue; }

                // Variable wind direction (e.g. 150V210)
                if (!visSeen && Regex.IsMatch(tok, @"^\d{3}V\d{3}$"))
                { result.Add((orig, null)); continue; }

                // CAVOK
                if (!visSeen && tok == "CAVOK")
                { visSeen = true; result.Add((orig, FlightCategory.VFR)); continue; }

                // Visibility: 4-digit group in metres
                if (!visSeen && tok.Length >= 4 && tok.Substring(0, 4).All(char.IsDigit))
                {
                    int visM = int.Parse(tok.Substring(0, 4));
                    visSeen = true;
                    result.Add((orig, ClassifyVisibility(visM)));
                    continue;
                }

                // Runway visual range (R28/0600U style)
                if (tok.StartsWith("R") && tok.Contains("/") && tok.Length > 3)
                { result.Add((orig, null)); continue; }

                // Cloud groups — all layers coloured by height against ceiling thresholds
                var cloudMatch = Regex.Match(tok, @"^(FEW|SCT|BKN|OVC|VV)(\d{3})");
                if (cloudMatch.Success)
                {
                    int heightFt = int.Parse(cloudMatch.Groups[2].Value) * 100;
                    result.Add((orig, ClassifyCeiling(heightFt)));
                    continue;
                }

                // Clear sky groups
                if (tok == "SKC" || tok == "CLR" || tok == "NSC" || tok == "NCD")
                { result.Add((orig, FlightCategory.VFR)); continue; }

                result.Add((orig, null));
            }

            return result;
        }

        // ── Parsers ───────────────────────────────────────────────────────────

        public static int ParseVisibilityMeters(string upper)
        {
            if (upper.Contains("CAVOK")) return 10000;

            var tokens = upper.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            bool pastWind = false;

            foreach (var tok in tokens)
            {
                if (tok == "METAR" || tok == "SPECI") continue;
                if (tok.Length == 4 && tok.All(char.IsLetter)) continue;
                if (tok.EndsWith("Z") && tok.Length >= 6 &&
                    tok.Substring(0, tok.Length - 1).All(char.IsDigit)) continue;
                if (tok == "AUTO" || tok == "COR" || tok == "NIL") continue;

                if (tok.EndsWith("KT") || tok.EndsWith("MPS") ||
                    (tok.StartsWith("VRB") && tok.Length >= 5))
                { pastWind = true; continue; }

                if (!pastWind) continue;

                if (tok.Length == 5 && tok[0] == 'Q' && tok.Substring(1).All(char.IsDigit)) continue;
                if (Regex.IsMatch(tok, @"^M?\d{2}/M?\d{2}$")) break;

                var visPart = tok.Length >= 4 ? tok.Substring(0, 4) : tok;
                if (visPart.Length == 4 && visPart.All(char.IsDigit) &&
                    (tok.Length == 4 || tok == visPart + "NDV"))
                    return int.Parse(visPart);
            }
            return -1;
        }

        public static int ParseCeilingFeet(string upper)
        {
            if (Regex.IsMatch(upper, @"\b(SKC|CLR|NSC)\b")) return -1;

            int lowest = int.MaxValue;

            foreach (Match m in Regex.Matches(upper, @"\b(BKN|OVC)(\d{3})\b"))
            {
                int alt = int.Parse(m.Groups[2].Value) * 100;
                if (alt < lowest) lowest = alt;
            }

            foreach (Match m in Regex.Matches(upper, @"\bVV(\d{3})\b"))
            {
                int alt = int.Parse(m.Groups[1].Value) * 100;
                if (alt < lowest) lowest = alt;
            }

            return lowest == int.MaxValue ? -1 : lowest;
        }

        // ── Classification ────────────────────────────────────────────────────

        // NOAA VFR/MVFR/IFR/LIFR thresholds (no SVFR)
        private static FlightCategory DetermineCategory(int visM, int ceilFt)
        {
            if (visM < 0 && ceilFt < 0) return FlightCategory.Unknown;

            // LIFR: ceiling < 500 ft OR vis < 1 SM (~1600 m)
            if ((ceilFt >= 0 && ceilFt < 500) || (visM >= 0 && visM < 1600))
                return FlightCategory.LIFR;

            // IFR: ceiling 500–999 ft OR vis 1–3 SM (~1600–4800 m)
            if ((ceilFt >= 0 && ceilFt < 1000) || (visM >= 0 && visM < 4800))
                return FlightCategory.IFR;

            // MVFR: ceiling 1000–2999 ft OR vis 3–5 SM (~4800–8000 m)
            if ((ceilFt >= 0 && ceilFt < 3000) || (visM >= 0 && visM < 8000))
                return FlightCategory.MVFR;

            return FlightCategory.VFR;
        }
    }
}
