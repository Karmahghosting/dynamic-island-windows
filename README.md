# Dynamic Island (Windows + Linux)

Une « Dynamic Island » à la macOS, posée en haut de l'écran. Portée en **Avalonia (.NET 9)** :
**un seul code** pour **Windows** et **Linux**, avec des intégrations natives par OS.

![platforms](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-blue)

## Lancer (développement)

```bash
# Windows
dotnet run -f net9.0-windows10.0.19041.0
# Linux
dotnet run -f net9.0
```

- **Survole** l'île → elle s'agrandit.
- **Onglets** : Média · Minuteur · Fichiers.
- **•••** → réglages (recentrer, démarrage auto, quitter).
- **Glisse des fichiers** dessus → ils se rangent dans l'onglet Fichiers (clic pour ouvrir).

## Fonctions

| Fonction | Windows | Linux |
|---|---|---|
| Pilule compacte + expansion animée | ✅ | ✅ |
| Heure fermée / titre du morceau en lecture | ✅ | ✅ |
| Média : titre/artiste + lecture/pause/suiv/préc + progression | ✅ SMTC | ✅ `playerctl` (MPRIS) |
| Pochette + logo de l'app | ✅ | ⚠️ (pochette/icone non résolues) |
| Minuteur (+1/+5/+10, démarrer/pause/réinit) | ✅ | ✅ |
| Étagère de fichiers (drop entrant) | ✅ | ✅ |
| Notifications en bannière | ✅ UserNotificationListener | ⚠️ non implémenté (best-effort à venir) |
| Batterie | ✅ | ✅ `/sys` |
| Batterie Bluetooth | ✅ | ⚠️ best-effort `upower` |
| Démarrage auto | ✅ registre | ✅ `~/.config/autostart` |
| Toujours au-dessus, sans décorations | ✅ | ✅ |

> **Linux** : le portage compile et produit les binaires (.deb / tar.gz). Les intégrations
> simples (média via `playerctl`, batterie, démarrage auto) sont en place ; pochette, icônes,
> notifications et batterie BT sont *best-effort* et restent à valider sur une vraie machine Linux.
> Pré-requis Linux pour le média : le paquet **`playerctl`**.

### Notifications & « Ne pas déranger »

Sous Windows on lit le centre de notifications. Une app comme Discord, lorsqu'elle est elle-même
en *Ne pas déranger*, **n'envoie rien** à Windows : rien à capter (limite côté Discord).

## Construire les paquets

### Windows (.exe + installateur Inno Setup)

```powershell
dotnet publish DynamicIsland.csproj -f net9.0-windows10.0.19041.0 -r win-x64 --self-contained true -o publish
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\DynamicIsland.iss
# -> installer\Output\DynamicIsland-Setup.exe
```

### Linux (.deb + tar.gz)

```bash
dotnet publish DynamicIsland.csproj -f net9.0 -r linux-x64 --self-contained true -o publish-linux
tar czf DynamicIsland-linux-x64.tar.gz -C publish-linux .
bash installer/build-deb.sh        # -> dynamic-island_1.0.0_amd64.deb
```

## CI

`.github/workflows/build.yml` : deux jobs (**windows** + **linux**) qui publient, packagent
(installateur Windows, .deb/tar.gz Linux), publient les artefacts et créent une *release* sur tag `v*`.

## Structure

```
DynamicIsland.csproj          multi-cible net9.0 (Linux) + net9.0-windows (WinRT)
Program.cs / App.axaml        bootstrap Avalonia
MainWindow.axaml(.cs)         UI + logique commune
Settings.cs                   préférences persistées (%APPDATA% / ~/.config)
Services/Abstractions.cs      interfaces + modèles
Services/PlatformServices.cs  fabrique (#if WINDOWS)
Platform/Windows/*.cs         SMTC, notifications, batterie, BT, icônes, démarrage
Platform/Linux/LinuxServices.cs  playerctl, /sys, upower, .desktop
installer/DynamicIsland.iss   installateur Windows
installer/build-deb.sh        paquet Debian/Ubuntu
.github/workflows/build.yml   CI Windows + Linux
```
