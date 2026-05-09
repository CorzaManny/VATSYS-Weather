using System;
using System.Linq;
using System.Text.RegularExpressions;
using WeatherPlugin.Models;

namespace WeatherPlugin.Services
{
    public static class MetarDecoder
    {
        // ── Public entry point ────────────────────────────────────────────────

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

        // ── Parsers ───────────────────────────────────────────────────────────

        public static int ParseVisibilityMeters(string upper)
        {
            if (upper.Contains("CAVOK")) return 10000;

            var tokens = upper.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            bool pastWind = false;

            foreach (var tok in tokens)
            {
                if (tok == "METAR" || tok == "SPECI") continue;

                // ICAO: exactly 4 letters
                if (tok.Length == 4 && tok.All(char.IsLetter)) continue;

                // Observation time: DDHHMMz
                if (tok.EndsWith("Z") && tok.Length >= 6 &&
                    tok.Substring(0, tok.Length - 1).All(char.IsDigit)) continue;

                if (tok == "AUTO" || tok == "COR" || tok == "NIL") continue;

                // Wind group (marks everything before as pre-visibility)
                if (tok.EndsWith("KT") || tok.EndsWith("MPS") ||
                    (tok.StartsWith("VRB") && tok.Length >= 5))
                { pastWind = true; continue; }

                if (!pastWind) continue;

                // QNH: Q followed by 4 digits
                if (tok.Length == 5 && tok[0] == 'Q' && tok.Substring(1).All(char.IsDigit)) continue;

                // Temperature/dewpoint group signals end of visibility section
                if (Regex.IsMatch(tok, @"^M?\d{2}/M?\d{2}$")) break;

                // 4-digit visibility, optionally followed by NDV (no directional variation)
                var visPart = tok.Length >= 4 ? tok.Substring(0, 4) : tok;
                if (visPart.Length == 4 && visPart.All(char.IsDigit) &&
                    (tok.Length == 4 || tok == visPart + "NDV"))
                    return int.Parse(visPart);
            }
            return -1;
        }

        public static int ParseCeilingFeet(string upper)
        {
            // Explicit no-cloud groups — ceiling irrelevant, use vis only
            if (Regex.IsMatch(upper, @"\b(SKC|CLR|NSC)\b")) return -1;

            int lowest = int.MaxValue;

            // Broken or overcast cloud layers: BKN/OVC + 3-digit height (hundreds of feet)
            foreach (Match m in Regex.Matches(upper, @"\b(BKN|OVC)(\d{3})\b"))
            {
                int alt = int.Parse(m.Groups[2].Value) * 100;
                if (alt < lowest) lowest = alt;
            }

            // Vertical visibility in obscuration (e.g. fog): VV002 = 200ft ceiling equivalent
            foreach (Match m in Regex.Matches(upper, @"\bVV(\d{3})\b"))
            {
                int alt = int.Parse(m.Groups[1].Value) * 100;
                if (alt < lowest) lowest = alt;
            }

            return lowest == int.MaxValue ? -1 : lowest;
        }

        // ── Classification ────────────────────────────────────────────────────

        // Thresholds align with Australian VMC minima (vis ≥5000m, ceiling ≥1500ft in CAS)
        // and standard LIFR/IFR breakpoints used internationally.
        private static FlightCategory DetermineCategory(int visM, int ceilFt)
        {
            if (visM < 0 && ceilFt < 0) return FlightCategory.Unknown;

            // LIFR: vis < 800m OR ceiling < 200ft
            if ((visM >= 0 && visM < 800) || (ceilFt >= 0 && ceilFt < 200))
                return FlightCategory.LIFR;

            // IFR: vis < 1600m OR ceiling < 500ft
            if ((visM >= 0 && visM < 1600) || (ceilFt >= 0 && ceilFt < 500))
                return FlightCategory.IFR;

            // SVFR: vis < 5000m OR ceiling < 1500ft (below AU VMC minima in CAS)
            if ((visM >= 0 && visM < 5000) || (ceilFt >= 0 && ceilFt < 1500))
                return FlightCategory.SVFR;

            // MVFR: vis < 8000m OR ceiling < 3000ft (marginal)
            if ((visM >= 0 && visM < 8000) || (ceilFt >= 0 && ceilFt < 3000))
                return FlightCategory.MVFR;

            return FlightCategory.VFR;
        }
    }
}
