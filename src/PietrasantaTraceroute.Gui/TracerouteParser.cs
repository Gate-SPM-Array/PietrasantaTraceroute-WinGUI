using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace PstrGui
{
    /// <summary>Un hop del percorso, ricostruito da una riga di output di Pietrasanta traceroute.</summary>
    public sealed class HopResult
    {
        public int Hop;
        public readonly List<string> Names = new List<string>();   // nomi DNS (se risolti)
        public readonly List<string> Ips = new List<string>();     // indirizzi IP che hanno risposto
        public readonly List<string> AsNumbers = new List<string>();
        public readonly List<double> Rtts = new List<double>();
        public int Timeouts;
        public readonly List<string> TosInfo = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public string Raw = "";

        public int Probes => Rtts.Count + Timeouts;
        public double? Min => Rtts.Count > 0 ? Rtts.Min() : (double?)null;
        public double? Max => Rtts.Count > 0 ? Rtts.Max() : (double?)null;
        public double? Avg => Rtts.Count > 0 ? Rtts.Average() : (double?)null;
        public double LossPercent => Probes == 0 ? 0 : 100.0 * Timeouts / Probes;
        public bool NoReply => Rtts.Count == 0 && Ips.Count == 0;

        public string HostDisplay => Names.Count > 0 ? string.Join(", ", Names) : (Ips.Count > 0 ? string.Join(", ", Ips) : "*");
        public string IpDisplay => string.Join(", ", Ips);
        public string AsDisplay => string.Join(", ", AsNumbers);
        public string TosDisplay => string.Join(" ", TosInfo);
        public string NotesDisplay => string.Join(" ", Notes);
    }

    /// <summary>Righe di riepilogo stampate da Pietrasanta traceroute in fondo all'esecuzione.</summary>
    public sealed class TraceSummary
    {
        public string Header;            // "traceroute to x (y), 30 hops max, ..."
        public string TargetIp;
        public bool? TimedOut;
        public bool? DestinationReached;
        public string Duration;          // es. "5005.919 ms"
        public string PathMtu;           // es. "1400 (Potentially overestimated)"
    }

    public static class TracerouteParser
    {
        static readonly Regex HopLine = new Regex(@"^\s*(\d{1,3})\s+(.+)$", RegexOptions.Compiled);

        // Ordine delle alternative importante: RTT prima del token generico.
        static readonly Regex Token = new Regex(
            @"(?<rtt>\d+(?:\.\d+)?)\s+ms\b" +
            @"|(?<paren>\([^)]*\))" +
            @"|(?<as>\[[^\]]*\])" +
            @"|(?<angle><[^>]*>)" +
            @"|(?<star>\*)" +
            @"|(?<bang>![A-Za-z0-9<>=]*)" +
            @"|(?<word>\S+)",
            RegexOptions.Compiled);

        static readonly Regex HeaderLine = new Regex(@"^traceroute to (\S+) \(([^)]+)\)", RegexOptions.Compiled);

        public static HopResult ParseHopLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var m = HopLine.Match(line);
            if (!m.Success) return null;

            var hop = new HopResult { Hop = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Raw = line.Trim() };
            var tokens = Token.Matches(m.Groups[2].Value).Cast<Match>().ToList();

            // Una riga di hop valida deve contenere almeno un RTT, un asterisco o un indirizzo.
            bool plausible = false;

            for (int i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Groups["rtt"].Success)
                {
                    hop.Rtts.Add(double.Parse(t.Groups["rtt"].Value, CultureInfo.InvariantCulture));
                    plausible = true;
                }
                else if (t.Groups["star"].Success)
                {
                    hop.Timeouts++;
                    plausible = true;
                }
                else if (t.Groups["as"].Success)
                {
                    var v = t.Value.Trim('[', ']');
                    if (v != "*" && v.Length > 0) AddUnique(hop.AsNumbers, v);
                }
                else if (t.Groups["angle"].Success)
                {
                    var v = t.Value.Trim('<', '>');
                    if (v.StartsWith("TOS:", StringComparison.OrdinalIgnoreCase)) AddUnique(hop.TosInfo, v);
                    else AddUnique(hop.Notes, t.Value);
                }
                else if (t.Groups["bang"].Success)
                {
                    AddUnique(hop.Notes, t.Value);
                }
                else if (t.Groups["paren"].Success)
                {
                    // parentesi non precedute da un nome: le trattiamo come IP se lo sono
                    var v = t.Value.Trim('(', ')');
                    if (IsIp(v)) { AddUnique(hop.Ips, v); plausible = true; }
                    else AddUnique(hop.Notes, t.Value);
                }
                else // word
                {
                    var w = t.Value;
                    bool nextIsParen = i + 1 < tokens.Count && tokens[i + 1].Groups["paren"].Success;
                    if (nextIsParen)
                    {
                        var ip = tokens[i + 1].Value.Trim('(', ')');
                        if (IsIp(ip))
                        {
                            if (!string.Equals(w, ip, StringComparison.OrdinalIgnoreCase)) AddUnique(hop.Names, w);
                            AddUnique(hop.Ips, ip);
                            plausible = true;
                            i++; // consuma la parentesi
                            continue;
                        }
                    }
                    if (IsIp(w)) { AddUnique(hop.Ips, w); plausible = true; }
                    else AddUnique(hop.Notes, w); // es. F=1400, MTU, annotazioni varie
                }
            }

            return plausible ? hop : null;
        }

        /// <summary>Aggiorna il riepilogo se la riga è un header o una riga finale. Ritorna true se riconosciuta.</summary>
        public static bool TryParseSummary(string line, TraceSummary s)
        {
            if (line == null) return false;
            var t = line.Trim();
            var h = HeaderLine.Match(t);
            if (h.Success) { s.Header = t; s.TargetIp = h.Groups[2].Value; return true; }
            if (t.StartsWith("Timedout:", StringComparison.Ordinal)) { s.TimedOut = ParseBool(t.Substring(9)); return true; }
            if (t.StartsWith("DestinationReached:", StringComparison.Ordinal)) { s.DestinationReached = ParseBool(t.Substring(19)); return true; }
            if (t.StartsWith("Duration:", StringComparison.Ordinal)) { s.Duration = t.Substring(9).Trim(); return true; }
            if (t.StartsWith("Path MTU:", StringComparison.Ordinal)) { s.PathMtu = t.Substring(9).Trim(); return true; }
            return false;
        }

        static bool? ParseBool(string v)
        {
            v = v.Trim();
            if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }

        public static bool IsIp(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.IndexOf('.') < 0 && s.IndexOf(':') < 0) return false;
            var core = s;
            int pct = core.IndexOf('%'); // zone id IPv6
            if (pct > 0) core = core.Substring(0, pct);
            return IPAddress.TryParse(core, out _);
        }

        static void AddUnique(List<string> list, string v)
        {
            if (!list.Contains(v)) list.Add(v);
        }
    }
}
