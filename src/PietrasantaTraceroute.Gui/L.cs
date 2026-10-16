using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PstrGui
{
    /// <summary>Una lingua disponibile: l'italiano è incorporato nel codice, l'inglese è incorporato come risorsa
    /// e qualsiasi lingua può essere aggiunta (o sovrascritta) con un file <c>lang\&lt;codice&gt;.json</c>.</summary>
    public sealed class LanguageInfo
    {
        public string Code;
        public string Name;
        public string Source;   // "incorporato" / percorso del file
        public override string ToString() => Name;
    }

    /// <summary>
    /// Traduzione dell'interfaccia, stesso schema di AdbSync (stile gettext).
    /// Il testo italiano scritto nel codice è la chiave: <c>L.T("Avvia")</c>, <c>L.F("Hop: {0}", n)</c>.
    /// Un file di lingua associa a ogni testo italiano la sua traduzione; ciò che manca resta in italiano.
    /// Formato di <c>lang\en.json</c>:
    /// <code>
    /// { "code": "en", "language": "English", "translators": "...", "app_version": "1.2.1",
    ///   "strings": { "Avvia": "Start", "Hop: {0}": "Hops: {0}" } }
    /// </code>
    /// I segnaposto {0}, {1}… devono restare gli stessi: una traduzione che li altera viene ignorata.
    /// Ricerca del file: prima <c>lang\</c> accanto all'eseguibile (modificabile, programma portable),
    /// poi la copia incorporata nell'eseguibile (solo inglese).
    /// </summary>
    public static class L
    {
        public const string DefaultCode = "it";
        public const string DefaultName = "Italiano";

        static Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.Ordinal);

        public static string Code { get; private set; } = DefaultCode;
        public static string Name { get; private set; } = DefaultName;

        /// <summary>Messaggi prodotti dall'ultima <see cref="Init"/> (file non valido, traduzioni scartate).</summary>
        public static readonly List<string> Messages = new List<string>();

        public static string LangDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang");

        /// <summary>Traduce un testo fisso.</summary>
        public static string T(string italian)
        {
            if (_map.Count == 0 || italian == null) return italian;
            var key = italian.IndexOf('\r') >= 0 ? italian.Replace("\r\n", "\n") : italian;
            return _map.TryGetValue(key, out var t) ? t : italian;
        }

        /// <summary>Traduce un testo con segnaposto {0}, {1}… e lo compone.</summary>
        public static string F(string italianFormat, params object[] args)
        {
            var fmt = T(italianFormat);
            try { return string.Format(CultureInfo.CurrentCulture, fmt, args); }
            catch (FormatException) { return string.Format(CultureInfo.CurrentCulture, italianFormat, args); }
        }

        /// <summary>Codice da usare quando l'utente non ha ancora scelto: italiano se Windows è in italiano, altrimenti inglese.</summary>
        public static string AutoCode() =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("it", StringComparison.OrdinalIgnoreCase) ? "it" : "en";

        /// <summary>Lingue disponibili: italiano, inglese (incorporato) e ogni file valido in <c>lang\</c>.</summary>
        public static List<LanguageInfo> Available(string directory = null)
        {
            var list = new List<LanguageInfo> { new LanguageInfo { Code = DefaultCode, Name = DefaultName, Source = "built-in" } };
            var embedded = EmbeddedJson("en");
            if (embedded != null)
            {
                try
                {
                    var f = LanguageFile.Parse(embedded);
                    list.Add(new LanguageInfo { Code = f.Code ?? "en", Name = f.Language ?? "English", Source = "built-in" });
                }
                catch { }
            }
            directory = directory ?? LangDirectory;
            if (!Directory.Exists(directory)) return list;
            foreach (var file in Directory.GetFiles(directory, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var f = LanguageFile.Parse(File.ReadAllText(file, Encoding.UTF8));
                    var code = f.Code ?? Path.GetFileNameWithoutExtension(file);
                    if (code.Equals(DefaultCode, StringComparison.OrdinalIgnoreCase)) continue;
                    var existing = list.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) { existing.Source = file; continue; }   // il file esterno sostituisce quello incorporato
                    list.Add(new LanguageInfo { Code = code, Name = string.IsNullOrWhiteSpace(f.Language) ? code : f.Language, Source = file });
                }
                catch { /* file non valido: non proposto */ }
            }
            return list;
        }

        /// <summary>Attiva una lingua ("" = automatica). In caso di problemi resta l'italiano per ciò che non è utilizzabile.</summary>
        public static void Init(string code, string directory = null)
        {
            Messages.Clear();
            _map = new Dictionary<string, string>(StringComparer.Ordinal);
            Code = DefaultCode; Name = DefaultName;
            if (string.IsNullOrWhiteSpace(code)) code = AutoCode();
            if (code.Equals(DefaultCode, StringComparison.OrdinalIgnoreCase)) return;

            directory = directory ?? LangDirectory;
            string json = null, source = null;
            var path = Path.Combine(directory, code + ".json");
            try { if (File.Exists(path)) { json = File.ReadAllText(path, Encoding.UTF8); source = path; } }
            catch (Exception ex) { Messages.Add("Language file not readable: " + path + " (" + ex.Message + ")"); }
            if (json == null) { json = EmbeddedJson(code); source = "built-in"; }
            if (json == null) { Messages.Add("Language not available: " + code); return; }

            LanguageFile file;
            try { file = LanguageFile.Parse(json); }
            catch (Exception ex) { Messages.Add("Invalid language file " + source + ": " + ex.Message); return; }

            int rejected = 0;
            foreach (var kv in file.Strings)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (!SamePlaceholders(kv.Key, kv.Value)) { rejected++; continue; }
                _map[kv.Key.Replace("\r\n", "\n")] = kv.Value.Replace("\r\n", "\n");
            }
            if (rejected > 0) Messages.Add(rejected + " translations ignored (placeholders differ from the Italian text)");
            Code = file.Code ?? code;
            Name = file.Language ?? code;
        }

        static readonly Regex Placeholder = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}");

        /// <summary>Vero se le due stringhe usano gli stessi segnaposto {n} (stesso insieme, qualsiasi ordine).</summary>
        public static bool SamePlaceholders(string a, string b)
        {
            var pa = Placeholder.Matches(a ?? "").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            var pb = Placeholder.Matches(b ?? "").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);
            return pa.SequenceEqual(pb);
        }

        static string EmbeddedJson(string code)
        {
            try
            {
                using (var s = typeof(L).Assembly.GetManifestResourceStream("lang." + code + ".json"))
                {
                    if (s == null) return null;
                    using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
                }
            }
            catch { return null; }
        }
    }

    /// <summary>Contenuto di un file di lingua.</summary>
    public sealed class LanguageFile
    {
        public string Code, Language, Translators, AppVersion;
        public Dictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.Ordinal);

        public static LanguageFile Parse(string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root)) throw new FormatException("root is not an object");
            var f = new LanguageFile
            {
                Code = root.TryGetValue("code", out var c) ? c as string : null,
                Language = root.TryGetValue("language", out var l) ? l as string : null,
                Translators = root.TryGetValue("translators", out var t) ? t as string : null,
                AppVersion = root.TryGetValue("app_version", out var v) ? v as string : null,
            };
            if (root.TryGetValue("strings", out var s) && s is Dictionary<string, object> strings)
                foreach (var kv in strings) if (kv.Value is string str) f.Strings[kv.Key] = str;
            return f;
        }
    }

    /// <summary>Parser JSON minimo (oggetti, array, stringhe, numeri, true/false/null): niente dipendenze su .NET Framework 4.8.</summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            int i = 0;
            var v = Value(json, ref i);
            Ws(json, ref i);
            if (i != json.Length) throw new FormatException("unexpected text at position " + i);
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == st) throw new FormatException("unexpected character '" + c + "' at position " + i);
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }

        static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; Ws(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                Ws(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("expected a key at position " + i);
                var k = Str(s, ref i);
                Ws(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("expected ':' at position " + i);
                i++;
                d[k] = Value(s, ref i);
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return d; }
                throw new FormatException("expected ',' or '}' at position " + i);
            }
        }

        static List<object> Arr(string s, ref int i)
        {
            var a = new List<object>();
            i++; Ws(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Add(Value(s, ref i));
                Ws(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return a; }
                throw new FormatException("expected ',' or ']' at position " + i);
            }
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // "
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: throw new FormatException("bad escape \\" + e);
                }
            }
            throw new FormatException("unterminated string");
        }
    }
}
