using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PstrGui
{
    public sealed class MainForm : Form
    {
        public const string AppVersion = "1.2";

        sealed class Method
        {
            public string Label; public string[] Args; public string Help;
            public override string ToString() => Label;
        }

        static readonly Method[] Methods =
        {
            new Method { Label = "UDP (classico, porte incrementali)", Args = new string[0], Help = "Metodo predefinito di traceroute: datagrammi UDP verso porte alte incrementali (33434+)." },
            new Method { Label = "ICMP Echo (-I)", Args = new[] { "-I" }, Help = "Come tracert di Windows: ICMP Echo Request." },
            new Method { Label = "TCP SYN (-T)", Args = new[] { "-T" }, Help = "Pacchetti TCP SYN verso la porta indicata (predefinita 80). Utile oltre i firewall che lasciano passare il web." },
            new Method { Label = "TCP InSession (--tcpinsession)", Args = new[] { "--tcpinsession" }, Help = "Apre una vera sessione TCP con la destinazione e invia le sonde DENTRO la sessione: segue un unico flusso, evita falsi drop dei firewall e il bilanciamento ECMP." },
            new Method { Label = "TCP connect (-M tcpconn)", Args = new[] { "-M", "tcpconn" }, Help = "Usa connect() TCP standard con TTL crescente." },
            new Method { Label = "UDP porta fissa (-U)", Args = new[] { "-U" }, Help = "UDP verso una porta fissa (predefinita 53)." },
            new Method { Label = "UDP InSession (--udpinsession)", Args = new[] { "--udpinsession" }, Help = "Sonde UDP all'interno dello stesso flusso (stessa 5-tupla)." },
            new Method { Label = "QUIC (--quic)", Args = new[] { "--quic" }, Help = "Pacchetti QUIC Initial verso la porta 443 (HTTP/3)." },
            new Method { Label = "DNS (--dns)", Args = new[] { "--dns" }, Help = "Pacchetti con forma di query DNS verso la porta 53." },
            new Method { Label = "DCCP (-D)", Args = new[] { "-D" }, Help = "DCCP Request (raro, per test specifici)." },
        };

        static readonly string[] DscpItems =
        {
            "—", "0 (CS0 / Best Effort)", "8 (CS1)", "10 (AF11)", "12 (AF12)", "14 (AF13)", "16 (CS2)", "18 (AF21)", "20 (AF22)", "22 (AF23)",
            "24 (CS3)", "26 (AF31)", "28 (AF32)", "30 (AF33)", "32 (CS4)", "34 (AF41)", "36 (AF42)", "38 (AF43)", "40 (CS5)",
            "44 (VOICE-ADMIT)", "46 (EF)", "48 (CS6)", "56 (CS7)"
        };
        static readonly string[] EcnItems = { "—", "0 (Not-ECT)", "1 (ECT(1))", "2 (ECT(0))", "3 (CE)" };

        // ---------------------------------------------------------------- controlli
        readonly ComboBox cbDistro = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly Button btnRefresh = new Button { Text = "Aggiorna", AutoSize = true };
        readonly Label lblTool = new Label { AutoSize = true, Text = "Verifica WSL in corso…", Padding = new Padding(0, 6, 0, 0) };
        readonly Button btnInstall = new Button { Text = "Installa / aggiorna tool in WSL", AutoSize = true, Enabled = false };
        readonly Label lblNet = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 0), ForeColor = SystemColors.GrayText };

        readonly ComboBox cbTarget = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 280 };
        readonly ComboBox cbMethod = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly NumericUpDown numPort = new NumericUpDown { Minimum = 0, Maximum = 65535, Width = 75 };
        readonly ComboBox cbIp = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        readonly NumericUpDown numMaxHops = new NumericUpDown { Minimum = 1, Maximum = 255, Value = 30, Width = 60 };
        readonly NumericUpDown numQueries = new NumericUpDown { Minimum = 1, Maximum = 20, Value = 3, Width = 55 };
        readonly NumericUpDown numTimeout = new NumericUpDown { Minimum = 0, Maximum = 3600, Value = 0, Width = 65 };
        readonly ComboBox cbDscp = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 150 };
        readonly ComboBox cbEcn = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        readonly CheckBox chkNoDns = new CheckBox { Text = "Non risolvere i nomi (-n)", AutoSize = true };
        readonly CheckBox chkAs = new CheckBox { Text = "Lookup AS (-A)", AutoSize = true };
        readonly CheckBox chkExt = new CheckBox { Text = "Estensioni ICMP / MPLS (-e)", AutoSize = true };
        readonly CheckBox chkMtu = new CheckBox { Text = "Path MTU (--mtu)", AutoSize = true };
        readonly CheckBox chkBack = new CheckBox { Text = "Stima percorso di ritorno (--back)", AutoSize = true };
        readonly TextBox txtExtra = new TextBox { Width = 420 };

        readonly TextBox txtCmd = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f) };
        readonly Button btnRun = new Button { Text = "▶  Avvia", AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        readonly Button btnStop = new Button { Text = "■  Interrompi", AutoSize = true, Enabled = false };
        readonly Button btnCsv = new Button { Text = "Esporta CSV…", AutoSize = true, Enabled = false };
        readonly Button btnTxt = new Button { Text = "Esporta report TXT…", AutoSize = true, Enabled = false };

        readonly ListView lvHops = new ListView { View = View.Details, FullRowSelect = true, GridLines = true, Dock = DockStyle.Fill, HideSelection = false };
        readonly RichTextBox rtbRaw = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9.5f), WordWrap = false, BackColor = Color.FromArgb(250, 250, 250), DetectUrls = false };
        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };

        readonly ToolStripStatusLabel stStatus = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly ToolStripStatusLabel stElapsed = new ToolStripStatusLabel();
        readonly ToolTip tip = new ToolTip { AutoPopDelay = 15000 };
        readonly Timer timer = new Timer { Interval = 200 };

        ToolStripMenuItem miMirrored;

        // ---------------------------------------------------------------- stato
        Process proc;
        int? linuxPid;
        bool stopRequested;
        readonly Stopwatch sw = new Stopwatch();
        readonly SortedDictionary<int, HopResult> hops = new SortedDictionary<int, HopResult>();
        TraceSummary summary = new TraceSummary();
        string lastCommand = "", lastDistro = "", lastToolVersion = "";
        DateTime lastStartLocal, lastStartUtc;
        ToolStatus toolStatus;
        readonly Settings settings = Settings.Load();

        public MainForm()
        {
            Text = "Pietrasanta Traceroute per Windows (WSL) – v" + AppVersion;
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 640);
            Size = new Size(1180, 800);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildMenu();
            BuildLayout();
            FillCombos();
            WireEvents();
            ApplySettings();
            UpdateCommandPreview();
            UpdateNetLabel();
        }

        // ================================================================= layout

        void BuildMenu()
        {
            var ms = new MenuStrip();
            var mFile = new ToolStripMenuItem("&File");
            mFile.DropDownItems.Add("Esporta CSV…", null, (s, e) => ExportCsv());
            mFile.DropDownItems.Add("Esporta report TXT…", null, (s, e) => ExportTxt());
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("Apri la cartella del programma", null, (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", Wsl.QuoteArg(Settings.ExeDir)) { UseShellExecute = true }); } catch { }
            });
            mFile.DropDownItems.Add(new ToolStripSeparator());
            mFile.DropDownItems.Add("Esci", null, (s, e) => Close());

            var mTools = new ToolStripMenuItem("&Strumenti");
            mTools.DropDownItems.Add("Installa / aggiorna tool nella distribuzione selezionata", null, async (s, e) => await InstallToolAsync());
            mTools.DropDownItems.Add("Ricarica elenco distribuzioni WSL", null, async (s, e) => await RefreshDistrosAsync());
            mTools.DropDownItems.Add(new ToolStripSeparator());
            miMirrored = new ToolStripMenuItem("Rete WSL in modalità \"mirrored\" (consigliata)", null, (s, e) => ToggleMirrored());
            mTools.DropDownItems.Add(miMirrored);
            mTools.DropDownItems.Add("Installa Ubuntu 24.04 in WSL (per l'utente corrente)…", null, (s, e) => InstallWsl());
            mTools.DropDownItems.Add("Diagnostica WSL", null, async (s, e) => await RunDiagnosticsAsync());
            mTools.DropDownItems.Add(new ToolStripSeparator());
            mTools.DropDownItems.Add("Apri una shell nella distribuzione", null, (s, e) => OpenShell());

            var mHelp = new ToolStripMenuItem("&?");
            mHelp.DropDownItems.Add("Guida opzioni del tool (--help)", null, async (s, e) => await ShowToolHelpAsync());
            mHelp.DropDownItems.Add("Informazioni", null, (s, e) => ShowAbout());

            ms.Items.AddRange(new ToolStripItem[] { mFile, mTools, mHelp });
            MainMenuStrip = ms;
            Controls.Add(ms);
        }

        static Label L(string text) => new Label { Text = text, AutoSize = true, Padding = new Padding(0, 6, 0, 0), Margin = new Padding(8, 3, 2, 3) };

        static FlowLayoutPanel Row(params Control[] ctrls)
        {
            var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Dock = DockStyle.Top, Margin = new Padding(0) };
            f.Controls.AddRange(ctrls);
            return f;
        }

        static Label H(string text) => new Label { Text = text, AutoSize = false, Size = new Size(82, 24), Font = new Font("Segoe UI", 9f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 3, 4, 3) };

        void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(8, 6, 8, 4) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 7; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Control Fill(Control c) { c.Dock = DockStyle.Fill; return c; }

            // --- ambiente
            root.Controls.Add(Fill(Row(H("Ambiente"), L("Distribuzione WSL:"), cbDistro, btnRefresh, L("Tool:"), lblTool, btnInstall)), 0, 0);
            var envRow2 = Fill(Row(H(""), lblNet));
            root.Controls.Add(envRow2, 0, 1);

            // --- parametri
            root.Controls.Add(Fill(Row(H("Parametri"), L("Destinazione:"), cbTarget, L("Metodo:"), cbMethod, L("Porta:"), numPort, L("(0 = predef.)"), L("IP:"), cbIp)), 0, 2);
            root.Controls.Add(Fill(Row(H(""), L("Max hop:"), numMaxHops, L("Sonde per hop:"), numQueries, L("Timeout totale (s):"), numTimeout, L("(0 = nessuno)"), L("DSCP:"), cbDscp, L("ECN:"), cbEcn)), 0, 3);
            root.Controls.Add(Fill(Row(H(""), chkNoDns, chkAs, chkExt, chkMtu, chkBack)), 0, 4);
            root.Controls.Add(Fill(Row(H(""), L("Opzioni aggiuntive:"), txtExtra, L("es.  -f 3   -N 1   -z 50   --sport=40000"))), 0, 5);
            foreach (var c in new Control[] { chkNoDns, chkAs, chkExt, chkMtu, chkBack }) c.Margin = new Padding(8, 6, 6, 3);

            // --- comando + pulsanti
            var cmdRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 6, 0, 6) };
            cmdRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cmdRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cmdRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cmdRow.Controls.Add(H("Comando"), 0, 0);
            txtCmd.Margin = new Padding(3, 6, 6, 3);
            cmdRow.Controls.Add(txtCmd, 1, 0);
            var btns = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
            btns.Controls.AddRange(new Control[] { btnRun, btnStop, btnCsv, btnTxt });
            cmdRow.Controls.Add(btns, 2, 0);
            root.Controls.Add(cmdRow, 0, 6);

            // --- risultati
            lvHops.Columns.Add("Hop", 45, HorizontalAlignment.Right);
            lvHops.Columns.Add("Host", 260);
            lvHops.Columns.Add("Indirizzo IP", 150);
            lvHops.Columns.Add("AS", 90);
            lvHops.Columns.Add("Min (ms)", 75, HorizontalAlignment.Right);
            lvHops.Columns.Add("Media (ms)", 80, HorizontalAlignment.Right);
            lvHops.Columns.Add("Max (ms)", 75, HorizontalAlignment.Right);
            lvHops.Columns.Add("Perdita", 65, HorizontalAlignment.Right);
            lvHops.Columns.Add("Sonde", 55, HorizontalAlignment.Right);
            lvHops.Columns.Add("TOS / DSCP / ECN", 170);
            lvHops.Columns.Add("Note", 160);
            var cm = new ContextMenuStrip();
            cm.Items.Add("Copia righe selezionate", null, (s, e) => CopySelectedHops());
            cm.Items.Add("Copia indirizzo IP", null, (s, e) => CopySelectedIp());
            lvHops.ContextMenuStrip = cm;

            var tpHops = new TabPage("Percorso");
            tpHops.Controls.Add(lvHops);
            var tpRaw = new TabPage("Output del tool");
            tpRaw.Controls.Add(rtbRaw);
            tabs.TabPages.Add(tpHops);
            tabs.TabPages.Add(tpRaw);
            root.Controls.Add(tabs, 0, 7);

            var ss = new StatusStrip();
            ss.Items.AddRange(new ToolStripItem[] { stStatus, stElapsed });

            Controls.Add(root);
            Controls.Add(ss);
            root.BringToFront();
        }

        void FillCombos()
        {
            cbMethod.Items.AddRange(Methods);
            cbMethod.SelectedIndex = 0;
            cbIp.Items.AddRange(new object[] { "Auto", "IPv4", "IPv6" });
            cbIp.SelectedIndex = 0;
            cbDscp.Items.AddRange(DscpItems);
            cbDscp.SelectedIndex = 0;
            cbEcn.Items.AddRange(EcnItems);
            cbEcn.SelectedIndex = 0;

            tip.SetToolTip(cbTarget, "Nome host o indirizzo IPv4/IPv6 di destinazione");
            tip.SetToolTip(numPort, "0 = porta predefinita del metodo (UDP 33434+, TCP 80, UDP fisso/DNS 53, QUIC 443)");
            tip.SetToolTip(cbDscp, "Valore DSCP da impostare nei pacchetti (0–63). Il tool mostra anche il TOS ricevuto da ogni hop.");
            tip.SetToolTip(chkAs, "Interroga i registri di routing (whois) per l'Autonomous System di ogni hop");
            tip.SetToolTip(txtExtra, "Altre opzioni di Pietrasanta traceroute, separate da spazi (vedi menu ? > Guida opzioni)");
            tip.SetToolTip(btnInstall, "Copia nella distribuzione WSL il binario Linux incluso in questo programma");
        }

        void WireEvents()
        {
            EventHandler upd = (s, e) => UpdateCommandPreview();
            foreach (var c in new Control[] { cbTarget, cbMethod, cbIp, cbDscp, cbEcn, txtExtra })
                c.TextChanged += upd;
            cbMethod.SelectedIndexChanged += (s, e) => { tip.SetToolTip(cbMethod, ((Method)cbMethod.SelectedItem).Help); UpdateCommandPreview(); };
            foreach (var n in new[] { numPort, numMaxHops, numQueries, numTimeout }) n.ValueChanged += upd;
            foreach (var c in new[] { chkNoDns, chkAs, chkExt, chkMtu, chkBack }) c.CheckedChanged += upd;
            cbDistro.SelectedIndexChanged += async (s, e) => await CheckToolAsync();

            btnRefresh.Click += async (s, e) => await RefreshDistrosAsync();
            btnInstall.Click += async (s, e) => await InstallToolAsync();
            btnRun.Click += (s, e) => StartTrace();
            btnStop.Click += async (s, e) => await StopTraceAsync();
            btnCsv.Click += (s, e) => ExportCsv();
            btnTxt.Click += (s, e) => ExportTxt();
            cbTarget.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && btnRun.Enabled) { e.SuppressKeyPress = true; StartTrace(); } };

            timer.Tick += (s, e) => stElapsed.Text = sw.IsRunning ? $"{sw.Elapsed.TotalSeconds:0.0} s" : stElapsed.Text;

            Shown += async (s, e) => await RefreshDistrosAsync();
            FormClosing += (s, e) =>
            {
                closing = true;
                SaveSettings();
                if (proc != null && !proc.HasExited)
                {
                    try { if (linuxPid.HasValue) Wsl.ExecRootAsync(lastDistro, new[] { "kill", "-TERM", linuxPid.Value.ToString() }).Wait(4000); } catch { }
                    try { proc.Kill(); } catch { }
                }
            };
        }

        // ================================================================= impostazioni

        void ApplySettings()
        {
            foreach (var t in settings.TargetHistory) cbTarget.Items.Add(t);
            cbTarget.Text = settings.TargetHistory.FirstOrDefault() ?? "";
            if (settings.MethodIndex >= 0 && settings.MethodIndex < Methods.Length) cbMethod.SelectedIndex = settings.MethodIndex;
            chkNoDns.Checked = settings.NoDns;
        }

        void SaveSettings()
        {
            try
            {
                settings.LastDistro = (cbDistro.SelectedItem as WslDistro)?.Name ?? settings.LastDistro;
                settings.MethodIndex = cbMethod.SelectedIndex;
                settings.NoDns = chkNoDns.Checked;
                settings.Save();
            }
            catch { }
        }

        void RememberTarget(string t)
        {
            settings.TargetHistory.RemoveAll(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase));
            settings.TargetHistory.Insert(0, t);
            if (settings.TargetHistory.Count > 20) settings.TargetHistory.RemoveRange(20, settings.TargetHistory.Count - 20);
            cbTarget.Items.Clear();
            foreach (var x in settings.TargetHistory) cbTarget.Items.Add(x);
            cbTarget.Text = t;
        }

        // ================================================================= WSL

        string SelectedDistro => (cbDistro.SelectedItem as WslDistro)?.Name;

        async Task RefreshDistrosAsync()
        {
            if (!Wsl.IsWslPresent())
            {
                SetToolLabel("WSL non presente", Color.Firebrick);
                SetStatus("WSL non è installato. Usa Strumenti > Installa WSL + Ubuntu 24.04.");
                btnInstall.Enabled = false;
                return;
            }
            SetToolLabel("Verifica WSL in corso…", SystemColors.ControlText);
            btnRefresh.Enabled = false;
            try
            {
                var list = await Wsl.ListDistrosAsync();
                cbDistro.Items.Clear();
                foreach (var d in list) cbDistro.Items.Add(d);
                if (list.Count == 0)
                {
                    btnInstall.Enabled = false;
                    var raw = Wsl.LastListRaw;
                    if (Wsl.SkippedDistros.Count > 0)
                    {
                        SetToolLabel("Solo distribuzioni Docker Desktop (non utilizzabili)", Color.Firebrick);
                        SetStatus("Installa Ubuntu: Strumenti > Installa Ubuntu 24.04 in WSL.");
                    }
                    else
                    {
                        SetToolLabel("Nessuna distribuzione WSL per l'utente " + Environment.UserName, Color.Firebrick);
                        SetStatus("Installa Ubuntu con Strumenti > Installa Ubuntu 24.04 in WSL, oppure vedi Strumenti > Diagnostica WSL.");
                    }
                    tabs.SelectedIndex = 1;
                    AppendRaw("=== Nessuna distribuzione WSL utilizzabile trovata ===", Color.Firebrick);
                    AppendRaw("Utente Windows: " + Environment.UserDomainName + "\\" + Environment.UserName +
                              "  (le distribuzioni WSL sono registrate per singolo utente)", Color.Firebrick);
                    if (raw != null)
                    {
                        AppendRaw("> wsl -l -v   [codice uscita " + raw.ExitCode + "]", Color.SteelBlue);
                        foreach (var l in raw.All.Replace("\r", "").Split('\n')) if (l.Trim().Length > 0) AppendRaw(l, rtbRaw.ForeColor);
                    }
                    if (Wsl.SkippedDistros.Count > 0) AppendRaw("Scartate: " + string.Join(", ", Wsl.SkippedDistros), Color.Firebrick);
                    return;
                }
                var pick = list.FindIndex(d => d.Name == settings.LastDistro);
                if (pick < 0) pick = list.FindIndex(d => d.Name.StartsWith("Ubuntu", StringComparison.OrdinalIgnoreCase) && d.Version == 2);
                if (pick < 0) pick = 0;
                cbDistro.SelectedIndex = pick; // scatena CheckToolAsync
            }
            catch (Exception ex)
            {
                SetToolLabel("Errore WSL", Color.Firebrick);
                SetStatus("Errore nel leggere le distribuzioni WSL: " + ex.Message);
            }
            finally { btnRefresh.Enabled = true; }
        }

        async Task CheckToolAsync()
        {
            var d = cbDistro.SelectedItem as WslDistro;
            if (d == null) return;
            if (d.Version == 1)
            {
                SetToolLabel("Distribuzione WSL1: serve WSL2", Color.Firebrick);
                SetStatus($"Converti la distribuzione con:  wsl --set-version {d.Name} 2");
                btnInstall.Enabled = false; btnRun.Enabled = false;
                return;
            }
            SetToolLabel("Verifica in corso… (l'avvio di WSL può richiedere qualche secondo)", SystemColors.ControlText);
            btnInstall.Enabled = false;
            btnRun.Enabled = false;
            toolStatus = await Wsl.CheckToolAsync(d.Name);
            switch (toolStatus.State)
            {
                case ToolState.Ready:
                    SetToolLabel("Pronto – versione " + toolStatus.Version, Color.ForestGreen);
                    SetStatus("Pronto.");
                    break;
                case ToolState.NotInstalled:
                    SetToolLabel("Non installato in questa distribuzione", Color.DarkOrange);
                    SetStatus("Premi \"Installa / aggiorna tool in WSL\" (operazione di pochi secondi).");
                    break;
                case ToolState.Outdated:
                    SetToolLabel("Installato ma diverso dalla versione inclusa – aggiorna", Color.DarkOrange);
                    break;
                case ToolState.BrokenDeps:
                    SetToolLabel("Installato ma non si avvia (librerie mancanti) – reinstalla", Color.Firebrick);
                    break;
                default:
                    SetToolLabel("Impossibile verificare", Color.Firebrick);
                    SetStatus("WSL ha risposto: " + FirstLine(toolStatus.Detail));
                    break;
            }
            btnInstall.Enabled = true;
            btnRun.Enabled = toolStatus.State == ToolState.Ready;
        }

        async Task InstallToolAsync()
        {
            var d = SelectedDistro;
            if (d == null) { MessageBox.Show(this, "Seleziona prima una distribuzione WSL.", Text); return; }
            if (proc != null) return;
            btnInstall.Enabled = btnRun.Enabled = false;
            tabs.SelectedIndex = 1;
            AppendRaw($"=== Installazione di Pietrasanta traceroute in WSL ({d}) ===", Color.SteelBlue);
            SetStatus("Installazione in corso…");
            var r = await Wsl.InstallToolAsync(d, msg => Ui(() => AppendRaw(msg, Color.SteelBlue)));
            if (r.ExitCode == 0) SetStatus("Installazione completata.");
            else SetStatus("Installazione non riuscita: vedi la scheda \"Output del tool\".");
            await CheckToolAsync();
        }

        void ToggleMirrored()
        {
            bool now = Wsl.GetNetworkingMode() == "mirrored";
            var msg = now
                ? "Disattivare la rete \"mirrored\" e tornare alla modalità NAT predefinita di WSL?"
                : "La modalità di rete \"mirrored\" fa usare a WSL direttamente le schede di rete di Windows: il traceroute non mostra più l'hop " +
                  "aggiuntivo del NAT interno di WSL ed è più fedele.\n\nRichiede Windows 11 22H2 o successivo. Verrà modificato il file:\n" +
                  Wsl.WslConfigPath + "\n(con copia di backup).";
            if (MessageBox.Show(this, msg + "\n\nPer applicare la modifica WSL deve essere riavviato (wsl --shutdown): tutte le distribuzioni in esecuzione verranno chiuse. Procedere?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                var bak = Wsl.SetMirrored(!now);
                Wsl.RunAsync(new[] { "--shutdown" }, null, 60000).Wait(60000);
                UpdateNetLabel();
                MessageBox.Show(this, "Impostazione applicata." + (bak != null ? "\nBackup: " + bak : ""), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _ = CheckToolAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Errore: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void UpdateNetLabel()
        {
            string mode;
            try { mode = Wsl.GetNetworkingMode(); } catch { mode = "?"; }
            lblNet.Text = "Rete WSL: " + (mode == "mirrored" ? "mirrored" : mode + "  (consigliata: mirrored, menu Strumenti)");
            if (miMirrored != null) miMirrored.Checked = mode == "mirrored";
        }

        void InstallWsl()
        {
            if (MessageBox.Show(this,
                    "Verrà aperta una finestra di comando che esegue, con il TUO utente (" + Environment.UserName + "):\n\n" +
                    "    wsl --install -d Ubuntu-24.04\n\n" +
                    "Se il componente WSL non è ancora attivo, Windows chiederà la conferma da amministratore solo per quella parte " +
                    "e potrebbe essere necessario riavviare il PC (poi ripetere questo comando).\n\n" +
                    "Al termine Ubuntu chiede di creare un utente Linux: completare quel passaggio, chiudere la finestra e premere " +
                    "\"Aggiorna\" in questo programma.\n\n" +
                    "IMPORTANTE: non eseguire l'installazione di Ubuntu con un altro account amministratore, altrimenti la distribuzione " +
                    "viene registrata per quell'account e non è visibile qui.\n\nProcedere?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            try
            {
                var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                Process.Start(new ProcessStartInfo(cmd, "/k \"" + Wsl.WslExe + "\" --install -d Ubuntu-24.04") { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(this, "Avvio non riuscito: " + ex.Message, Text); }
        }

        async Task RunDiagnosticsAsync()
        {
            tabs.SelectedIndex = 1;
            AppendRaw("=== Diagnostica WSL (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + ") ===", Color.SteelBlue);
            SetStatus("Diagnostica in corso…");
            AppendRaw(SettingsInfo().Replace("\n", "  "), rtbRaw.ForeColor);
            var text = await Wsl.DiagnoseAsync();
            foreach (var l in text.Split('\n')) AppendRaw(l, l.StartsWith(">") ? Color.SteelBlue : rtbRaw.ForeColor);
            btnTxt.Enabled = true;
            SetStatus("Diagnostica completata: vedi la scheda \"Output del tool\" (esportabile con \"Esporta report TXT\").");
        }

        void OpenShell()
        {
            var d = SelectedDistro;
            if (d == null) return;
            try { Process.Start(new ProcessStartInfo(Wsl.WslExe, "-d " + Wsl.QuoteArg(d) + " --cd ~") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
        }

        async Task ShowToolHelpAsync()
        {
            var d = SelectedDistro;
            if (d == null || toolStatus == null || toolStatus.State == ToolState.NotInstalled)
            {
                MessageBox.Show(this, "Installa prima il tool nella distribuzione WSL.", Text); return;
            }
            var r = await Wsl.ExecRootAsync(d, new[] { Wsl.ToolPath, "--help" });
            var f = new Form { Text = "Pietrasanta traceroute – opzioni", Size = new Size(860, 700), StartPosition = FormStartPosition.CenterParent, Font = Font };
            f.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f), Text = r.All.Replace("\r", "").Replace("\n", "\r\n") });
            f.Show(this);
        }

        string SettingsInfo()
        {
            var target = Settings.TargetPath;
            return "Preferenze (" + (string.Equals(target, Settings.LocalPath, StringComparison.OrdinalIgnoreCase)
                       ? "modalità portable" : "cartella del programma in sola lettura: uso %APPDATA%") + "):\n" + target +
                   (settings.LoadedFrom.Length > 0 && !string.Equals(settings.LoadedFrom, target, StringComparison.OrdinalIgnoreCase)
                       ? "\n(importate da: " + settings.LoadedFrom + ")" : "");
        }

        void ShowAbout()
        {
            MessageBox.Show(this,
                "Pietrasanta Traceroute GUI per Windows – v" + AppVersion + "\n\n" +
                "Interfaccia grafica che esegue Pietrasanta traceroute (Catchpoint) all'interno di WSL2.\n" +
                "Il binario Linux incluso (Ubuntu 24.04 x86_64, v0.1.36) è distribuito sotto licenza GNU GPL v2 o successiva;\n" +
                "sorgenti: https://github.com/catchpoint/Networking.traceroute\n\n" +
                "SHA-256 del binario incluso:\n" + Wsl.EmbeddedHash + "\n\n" +
                SettingsInfo(),
                "Informazioni", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ================================================================= comando

        static readonly Regex TargetRx = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._:%\-]*$");

        List<string> BuildArgs(out string error)
        {
            error = null;
            var a = new List<string>();
            var m = (Method)cbMethod.SelectedItem;
            if (m != null) a.AddRange(m.Args);
            if (cbIp.SelectedIndex == 1) a.Add("-4");
            if (cbIp.SelectedIndex == 2) a.Add("-6");
            if (chkNoDns.Checked) a.Add("-n");
            if (chkAs.Checked) a.Add("-A");
            if (chkExt.Checked) a.Add("-e");
            if (chkMtu.Checked) a.Add("--mtu");
            if (chkBack.Checked) a.Add("--back");
            a.Add("-m"); a.Add(((int)numMaxHops.Value).ToString());
            a.Add("-q"); a.Add(((int)numQueries.Value).ToString());
            if (numPort.Value > 0) { a.Add("-p"); a.Add(((int)numPort.Value).ToString()); }
            if (numTimeout.Value > 0) { a.Add("-Q"); a.Add(((int)numTimeout.Value).ToString()); }

            var dscpText = cbDscp.Text.Replace("—", "").Trim();
            var dscp = LeadingInt(dscpText);
            if (dscp.HasValue)
            {
                if (dscp < 0 || dscp > 63) error = "DSCP deve essere tra 0 e 63.";
                else a.Add("--dscp=" + dscp.Value);
            }
            else if (dscpText.Length > 0) error = "Valore DSCP non valido (0–63).";
            var ecn = LeadingInt(cbEcn.Text);
            if (ecn.HasValue) a.Add("--ecn=" + ecn.Value);

            a.AddRange(SplitExtra(txtExtra.Text));

            var target = cbTarget.Text.Trim();
            if (target.Length == 0) error = error ?? "Indica la destinazione.";
            else if (!TargetRx.IsMatch(target)) error = error ?? "Destinazione non valida (usa un nome host o un indirizzo IP).";
            a.Add(target);
            return a;
        }

        static int? LeadingInt(string s)
        {
            var m = Regex.Match(s ?? "", @"^\s*(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value) : (int?)null;
        }

        static IEnumerable<string> SplitExtra(string s)
        {
            foreach (Match m in Regex.Matches(s ?? "", "\"([^\"]*)\"|(\\S+)"))
                yield return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        }

        static string ShellQuote(string a) => Regex.IsMatch(a, @"^[A-Za-z0-9._:%=,/@+\-]+$") ? a : "'" + a.Replace("'", "'\\''") + "'";

        void UpdateCommandPreview()
        {
            var args = BuildArgs(out _);
            if (args.Count > 0 && args[args.Count - 1].Length == 0) args[args.Count - 1] = "<destinazione>";
            txtCmd.Text = "traceroute " + string.Join(" ", args.Select(a => a == "<destinazione>" ? a : ShellQuote(a)));
        }

        // ================================================================= esecuzione

        void StartTrace()
        {
            if (proc != null) return;
            var distro = SelectedDistro;
            if (distro == null) { MessageBox.Show(this, "Nessuna distribuzione WSL selezionata.", Text); return; }
            var args = BuildArgs(out var err);
            if (err != null) { MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            RememberTarget(cbTarget.Text.Trim());
            hops.Clear();
            lvHops.Items.Clear();
            rtbRaw.Clear();
            summary = new TraceSummary();
            linuxPid = null;
            stopRequested = false;
            lastDistro = distro;
            lastToolVersion = toolStatus?.Version ?? "";
            lastCommand = txtCmd.Text;
            lastStartLocal = DateTime.Now;
            lastStartUtc = DateTime.UtcNow;

            var wslArgs = new List<string> { "-d", distro, "-u", "root", "--exec", Wsl.WrapperPath };
            wslArgs.AddRange(args);

            var psi = new ProcessStartInfo(Wsl.WslExe, Wsl.JoinArgs(wslArgs))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.EnvironmentVariables["WSL_UTF8"] = "1";

            try
            {
                proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                outEof = errEof = exited = false;
                var me = proc;
                proc.OutputDataReceived += (s, e) => Ui(() => { if (e.Data == null) { outEof = true; TryFinish(me); } else OnLine(e.Data, false); });
                proc.ErrorDataReceived += (s, e) => Ui(() => { if (e.Data == null) { errEof = true; TryFinish(me); } else OnLine(e.Data, true); });
                proc.Exited += (s, e) => Ui(() => { exited = true; TryFinish(me); });
                proc.Start();
                try { proc.StandardInput.Close(); } catch { }
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                proc = null;
                MessageBox.Show(this, "Impossibile avviare wsl.exe: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            AppendRaw("$ " + lastCommand, Color.SteelBlue);
            SetRunning(true);
            sw.Restart();
            timer.Start();
            SetStatus("In esecuzione verso " + cbTarget.Text.Trim() + " …");
            tabs.SelectedIndex = 0;
        }

        void OnLine(string line, bool isErr)
        {
            line = line.Replace("\0", "");
            if (!isErr && line.StartsWith(Wsl.PidMarker))
            {
                if (int.TryParse(line.Substring(Wsl.PidMarker.Length).Trim(), out var pid)) linuxPid = pid;
                return;
            }
            AppendRaw(line, isErr ? Color.Firebrick : rtbRaw.ForeColor);
            if (isErr) return;

            if (TracerouteParser.TryParseSummary(line, summary))
            {
                if (summary.Header != null && line.TrimStart().StartsWith("traceroute to"))
                    SetStatus("In esecuzione: " + summary.Header);
                return;
            }
            var hop = TracerouteParser.ParseHopLine(line);
            if (hop != null) AddOrUpdateHop(hop);
        }

        void AddOrUpdateHop(HopResult h)
        {
            hops[h.Hop] = h;
            var key = h.Hop.ToString();
            var item = lvHops.Items.ContainsKey(key) ? lvHops.Items[key] : null;
            var cells = new[]
            {
                h.Hop.ToString(),
                h.NoReply ? "* (nessuna risposta)" : h.HostDisplay,
                h.IpDisplay,
                h.AsDisplay,
                Fmt(h.Min), Fmt(h.Avg), Fmt(h.Max),
                h.Probes == 0 ? "" : h.LossPercent.ToString("0", CultureInfo.CurrentCulture) + " %",
                h.Probes.ToString(),
                h.TosDisplay,
                h.NotesDisplay,
            };
            if (item == null)
            {
                item = new ListViewItem(cells) { Name = key };
                // inserimento ordinato per numero di hop
                int idx = 0;
                while (idx < lvHops.Items.Count && int.Parse(lvHops.Items[idx].Name) < h.Hop) idx++;
                lvHops.Items.Insert(idx, item);
            }
            else
            {
                for (int i = 0; i < cells.Length; i++) item.SubItems[i].Text = cells[i];
            }
            item.UseItemStyleForSubItems = true;
            if (h.NoReply) { item.ForeColor = SystemColors.GrayText; item.BackColor = lvHops.BackColor; }
            else if (h.Timeouts > 0) { item.ForeColor = lvHops.ForeColor; item.BackColor = Color.FromArgb(255, 243, 205); }
            else { item.ForeColor = lvHops.ForeColor; item.BackColor = lvHops.BackColor; }
            if (summary.TargetIp != null && h.Ips.Contains(summary.TargetIp))
                item.Font = new Font(lvHops.Font, FontStyle.Bold);
            item.EnsureVisible();
        }

        static string Fmt(double? v) => v.HasValue ? v.Value.ToString("0.000", CultureInfo.CurrentCulture) : "";

        bool outEof, errEof, exited;

        // La traccia è conclusa solo quando il processo è uscito E entrambi gli stream sono chiusi
        // (l'evento Exited può arrivare prima delle ultime righe di output).
        void TryFinish(Process p)
        {
            if (p != proc || !exited || !outEof || !errEof) return;
            OnExited();
        }

        void OnExited()
        {
            sw.Stop();
            timer.Stop();
            stElapsed.Text = $"{sw.Elapsed.TotalSeconds:0.0} s";
            int code = -1;
            try { code = proc.ExitCode; } catch { }
            try { proc.Dispose(); } catch { }
            proc = null;
            SetRunning(false);

            // evidenzia l'hop di destinazione anche se l'IP è arrivato dopo l'header
            if (summary.TargetIp != null)
                foreach (var h in hops.Values.Where(x => x.Ips.Contains(summary.TargetIp)).ToList()) AddOrUpdateHop(h);

            var parts = new List<string>();
            if (stopRequested) parts.Add("Interrotto dall'utente");
            else if (code != 0 && summary.Header == null) parts.Add("Errore (codice " + code + "): vedi la scheda \"Output del tool\"");
            else parts.Add("Completato");
            if (summary.DestinationReached.HasValue) parts.Add("destinazione raggiunta: " + (summary.DestinationReached.Value ? "SÌ" : "NO"));
            if (hops.Count > 0) parts.Add("hop: " + hops.Keys.Max());
            if (summary.Duration != null) parts.Add("durata: " + summary.Duration);
            if (summary.PathMtu != null) parts.Add("Path MTU: " + summary.PathMtu);
            if (summary.TimedOut == true) parts.Add("timeout globale raggiunto");
            SetStatus(string.Join("  –  ", parts));
            if (code != 0 && summary.Header == null && !stopRequested) tabs.SelectedIndex = 1;
            btnCsv.Enabled = btnTxt.Enabled = rtbRaw.TextLength > 0;
        }

        async Task StopTraceAsync()
        {
            if (proc == null) return;
            stopRequested = true;
            btnStop.Enabled = false;
            SetStatus("Interruzione in corso…");
            if (linuxPid.HasValue)
                await Wsl.ExecRootAsync(lastDistro, new[] { "kill", "-INT", linuxPid.Value.ToString() }, null, 15000);
            await Task.Delay(1500);
            var p = proc;
            if (p != null)
            {
                try { if (!p.HasExited) p.Kill(); } catch { }
                await Task.Delay(1500);
                if (proc == p) { outEof = errEof = exited = true; TryFinish(p); }
            }
        }

        void SetRunning(bool running)
        {
            btnRun.Enabled = !running;
            btnStop.Enabled = running;
            btnInstall.Enabled = !running;
            cbDistro.Enabled = !running;
            btnRefresh.Enabled = !running;
            if (running) { btnCsv.Enabled = btnTxt.Enabled = false; }
        }

        // ================================================================= export

        string DefaultFileName(string ext)
        {
            var t = Regex.Replace(cbTarget.Text.Trim(), @"[^A-Za-z0-9._-]", "_");
            return $"traceroute_{t}_{lastStartLocal:yyyyMMdd_HHmmss}.{ext}";
        }

        void ExportCsv()
        {
            if (hops.Count == 0) { MessageBox.Show(this, "Nessun risultato da esportare.", Text); return; }
            using (var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = DefaultFileName("csv") })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var sep = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
                if (string.IsNullOrEmpty(sep)) sep = ";";
                string Q(string v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(sep, new[] { "Hop", "Host", "IP", "AS", "RTT min (ms)", "RTT medio (ms)", "RTT max (ms)", "Perdita %", "Sonde", "Risposte", "TOS/DSCP/ECN", "Note", "Riga originale" }.Select(Q)));
                foreach (var h in hops.Values)
                {
                    sb.AppendLine(string.Join(sep, new[]
                    {
                        h.Hop.ToString(), h.NoReply ? "*" : h.HostDisplay, h.IpDisplay, h.AsDisplay,
                        Fmt(h.Min), Fmt(h.Avg), Fmt(h.Max),
                        h.LossPercent.ToString("0.#", CultureInfo.CurrentCulture),
                        h.Probes.ToString(), h.Rtts.Count.ToString(), h.TosDisplay, h.NotesDisplay, h.Raw
                    }.Select(Q)));
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                SetStatus("CSV salvato: " + dlg.FileName);
            }
        }

        void ExportTxt()
        {
            if (rtbRaw.TextLength == 0) { MessageBox.Show(this, "Nessun output da esportare.", Text); return; }
            using (var dlg = new SaveFileDialog { Filter = "Testo (*.txt)|*.txt", FileName = DefaultFileName("txt") })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var sb = new StringBuilder();
                sb.AppendLine("PIETRASANTA TRACEROUTE – REPORT DI ESECUZIONE");
                sb.AppendLine(new string('=', 60));
                sb.AppendLine("Data/ora locale     : " + lastStartLocal.ToString("yyyy-MM-dd HH:mm:ss zzz"));
                sb.AppendLine("Data/ora UTC        : " + lastStartUtc.ToString("yyyy-MM-dd HH:mm:ss") + "Z");
                sb.AppendLine("Host Windows        : " + Environment.MachineName + " (utente " + Environment.UserName + ")");
                sb.AppendLine("Distribuzione WSL   : " + lastDistro + "   rete WSL: " + Wsl.GetNetworkingMode());
                sb.AppendLine("Versione tool       : Pietrasanta traceroute " + lastToolVersion + "  (SHA-256 " + Wsl.EmbeddedHash + ")");
                sb.AppendLine("GUI                 : Pietrasanta Traceroute GUI v" + AppVersion);
                sb.AppendLine("Comando             : " + lastCommand);
                if (summary.DestinationReached.HasValue) sb.AppendLine("Destinazione raggiunta: " + (summary.DestinationReached.Value ? "sì" : "no"));
                if (summary.Duration != null) sb.AppendLine("Durata              : " + summary.Duration);
                if (summary.PathMtu != null) sb.AppendLine("Path MTU            : " + summary.PathMtu);
                sb.AppendLine(new string('-', 60));
                sb.AppendLine(rtbRaw.Text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                SetStatus("Report salvato: " + dlg.FileName);
            }
        }

        void CopySelectedHops()
        {
            var sb = new StringBuilder();
            foreach (ListViewItem it in lvHops.SelectedItems)
                sb.AppendLine(string.Join("\t", it.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text)));
            if (sb.Length > 0) Clipboard.SetText(sb.ToString());
        }

        void CopySelectedIp()
        {
            if (lvHops.SelectedItems.Count == 0) return;
            var ip = lvHops.SelectedItems[0].SubItems[2].Text;
            if (ip.Length > 0) Clipboard.SetText(ip);
        }

        // ================================================================= helper UI

        void AppendRaw(string line, Color color)
        {
            rtbRaw.SelectionStart = rtbRaw.TextLength;
            rtbRaw.SelectionLength = 0;
            rtbRaw.SelectionColor = color;
            rtbRaw.AppendText(line + "\n");
            rtbRaw.SelectionColor = rtbRaw.ForeColor;
            rtbRaw.ScrollToCaret();
        }

        void SetStatus(string s) => stStatus.Text = s;

        bool closing;

        /// <summary>Esegue sul thread UI ignorando le chiamate che arrivano a finestra chiusa.</summary>
        void Ui(Action a)
        {
            if (closing || IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(a); }
            catch (InvalidOperationException) { } // include ObjectDisposedException
        }

        void SetToolLabel(string s, Color c) { lblTool.Text = s; lblTool.ForeColor = c; }

        static string FirstLine(string s) => (s ?? "").Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? "";
    }
}
