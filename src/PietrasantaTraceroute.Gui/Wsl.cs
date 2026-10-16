using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PstrGui
{
    public sealed class WslDistro
    {
        public string Name;
        public bool IsDefault;
        public int Version;
        public string State;
        public override string ToString() =>
            Name + (IsDefault ? "  " + L.T("(predefinita)") : "") + (Version == 1 ? "  " + L.T("[WSL1 - non supportata]") : "");
    }

    public sealed class RunResult
    {
        public int ExitCode;
        public string StdOut = "";
        public string StdErr = "";
        public string All => (StdOut + "\n" + StdErr).Trim();
    }

    public enum ToolState { Unknown, NotInstalled, Outdated, BrokenDeps, Ready }

    public sealed class ToolStatus
    {
        public ToolState State;
        public string Version = "";
        public string Detail = "";
    }

    /// <summary>Tutte le interazioni con wsl.exe.</summary>
    public static class Wsl
    {
        public const string ToolPath = "/usr/local/bin/pietrasanta-traceroute";
        public const string WrapperPath = "/usr/local/bin/pstr-run";
        public const string PidMarker = "PSTR_PID:";

        // Script di avvio: comunica il PID (per poter interrompere) e poi esegue il tool con gli argomenti ricevuti.
        // Nessuna interpretazione da shell degli argomenti: vengono passati come "$@".
        const string WrapperScript =
            "#!/bin/sh\n" +
            "# Installato da Pietrasanta Traceroute GUI per Windows\n" +
            "echo \"" + PidMarker + "$$\"\n" +
            "exec " + ToolPath + " \"$@\"\n";

        static string _exe;
        public static string WslExe
        {
            get
            {
                if (_exe != null) return _exe;
                var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var candidates = new[]
                {
                    Path.Combine(win, "System32", "wsl.exe"),
                    Path.Combine(win, "Sysnative", "wsl.exe"),
                };
                _exe = candidates.FirstOrDefault(File.Exists) ?? "wsl.exe";
                return _exe;
            }
        }

        public static bool IsWslPresent() => File.Exists(WslExe);

        // ---------------------------------------------------------------- esecuzione processi

        public static string QuoteArg(string a)
        {
            if (a == null) a = "";
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"', '\n' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in a)
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1).Append('"'); bs = 0; continue; }
                sb.Append('\\', bs); bs = 0; sb.Append(c);
            }
            sb.Append('\\', bs * 2).Append('"');
            return sb.ToString();
        }

        public static string JoinArgs(IEnumerable<string> args) => string.Join(" ", args.Select(QuoteArg));

        /// <summary>wsl.exe scrive i propri messaggi in UTF-16, i programmi Linux in UTF-8: rileviamo quale.</summary>
        public static string Decode(byte[] b)
        {
            if (b == null || b.Length == 0) return "";
            int zeros = 0, n = Math.Min(b.Length, 400);
            for (int i = 1; i < n; i += 2) if (b[i] == 0) zeros++;
            string s = zeros > n / 6 ? Encoding.Unicode.GetString(b) : Encoding.UTF8.GetString(b);
            return s.Replace("\0", "").TrimStart('﻿');
        }

        public static Task<RunResult> RunAsync(IEnumerable<string> args, byte[] stdin = null, int timeoutMs = 120000)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo(WslExe, JoinArgs(args))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true, // sempre: lo chiudiamo subito se non serve
                };
                psi.EnvironmentVariables["WSL_UTF8"] = "1";
                using (var p = Process.Start(psi))
                {
                    var outMs = new MemoryStream();
                    var errMs = new MemoryStream();
                    var tOut = p.StandardOutput.BaseStream.CopyToAsync(outMs);
                    var tErr = p.StandardError.BaseStream.CopyToAsync(errMs);
                    if (stdin == null) { try { p.StandardInput.Close(); } catch { } }
                    else
                    {
                        try
                        {
                            p.StandardInput.BaseStream.Write(stdin, 0, stdin.Length);
                            p.StandardInput.BaseStream.Flush();
                        }
                        catch (IOException) { /* il processo può essere già terminato con errore */ }
                        finally { try { p.StandardInput.Close(); } catch { } }
                    }
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return new RunResult { ExitCode = -1, StdErr = L.F("Timeout dopo {0} s", timeoutMs / 1000) };
                    }
                    Task.WaitAll(new Task[] { tOut, tErr }, 5000);
                    return new RunResult
                    {
                        ExitCode = p.ExitCode,
                        StdOut = Decode(outMs.ToArray()),
                        StdErr = Decode(errMs.ToArray()),
                    };
                }
            });
        }

        /// <summary>Esegue un comando come root nella distribuzione, senza shell (--exec).</summary>
        public static Task<RunResult> ExecRootAsync(string distro, IEnumerable<string> cmd, byte[] stdin = null, int timeoutMs = 120000)
        {
            var a = new List<string> { "-d", distro, "-u", "root", "--exec" };
            a.AddRange(cmd);
            return RunAsync(a, stdin, timeoutMs);
        }

        public static Task<RunResult> ShRootAsync(string distro, string script, byte[] stdin = null, int timeoutMs = 120000)
            => ExecRootAsync(distro, new[] { "/bin/sh", "-c", script }, stdin, timeoutMs);

        // ---------------------------------------------------------------- distribuzioni

        static readonly Regex DistroLine = new Regex(@"^\s*(\*)?\s*(\S+)\s+(.+?)\s+(\d)\s*$");

        /// <summary>Output grezzo dell'ultima chiamata "wsl -l -v" (per la diagnostica).</summary>
        public static RunResult LastListRaw;
        /// <summary>Distribuzioni scartate perché non utilizzabili (es. docker-desktop).</summary>
        public static readonly List<string> SkippedDistros = new List<string>();

        public static async Task<List<WslDistro>> ListDistrosAsync()
        {
            SkippedDistros.Clear();
            var r = await RunAsync(new[] { "-l", "-v" }, null, 60000).ConfigureAwait(false);
            LastListRaw = r;
            var list = new List<WslDistro>();
            foreach (var raw in r.StdOut.Split('\n'))
            {
                var m = DistroLine.Match(raw.TrimEnd('\r'));
                if (!m.Success) continue;
                var name = m.Groups[2].Value;
                if (name.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase)) { SkippedDistros.Add(name); continue; }
                list.Add(new WslDistro
                {
                    Name = name,
                    IsDefault = m.Groups[1].Success,
                    State = m.Groups[3].Value,
                    Version = int.Parse(m.Groups[4].Value),
                });
            }
            if (list.Count == 0)
            {
                // Fallback per versioni di WSL che non supportano "-l -v"
                var q = await RunAsync(new[] { "-l", "-q" }, null, 60000).ConfigureAwait(false);
                bool first = true;
                foreach (var raw in q.StdOut.Split('\n'))
                {
                    var n = raw.Trim();
                    if (n.Length == 0 || n.Contains(" ") || n.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new WslDistro { Name = n, IsDefault = first, Version = 2, State = "" });
                    first = false;
                }
            }
            return list.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name).ToList();
        }

        /// <summary>Raccoglie lo stato di WSL per la diagnostica.</summary>
        public static async Task<string> DiagnoseAsync()
        {
            var sb = new StringBuilder();
            sb.AppendLine(L.T("Utente Windows") + " : " + Environment.UserDomainName + "\\" + Environment.UserName);
            sb.AppendLine(L.F("Processo 64 bit: {0}   OS 64 bit: {1}", Environment.Is64BitProcess, Environment.Is64BitOperatingSystem));
            sb.AppendLine("wsl.exe        : " + WslExe + (File.Exists(WslExe) ? "" : "  " + L.T("(NON TROVATO)")));
            foreach (var args in new[] { new[] { "--version" }, new[] { "--status" }, new[] { "-l", "-v" } })
            {
                sb.AppendLine();
                sb.AppendLine("> wsl " + string.Join(" ", args));
                try
                {
                    var r = await RunAsync(args, null, 60000).ConfigureAwait(false);
                    sb.AppendLine(L.F("[codice uscita {0}]", r.ExitCode));
                    if (r.StdOut.Trim().Length > 0) sb.AppendLine(r.StdOut.TrimEnd());
                    if (r.StdErr.Trim().Length > 0) sb.AppendLine("[stderr] " + r.StdErr.TrimEnd());
                }
                catch (Exception ex) { sb.AppendLine(L.F("ERRORE: {0}", ex.Message)); }
            }
            return sb.ToString().Replace("\r", "");
        }

        // ---------------------------------------------------------------- tool incorporato

        public static byte[] EmbeddedBinary()
        {
            using (var s = typeof(Wsl).Assembly.GetManifestResourceStream("pietrasanta-traceroute.bin"))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        static string _embeddedHash;
        public static string EmbeddedHash => _embeddedHash ?? (_embeddedHash = Sha256Hex(EmbeddedBinary()));

        public static async Task<ToolStatus> CheckToolAsync(string distro)
        {
            const string script =
                "if [ ! -x " + ToolPath + " ]; then echo STATE:MISSING; exit 0; fi; " +
                "echo HASH:$(sha256sum " + ToolPath + " | cut -c1-64); " +
                "[ -x " + WrapperPath + " ] && echo WRAP:OK; " +
                ToolPath + " -V >/tmp/pstr_ver.txt 2>&1; echo RC:$?; head -3 /tmp/pstr_ver.txt";
            var r = await ShRootAsync(distro, script, null, 90000).ConfigureAwait(false);
            var st = new ToolStatus();
            var text = r.StdOut;

            if (r.ExitCode != 0 && !text.Contains("STATE:") && !text.Contains("HASH:"))
            {
                st.State = ToolState.Unknown;
                st.Detail = r.All;
                return st;
            }
            if (text.Contains("STATE:MISSING")) { st.State = ToolState.NotInstalled; return st; }

            var hash = Regex.Match(text, @"HASH:([0-9a-f]{64})").Groups[1].Value;
            var rc = Regex.Match(text, @"RC:(\d+)").Groups[1].Value;
            var ver = Regex.Match(text, @"version\s+([0-9][0-9.]*)");
            st.Version = ver.Success ? ver.Groups[1].Value : "";

            if (rc != "0") { st.State = ToolState.BrokenDeps; st.Detail = text; return st; }
            if (!text.Contains("WRAP:OK") || !string.Equals(hash, EmbeddedHash, StringComparison.OrdinalIgnoreCase))
            {
                st.State = ToolState.Outdated; return st;
            }
            st.State = ToolState.Ready;
            return st;
        }

        /// <summary>Copia in WSL il binario incorporato e lo script di avvio; installa libcrypto se manca.</summary>
        public static async Task<RunResult> InstallToolAsync(string distro, Action<string> log)
        {
            log(L.F("Copia del binario in {0} ...", ToolPath));
            var b64 = Encoding.ASCII.GetBytes(Convert.ToBase64String(EmbeddedBinary(), Base64FormattingOptions.InsertLineBreaks).Replace("\r\n", "\n") + "\n");
            var r = await ShRootAsync(distro,
                "set -e; mkdir -p /usr/local/bin; " +
                "base64 -d > " + ToolPath + ".tmp; chmod 755 " + ToolPath + ".tmp; mv -f " + ToolPath + ".tmp " + ToolPath,
                b64).ConfigureAwait(false);
            if (r.ExitCode != 0) { log(L.F("ERRORE: {0}", r.All)); return r; }

            log(L.F("Installazione script di avvio {0} ...", WrapperPath));
            r = await ShRootAsync(distro,
                "set -e; cat > " + WrapperPath + ".tmp; chmod 755 " + WrapperPath + ".tmp; mv -f " + WrapperPath + ".tmp " + WrapperPath,
                Encoding.ASCII.GetBytes(WrapperScript)).ConfigureAwait(false);
            if (r.ExitCode != 0) { log(L.F("ERRORE: {0}", r.All)); return r; }

            log(L.T("Verifica esecuzione ..."));
            r = await ExecRootAsync(distro, new[] { ToolPath, "-V" }).ConfigureAwait(false);
            if (r.ExitCode != 0)
            {
                log(L.F("Il tool non parte ({0}). Installo la libreria OpenSSL 3 (libcrypto) ...", r.All.Split('\n').FirstOrDefault()));
                var dep = await ShRootAsync(distro,
                    "export DEBIAN_FRONTEND=noninteractive; " +
                    "if command -v apt-get >/dev/null; then apt-get update -q && (apt-get install -y -q libssl3t64 || apt-get install -y -q libssl3); " +
                    "elif command -v dnf >/dev/null; then dnf install -y openssl-libs; " +
                    "elif command -v zypper >/dev/null; then zypper -n install libopenssl3; " +
                    "else echo 'Gestore pacchetti non riconosciuto'; exit 1; fi",
                    null, 600000).ConfigureAwait(false);
                log(dep.All.Split('\n').LastOrDefault(l => l.Trim().Length > 0) ?? "");
                r = await ExecRootAsync(distro, new[] { ToolPath, "-V" }).ConfigureAwait(false);
                if (r.ExitCode != 0)
                {
                    log(L.F("ERRORE: il binario non si avvia in questa distribuzione: {0}", r.All));
                    log(L.T("Suggerimento: usare Ubuntu 24.04 (wsl --install -d Ubuntu-24.04)."));
                    return r;
                }
            }
            log("OK: " + (r.All.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? ""));
            return r;
        }

        // ---------------------------------------------------------------- rete mirrored (.wslconfig)

        public static string WslConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");

        static readonly Regex NetModeLine = new Regex(@"^\s*networkingMode\s*=\s*(\S+)", RegexOptions.IgnoreCase);

        public static string GetNetworkingMode()
        {
            if (!File.Exists(WslConfigPath)) return "nat";
            string section = "";
            foreach (var l in File.ReadAllLines(WslConfigPath))
            {
                var t = l.Trim();
                if (t.StartsWith("[")) { section = t.ToLowerInvariant(); continue; }
                if (section == "[wsl2]")
                {
                    var m = NetModeLine.Match(t);
                    if (m.Success) return m.Groups[1].Value.ToLowerInvariant();
                }
            }
            return "nat";
        }

        /// <summary>Imposta networkingMode=mirrored (true) o lo rimuove (false). Restituisce il percorso del backup, se creato.</summary>
        public static string SetMirrored(bool mirrored)
        {
            var path = WslConfigPath;
            string backup = null;
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            if (File.Exists(path))
            {
                backup = path + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(path, backup, true);
            }

            int secStart = lines.FindIndex(l => l.Trim().Equals("[wsl2]", StringComparison.OrdinalIgnoreCase));
            if (secStart < 0)
            {
                if (mirrored)
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0) lines.Add("");
                    lines.Add("[wsl2]");
                    lines.Add("networkingMode=mirrored");
                }
            }
            else
            {
                int secEnd = lines.FindIndex(secStart + 1, l => l.Trim().StartsWith("["));
                if (secEnd < 0) secEnd = lines.Count;
                int idx = -1;
                for (int i = secStart + 1; i < secEnd; i++) if (NetModeLine.IsMatch(lines[i])) { idx = i; break; }
                if (mirrored)
                {
                    if (idx >= 0) lines[idx] = "networkingMode=mirrored";
                    else lines.Insert(secStart + 1, "networkingMode=mirrored");
                }
                else if (idx >= 0) lines.RemoveAt(idx);
            }
            File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
            return backup;
        }
    }
}
