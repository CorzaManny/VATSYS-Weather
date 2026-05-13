using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace WeatherPlugin.Services
{
    // Per-station METAR + TAF + ATIS from the Airservices Australia NAIPS SOAP service.
    //
    // Setup: create a plain-text naips.cfg file next to WeatherPlugin.dll, OR at
    //        %APPDATA%\vatSys\naips.cfg, with the two lines:
    //
    //   username=YOURNAIPSUSERNAME
    //   password=YOURNAIPSPASSWORD
    //
    // Free registration: https://www.airservicesaustralia.com/naips/
    // Without credentials the plugin falls back to VATSIM METAR + AWC TAF.
    public static class NaipsService
    {
        private const string Endpoint =
            "https://www.airservicesaustralia.com/naips/briefing-service?wsdl";

        // SOAP 1.1 request body — {0}=USERNAME {1}=PASSWORD {2}=ICAO
        private const string SoapFmt =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<SOAP-ENV:Envelope" +
            " xmlns:ns0=\"http://schemas.xmlsoap.org/soap/envelope/\"" +
            " xmlns:ns1=\"http://www.airservicesaustralia.com/naips/xsd\"" +
            " xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"" +
            " xmlns:SOAP-ENV=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
            "<SOAP-ENV:Header/>" +
            "<ns0:Body>" +
            "<ns1:loc-brief-rqs password=\"{1}\" requestor=\"{0}\" source=\"atis\">" +
            "<ns1:loc>{2}</ns1:loc>" +
            "<ns1:flags met=\"true\"/>" +
            "</ns1:loc-brief-rqs>" +
            "</ns0:Body>" +
            "</SOAP-ENV:Envelope>";

        public static string Username { get; private set; }
        public static string Password { get; private set; }
        public static bool HasCredentials => Username != null;

        static NaipsService()
        {
            LoadCredentials();
        }

        private static void LoadCredentials()
        {
            var dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "vatSys");

            foreach (var dir in new[] { dllDir, appData })
            {
                var path = Path.Combine(dir, "naips.cfg");
                if (!File.Exists(path)) continue;

                string user = null, pass = null;
                foreach (var raw in File.ReadAllLines(path))
                {
                    var eq = raw.IndexOf('=');
                    if (eq < 0) continue;
                    var key = raw.Substring(0, eq).Trim().ToLowerInvariant();
                    var val = raw.Substring(eq + 1).Trim();
                    if (key == "username") user = val;
                    else if (key == "password") pass = val;
                }

                if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass))
                {
                    Username = user;
                    Password = pass;
                    return;
                }
            }
        }

        // Returns (metar, taf, atis) — any may be null if not present in the response.
        public static async Task<(string Metar, string Taf, string Atis)> FetchAsync(
            string icao, HttpClient http)
        {
            var soap = string.Format(SoapFmt, Username.ToUpperInvariant(), Password, icao.ToUpperInvariant());
            var content = new StringContent(soap, Encoding.UTF8, "text/xml");

            using (var req = new HttpRequestMessage(HttpMethod.Post, Endpoint))
            {
                req.Content = content;
                req.Headers.Add("Accept",        "text/xml");
                req.Headers.Add("Cache-Control", "no-cache");
                req.Headers.Add("Pragma",        "no-cache");
                req.Headers.Add("SOAPAction",    Endpoint);

                using (var resp = await http.SendAsync(req))
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    return (ExtractMet(body), ExtractTaf(body), ExtractAtis(body, icao));
                }
            }
        }

        // Finds the first METAR or SPECI line in the raw SOAP response string.
        private static string ExtractMet(string body)
        {
            foreach (var prefix in new[] { "METAR ", "SPECI " })
            {
                int i = body.IndexOf(prefix, StringComparison.Ordinal);
                if (i < 0) continue;
                int end = body.IndexOf('\n', i);
                var line = (end < 0 ? body.Substring(i) : body.Substring(i, end - i))
                           .Trim().TrimEnd('=');
                if (line.Length > 8) return line;
            }
            return null;
        }

        // Extracts a TAF block — multi-line, ends at a blank line or opening XML tag.
        private static string ExtractTaf(string body)
        {
            int i = body.IndexOf("TAF ", StringComparison.Ordinal);
            if (i < 0) return null;

            var sb = new StringBuilder();
            foreach (var raw in body.Substring(i).Split('\n'))
            {
                var line = raw.TrimEnd().TrimEnd('=');
                if (string.IsNullOrWhiteSpace(line)) break;
                if (line.TrimStart().StartsWith("<")) break;
                sb.AppendLine(line);
            }
            var result = sb.ToString().Trim();
            return result.Length > 8 ? result : null;
        }

        // Extracts an ATIS block — looks for "ATIS ICAO" then collects until blank line or XML tag.
        private static string ExtractAtis(string body, string icao)
        {
            // Look for "ATIS XXXX" where XXXX is the station identifier
            var marker = "ATIS " + icao.ToUpperInvariant();
            int i = body.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;

            var sb = new StringBuilder();
            foreach (var raw in body.Substring(i).Split('\n'))
            {
                var line = raw.TrimEnd().TrimEnd('=');
                if (string.IsNullOrWhiteSpace(line)) break;
                if (line.TrimStart().StartsWith("<")) break;
                sb.AppendLine(line);
            }
            var result = sb.ToString().Trim();
            return result.Length > 8 ? result : null;
        }
    }
}
