using System.IO;
using System.Linq;
using PstrGui;
using Xunit;

namespace PietrasantaTraceroute.Tests
{
    public class ParserTests
    {
        static HopResult Hop(string line)
        {
            var h = TracerouteParser.ParseHopLine(line);
            Assert.NotNull(h);
            return h;
        }

        [Fact]
        public void HopWithNameEqualToIp_DoesNotDuplicateName()
        {
            var h = Hop("   1  192.0.2.1 (192.0.2.1)  0.247 ms  0.193 ms  0.185 ms");
            Assert.Equal(1, h.Hop);
            Assert.Empty(h.Names);
            Assert.Equal(new[] { "192.0.2.1" }, h.Ips);
            Assert.Equal(3, h.Probes);
            Assert.Equal(0.185, h.Min);
            Assert.Equal(0.247, h.Max);
            Assert.Equal(0, h.LossPercent);
        }

        [Fact]
        public void AllTimeouts_IsNoReplyWithFullLoss()
        {
            var h = Hop("   3  * * *");
            Assert.True(h.NoReply);
            Assert.Equal(3, h.Timeouts);
            Assert.Equal(100, h.LossPercent);
            Assert.Null(h.Avg);
        }

        [Fact]
        public void TosDscpEcn_IsExtractedOncePerHop()
        {
            var h = Hop("   1  192.0.2.1 <TOS:185,DSCP:46,ECN:1>  0.187 ms <TOS:185,DSCP:46,ECN:1>  0.139 ms");
            Assert.Equal(new[] { "TOS:185,DSCP:46,ECN:1" }, h.TosInfo);
            Assert.Equal(2, h.Rtts.Count);
        }

        [Fact]
        public void MultipleRespondersAndAs_AreAllKept()
        {
            var h = Hop(" 5  ae-1.r20.example.net (129.250.6.94) [AS2914]  12.512 ms  xe-0.r21.example.net (129.250.6.98) [AS2914]  12.701 ms *");
            Assert.Equal(new[] { "ae-1.r20.example.net", "xe-0.r21.example.net" }, h.Names);
            Assert.Equal(new[] { "129.250.6.94", "129.250.6.98" }, h.Ips);
            Assert.Equal(new[] { "AS2914" }, h.AsNumbers);
            Assert.Equal(1, h.Timeouts);
            Assert.Equal(3, h.Probes);
        }

        [Fact]
        public void Annotations_MtuAndMpls_GoToNotes()
        {
            Assert.Contains("F=1400", Hop("   1  192.0.2.1  0.256 ms F=1400").Notes);
            Assert.Contains("!H", Hop(" 6  10.0.0.1  1.234 ms !H  1.111 ms !H  *").Notes);
            Assert.Contains("<MPLS:L=24001,E=0,S=1,T=1>",
                Hop(" 9  router.example (192.0.2.9) [AS64500/AS64501]  3.000 ms <MPLS:L=24001,E=0,S=1,T=1>  3.100 ms").Notes);
        }

        [Theory]
        [InlineData(" 7  2001:4860:0:1::5c5e  14.211 ms  14.100 ms", "2001:4860:0:1::5c5e")]
        [InlineData("10  fe80::1%eth0  0.500 ms", "fe80::1%eth0")]
        public void Ipv6Addresses_AreRecognised(string line, string ip)
        {
            Assert.Equal(new[] { ip }, Hop(line).Ips);
        }

        [Theory]
        [InlineData("hand 10.209 ms")]
        [InlineData("nonexistent.invalid: Name or service not known")]
        [InlineData("Cannot handle \"host\" cmdline arg `x' on position 1 (argc 2)")]
        [InlineData("2 hops")]
        [InlineData("")]
        public void NonHopLines_AreRejected(string line)
        {
            Assert.Null(TracerouteParser.ParseHopLine(line));
        }

        [Fact]
        public void Summary_HeaderAndFooterLines()
        {
            var s = new TraceSummary();
            Assert.True(TracerouteParser.TryParseSummary("traceroute to github.com (140.82.113.4), 255 hops max, 53 byte packets", s));
            Assert.True(TracerouteParser.TryParseSummary("   Timedout: false", s));
            Assert.True(TracerouteParser.TryParseSummary("   Duration: 0.321 ms", s));
            Assert.True(TracerouteParser.TryParseSummary("   DestinationReached: true", s));
            Assert.True(TracerouteParser.TryParseSummary("   Path MTU: 1400 (Potentially overestimated)", s));
            Assert.False(TracerouteParser.TryParseSummary("   1  192.0.2.1  0.2 ms", s));
            Assert.Equal("140.82.113.4", s.TargetIp);
            Assert.False(s.TimedOut);
            Assert.True(s.DestinationReached);
            Assert.Equal("0.321 ms", s.Duration);
            Assert.Equal("1400 (Potentially overestimated)", s.PathMtu);
        }

        [Fact]
        public void RealToolOutput_EveryLineIsClassified()
        {
            // Output reale di Pietrasanta traceroute 0.1.36 (UDP, ICMP, TCP InSession, DSCP/ECN, --mtu, -e)
            // più alcuni casi sintetici: ogni riga deve essere hop, riepilogo o riga nota da ignorare.
            var lines = File.ReadAllLines(Path.Combine("samples", "outputs.txt"));
            var s = new TraceSummary();
            int hops = 0;
            foreach (var l in lines)
            {
                if (TracerouteParser.TryParseSummary(l, s)) continue;
                if (TracerouteParser.ParseHopLine(l) != null) { hops++; continue; }
                Assert.StartsWith("hand ", l.Trim()); // tempo di handshake di TCP InSession
            }
            Assert.Equal(24, hops);
        }
    }
}
