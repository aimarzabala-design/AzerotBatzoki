# AzerothCore Windows Installer and Launcher

This repository contains the Windows PowerShell installer for an AzerothCore server and a native C# WinForms desktop launcher.

## Quick start

1. Download the repository ZIP and extract it to a local folder.
2. On the Windows 10 Pro x64 server PC, run `Instalar_WoW_Server.exe` as administrator and follow the prompts. Allow time for WSL, Docker, and AzerothCore to install.
3. Start the server and verify that authserver and worldserver are running.
4. On the download-host PC (`192.168.201.209`), build and run `LaunchWoW\AzerotBatzoki.exe`, then choose **ACTIVAR LAN**. This serves the private client on HTTPS port `8788`.
5. On each player PC, run `LaunchWoW\AzerotBatzoki.exe`, choose **CLIENTE / INSTALAR** → **No**, and let the launcher download, verify and configure the client. Press **JUGAR** to start WoW.

See [LEEME.md](./LEEME.md) for detailed installation and troubleshooting instructions.

## Network and client requirements

- The game realm is `192.168.201.213` (authentication port `3724`, world port `8085`). The separate client-download host is `192.168.201.209:8788`; both addresses require LAN access.
- Keep the download host running while players install. The launcher serves only manifest-listed client files over HTTPS, restricts requests to private-network addresses, pins the host certificate, verifies SHA-256 for every file, and retries/resumes a transfer up to three times while the installer remains open. It reads `Documents\AzerotBatzoki-ClientDistribution`; client files and the private certificate are not in Git. If the certificate is rotated, rebuild and redistribute the launcher with the new public fingerprint; never distribute the PFX.
- On the download host, set the trusted Wi-Fi network profile to Private, then run PowerShell as administrator and execute `LaunchWoW\Enable-DownloadHostFirewall.ps1` once. It allows inbound TCP `8788` only for `LocalSubnet` on the Private profile. Port `8787` is left as-is for the existing account service; this launcher change does not alter the remote server or its ports.
- The launcher checks WoW 3.3.5a build 12340, `Wow.exe`, `Data\common.MPQ`, and one locale (`Data\enUS`, `Data\enGB`, or `Data\esES`). `Data\interface.MPQ` is not required. It includes `Interface\AddOns` and omits the rest of `Interface`, `WTF`, caches, logs, screenshots, errors, realmlist files, and Frostbound files/folders from the download manifest.
- The server must be installed and running before players can log in. The launcher and installer do not themselves host a public game service.

The `.gitignore` excludes common client assets and local credentials as a safeguard. Do not remove these exclusions to commit client archives or secrets.

## Client and addon updates

Place shared addons under `Documents\AzerotBatzoki-ClientDistribution\Interface\AddOns\AddonName` on the download-host PC. Complete the copy before players start an update. The host watches published distribution files and rebuilds its cached manifest on the next request after changes; calculating hashes may take time. Changes to excluded settings, logs and caches do not invalidate it. Restart the host once after upgrading its executable to enable this behavior.

On player PCs, close WoW, select the existing client through **INSTALAR** or **CUENTA · AJUSTES**, then press **ACTUALIZAR**. The launcher compares file sizes and SHA-256 hashes with the host, downloads missing or changed files, verifies them, and replaces each file after verification. It preserves files absent from the manifest, including local addons and settings; an addon with the same path as a shared addon will be updated to the host's version. It does not remove addons deleted from the distribution. Version 1.2.1 separates comparison from transfer, downloads up to four pending files concurrently, and reaches 100% only after every replacement is verified. Copy the new launcher to player PCs as well as the host; an already running launcher continues using its old code.

## Build and test on Windows

Requirements:

- Windows PowerShell 5.1.
- Go, Git Bash, and the .NET Framework 4.8 C# compiler for the full package.
- .NET Framework 4.8 to run the desktop launcher.

Build the complete package and run the isolated tests:

```powershell
.\Compilar.ps1
```

To build and self-test the branded desktop launcher (`AzerotBatzoki.exe`):

```powershell
.\LaunchWoW\Compilar.ps1
```

The custom launcher embeds its multi-resolution Windows icon (`LaunchWoW\Assets\AzerotBatzoki.ico`) and the launcher artwork at build time, so those visual assets do not need to sit beside `AzerotBatzoki.exe` on player PCs. Launcher 1.2 uses an adaptive layout, a Northrend artwork cover, consistent dark settings and a prominent gold Play button. Its window can be resized or maximized and adjusts when the display resolution changes. It blocks updates while the selected WoW client is running. The legacy `LaunchWoW.exe` is a separate executable kept at `LaunchWoW\Assets\LaunchWoW.exe`; the build above creates `AzerotBatzoki.exe` and does not replace it.

`Test-Installer.ps1` uses a simulated Docker command and does not install WSL or change firewall rules or scheduled tasks. The launcher self-test covers manifest safety and installer behavior; a full 17+ GB download, real game launch, and connection from another PC still need LAN verification.

## Collaboration

If the repository is private, the owner must invite collaborators through GitHub repository settings before they can download it. Review all changes and test the installer on a disposable or backed-up Windows installation before using it on a server with valuable data.

## Licensing

No license is granted here for third-party game clients or assets. This repository does not redistribute those files. Check the licenses and attribution notices for third-party components before modifying or redistributing them.

Update regression checks: run LaunchWoW\Tests\Test-Updates.ps1. It uses synthetic files and an HTTP listener bound to loopback, checks progress and concurrency, and verifies that corrupt downloads preserve the original. It does not download or modify real game data.
