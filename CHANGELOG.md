# Changelog

Regola: ogni versione consegnata ha un numero diverso; anche una modifica minima del codice produce una nuova versione con la sua voce qui sotto.

## v1.2.1 — 2026-10-16

Solo interfaccia: nessuna modifica al funzionamento del traceroute.

### Novità
- **Interfaccia in italiano e inglese.** Menu **🌐 Lingua / Language**; la scelta viene salvata nelle preferenze (`Language=` in `PietrasantaTracerouteGUI.settings.ini`) e si applica al riavvio, che il programma propone subito.
  - Al primo avvio la lingua segue quella di Windows: italiano se Windows è in italiano, altrimenti inglese.
  - Stesso schema di AdbSync: il testo italiano nel codice è la chiave, l'inglese è in `lang\en.json`. Chiunque può correggere una traduzione o aggiungere una lingua con un file `lang\<codice>.json`; ciò che manca resta in italiano e una traduzione che altera i segnaposto `{0}` viene ignorata.
  - `en.json` è anche incorporato nell'eseguibile, che da solo resta autosufficiente; il file in `lang\` accanto all'exe, se presente, ha la precedenza. *🌐 Lingua → Apri la cartella delle traduzioni*.
  - Tradotti anche il report TXT, la diagnostica WSL e i messaggi di installazione. Le intestazioni del CSV restano invariate, per non rompere chi lo elabora.
- *? › Informazioni* mostra la lingua in uso e l'indirizzo del progetto su GitHub.

### Altro
- **Build riproducibile:** l'eseguibile non contiene più l'hash del commit git nel numero di versione, quindi la stessa versione produce lo stesso exe sia da un clone git sia dallo zip dei sorgenti.
- Test: 37, di cui 11 nuovi sulla localizzazione (ogni testo tradotto ha la sua voce in `en.json`, nessuna voce orfana, segnaposto identici, nessun testo a video senza `L.T`, lingua automatica, ritorno all'italiano se il file manca o non è valido).

## v1.2 — 2026-10-14

### Novità
- **Modalità portable.** Le preferenze (ultima distribuzione WSL, metodo, storico delle destinazioni) sono salvate nella cartella dell'eseguibile, in `PietrasantaTracerouteGUI.settings.ini`. Copiando la cartella ci si porta dietro anche le preferenze.
  - Lettura: prima la cartella dell'eseguibile; se lì il file non c'è, `%APPDATA%\PietrasantaTracerouteGUI\settings.ini`. Al primo salvataggio le preferenze trovate in `%APPDATA%` vengono copiate in locale.
  - Solo se la cartella dell'eseguibile è in sola lettura (per esempio `C:\Program Files`) il salvataggio avviene in `%APPDATA%`.
  - Il nome del file non contiene la versione: sostituendo l'eseguibile le preferenze restano.
  - Il percorso in uso è indicato in *? › Informazioni* e nella diagnostica.
- *File › Apri la cartella del programma*.

## v1.1 — 2026-10-14

### Corretto
- **"Nessuna distribuzione WSL" senza spiegazione.** Con la sola distribuzione `docker-desktop` (creata da Docker Desktop, non utilizzabile) la GUI non diceva perché. Ora mostra "Solo distribuzioni Docker Desktop (non utilizzabili)" e l'output grezzo di `wsl -l -v` nella scheda *Output del tool*.
- **Installazione di Ubuntu per l'utente sbagliato.** *Strumenti › Installa…* avviava `wsl --install` come amministratore: con un account amministrativo separato la distribuzione veniva registrata per quell'account, invisibile all'utente che usa la GUI. Ora l'installazione parte con l'utente corrente, in una finestra di comando visibile.

### Novità
- *Strumenti › Diagnostica WSL*: utente Windows, versione e stato di WSL, elenco delle distribuzioni; esportabile con *Esporta report TXT*.

## v1.0 — 2026-10-14

Prima versione.

- GUI Windows (WinForms, .NET Framework 4.8 già incluso in Windows 11, nessun privilegio di amministratore) per **Pietrasanta traceroute 0.1.36** di Catchpoint, che gira dentro **WSL2**: tutte le modalità sono disponibili, comprese TCP InSession, UDP InSession e QUIC.
- Il binario Linux ufficiale è incorporato nell'eseguibile; *Installa / aggiorna tool in WSL* lo copia in `/usr/local/bin/pietrasanta-traceroute` (verifica SHA-256) e installa lo script di avvio `pstr-run`. Se manca, installa `libcrypto` (OpenSSL 3).
- Parametri principali a video (metodo, porta, IPv4/IPv6, hop massimi, sonde, timeout, DSCP/ECN, AS lookup, estensioni ICMP/MPLS, Path MTU), opzioni aggiuntive libere e anteprima del comando esatto.
- Tabella degli hop aggiornata in tempo reale (RTT min/medio/max, perdita, AS, TOS/DSCP/ECN ricevuti), output originale completo, interruzione dell'esecuzione.
- Esportazione CSV (separatore e decimali secondo le impostazioni di Windows) e report TXT con data/ora locale e UTC, comando, versione e SHA-256 del binario.
- Opzione per la rete WSL "mirrored" (`%USERPROFILE%\.wslconfig`, con copia di backup).
- Test prima della consegna (Mono + `wsl.exe` simulato): installazione, esecuzione, interruzione, export, chiusura durante un'esecuzione. Corretti tre errori trovati così: riepilogo mancante quando la destinazione veniva raggiunta, errori alla chiusura con una traccia in corso, campo DSCP.
