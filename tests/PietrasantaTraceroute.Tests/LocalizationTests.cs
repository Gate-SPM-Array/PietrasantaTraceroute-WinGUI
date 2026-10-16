using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using PstrGui;
using Xunit;

namespace PietrasantaTraceroute.Tests
{
    /// <summary>
    /// Localizzazione (stesso schema di AdbSync): ogni testo dell'interfaccia passa da L.T("...") / L.F("...", x)
    /// con un letterale italiano, che è la chiave di lang/en.json.
    /// </summary>
    [Collection("Localization")]   // L è statica: questi test non girano in parallelo tra loro
    public class LocalizationTests : IDisposable
    {
        public void Dispose() => L.Init("it");

        static string Root
        {
            get
            {
                var d = new DirectoryInfo(AppContext.BaseDirectory);
                while (d != null && !File.Exists(Path.Combine(d.FullName, "CHANGELOG.md"))) d = d.Parent;
                Assert.NotNull(d);
                return d.FullName;
            }
        }

        static string LangDir => Path.Combine(Root, "lang");
        static string SrcDir => Path.Combine(Root, "src", "PietrasantaTraceroute.Gui");

        /// <summary>Sorgenti senza le righe di commento (gli esempi nei commenti non sono chiavi).</summary>
        static IEnumerable<(string File, string Code)> Sources() =>
            Directory.GetFiles(SrcDir, "*.cs").Select(f => (Path.GetFileName(f),
                string.Join("\n", File.ReadAllLines(f).Where(l => !l.TrimStart().StartsWith("//")))));

        static readonly Regex Call = new Regex(@"\bL\.[TF]\(\s*""((?:[^""\\]|\\.)*)""");

        static string Unescape(string s) => JsonSerializer.Deserialize<string>("\"" + s.Replace("\\'", "'") + "\"");

        static HashSet<string> KeysInCode() =>
            new HashSet<string>(Sources().SelectMany(s => Call.Matches(s.Code).Cast<Match>().Select(m => Unescape(m.Groups[1].Value))));

