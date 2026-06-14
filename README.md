# Dynamic Island pour Windows

Une « Dynamic Island » à la macOS, posée en haut de l'écran, écrite en **C# / WPF (.NET 10)** —
native Windows, légère et fluide.

![status](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)

## Lancer (développement)

```powershell
dotnet run -c Release --project DynamicIsland.csproj
```

Une pilule noire apparaît, centrée en haut de l'écran.

- **Survole-la** (ou clique) → elle s'agrandit.
- **Onglets** en haut à gauche : Média · Minuteur · Fichiers.
- **Engrenage** en haut à droite → réglages (recentrer, démarrage Windows, quitter).
- **Glisse des fichiers** sur l'île → ils se rangent dans l'onglet Fichiers (re-glissables vers l'extérieur).

## Fonctions

| Fonction | État |
|---|---|
| Pilule compacte + expansion animée | ✅ |
| Heure quand fermée (titre du morceau si lecture) | ✅ |
| Logo de l'app qui joue (Spotify, navigateur…) | ✅ |
| Média : titre/artiste/pochette + ⏮ ▶/⏸ ⏭ + progression | ✅ via Windows SMTC |
| Indicateur audio animé | ✅ |
| Minuteur (+1/+5/+10 min, démarrer/pause/réinit, décompte dans la pilule) | ✅ |
| Étagère de fichiers (drag & drop entrant ET sortant) | ✅ |
| Notifications Windows en bannière | ✅ via UserNotificationListener |
| Batterie + appareils Bluetooth | ✅ |
| Premier lancement guidé + réglages persistés | ✅ |
| Démarrage avec Windows | ✅ |
| Toujours au-dessus, hors barre des tâches, DPI-aware | ✅ |

### À propos des notifications & du mode « Ne pas déranger »

L'app lit le **centre de notifications** Windows. Une app comme Discord, lorsqu'elle est elle-même
en *Ne pas déranger*, **n'envoie rien** à Windows : il n'y a alors rien à capter (limite côté Discord,
pas de l'app). Les notifications Windows normales, elles, sont bien affichées.

## Installateur (Windows)

Construit avec **Inno Setup**. En CI (voir plus bas) il produit `DynamicIsland-Setup.exe` :
raccourci menu Démarrer, raccourci bureau (optionnel), lancement au démarrage (optionnel),
et désinstalleur.

Compilation locale de l'installateur :

```powershell
dotnet publish DynamicIsland.csproj -c Release -r win-x64 --self-contained true -o publish
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\DynamicIsland.iss
# -> installer\Output\DynamicIsland-Setup.exe
```

## Intégration continue

`.github/workflows/build.yml` (GitHub Actions) :
- publie le build Windows self-contained,
- compile l'installateur Inno Setup,
- publie les artefacts (portable + setup),
- attache le setup à la *release* sur un tag `v*`.

## Linux (Debian/Ubuntu)

WPF est **Windows uniquement**. Une version Debian/Ubuntu nécessite un portage de l'UI vers
**Avalonia** et des intégrations natives par OS (MPRIS, UPower, libnotify, BlueZ).
C'est la prochaine étape du projet ; le job CI Linux sera ajouté avec ce portage.

## Structure

```
DynamicIsland/
├─ DynamicIsland.csproj        cible net10.0-windows, WPF
├─ app.manifest                DPI per-monitor v2
├─ App.xaml(.cs)               thème / couleurs
├─ MainWindow.xaml(.cs)        UI + logique (média, minuteur, fichiers, notifs, réglages)
├─ AppIcon.cs                  icône de l'app qui joue / des fichiers
├─ NotificationListener.cs     lecture du centre de notifications
├─ BluetoothBattery.cs         batterie des appareils Bluetooth
├─ Settings.cs                 préférences persistées (%APPDATA%)
├─ installer/DynamicIsland.iss installateur Inno Setup
└─ .github/workflows/build.yml CI
```
