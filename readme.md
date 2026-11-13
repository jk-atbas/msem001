# MSEM001

RDP-Verbindung auf die Maschine (VPN muss aktiv sein ...duh):

```
Adresse: MSEM001
Anmeldename: ATBAS
Passwort: Bitwarden unter 
ASUS Lapi Entwicklung MSEM001
```

## Send-WakeOnLan

Script für das Entsenden von Magic Pakets per UDP Broadcast

Bsp:

```pwsh
.\Send-WakeOnLan.ps1 -MacAddress 'AA:BB:CC:DD:EE:FF' -Verbose
```

## Start-MSEM001

Benutzt das [Send-WakeOnLan.ps1](Send-WakeOnLan.ps1) Script, speziell mit Mac-Adress

## Scripts-Ordner

### Clean-JenkinsWorkspace

Bereinigt `C:\jenkins\workspace`

### Reset-PodmanMachine

Löscht und setzt die Podman-Maschine neu auf (rootless).

## Watchdog-Dienst (`src/Msem001.Watchdog`)

.NET-11-Windows-Dienst, der MSEM001 als Build-Agent am Laufen hält. Er läuft unter `MSEM001\atbas`
(lokaler Admin, Besitzer der Podman-Machine). Der vom Dienst gestartete Jenkins-Agent erbt dieses Konto
mit vollem Admin-Token, weil Dienst-Anmeldungen nicht der UAC-Filterung unterliegen.

### Was der Dienst tut

| Prüfung | Intervall | Reaktion |
|---|---|---|
| Speicher C: belegt > 90 % / > 95 % | 5 min | Log `Error` / `Critical` und Mail (Wiederholung nach 6 h, Eskalation sofort) |
| RAM belegt > 90 % / > 95 % (seit v2) | 1 min | wie beim Speicher, aber erst wenn die Schwelle **5 min am Stück** überschritten ist (kurze Spitzen während Builds lösen keinen Alarm aus; `Watchdog:Memory:SustainedDuration`) |
| Freier Speicher < 50 GB | 5 min (Sperrfrist 2 h) | Agent stoppen, `Clean-JenkinsWorkspace.ps1` |
| …danach weiterhin < 200 GB | – | `Reset-PodmanMachine.ps1 -Force`, danach sicherstellen, dass die Machine läuft (sonst `podman machine start`) |
| Nightly-Vorabprüfung | täglich 20:45 (Nightly um 22:00) | wie oben, ohne Sperrfrist |
| Podman-Machine läuft + Docker-API über `\.\pipe\docker_engine` (win-sshproxy) antwortet auf `/_ping` | 2 min, erstmals 1 min nach Start | Machine gestoppt (z.B. nach Neustart): starten. Proxy weiterhin tot oder 2× hintereinander tot: Machine zurücksetzen. Schlägt das fehl: `Critical` und neuer Versuch nach 30 min |
| Jenkins-Agent läuft | 2 min | `java -jar agent.jar -url … -secret … -name einarbeitungs-laptop -webSocket -workDir C:\jenkins` starten |

Jede Wartungsroutine läuft exklusiv: Agent beenden → Skript(e) → Podman prüfen → Agent neu verbinden.
Der Agent wird auch dann neu gestartet, wenn ein Skript fehlschlägt.

**Sanfte Wartung (empfohlen):** Mit einem Jenkins-API-Token (`Watchdog:JenkinsAgent:ApiUser`/`ApiToken`)
bricht eine Wartung laufende Builds nicht mehr ab:

1. Der Agent wird in Jenkins *vorübergehend offline* geschaltet (Grund „[MSEM001 Watchdog] Wartung …“), es starten
   keine neuen Builds.
2. Der Dienst wartet, bis kein Build mehr läuft, höchstens `DrainTimeout` (1 h), Abfrage alle 30 s.
3. Erst dann wird der Agent beendet und die Wartung ausgeführt.
4. Danach startet der Agent neu und wird in Jenkins wieder online geschaltet.

Sinkt der freie Speicher unter `Watchdog:Disk:ImmediateMaintenanceThresholdGb` (10 GB), wird nicht (weiter) gewartet.
Hat eine Person den Agent offline geschaltet, wird gewartet, der Agent bleibt danach aber offline. Bleibt der
Watchdog-Offline-Status hängen (Absturz, Jenkins nicht erreichbar), schaltet die Agent-Überwachung ihn später wieder
online. Ohne Token wird der Agent wie bisher sofort beendet (Warnung im Startlog).

Token anlegen: in Jenkins als Benutzer mit dem Recht *Agent → Disconnect* (z.B. Admin) anmelden → Benutzer →
Security → API Token → „Add new Token“. Benutzer und Token in `appsettings.Local.json` eintragen.

Alle Werte sind in `appsettings.json` (Abschnitt `Watchdog`) konfigurierbar.

### Logging und Mails

