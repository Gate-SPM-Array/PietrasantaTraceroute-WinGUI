# Pietrasanta Traceroute GUI per Windows v1.2.1

[English](README.md) · **Italiano**

> **Lingua:** il programma parla **italiano** e **inglese**. Si sceglie dal menu **🌐 Lingua** e si applica al riavvio, che il programma propone subito. Al primo avvio segue la lingua di Windows. Chiunque può correggere una traduzione o aggiungere una lingua con un file JSON: vedi [Traduzioni](#7-traduzioni).

Interfaccia grafica Windows per **[Pietrasanta traceroute](https://github.com/catchpoint/Networking.traceroute)** (Catchpoint), il traceroute Linux evoluto con **TCP InSession**, UDP InSession, QUIC e lettura di DSCP/ECN.

Pietrasanta traceroute esiste solo per Linux: usa funzioni del kernel Linux, come la coda degli errori ICMP e i raw socket TCP, che Windows non offre. Per questo la GUI non lo porta su Windows: esegue il **binario Linux ufficiale, senza modifiche, dentro WSL2**. Funzionano tutte le modalità, compresa TCP InSession.

---

## 1. Requisiti

- Windows 11 (oppure Windows 10 21H2 o successivo), x64
- **WSL2** con una distribuzione Linux vera (consigliata Ubuntu 24.04). La distribuzione `docker-desktop` creata da Docker Desktop **non** è utilizzabile e viene ignorata.
- .NET Framework 4.8: già presente in Windows 11, niente da installare
- Per usare la GUI non servono privilegi di amministratore

## 2. Preparazione (una volta sola)

1. **Installa Ubuntu** da un PowerShell **normale** (non "come amministratore"):
   ```
   wsl --install -d Ubuntu-24.04
   ```
   In alternativa usa il menu *Strumenti › Installa Ubuntu 24.04 in WSL*. Quando Ubuntu lo chiede, crea l'utente Linux.
   Le distribuzioni WSL sono registrate **per utente Windows**: se Ubuntu viene installato con un altro account (per esempio un account amministrativo separato), il tuo utente non lo vede.
2. Estrai lo zip in una cartella a tua scelta e avvia `PietrasantaTracerouteGUI_v1.2.1.exe`. Scegli la distribuzione e premi **Installa / aggiorna tool in WSL** (pochi secondi). Lo stato diventa *Pronto – versione 0.1.36*.
3. Consigliato: *Strumenti › Rete WSL in modalità "mirrored"*. Con la rete NAT predefinita di WSL compare un hop in più, il NAT interno di WSL (di solito 172.x.x.1). In modalità mirrored WSL usa direttamente le schede di rete del PC. Viene modificato `%USERPROFILE%\.wslconfig`, con copia di backup.

L'eseguibile non è firmato digitalmente: Windows SmartScreen può mostrare "Windows ha protetto il PC". Scegli *Ulteriori informazioni › Esegui comunque*, oppure tasto destro sul file › Proprietà › *Annulla blocco*.

## 3. Uso

- Inserisci la **destinazione**, scegli il **metodo** (UDP, ICMP, TCP SYN, TCP InSession, TCP connect, UDP InSession, QUIC, DNS, DCCP) e premi **▶ Avvia** o Invio.
- Il campo *Comando* mostra la riga di comando esatta che viene eseguita.
- **Scheda *Percorso*:** gli hop aggiornati in tempo reale, con RTT minimo, medio e massimo, perdita, AS e TOS/DSCP/ECN ricevuti da ogni hop. I colori delle righe:
  - giallo: perdita parziale;
  - grigio: nessuna risposta;
  - grassetto: la destinazione.
- **Scheda *Output del tool*:** l'output originale completo.
- ***Esporta CSV*:** la tabella degli hop. Separatore e decimali seguono le impostazioni internazionali di Windows, quindi si apre direttamente in Excel.
- ***Esporta report TXT*:** data e ora locale e UTC, host, distribuzione WSL, versione e SHA-256 del tool, comando e output integrale. Utile come evidenza.
- ***? › Guida opzioni*:** mostra il `--help` completo del tool. Le opzioni che non sono a video si scrivono in *Opzioni aggiuntive*.

## 4. Programma portable

Tutto ciò che il programma scrive resta nella sua cartella:

```
PietrasantaTracerouteGUI_v1.2.1\
  PietrasantaTracerouteGUI_v1.2.1.exe
  PietrasantaTracerouteGUI.settings.ini   ultima distribuzione, metodo, storico delle destinazioni, lingua
  lang\en.json                            traduzione inglese (qui si aggiungono altre lingue)
```

- **Ordine di lettura:** prima la cartella dell'eseguibile, poi `%APPDATA%\PietrasantaTracerouteGUI\settings.ini`. Al primo salvataggio le preferenze trovate lì vengono copiate accanto all'eseguibile.
- **Cartella in sola lettura:** se la cartella dell'eseguibile è in sola lettura (per esempio `C:\Program Files`), il salvataggio avviene in `%APPDATA%`. *? › Informazioni* indica il file in uso.
- **Aggiornamenti:** il nome del file delle preferenze non contiene la versione, quindi sostituendo l'eseguibile le preferenze restano.
- **Cosa resta fuori dalla cartella:** solo ciò che per sua natura non può starci, cioè la distribuzione WSL (con `/usr/local/bin/pietrasanta-traceroute` e `/usr/local/bin/pstr-run`) e, se si attiva la rete mirrored, `%USERPROFILE%\.wslconfig`.

Per rimuovere il tool da WSL:
```
wsl -d Ubuntu-24.04 -u root rm /usr/local/bin/pietrasanta-traceroute /usr/local/bin/pstr-run
```

## 5. Come funziona

- Il tool gira come root dentro WSL (`wsl -d <distro> -u root --exec …`), quindi le modalità con raw socket funzionano senza configurazioni aggiuntive.
- Gli argomenti sono passati con `--exec`, senza shell. La destinazione è validata e l'input dell'utente non viene mai interpretato da una shell.
- `pstr-run` comunica il PID Linux prima di avviare il tool, così *■ Interrompi* può inviargli `SIGINT`.
- Il binario incorporato è quello upstream (`binaries/ubuntu24/x86_64/traceroute`), senza modifiche.
  SHA-256 `a998f88c2c9a2de1f71e5d6507ad9d6998dd1d2866361bcd93831689f1511902`.
  La GUI confronta la copia installata con questo binario e propone l'aggiornamento se differisce.

## 6. Compilare dai sorgenti

Serve il .NET SDK 8 (su Windows, oppure su Linux con la build ufficiale Microsoft):

```
dotnet build -c Release
dotnet test -c Release
```

L'eseguibile viene prodotto in `src/PietrasantaTraceroute.Gui/bin/Release/net48/`. I pacchetti di rilascio hanno la versione nel nome, per esempio `PietrasantaTracerouteGUI_v1.2.1.zip`.

```
src/PietrasantaTraceroute.Gui/     GUI WinForms (net48)
  MainForm.cs                      finestra, avvio/interruzione, export
  Wsl.cs                           chiamate a wsl.exe, installazione del tool, .wslconfig
  TracerouteParser.cs              interpretazione dell'output di traceroute
  Settings.cs                      preferenze portable
  L.cs                             traduzioni (il testo italiano è la chiave)
lang/en.json                       traduzione inglese (incorporata nell'exe e copiata accanto)
  res/pietrasanta-traceroute       binario Linux incorporato (GPL, senza modifiche)
tests/PietrasantaTraceroute.Tests/ test xunit (net8.0, girano anche su Linux)
third_party/pietrasanta-traceroute-0.1.36/   sorgente upstream corrispondente (GPL)
```

## 7. Traduzioni

Stesso schema di AdbSync. Il testo italiano nel codice è la chiave: `L.T("Avvia")`. Un file di lingua associa a ogni testo italiano la sua traduzione; ciò che manca resta in italiano.

```json
{ "code": "en", "language": "English", "translators": "...", "app_version": "1.2.1",
  "strings": { "Avvia": "Start", "hop: {0}": "hops: {0}" } }
```

- Per correggere una traduzione, modifica `lang\en.json` accanto all'eseguibile. Ha la precedenza sulla copia incorporata nell'exe.
- Per aggiungere una lingua, copia `en.json` in `lang\<codice>.json` (per esempio `de.json`), cambia `code` e `language` e traduci i valori. La lingua compare nel menu **🌐 Lingua** al riavvio successivo.
- I segnaposto `{0}`, `{1}` devono restare gli stessi. Una traduzione che li altera viene ignorata e al suo posto compare il testo italiano.
- Le intestazioni delle colonne del CSV non vengono tradotte, così gli script che leggono il CSV continuano a funzionare.

## 8. Licenza

- La GUI (`src/`, `tests/`, documentazione) è distribuita con licenza **MIT** (vedi [LICENSE](LICENSE)). La licenza MIT non copre il binario di Pietrasanta traceroute né `third_party/`.
- Pietrasanta traceroute è © Catchpoint Systems, Inc. e Dmitry Butskoy, con licenza **GNU GPL v2 o successiva**. Il suo binario è incluso senza modifiche come programma separato; il sorgente corrispondente è in [`third_party/`](third_party/pietrasanta-traceroute-0.1.36).
- Vedi [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
