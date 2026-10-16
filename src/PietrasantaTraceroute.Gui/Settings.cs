using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PstrGui
{
    /// <summary>
    /// Preferenze del programma, in modalità PORTABLE.
    ///
    /// Lettura:   1) cartella dell'eseguibile  (PietrasantaTracerouteGUI.settings.ini)
    ///            2) se lì non esiste: %APPDATA%\PietrasantaTracerouteGUI\settings.ini
    /// Scrittura: sempre nella cartella dell'eseguibile, se scrivibile
    ///            (le preferenze trovate in %APPDATA% vengono così copiate in locale);
    ///            %APPDATA% solo se la cartella dell'eseguibile è in sola lettura.
    ///
    /// Il nome del file non contiene la versione: aggiornando l'eseguibile nella stessa
    /// cartella le preferenze vengono mantenute.
    /// </summary>
    public sealed class Settings
    {
        public const string LocalFileName = "PietrasantaTracerouteGUI.settings.ini";

        public string LastDistro = "";
        public int MethodIndex = 0;
        public bool NoDns = false;
        /// <summary>Codice della lingua dell'interfaccia ("" = automatica, secondo Windows).</summary>
        public string Language = "";
        public List<string> TargetHistory = new List<string>();

        /// <summary>File da cui sono state lette le preferenze ("" = valori predefiniti).</summary>
        public string LoadedFrom { get; private set; } = "";
        /// <summary>File in cui sono state salvate l'ultima volta.</summary>
        public string SavedTo { get; private set; } = "";

        public static string ExeDir => Path.GetDirectoryName(Application.ExecutablePath);
        public static string LocalPath => Path.Combine(ExeDir, LocalFileName);
        static string AppDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PietrasantaTracerouteGUI");
        public static string AppDataPath => Path.Combine(AppDataDir, "settings.ini");

        /// <summary>Percorso in cui verranno salvate le preferenze.</summary>
        public static string TargetPath => IsDirWritable(ExeDir) ? LocalPath : AppDataPath;

        public bool IsPortable => string.Equals(SavedTo.Length > 0 ? SavedTo : TargetPath, LocalPath, StringComparison.OrdinalIgnoreCase);

        public static Settings Load()
        {
            var s = new Settings();
            foreach (var path in new[] { LocalPath, AppDataPath })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    s.Parse(File.ReadAllLines(path, Encoding.UTF8));
                    s.LoadedFrom = path;
                    break;
                }
                catch { /* file illeggibile: si prova il successivo */ }
            }
            return s;
        }

        void Parse(IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("#") || line.TrimStart().StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var k = line.Substring(0, eq).Trim();
                var v = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "LastDistro": LastDistro = v; break;
                    case "MethodIndex": int.TryParse(v, out MethodIndex); break;
                    case "NoDns": NoDns = v == "1"; break;
                    case "Language": Language = v; break;
                    case "Target": if (v.Length > 0 && !TargetHistory.Contains(v)) TargetHistory.Add(v); break;
                }
            }
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Pietrasanta Traceroute GUI - preferenze (modalita' portable)");
            sb.AppendLine("LastDistro=" + LastDistro);
            sb.AppendLine("MethodIndex=" + MethodIndex);
            sb.AppendLine("NoDns=" + (NoDns ? "1" : "0"));
            sb.AppendLine("Language=" + Language);
            foreach (var t in TargetHistory.Take(20)) sb.AppendLine("Target=" + t);
            var content = sb.ToString();

            // 1) locale (cartella dell'eseguibile)
            try
            {
                File.WriteAllText(LocalPath, content, new UTF8Encoding(false));
                SavedTo = LocalPath;
                return;
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            // 2) fallback %APPDATA% (cartella dell'eseguibile in sola lettura)
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(AppDataPath, content, new UTF8Encoding(false));
            SavedTo = AppDataPath;
        }

        static bool IsDirWritable(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".pstr_write_test_" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }
    }
}
