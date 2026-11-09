# AzerothCore Windows Installer and Desktop Launcher

This private repository contains the Windows PowerShell installer for an AzerothCore server and a native C# WinForms desktop launcher.

## What is included

- Server installer source and its Windows x64 executable.
- Desktop launcher source and its Windows executable.
- Build and non-destructive test scripts.
- Spanish installation, review, and change documentation.

The desktop launcher defaults to the LAN realm address `192.168.201.213`. It lets a player select an already-installed WoW 3.3.5a client, writes the realm list, and starts the game. It also provides LAN account registration through a TLS-pinned HTTPS service. The account service is limited to private LAN clients; do not expose its port to the public Internet.

## Client files are not included

This repository deliberately excludes WoW client executables, MPQ archives, client ZIP parts, add-ons bundled with a client, and per-user game data. Each player must obtain and select a complete client separately. The launcher checks for `Wow.exe`, `Data\interface.MPQ`, and a supported locale directory before starting.

The `.gitignore` excludes common client assets and local credentials as a safeguard. Do not remove these exclusions to commit client archives or secrets.

## Build and test on Windows

Requirements:

- Windows PowerShell 5.1.
- Go, Git Bash, and the .NET Framework 4.8 C# compiler for the full package.
- .NET Framework 4.8 to run the desktop launcher.

Run the complete package build and isolated tests:

```powershell
.\Compilar.ps1
```

Build and self-test only the desktop launcher:

```powershell
.\LaunchWoW\Compilar.ps1
```

`Test-Installer.ps1` uses a simulated Docker command and does not install WSL or change firewall rules or scheduled tasks.

## Collaboration

This repository is private. The owner must invite collaborators through GitHub repository settings before they can access it. Review all changes and test the installer on a disposable or backed-up Windows installation before using it on a server with valuable data.

## Licensing

No license is granted here for third-party game clients or assets. This repository does not redistribute those files. Check the licenses and attribution notices for third-party components before modifying or redistributing them.
