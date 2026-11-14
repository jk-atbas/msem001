# AGENTS.md – Spezifikation für Coding-Agents

Verbindliche Spezifikation und Arbeitsregeln für dieses Repository. Bedienungs- und Installationsdoku
für Menschen steht in [readme.md](readme.md); diese Datei beschreibt, **was** der Dienst leisten muss,
**warum** er so gebaut ist und **welche Regeln** bei Änderungen gelten.

## 1. Zweck und Kontext

MSEM001 ist ein Windows-11-Laptop (26H2), der als Jenkins-Build-Agent dient. Der Watchdog-Dienst
(`src/Msem001.Watchdog`) hält ihn ohne manuelle Eingriffe betriebsbereit.

* Jenkins-Controller: `http://10.24.9.62:8080/`, Agent-Name `einarbeitungs-laptop`, WorkDir `C:\jenkins`.
* Container-Builds laufen über Podman (WSL-Machine `podman-machine-default`). Die Docker-API wird von
  `win-sshproxy` auf `\\.\pipe\docker_engine` bereitgestellt.
* **Bekannter Podman-6.x-Fehler:** Nach jedem Neustart von Windows funktioniert der win-sshproxy nicht mehr.
  Die einzige verlässliche Abhilfe ist ein Reset der Machine (`scripts\Reset-PodmanMachine.ps1 -Force`).
* Nightly-Jenkins-Job um 22:00. Der Rechner muss davor genug Speicher haben.

## 2. Fachliche Anforderungen

Versionen: v1 = Commit `44fecc3`, v2 = `e008ecf` (RAM-Prüfung). Aktuelle `<Version>` im csproj: 2.0.0.

| # | Anforderung | Umsetzung |
|---|---|---|
| F1 | Freier Speicher auf C: < 50 GB → `scripts\Clean-JenkinsWorkspace.ps1` | `DiskMaintenanceWorkflow` (alle 5 min via `DiskMonitorWorker`, Sperrfrist 2 h) |
| F2 | Danach weiterhin < 200 GB → `scripts\Reset-PodmanMachine.ps1 -Force`, danach sicherstellen, dass die Machine läuft (sonst starten) | `DiskMaintenanceWorkflow` + `PodmanRecovery`. `-Force` wird **immer im Code** angehängt (siehe §6) |
| F3 | Prüfung F1/F2 zusätzlich ca. 1 h + Puffer vor dem Nightly (22:00) | `NightlyPreCheckWorker`, Standard 20:45, ohne Sperrfrist |
| F4 | win-sshproxy/Docker-API zyklisch prüfen, bei Defekt Reset | `BuildAgentHealthWorker` → `BuildAgentHealthMonitor` → `PodmanHealthChecker` (`/_ping` über Named Pipe). Machine gestoppt → starten. Proxy tot (2× in Folge) → Reset. Reset fehlgeschlagen → `Critical`, neuer Versuch nach 30 min |
| F5 | Vor **jeder** Skript-Routine Jenkins-Agent stoppen, danach wieder verbinden – auch wenn das Skript fehlschlägt | `MaintenanceExecutor` (exklusiv per Semaphore): Agent leerlaufen lassen → Agent stoppen → Skript(e) → Podman prüfen → Agent starten → Node wieder online |
| F14 | Laufende Builds bei einer Wartung nicht abbrechen | `JenkinsAgentDrain` über `JenkinsApiClient` (REST, Basic Auth mit API-Token): Node *temporarily offline* (Grund mit Präfix `[MSEM001 Watchdog]`), warten bis `idle` (max. `DrainTimeout` 1 h; Podman-Reparatur max. `Podman:RepairDrainTimeout` 10 min, da Docker-Builds bei totem Proxy nicht fertig werden), danach wieder online. Sofort ohne Warten bei < `ImmediateMaintenanceThresholdGb` (10 GB) oder ohne Token. Offline durch eine Person bleibt erhalten. Hängengebliebener eigener Offline-Status wird von `TryEnsureAgentRunningAsync` aufgeräumt |
| F6 | Agent-Start: `java -jar agent.jar -url <url> -secret <secret> -name "einarbeitungs-laptop" -webSocket -workDir "C:\jenkins"`; der Agent muss als Admin laufen | `JenkinsAgentController`. Der Agent erbt das Dienstkonto. Laufende Agents werden per WMI-Kommandozeile erkannt, damit kein zweiter gestartet wird |
| F7 | Dienstkonto `MSEM001\atbas` (lokaler Admin) | `deploy\Install-WatchdogService.ps1`. `StartupDiagnostics` prüft das Token (`IProcessTokenInspector`): Administratorgruppe aktiv **und** Integritätsstufe ≥ High. Sonst `Error` mit Ursache und Abhilfe (UAC-gefiltert, kein Mitglied, Administratorschutz) |
| F8 | Ausführliches, strukturiertes Logging | Serilog: Konsole + Text + CLEF unter `%ProgramData%\Msem001Watchdog\logs`. **Alle** Logs über `[LoggerMessage]`-Source-Generator mit fester EventId |
| F9 | Mail bei jedem `Error`/`Critical` an eine konfigurierbare Empfängerliste | `MailAlertLoggerProvider` → `AlertQueue` → `MailAlertDispatcher` (30 s Bündelung, Retry) → `MailKitMailSender`. SMTP `in-v3.mailjet.com:587` STARTTLS. Empfänger ohne `@` erhalten `@atbas.de` |
| F10 | Disk belegt > 90 % → `Error`, > 95 % → `Critical` (jeweils mit Mail) | `DiskUsageAlertEvaluator` + `UsageAlertTracker`: Eskalation sofort, Wiederholung nach 6 h, Entwarnung als `Information` |
| F11 | Jede Mail enthält den aktuellen freien Speicherplatz | `AlertMailComposer`: Zeile im Mailtext + `\| 42.5 GB frei` im Betreff |
| F15 | Laufende Container älter als 1 h (konfigurierbar) automatisch entfernen; der Build-Agent soll keine lang laufenden Container haben (liegengebliebene Testcontainers) | `ContainerCleanupWorker` (10 min, erstmals nach `Podman:StartupDelay`) → `ContainerCleanup` → `PodmanContainerCli` (`podman ps --format json`, Alter aus `StartedAt`; `podman rm --force --volumes --time`). Pod-Infra-Container und `ExcludedNamePrefixes` bleiben. Übersprungen, solange der `MaintenanceExecutor` beschäftigt ist. Entfernen = `Warning`, kein Mail |
| F12 (v2) | RAM belegt > 90 % → `Error`, > 95 % → `Critical`, analog zur Disk | `MemoryMonitorWorker` (1 min) → `MemoryUsageAlertEvaluator` über `WindowsMemoryProbe` (`GlobalMemoryStatusEx`) |
| F13 (v2) | Jede Mail enthält die RAM-Auslastung in Prozent | `AlertMailComposer`: Zeile `RAM-Auslastung:` + `\| RAM 63.5 %` im Betreff. Schlägt eine Messung fehl, geht die Mail trotzdem raus („unbekannt (…)“) |

