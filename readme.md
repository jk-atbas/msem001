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