# AzerothCore Windows Installer and Launcher

This repository contains the Windows PowerShell installer for an AzerothCore server and a native C# WinForms desktop launcher.

## Quick start

1. Download the repository ZIP and extract it to a local folder.
2. On the Windows 10 Pro x64 server PC, run `Instalar_WoW_Server.exe` as administrator and follow the prompts. Allow time for WSL, Docker, and AzerothCore to install.
3. Start the server and verify that authserver and worldserver are running.
4. On each player PC, run `LaunchWoW\LaunchWoW.exe` and select an existing compatible client, or choose the option to install from a client ZIP you already have.
5. Press **JUGAR**. Create accounts through the launcher's LAN registration flow or with the server's account-management commands.

See [LEEME.md](./LEEME.md) for detailed installation and troubleshooting instructions.

## Network and client requirements

- The launcher is configured for realm and account service address `192.168.201.213`. This is a private LAN address and only works for devices that can reach the server on the same local network. Internet play requires a separately planned public network setup; the installer does not configure router access.
- The account-registration service uses TLS certificate pinning. Activate it on the server PC and provide its certificate fingerprint to players through a separate trusted channel. Do not expose port 8787 to the public Internet.
- The repository does not include or download a WoW client, game executables, MPQ archives, client ZIP parts, add-ons bundled with a client, or per-user game data. Each player must supply a compatible client or client ZIP separately. The launcher checks for WoW 3.3.5a build 12340, `Wow.exe`, `Data\common.MPQ`, and a locale directory (`Data\enUS`, `Data\enGB`, or `Data\esES`). It does not require `Data\interface.MPQ`.
- The server must be installed and running before players can log in. The launcher and installer do not themselves host a public game service.

The `.gitignore` excludes common client assets and local credentials as a safeguard. Do not remove these exclusions to commit client archives or secrets.

## Build and test on Windows

Requirements:

- Windows PowerShell 5.1.
- Go, Git Bash, and the .NET Framework 4.8 C# compiler for the full package.
- .NET Framework 4.8 to run the desktop launcher.

Build the complete package and run the isolated tests:

```powershell
.\Compilar.ps1
```

To build and self-test only the desktop launcher:

```powershell
.\LaunchWoW\Compilar.ps1
```

`Test-Installer.ps1` uses a simulated Docker command and does not install WSL or change firewall rules or scheduled tasks. Passing build tests is not a substitute for testing the server installation and a real login on the target network.

## Collaboration

If the repository is private, the owner must invite collaborators through GitHub repository settings before they can download it. Review all changes and test the installer on a disposable or backed-up Windows installation before using it on a server with valuable data.

## Licensing

No license is granted here for third-party game clients or assets. This repository does not redistribute those files. Check the licenses and attribution notices for third-party components before modifying or redistributing them.