**Bewusste Ergänzung zu F12:** Ein RAM-Alarm wird erst ausgelöst, wenn die Schwelle `SustainedDuration`
(Standard 5 min) am Stück überschritten ist. Grund: Builds erzeugen kurze RAM-Spitzen. Fällt der Wert
zwischendurch unter die Schwelle, beginnt die Zählung neu. Mit `00:00:00` wird sofort gemeldet, genau wie
bei der Disk.

## 3. Nicht verhandelbare Regeln für Agents

1. **Kein Git-Branching, keine Commits und kein Push**, außer der Nutzer verlangt es ausdrücklich.
   Branches und Versionen verwaltet der Nutzer selbst.
2. **Secrets nie ins Repo.** Jenkins-Secret, Jenkins-API-Token, Mailjet-Key/-Secret und Empfänger stehen nur in
   `appsettings.Local.json` (gitignored). Vorlage: `appsettings.Local.example.json`. In Tests ist
   `"s3cr3t"` der Platzhalter.
3. **Keine persönlichen Daten** (Mailadressen, Namen) in Code, Tests, Doku oder externen Requests.
4. **Build muss ohne Warnungen durchlaufen** (`TreatWarningsAsErrors`). Regeln nicht per Pragma oder
   `.editorconfig` abschalten, um einen Build grün zu bekommen. Ausnahme: die dokumentierten
   Adapter-Pragmas (§5).
5. **Alle Tests grün und schnell.** Die Suite läuft in ca. 10 s. Ein hängender Test schlägt nach 30 s fehl
   (`[assembly: Timeout(30_000)]`). Jede Verhaltensänderung braucht Tests. Coverage-Ziel: Zeilen ≥ 95 %
   (aktuell 96,4 % Zeilen / 88,9 % Branches).
6. **Neue Features konfigurierbar machen** (`appsettings.json`, Abschnitt `Watchdog`), in
   `WatchdogOptionsValidator` validieren, in `StartupDiagnostics` loggen und in `readme.md`
   dokumentieren.
