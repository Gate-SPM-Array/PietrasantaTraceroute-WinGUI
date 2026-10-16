using System.IO;
using System.Text.RegularExpressions;
using PstrGui;
using Xunit;

namespace PietrasantaTraceroute.Tests
{
    /// <summary>Controlli di coerenza del repository: versione e binario incluso.</summary>
    public class RepositoryTests
    {
        static string Root
        {
            get
            {
                var d = new DirectoryInfo(System.AppContext.BaseDirectory);
                while (d != null && !File.Exists(Path.Combine(d.FullName, "CHANGELOG.md"))) d = d.Parent;
                Assert.NotNull(d);
                return d.FullName;
            }
        }

        static string Read(params string[] p) => File.ReadAllText(Path.Combine(Root, Path.Combine(p)));

        [Fact]
        public void VersionNumber_IsTheSameEverywhere()
        {
            var csproj = Read("src", "PietrasantaTraceroute.Gui", "PietrasantaTraceroute.Gui.csproj");
            var version = Regex.Match(csproj, @"<Version>(\d+)\.(\d+)\.(\d+)</Version>");
            Assert.True(version.Success, "Version non trovata nel csproj");
            var full = version.Groups[1].Value + "." + version.Groups[2].Value + "." + version.Groups[3].Value;
            // Nome visibile: "1.2" per le versioni x.y.0, "1.2.1" altrimenti (regola del maintainer: ogni consegna ha un numero nuovo)
            var v = version.Groups[3].Value == "0" ? version.Groups[1].Value + "." + version.Groups[2].Value : full;

            Assert.Contains("<AssemblyName>PietrasantaTracerouteGUI_v" + v + "</AssemblyName>", csproj);
            Assert.Contains("<FileVersion>" + full + ".0</FileVersion>", csproj);
            Assert.Contains("AppVersion = \"" + v + "\"", Read("src", "PietrasantaTraceroute.Gui", "MainForm.cs"));
            Assert.Matches(new Regex(@"^## v" + Regex.Escape(v) + @" ", RegexOptions.Multiline), Read("CHANGELOG.md"));
            Assert.Contains("v" + v, Read("README.md").Split('\n')[0]);
            Assert.Contains("v" + v, Read("README.it.md").Split('\n')[0]);
            Assert.Contains("\"app_version\": \"" + v + "\"", Read("lang", "en.json"));
        }

        [Fact]
        public void EmbeddedBinary_MatchesDocumentedHash()
        {
            var bin = File.ReadAllBytes(Path.Combine(Root, "src", "PietrasantaTraceroute.Gui", "res", "pietrasanta-traceroute"));
            var hash = Wsl.Sha256Hex(bin);
            Assert.Contains(hash, Read("THIRD-PARTY-NOTICES.txt"));
            Assert.Contains(hash, Read("README.md"));
        }

        [Fact]
        public void UpstreamSource_IsIncludedForGplCompliance()
        {
            var dir = Path.Combine(Root, "third_party", "pietrasanta-traceroute-0.1.36");
            Assert.True(File.Exists(Path.Combine(dir, "COPYING")));
            Assert.True(File.Exists(Path.Combine(dir, "traceroute", "traceroute.c")));
            Assert.Contains("0.1.36", File.ReadAllText(Path.Combine(dir, "VERSION")));
        }
    }
}