* Logs: `%ProgramData%\Msem001Watchdog\logs\watchdog-YYYYMMDD.log` (Text) und `.clef` (strukturiertes JSON,
  z.B. mit [Seq](https://datalust.co/seq) oder `clef-tool` auswertbar), 60 Tage Aufbewahrung.
* Jedes Log-Ereignis mit Level `Error`/`Critical` wird per Mail verschickt (Mailjet, Port 587, STARTTLS).
  Ereignisse innerhalb von 30 s werden zu einer Mail gebündelt. Jede Mail enthält den aktuellen freien
  Speicherplatz und die aktuelle RAM-Auslastung in Prozent (im Text und kurz im Betreff, z.B. `| 42.5 GB frei | RAM 63.5 %`).
* Empfänger ohne `@` bekommen automatisch `@atbas.de` angehängt.

### Build, Tests, Coverage

```pwsh
dotnet build Msem001.Watchdog.slnx
dotnet test --solution Msem001.Watchdog.slnx
# nur schnelle Unit-Tests (ohne echte Prozesse/Named Pipes)
dotnet test --solution Msem001.Watchdog.slnx -- --treenode-filter "/*/*/*/*[Category!=Integration]"
dotnet test --solution Msem001.Watchdog.slnx --coverage --coverage-settings coverage.config --coverage-output-format cobertura
```

* Test-Framework: [TUnit](https://tunit.dev) (Microsoft.Testing.Platform) mit TUnit.Assertions, TUnit.Mocks und
  TUnit.Mocks.Logging. Ein Test, der hängt, schlägt nach 30 s fehl (`[assembly: Timeout]`).
* Systemzugriffe laufen im Dienst ausschließlich über Abstraktionen. Direkte Aufrufe sind per
  `BannedSymbols.txt` (Microsoft.CodeAnalysis.BannedApiAnalyzers) ein Build-Fehler:

  | System-API | Abstraktion im Dienst | In Tests |
  |---|---|---|
  | `File`, `Directory`, `Path`, `DriveInfo` | `IFileSystem` ([System.IO.Abstractions](https://github.com/TestableIO/System.IO.Abstractions)) | `MockFileSystem` ([Testably.Abstractions.Testing](https://github.com/Testably/Testably.Abstractions)), inkl. simulierter Laufwerksgröße |
  | `Environment` | `IEnvironmentProvider` ([EnvironmentAbstractions](https://github.com/jeffkl/EnvironmentAbstractions)) | `MockEnvironmentProvider` bzw. TUnit.Mocks |
  | `Process` | `IProcessFactory` / `IProcess` (eigene, schlanke Abstraktion, siehe unten) | TUnit.Mocks inkl. Events |
  | `DateTime.Now`, `Task.Delay` | `TimeProvider` | `FakeTimeProvider` |
  | `AppContext.BaseDirectory` | `IHostEnvironment.ContentRootPath` | `HostingEnvironment` |

  Für `Process` gibt es kein gepflegtes Paket, das die moderne API (`Kill(entireProcessTree)`,
  `WaitForExitAsync`, `ArgumentList`) abbildet. `System.Diagnostics.Abstractions` ist seit 2020 unverändert
  und netstandard2.0-only. Die eigene Abstraktion bildet nur die genutzten Member 1:1 ab. Die Adapter
  (`ProcessFactory`, `WmiProcessQuery`, Named-Pipe-Probe) sind durch Integrationstests der Kategorie
  `Integration` abgedeckt.
* Code-Style: StyleCop.Analyzers + `.editorconfig`, Warnungen sind Fehler.

### Installation auf MSEM001

```pwsh
# Auf dem Entwicklungsrechner: testen + self-contained Single-File nach .\publish
.\deploy\Publish-Watchdog.ps1

# publish-Ordner auf MSEM001 kopieren, dann dort als Administrator:
.\deploy\Install-WatchdogService.ps1 -SourcePath .\publish     # fragt das Passwort von MSEM001\atbas ab
```

Beim ersten Mal wird `C:\Services\Msem001Watchdog\appsettings.Local.json` aus der Vorlage angelegt
(nicht im Git, nur für Admins lesbar). Dort Jenkins-Secret, Jenkins-API-Benutzer/-Token (sanfte Wartung),
Mailjet-API-Key/-Secret und Empfänger eintragen, danach `Restart-Service Msem001Watchdog`. Ohne Secret und Empfänger startet der Dienst
bewusst nicht (Konfigurationsvalidierung).

Der Dienst startet „Automatisch (verzögert)“ und wird bei Absturz nach 1/1/5 Minuten neu gestartet.
Ein bisher anderweitig gestarteter Agent (Autostart, geplante Aufgabe) sollte deaktiviert werden. Der
Dienst erkennt laufende Agents zwar an der Kommandozeile und startet keinen zweiten, übernimmt aber
nach der ersten Wartungsroutine ohnehin selbst.

Deinstallation: `.\deploy\Uninstall-WatchdogService.ps1 [-RemoveFiles]`