7. Die Skripte in `scripts\` und die Deploy-Skripte in `deploy\` nur auf ausdrücklichen Wunsch ändern.
   Die Deploy-Skripte sind nur syntaktisch geprüft und nie real ausgeführt worden.
8. Doku und Log-/Mail-Texte sind auf **Deutsch**, Code, Bezeichner und XML-Doku-Kommentare auf
   **Englisch**.

## 4. Technik-Stack

* .NET 11 (`global.json`: SDK 11.0.100), `net11.0-windows`, `Microsoft.NET.Sdk.Worker`,
  `AddWindowsService("Msem001Watchdog")`. Publish: self-contained Single-File `win-x64`
  (nur im Publish-Skript, nicht im csproj, sonst kann das Testprojekt den Dienst nicht referenzieren).
* Central Package Management: `Directory.Packages.props`. Gemeinsame Einstellungen:
  `Directory.Build.props` (`AnalysisLevel latest-recommended`, `EnforceCodeStyleInBuild`,
  `GenerateDocumentationFile`, `Nullable`).
* Code-Style: StyleCop.Analyzers (neueste Version, als GlobalPackageReference) + `stylecop.json` +
  `.editorconfig`. Die Regeln dort stammen aus `code-styles-style-cop-editorconfig.txt` und dürfen nicht
  verändert werden. Für `tests/**` sind nur CA1707, CA1848 und CA1861 abgeschaltet.
* Logging: Microsoft.Extensions.Logging mit Serilog als Provider. Mail: MailKit 5
  (`SendAsync(new SendRequest(message), ct)`).
* Solution: `Msem001.Watchdog.slnx`.

## 5. Architektur und Konventionen

### Systemzugriffe nur über Abstraktionen

`BannedSymbols.txt` (BannedApiAnalyzers) und EnvironmentAbstractions.BannedApiAnalyzer machen direkte
Aufrufe zum Build-Fehler (RS0030):

| Verboten | Stattdessen | In Tests |
|---|---|---|
| `File`, `Directory`, `Path`, `DriveInfo`, `FileInfo`, `DirectoryInfo` | `IFileSystem` (System.IO.Abstractions) | `MockFileSystem` von Testably.Abstractions.Testing (`SimulatingOperatingSystem(SimulationMode.Windows)`, `WithDrive(@"C:\", d => d.SetTotalSize(...))`) |
| `Environment` | `IEnvironmentProvider` (EnvironmentAbstractions) | `MockEnvironmentProvider`; für `IsPrivilegedProcess` (read-only) TUnit.Mocks |
| `Process` | `IProcessFactory` / `IProcess` (eigene Abstraktion in `Processes/Abstractions`) | TUnit.Mocks inkl. Events |
| `DateTime(Offset).Now/UtcNow`, `Task.Delay` ohne `TimeProvider` | `TimeProvider` | `FakeTimeProvider` |
| `AppContext.BaseDirectory` | `IHostEnvironment.ContentRootPath` | `TestData.CreateHostEnvironment()` |

Ein Paket für `Process` gibt es nicht: `System.Diagnostics.Abstractions` ist veraltet und kennt die
moderne API nicht. `#pragma warning disable RS0030` ist **nur** in `ProcessFactory` und `Program.cs`
erlaubt. Die dünnen Adapter (`ProcessFactory`, `WmiProcessQuery`, `NamedPipeDockerApiProbe`,
`WindowsMemoryProbe`, `WindowsProcessTokenInspector`) werden durch Tests der Kategorie `Integration` abgedeckt. Systemabstraktionen werden in `AddWatchdog` per `TryAdd`
registriert, damit Tests eigene Instanzen vorgeben können.

### Komponenten (Ordner unter `src/Msem001.Watchdog`)

* `Configuration/`: `WatchdogOptions` mit den Unterabschnitten Disk, Memory, Schedule, Podman, Containers, Scripts,
  JenkinsAgent und Mail, dazu `WatchdogOptionsValidator` (läuft beim Start, `ValidateOnStart`).
* `Monitoring/`: `UsageLevel`, `UsageAlertTracker` und `UsageAlertDecision`. Die gemeinsame
  Schwellwert-, Wiederholungs- und Entwarnungslogik für Disk **und** RAM. Neue Auslastungsprüfungen
  (z.B. CPU) sollen sie wiederverwenden.
* `Disk/`, `Memory/`: Probe-Interface, Implementierung, Info-Record (`ToDisplayString`) und Evaluator.
* `Processes/`: `ProcessRunner` (Timeout, Kill des Prozessbaums, Output-Logging),
  `SystemProcesses`/`WmiProcessQuery`.
* `Scripts/`, `Podman/`, `Jenkins/`: Anbindungen an PowerShell, die podman-CLI mit Named-Pipe-Probe, den
  Agent-Prozess und die Jenkins-REST-API (`JenkinsApiClient`, `JenkinsAgentDrain`). Der API-Client wird in Tests
  über `StubHttpHandler` (TestSupport) gegen feste HTTP-Antworten geprüft.
* `Maintenance/`: `MaintenanceExecutor` (exklusive Wartungsfenster), `DiskMaintenanceWorkflow`,
  `BuildAgentHealthMonitor`.
* `Workers/`: `BackgroundService`s. Ein Zyklus ist als `internal` Methode testbar
  (`RunCycle`/`RunCycleAsync`), die Schleife nutzt `TimeProvider`. `DailySchedule` liefert die
  Tageszeitpunkte.
* `Alerting/`: Logger-Provider → Queue → Dispatcher → Composer/Sender.
* `Hosting/`: `ServiceCollectionExtensions.AddWatchdog`, `StartupDiagnostics`.

Reihenfolge der Hosted Services (in `HostingTests` geprüft): StartupDiagnostics, MailAlertDispatcher,
BuildAgentHealthWorker, DiskMonitorWorker, MemoryMonitorWorker, NightlyPreCheckWorker, ContainerCleanupWorker.

### Logging-Konventionen

* Nur `[LoggerMessage]`-Methoden (`private static partial` am Ende der Klasse, nach den
  Instanzmethoden, wegen SA1204). CA1873 verlangt billige Argumente: Objekte oder lokale Variablen
  übergeben, nichts im Aufruf berechnen.
* `Error`/`Critical` erzeugen **Mails**. Diese Level nur für Zustände verwenden, die ein Mensch sehen muss.
* EventId-Bereiche je Klasse (eindeutig halten; neue Klassen bekommen den nächsten freien Bereich, z.B. 16000):

  | Bereich | Klasse | Bereich | Klasse |
  |---|---|---|---|
  | 100–109 | StartupDiagnostics | 7000 | DiskMaintenanceWorkflow |
  | 1100 | DiskUsageAlertEvaluator | 8000 | BuildAgentHealthMonitor |
  | 1200 | MemoryUsageAlertEvaluator | 9000 | MailAlertDispatcher |
  | 2000 | ProcessRunner | 10000 | DiskMonitorWorker |
  | 3000 | PowerShellMaintenanceScripts | 11000 | NightlyPreCheckWorker |
  | 4000 / 4100 / 4200 / 4300 | PodmanCli / PodmanHealthChecker / PodmanRecovery / PodmanContainerCli | 12000 | BuildAgentHealthWorker |
  | 5000 / 5100 | JenkinsAgentController / JenkinsAgentDrain | 13000 | MemoryMonitorWorker |
  | 6000 | MaintenanceExecutor | 14000 | ContainerCleanup |
  | | | 15000 | ContainerCleanupWorker |

## 6. Bekannte Fallstricke

* **Config-Binder hängt Listen an Defaults an.** Listen-Defaults in Options-Klassen müssen leer sein.
  Deshalb ist `PodmanResetArguments` leer und `-Force` wird in `BuildPodmanResetArguments` fest angehängt.
* **ProcessRunner** wartet auf das `Exited`-Event und danach höchstens 2 s (über `TimeProvider`) auf das
  Ende der Ausgabe. In Mock-Tests müssen die Streams geschlossen werden (`CloseStreams()`-Helper), sonst
  hängt der Test bzw. läuft in den 30-s-Timeout. Mocks für Methoden mit Timeout-Token müssen das Token
  beachten (`Task.Delay(Timeout.Infinite, token)`).
* Testably definiert ebenfalls einen `TimeProvider`. Deshalb gibt es im Testprojekt den globalen Alias
  `System.TimeProvider`.
* `<see cref="Process"/>` in XML-Doku gilt als Verwendung der gebannten API → `<c>Process</c>` schreiben.
* **Admin-Prüfung nicht über `IsPrivilegedProcess`.** `Environment.IsPrivilegedProcess` (= `TokenIsElevated`) ist für
  einen Dienst unter einem Admin-Konto `false`, weil es ohne UAC-Tokenpaar kein „elevated“-Token gibt. Das Token
  hat trotzdem volle Rechte (auf MSEM001 am 13.11.2026 beobachtet: Elevation `Default`, Administratorgruppe
  `Enabled`). Maßgeblich ist `ProcessTokenInfo.HasAdministratorRights`. `IsPrivilegedProcess` dient nur als
  Fallback, wenn die Token-Untersuchung fehlschlägt.
* **Jenkins-API ohne automatische Redirects.** `toggleOffline` antwortet mit 302 auf die Node-Seite.
  `HttpClient` entfernt beim Folgen den `Authorization`-Header, der anonyme Folge-Request endet mit 403,
  obwohl der Toggle gegriffen hat (auf MSEM001 am 13.11.2026 beobachtet). Deshalb `AllowAutoRedirect = false`
  in `JenkinsApiClient.CreateHandler` und 3xx als Erfolg werten.
* `Environment.NewLine` ist bereits vom EnvironmentAbstractions-Analyzer gebannt; ein zweiter Eintrag in
  `BannedSymbols.txt` führt zu RS0031.
* Mit .NET 10+ wird VSTest nicht mehr unterstützt. `global.json` setzt den Runner auf
  Microsoft.Testing.Platform; `dotnet test` braucht deshalb `--solution`.
* Public-Testmethoden dürfen keine `internal` Enums als Parameter haben (CS0051): `int` übergeben und casten.
* TUnit0018: Testmethoden dürfen keine Instanzfelder zuweisen; stattdessen lokale Variablen (`using var`).

## 7. Tests

* Framework: **TUnit** 1.x mit TUnit.Assertions, TUnit.Mocks und TUnit.Mocks.Logging. Nicht durch
  xUnit, NUnit, NSubstitute, Moq oder FluentAssertions/AwesomeAssertions ersetzen.
* Aufbau spiegelt `src/` (`tests/Msem001.Watchdog.Tests/<Ordner>/<Klasse>Tests.cs`). Gemeinsame Helfer
  liegen in `TestSupport/`: `TestData` (Options ohne Wartezeiten, FileSystem, Environment,
  `Disk(...)`, `Memory(...)`, Prozessergebnisse), `Wait.UntilAsync`, `LoggerAssertions`.
* Mock-API: `Mock.Of<T>()`, `mock.Method(Arg.Any<T>()).Returns(...)/.Throws(...)/.Callback(...)`,
  `.WasCalled(Times.Once)`, `Mock.Logger<T>()` + `logger.VerifyLog().AtLevel(...).ContainingMessage(...)`.
* Tests mit echten Prozessen, Named Pipes, WMI oder Win32 tragen `[Category("Integration")]`.
* Keine echten Wartezeiten: Zeitlogik mit `FakeTimeProvider` testen. `Task.Delay` ist nur für die wenigen
  Hintergrund-Dispatcher-Tests erlaubt.

```pwsh
dotnet build Msem001.Watchdog.slnx
dotnet test --solution Msem001.Watchdog.slnx
dotnet test --solution Msem001.Watchdog.slnx -- --treenode-filter "/*/*/*/*[Category!=Integration]"
dotnet test --solution Msem001.Watchdog.slnx --coverage --coverage-settings coverage.config --coverage-output-format cobertura
```

## 8. Definition of Done für Änderungen

- [ ] `dotnet build` ohne Warnungen und Fehler
- [ ] alle Tests grün, neue Logik getestet, Coverage nicht gesunken
- [ ] neue Konfiguration in `appsettings.json` (und ggf. `appsettings.Local.example.json`), im Validator
      und in `StartupDiagnostics` berücksichtigt
- [ ] `readme.md` (Bedienung) und diese Datei (Spezifikation, EventIds, Fallstricke) aktualisiert
- [ ] bei fachlicher Erweiterung `<Version>` im csproj erhöhen (nach Rücksprache mit dem Nutzer)
- [ ] nichts committet, keine Secrets im Diff

## 9. Offene Punkte / auf MSEM001 zu verifizieren

* `podman`/WSL aus einer Dienst-Sitzung (Session 0) unter `atbas` muss auf der echten Maschine
  verifiziert werden.
* Der Mailjet-Absender (`Mail:From` + Suffix) muss in Mailjet als Absender verifiziert sein.
  Echter SMTP-Versand ist nicht automatisiert getestet.
* Sanfte Wartung gegen den echten Jenkins-Controller verifizieren: Offline-/Online-Schalten mit dem
  API-Token, `idle`-Erkennung bei laufender Pipeline. Der Controller läuft über HTTP, der Token geht also
  unverschlüsselt durchs interne Netz.
* Ein anderweitiger Autostart des Agents (geplante Aufgabe o.ä.) sollte auf MSEM001 deaktiviert werden.
* `Program.cs` (Host-Aufbau) und die Deploy-Skripte sind nicht automatisiert getestet.