        static Dictionary<string, string> EnglishStrings()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(LangDir, "en.json")));
            return doc.RootElement.GetProperty("strings").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        }

        [Fact]
        public void EveryTranslatedTextInTheCode_HasAnEnglishTranslation()
        {
            var en = EnglishStrings();
            var missing = KeysInCode().Where(k => !en.ContainsKey(k)).OrderBy(k => k).ToList();
            Assert.True(missing.Count == 0, "Mancano in lang/en.json:\n" + string.Join("\n", missing));
        }

        [Fact]
        public void EnglishFile_HasNoOrphanKeys()
        {
            var code = KeysInCode();
            var orphans = EnglishStrings().Keys.Where(k => !code.Contains(k)).OrderBy(k => k).ToList();
            Assert.True(orphans.Count == 0, "Chiavi non più usate nel codice:\n" + string.Join("\n", orphans));
        }

        [Fact]
        public void Translations_KeepTheSamePlaceholders()
        {
            var bad = EnglishStrings().Where(kv => !L.SamePlaceholders(kv.Key, kv.Value) || kv.Value.Trim().Length == 0)
                                      .Select(kv => kv.Key).ToList();
            Assert.True(bad.Count == 0, "Segnaposto diversi o traduzione vuota:\n" + string.Join("\n", bad));
        }

        [Fact]
        public void LocalizationCalls_UseOnlyLiterals()
        {
            // L.T(variabile) o L.T($"...") non sarebbero estraibili come chiavi
            var bad = Sources().SelectMany(s => Regex.Matches(s.Code, @"\bL\.[TF]\(\s*(?!"")").Cast<Match>()
                                   .Select(m => s.File + ": " + s.Code.Substring(m.Index, Math.Min(60, s.Code.Length - m.Index)))).ToList();
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        // Testi a video uguali in italiano e inglese (sigle, nomi tecnici): ammessi senza L.T
        static readonly HashSet<string> SameInBothLanguages = new HashSet<string>
        {
            "", "&File", "&?", "Tool:", "IP:", "DSCP:", "ECN:", "Hop", "Host", "AS", "Min (ms)", "Max (ms)", "TOS / DSCP / ECN",
            "Lookup AS (-A)", "Path MTU (--mtu)", "ICMP Echo (-I)", "TCP SYN (-T)", "TCP InSession (--tcpinsession)",
            "TCP connect (-M tcpconn)", "UDP InSession (--udpinsession)", "QUIC (--quic)", "DNS (--dns)", "DCCP (-D)",
            "GUI", "Path MTU", "Path MTU: ", "traceroute ", "wsl.exe        : ", "[stderr] ",
        };

        [Fact]
        public void NoUntranslatedLiteralsInUiCalls()
        {
            var ui = new Regex(@"(?:SetStatus|SetToolLabel|MessageBox\.Show\(this,|Columns\.Add|new TabPage|SetToolTip\(\w+,|DropDownItems\.Add|cm\.Items\.Add|new ToolStripMenuItem|Text\s*=|Lbl|H|Label\s*=|Help\s*=|parts\.Add|AppendRaw|log|Line)\(?\s*""((?:[^""\\]|\\.)*)""");
            var bad = new List<string>();
            foreach (var (file, code) in Sources().Where(s => s.File == "MainForm.cs" || s.File == "Wsl.cs"))
                foreach (Match m in ui.Matches(code))
                {
                    var text = Unescape(m.Groups[1].Value);
                    if (SameInBothLanguages.Contains(text) || !Regex.IsMatch(text, "[a-zà-ù]{3,}")) continue;
                    if (text.StartsWith("=== ") || text.StartsWith("> ") || text.StartsWith("$ ")) continue;
                    bad.Add(file + ": " + text);
                }
            Assert.True(bad.Count == 0, "Testi a video non tradotti (usare L.T):\n" + string.Join("\n", bad));
        }

        [Fact]
        public void English_TranslatesAndFormats()
        {
            L.Init("en", LangDir);
            Assert.Equal("en", L.Code);
            Assert.Equal("English", L.Name);
            Assert.Equal("Start", L.T("Avvia"));
            Assert.Equal("hops: 7", L.F("hop: {0}", 7));
            Assert.Equal("Testo non presente", L.T("Testo non presente"));   // ciò che manca resta in italiano
        }

        [Fact]
        public void Italian_IsTheIdentity()
        {
            L.Init("it", LangDir);
            Assert.Equal("Avvia", L.T("Avvia"));
            Assert.Equal("hop: 7", L.F("hop: {0}", 7));
        }

        [Fact]
        public void TranslationWithDifferentPlaceholders_IsIgnored()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pstr_lang_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "xx.json"),
                    "{ \"code\": \"xx\", \"language\": \"Test\", \"strings\": { \"hop: {0}\": \"salti: {1}\", \"Avvia\": \"Go\" } }");
                L.Init("xx", dir);
                Assert.Equal("Go", L.T("Avvia"));
                Assert.Equal("hop: 3", L.F("hop: {0}", 3));          // traduzione scartata
                Assert.Contains(L.Messages, m => m.Contains("ignored"));
                var langs = L.Available(dir).Select(l => l.Code).ToList();
                Assert.Contains("it", langs);
                Assert.Contains("xx", langs);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void MissingOrInvalidLanguage_FallsBackToItalian()
        {
            L.Init("zz", LangDir);
            Assert.Equal("it", L.Code);
            Assert.Equal("Avvia", L.T("Avvia"));
        }

        [Fact]
        public void AutomaticLanguage_FollowsWindows()
        {
            var saved = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = new CultureInfo("it-IT");
                Assert.Equal("it", L.AutoCode());
                CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
                Assert.Equal("en", L.AutoCode());
            }
            finally { CultureInfo.CurrentUICulture = saved; }
        }

        [Fact]
        public void MiniJson_ParsesEscapesLikeSystemTextJson()
        {
            var json = File.ReadAllText(Path.Combine(LangDir, "en.json"));
            var mine = LanguageFile.Parse(json).Strings;
            Assert.Equal(EnglishStrings(), mine);
            var o = (Dictionary<string, object>)MiniJson.Parse("{\"a\":\"x\\n\\\"y\\\" \\u00e8\",\"b\":[1,true,null],\"c\":{}}");
            Assert.Equal("x\n\"y\" è", o["a"]);
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\": }"));
        }
    }
}
