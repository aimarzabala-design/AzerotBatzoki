# AzerothCore WotLK one-click installer for Windows 10 Pro (WSL2 + prebuilt Docker images)
# Windows PowerShell 5.1 compatible. Source is intentionally readable.
param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Root = Join-Path $env:SystemDrive 'WoWServer'
$Distro = 'WoWServer'
$LogDir = Join-Path $Root 'logs'
$LogFile = Join-Path $LogDir 'instalacion.log'
$SystemDir = [Environment]::SystemDirectory

if ($CheckOnly) {
    $tokens = $null; $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($PSCommandPath, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { Write-Error ($parseErrors | Out-String); exit 1 }
    Write-Host ('CHECK_OK SHA256=' + (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash)
    exit 0
}

function Protect-InstallerDirectory([string]$Path) {
    # Scripts in this directory are executed by an elevated task. Fail closed.
    $acl = New-Object System.Security.AccessControl.DirectorySecurity
    $admin = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')
    $system = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-18')
    $user = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($admin)
    foreach ($sid in @($admin, $system)) {
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    $readRule = New-Object System.Security.AccessControl.FileSystemAccessRule($user, 'ReadAndExecute', 'ContainerInherit, ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($readRule)
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Grant-InstallerDataAccess([string]$Path) {
    $acl = Get-Acl -LiteralPath $Path
    $user = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($user, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow')
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Protect-SecretFile([string]$Path) {
    $acl = New-Object System.Security.AccessControl.FileSecurity
    $admin = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner($admin)
    foreach ($id in @('S-1-5-32-544', 'S-1-5-18')) {
        $sid = New-Object System.Security.Principal.SecurityIdentifier($id)
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Write-Section([string]$Text) {
    Write-Host "`n============================================================" -ForegroundColor Cyan
    Write-Host " $Text" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
}
function Pause-And-Exit([int]$Code = 0) {
    try { Stop-Transcript | Out-Null } catch {}
    Write-Host "`nPulsa ENTER para cerrar esta ventana..." -ForegroundColor Yellow
    [void](Read-Host)
    exit $Code
}
function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Invoke-Wsl([string[]]$Arguments) {
    & "$env:WINDIR\System32\wsl.exe" @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Falló WSL (código $LASTEXITCODE): wsl.exe $($Arguments -join ' ')"
    }
}
function Get-RegisteredDistros {
    $out = & "$env:WINDIR\System32\wsl.exe" --list --quiet 2>$null
    if ($LASTEXITCODE -ne 0) { return @() }
    return @($out | ForEach-Object { (($_ -replace "`0", '').Trim()) } | Where-Object { $_ })
}
function Set-Wsl2Config {
    $cfgPath = Join-Path $env:USERPROFILE '.wslconfig'
    $content = ''
    if (Test-Path $cfgPath) {
        $content = Get-Content -LiteralPath $cfgPath -Raw
        Copy-Item -LiteralPath $cfgPath -Destination ($cfgPath + '.backup-wowserver-' + (Get-Date -Format 'yyyyMMddHHmmss')) -Force
    }
    $keys = [ordered]@{
        'memory' = '2GB'
        'processors' = '2'
        'swap' = '4GB'
        'localhostForwarding' = 'true'
    }
    if ($content -notmatch '(?im)^\s*\[wsl2\]\s*$') {
        if ($content -and -not $content.EndsWith("`n")) { $content += "`r`n" }
        $content += "`r`n[wsl2]`r`n"
    }
    $match = [regex]::Match($content, '(?im)^\s*\[wsl2\]\s*$')
    $start = $match.Index
    $afterHeader = $match.Index + $match.Length
    $next = [regex]::Match($content.Substring($afterHeader), '(?im)^\s*\[[^\]]+\]\s*$')
    if ($next.Success) {
        $end = $afterHeader + $next.Index
        $section = $content.Substring($start, $end - $start)
        $tail = $content.Substring($end)
    } else {
        $section = $content.Substring($start)
        $tail = ''
    }
    foreach ($key in $keys.Keys) {
        $line = "$key=$($keys[$key])"
        $pattern = '(?im)^\s*' + [regex]::Escape($key) + '\s*=.*$'
        if ([regex]::IsMatch($section, $pattern)) {
            $section = [regex]::Replace($section, $pattern, $line)
        } else {
            if (-not $section.EndsWith("`n")) { $section += "`r`n" }
            $section += "$line`r`n"
        }
    }
    $newContent = $content.Substring(0, $start) + $section + $tail
    Set-Content -LiteralPath $cfgPath -Value $newContent -Encoding UTF8
    Write-Host "Configuración WSL2 aplicada: memoria limitada a 2 GB y 4 GB de swap." -ForegroundColor Green
}

# Relaunch elevated when needed; the installer changes Windows optional features and firewall rules.
if (-not (Test-IsAdministrator)) {
    Write-Host 'Se necesitan permisos de administrador para activar WSL2 y configurar el firewall.' -ForegroundColor Yellow
    $quotedScript = '"' + $PSCommandPath + '"'
    $args = "-NoProfile -ExecutionPolicy Bypass -File $quotedScript"
    try {
        $elevated = Start-Process -FilePath "$PSHOME\powershell.exe" -Verb RunAs -ArgumentList $args -Wait -PassThru
        exit $elevated.ExitCode
    } catch {
        Write-Host "No se pudo solicitar elevación: $($_.Exception.Message)" -ForegroundColor Red
        Pause-And-Exit 1
    }
    exit 0
}

New-Item -ItemType Directory -Path $Root -Force | Out-Null
foreach ($relative in @('', 'WSL', 'logs', 'network-logs', 'AutoStart_WoW.ps1', 'Network_WoW.ps1', 'bootstrap.sh', 'db-password.txt')) {
    $path = if ($relative) { Join-Path $Root $relative } else { $Root }
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "No se permite instalar sobre un enlace o junction: $path"
    }
}
Protect-InstallerDirectory $Root
New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
Grant-InstallerDataAccess $LogDir
New-Item -ItemType Directory -Path (Join-Path $Root 'network-logs') -Force | Out-Null
Protect-InstallerDirectory (Join-Path $Root 'network-logs')
try { Start-Transcript -Path $LogFile -Append | Out-Null } catch {}

try {
    Write-Section 'INSTALADOR AUTOMÁTICO DE AZEROTHCORE - WOTLK 3.3.5a'
    Write-Host "Destino: $Root"
    Write-Host 'Este asistente usa Ubuntu en WSL2 y las imágenes precompiladas del proyecto.'
    Write-Host 'No instala ni distribuye el cliente comercial de World of Warcraft.'

    $os = Get-CimInstance Win32_OperatingSystem
    $build = [int]$os.BuildNumber
    if ($build -lt 19041) {
        throw "Tu Windows 10 es demasiado antiguo (compilación $build). Actualiza Windows 10 a la compilación 19041 o posterior y vuelve a ejecutar el instalador."
    }
    if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
        throw 'Se necesita Windows x64 (AMD64); este paquete no admite ARM64.'
    }
    $cs = Get-CimInstance Win32_ComputerSystem
    $ramGB = [math]::Round(($cs.TotalPhysicalMemory / 1GB), 1)
    $driveName = $env:SystemDrive.TrimEnd(':')
    $freeGB = [math]::Round(((Get-PSDrive -Name $driveName).Free / 1GB), 1)
    Write-Host "Windows build: $build | RAM detectada: $ramGB GB | Espacio libre en disco: $freeGB GB"
    if ($ramGB -lt 4) {
        Write-Host 'AVISO: con menos de 4 GB de RAM el servidor puede fallar por falta de memoria. El servidor será para pruebas o muy pocos jugadores y puede ir lento.' -ForegroundColor Yellow
    }
    if ($freeGB -lt 25) {
        throw "Hay solo $freeGB GB libres. Libera al menos 25 GB antes de continuar."
    }

    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    if ($null -ne $cpu.VirtualizationFirmwareEnabled -and -not $cpu.VirtualizationFirmwareEnabled) {
        Write-Host 'Windows no detecta la virtualización de CPU activada. WSL2 requiere Intel VT-x/AMD-V habilitado en BIOS/UEFI.' -ForegroundColor Yellow
        $go = Read-Host '¿Continuar intentando activar los componentes de Windows? (S/N)'
        if ($go -notmatch '^(S|SI|Y|YES)$') { Pause-And-Exit 1 }
    }

    Write-Section '1/4 - ACTIVAR COMPONENTES DE WINDOWS'
    $wslFeature = Get-WindowsOptionalFeature -Online -FeatureName Microsoft-Windows-Subsystem-Linux
    $vmFeature = Get-WindowsOptionalFeature -Online -FeatureName VirtualMachinePlatform
    $needsReboot = $false
    if ($wslFeature.State -ne 'Enabled') {
        Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Windows-Subsystem-Linux -All -NoRestart | Out-Null
        $needsReboot = $true
    }
    if ($vmFeature.State -ne 'Enabled') {
        Enable-WindowsOptionalFeature -Online -FeatureName VirtualMachinePlatform -All -NoRestart | Out-Null
        $needsReboot = $true
    }
    if ($needsReboot) {
        Write-Host 'Windows ha activado los componentes de WSL2. Hace falta reiniciar antes de seguir.' -ForegroundColor Yellow
        $restart = Read-Host 'Escribe R para reiniciar ahora (guarda primero todo tu trabajo) o cualquier otra tecla para salir'
        if ($restart -match '^(R|REINICIAR)$') { Restart-Computer -Force }
        Write-Host 'Cuando Windows vuelva a arrancar, ejecuta de nuevo Instalar_WoW_Server.exe.' -ForegroundColor Cyan
        Pause-And-Exit 0
    }

    # Update WSL kernel if the newer WSL package is available. Fall back to Microsoft's legacy kernel MSI.
    Write-Section '2/4 - PREPARAR WSL2 Y UBUNTU'
    $wslUpdateOk = $false
    try {
        & "$env:WINDIR\System32\wsl.exe" --update 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -eq 0) { $wslUpdateOk = $true }
    } catch { $wslUpdateOk = $false }
    if (-not $wslUpdateOk) {
        Write-Host 'No se pudo usar wsl --update; se intentará el instalador oficial del kernel de Microsoft.' -ForegroundColor Yellow
        $kernelMsi = Join-Path $Root 'wsl_update_x64.msi'
        if (-not (Test-Path $kernelMsi)) {
            Invoke-WebRequest -Uri 'https://wslstorestorage.blob.core.windows.net/wslblob/wsl_update_x64.msi' -OutFile $kernelMsi -UseBasicParsing
        }
        $signature = Get-AuthenticodeSignature -LiteralPath $kernelMsi
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') {
            throw 'El instalador del kernel WSL no tiene una firma válida de Microsoft. Elimina el MSI descargado y vuelve a intentarlo.'
        }
        $msi = Start-Process -FilePath 'msiexec.exe' -ArgumentList "/i `"$kernelMsi`" /qn /norestart" -Wait -PassThru
        if ($msi.ExitCode -notin @(0, 3010)) { throw "No se pudo instalar el kernel WSL2 (código $($msi.ExitCode))." }
        if ($msi.ExitCode -eq 3010) {
            Write-Host 'El kernel WSL necesita reiniciar Windows. Guarda tu trabajo, reinicia y vuelve a ejecutar el instalador.' -ForegroundColor Yellow
            Pause-And-Exit 0
        }
    }
    & "$env:WINDIR\System32\wsl.exe" --set-default-version 2 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw 'WSL2 no ha quedado preparado. Comprueba que la virtualización esté activada y vuelve a ejecutar el instalador.' }

    Set-Wsl2Config
    $ubuntuFile = 'ubuntu-jammy-wsl-amd64-ubuntu22.04lts.rootfs.tar.gz'
    $ubuntuUrl = "https://cloud-images.ubuntu.com/wsl/jammy/current/$ubuntuFile"
    $sumUrl = 'https://cloud-images.ubuntu.com/wsl/jammy/current/SHA256SUMS'
    $ubuntuArchive = Join-Path $Root $ubuntuFile
    if (-not (Test-Path $ubuntuArchive)) {
        Write-Host 'Descargando Ubuntu 22.04 LTS oficial para WSL2 (unos 325 MB)...'
        Invoke-WebRequest -Uri $ubuntuUrl -OutFile $ubuntuArchive -UseBasicParsing
    }
    Write-Host 'Verificando SHA-256 de la imagen de Ubuntu...'
    # Descargar la lista como archivo evita problemas de codificación/parseo de .Content en Windows PowerShell 5.1.
    $sumFile = Join-Path $Root 'ubuntu-SHA256SUMS'
    try {
        if (Test-Path -LiteralPath $sumFile) { Remove-Item -LiteralPath $sumFile -Force }
        Invoke-WebRequest -Uri $sumUrl -OutFile $sumFile -UseBasicParsing
        if (-not (Test-Path -LiteralPath $sumFile) -or (Get-Item -LiteralPath $sumFile).Length -lt 64) {
            throw 'El archivo SHA256SUMS descargado está vacío o incompleto.'
        }
        $sumLines = Get-Content -LiteralPath $sumFile -Encoding ASCII
    } catch {
        throw "No se pudo descargar o leer la lista SHA-256 oficial de Ubuntu ($sumUrl): $($_.Exception.Message). No se omitirá la verificación."
    }
    $expectedHash = $null
    foreach ($line in $sumLines) {
        # Formatos admitidos por sha256sum: HASH *filename o HASH  filename.
        $sumMatch = [regex]::Match([string]$line, '^\s*([A-Fa-f0-9]{64})\s+\*?(.+?)\s*$')
        if ($sumMatch.Success -and $sumMatch.Groups[2].Value.Trim() -eq $ubuntuFile) {
            $expectedHash = $sumMatch.Groups[1].Value.ToLowerInvariant()
            break
        }
    }
    if (-not $expectedHash) {
        throw "La lista SHA256SUMS oficial se descargó, pero no contiene una entrada válida para '$ubuntuFile'. Comprueba la conexión y vuelve a ejecutar el instalador. No se omitirá la verificación."
    }
    Write-Host "Suma SHA-256 oficial encontrada: $expectedHash" -ForegroundColor DarkGray
    $actualHash = (Get-FileHash -LiteralPath $ubuntuArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expectedHash -ne $actualHash) { Remove-Item -LiteralPath $ubuntuArchive -Force; throw 'La verificación SHA-256 de Ubuntu falló. Se eliminó la descarga; vuelve a intentarlo.' }

    $registered = Get-RegisteredDistros
    $distroPath = Join-Path $Root 'WSL'
    New-Item -ItemType Directory -Path $distroPath -Force | Out-Null
    Grant-InstallerDataAccess $distroPath
    if ($registered -notcontains $Distro) {
        Write-Host 'Registrando Ubuntu como distribución WSL privada del servidor...'
        Invoke-Wsl @('--import', $Distro, $distroPath, $ubuntuArchive, '--version', '2')
    } else {
        Write-Host 'La distribución WoWServer ya está instalada; se conservarán sus datos.' -ForegroundColor Green
    }
    # Keep the database password stable across installer re-runs.
    $passwordFile = Join-Path $Root 'db-password.txt'
    $dbPassword = ''
    if (Test-Path -LiteralPath $passwordFile) { $dbPassword = (Get-Content -LiteralPath $passwordFile -Raw).Trim() }
    if ($dbPassword -notmatch '^[a-f0-9]{32}$') {
        if ($registered -contains $Distro) {
            throw 'Falta una contraseña válida en db-password.txt para una distribución existente. Recupera el archivo original; no se cambiará la contraseña de tus bases de datos.'
        }
        $dbPassword = [guid]::NewGuid().ToString('N')
        [System.IO.File]::WriteAllText($passwordFile, $dbPassword, [System.Text.Encoding]::ASCII)
    }
    # Protect local credentials from ordinary users.
    Protect-SecretFile $passwordFile

    # Prepare a repeatable Linux-side bootstrap script. Password is hex only, so safe to embed.
    $bootstrap = @'
#!/bin/bash
set -Eeuo pipefail
umask 077
exec > >(tee -a /var/log/wowserver-install.log) 2>&1
export DEBIAN_FRONTEND=noninteractive
DB_PASSWORD='__DB_PASSWORD__'
ROOT=/opt/azerothcore

cat >/etc/wsl.conf <<'EOF'
[boot]
systemd=false
[user]
default=root
EOF

apt-get update
apt-get install -y ca-certificates curl gnupg git iproute2
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
. /etc/os-release
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" >/etc/apt/sources.list.d/docker.list
apt-get update
apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
mkdir -p /etc/docker
cat >/etc/docker/daemon.json <<'EOF'
{
  "log-driver": "json-file",
  "log-opts": {"max-size": "10m", "max-file": "3"}
}
EOF

if [ ! -d "$ROOT/.git" ]; then
  mkdir -p "$(dirname "$ROOT")"
  git clone --depth 1 https://github.com/azerothcore/acore-docker.git "$ROOT"
fi
cd "$ROOT"
mkdir -p scripts/lua backups
chmod 755 scripts scripts/lua
chmod 700 backups
if [ ! -f .env ] || ! grep -q '^DOCKER_DB_ROOT_PASSWORD=' .env; then
  cat >.env <<EOF
DOCKER_DB_ROOT_PASSWORD=${DB_PASSWORD}
DOCKER_IMAGE_TAG=master
DOCKER_WORLD_EXTERNAL_PORT=8085
DOCKER_DB_EXTERNAL_PORT=63306
DOCKER_SOAP_EXTERNAL_PORT=7878
EOF
fi
chmod 600 .env

# Limit database memory for this low-RAM machine.
cat >docker-compose.override.yml <<'EOF'
services:
  ac-database:
    command:
      - --innodb-buffer-pool-size=128M
      - --max-connections=32
  # TypeScript scripting is optional and disabled on this low-memory host.
  # Lua scripting remains available through the scripts/lua bind mount.
  ac-eluna-ts-dev:
    command: ["sh", "-c", "sleep infinity"]
    healthcheck:
      disable: true
EOF

# Keep the SQL database and SOAP management interface bound to Windows localhost only.
if [ -f docker-compose.yml ]; then
  sed -i 's#- ${DOCKER_DB_EXTERNAL_PORT:-63306}:3306#- "127.0.0.1:${DOCKER_DB_EXTERNAL_PORT:-63306}:3306"#' docker-compose.yml
  sed -i 's#- ${DOCKER_SOAP_EXTERNAL_PORT:-7878}:7878#- "127.0.0.1:${DOCKER_SOAP_EXTERNAL_PORT:-7878}:7878"#' docker-compose.yml
fi

cat >/usr/local/bin/wowctl-register.py <<'PY'
import hashlib
import json
import re
import secrets
import sys

request = json.load(sys.stdin)
username = request.get("username", "")
password = request.get("password", "")
if not re.fullmatch(r"[A-Za-z0-9_]{3,16}", username):
    raise SystemExit("Usuario no válido.")
if not re.fullmatch(r"[A-Za-z0-9!#$%&()*+,\-./:;=?@^_~]{8,16}", password):
    raise SystemExit("Contraseña no válida.")
username = username.upper()
password = password.upper()
salt = secrets.token_bytes(32)
inner = hashlib.sha1((username + ":" + password).encode("ascii")).digest()
x = hashlib.sha1(salt + inner).digest()
modulus = int("894B645E89E1535BBDAD5B8B290650530801B18EBFBF5E8FAB3C82872A3E9BB7", 16)
verifier = pow(7, int.from_bytes(x, "big"), modulus).to_bytes(32, "little")
salt_hex = salt.hex()
verifier_hex = verifier.hex()
print(
    "START TRANSACTION;"
    "INSERT INTO account(username,salt,verifier,expansion,reg_mail,email,joindate) "
    f"VALUES('{username}',UNHEX('{salt_hex}'),UNHEX('{verifier_hex}'),2,'','',NOW());"
    "SET @launcher_account_id=LAST_INSERT_ID();"
    "INSERT INTO realmcharacters(realmid,acctid,numchars) "
    "SELECT id,@launcher_account_id,0 FROM realmlist;"
    "COMMIT;"
)
PY
chmod 755 /usr/local/bin/wowctl-register.py
cat >/usr/local/bin/wowctl <<'EOF'
#!/bin/bash
set -Eeuo pipefail
ROOT=/opt/azerothcore
ensure_docker() {
  if docker info >/dev/null 2>&1; then return 0; fi
  if ! pgrep -x dockerd >/dev/null 2>&1; then
    nohup dockerd >/var/log/dockerd.log 2>&1 </dev/null &
  fi
  for i in $(seq 1 90); do
    if docker info >/dev/null 2>&1; then return 0; fi
    sleep 2
  done
  echo 'Docker no ha arrancado. Últimas líneas de /var/log/dockerd.log:' >&2
  tail -n 80 /var/log/dockerd.log >&2 || true
  return 1
}
cd "$ROOT"
case "${1:-status}" in
  start)
    ensure_docker
    docker compose up -d
    echo
    docker compose ps
    ;;
  stop)
    ensure_docker
    docker compose stop
    ;;
  restart)
    ensure_docker
    docker compose restart
    ;;
  status)
    ensure_docker
    docker compose ps
    ;;
  logs)
    ensure_docker
    docker compose logs --tail=100 -f
    ;;
  account)
    ensure_docker
    echo 'Dentro de la consola del servidor escribe: account create USUARIO CONTRASEÑA CONTRASEÑA'
    echo 'Para salir sin apagarlo usa Ctrl+P y después Ctrl+Q; no pulses Ctrl+C.'
    docker attach acore-docker-ac-worldserver-1
    ;;
  register)
    ensure_docker
    request="$(cat)"
    sql="$(printf '%s' "$request" | python3 /usr/local/bin/wowctl-register.py)" || {
      echo 'Solicitud de registro no válida.' >&2
      exit 2
    }
    password="$(sed -n 's/^DOCKER_DB_ROOT_PASSWORD=//p' .env | head -n1)"
    if [ -z "$password" ]; then echo 'No se encontró la contraseña de MySQL en .env' >&2; exit 2; fi
    ready=0
    for i in $(seq 1 30); do
      if printf '%s\n' 'SELECT 1;' | docker compose exec -T -e MYSQL_PWD="$password" ac-database mysql -uroot -D acore_auth >/dev/null 2>&1; then ready=1; break; fi
      sleep 2
    done
    if [ "$ready" -ne 1 ]; then echo 'La base de datos de cuentas no está disponible.' >&2; exit 3; fi
    printf '%s\n' "$sql" | docker compose exec -T -e MYSQL_PWD="$password" ac-database mysql -uroot -D acore_auth
    echo 'Cuenta creada correctamente.'
    ;;
  set-ip)
    ensure_docker
    ipaddr="${2:-}"
    if [[ ! "$ipaddr" =~ ^[A-Za-z0-9.-]+$ ]]; then
      echo 'Uso: wowctl set-ip DIRECCION_IP_O_DNS' >&2; exit 2
    fi
    password=$(sed -n 's/^DOCKER_DB_ROOT_PASSWORD=//p' .env | head -n1)
    if [ -z "$password" ]; then echo 'No se encontró la contraseña de MySQL en .env' >&2; exit 2; fi
    echo 'Esperando a que se importe la base de datos (puede tardar varios minutos)...'
    ok=0
    for i in $(seq 1 120); do
      if docker compose exec -T ac-database mysql -uroot -p"$password" -D acore_auth -e "SELECT 1 FROM realmlist LIMIT 1;" >/dev/null 2>&1; then ok=1; break; fi
      sleep 5
    done
    if [ "$ok" -ne 1 ]; then echo 'La base de datos aún no está lista. Espera y vuelve a ejecutar Configurar_IP_WoW.bat.' >&2; exit 3; fi
    docker compose exec -T ac-database mysql -uroot -p"$password" -D acore_auth -e "UPDATE realmlist SET address='$ipaddr' WHERE id=1;"
    echo "Dirección del reino actualizada a: $ipaddr"
    ;;
  backup)
    ensure_docker
    mkdir -p "$ROOT/backups"
    password=$(sed -n 's/^DOCKER_DB_ROOT_PASSWORD=//p' .env | head -n1)
    stamp=$(date +%Y%m%d-%H%M%S)
    umask 077
    backup="$ROOT/backups/mysql-$stamp-$$.sql"
    trap 'rm -f -- "$backup.partial"' EXIT
    docker compose exec -T ac-database mysqldump -uroot -p"$password" --all-databases --single-transaction --no-tablespaces >"$backup.partial"
    mv -- "$backup.partial" "$backup"
    echo "Copia creada en $backup"
    ;;
  *) echo 'Comando: start|stop|restart|status|logs|account|register|set-ip|backup'; exit 2 ;;
esac
EOF
chmod 755 /usr/local/bin/wowctl

# Start Docker for this install session, download official prebuilt images, and launch services.
if ! docker info >/dev/null 2>&1; then nohup dockerd >/var/log/dockerd.log 2>&1 </dev/null & fi
for i in $(seq 1 90); do docker info >/dev/null 2>&1 && break; sleep 2; done
docker info >/dev/null
docker compose pull
docker compose up -d

echo 'Instalación base terminada. La primera inicialización de datos y SQL seguirá en segundo plano.'
'@
    $bootstrap = $bootstrap.Replace('__DB_PASSWORD__', $dbPassword)
    $bootstrapPath = Join-Path $Root 'bootstrap.sh'
    $bootstrap = $bootstrap -replace "`r`n", "`n"
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($bootstrapPath, $bootstrap, $utf8NoBom)
    Protect-SecretFile $bootstrapPath

    Write-Section '3/4 - INSTALAR DOCKER Y EL SERVIDOR'
    # Linux can read the bootstrap file through the standard C: mount.
    $linuxBootstrapPath = '/mnt/' + $env:SystemDrive.Substring(0,1).ToLowerInvariant() + '/WoWServer/bootstrap.sh'
    try {
        Invoke-Wsl @('-d', $Distro, '-u', 'root', '--exec', 'bash', $linuxBootstrapPath)
    } finally {
        Remove-Item -LiteralPath $bootstrapPath -Force -ErrorAction SilentlyContinue
    }
    if ($LASTEXITCODE -ne 0) { throw 'El proceso de instalación dentro de Ubuntu no terminó correctamente.' }

    # Create Windows launchers and a quiet logon task that refreshes WSL2 port forwarding.
    Write-Section '4/4 - CREAR ACCESOS DIRECTOS Y REGLAS DE RED'
    $startBat = @'
@echo off
chcp 65001 >nul
setlocal
REM Ejecuta la tarea elevada para refrescar el reenvio WSL2 de la red local.
schtasks /Run /TN "AzerothCore-WoW-AutoStart" >nul 2>&1
if errorlevel 1 (
  "%WINDIR%\System32\wsl.exe" -d WoWServer -u root --exec /usr/local/bin/wowctl start
) else (
  timeout /t 10 /nobreak >nul
  "%WINDIR%\System32\wsl.exe" -d WoWServer -u root --exec /usr/local/bin/wowctl status
)
if errorlevel 1 echo Si falla, revisa C:\WoWServer\logs y ejecuta Estado_WoW_Server.bat.
echo.
echo Servidor iniciado o iniciando. La primera vez puede tardar bastante.
pause
'@
    Set-Content -LiteralPath (Join-Path $Root 'Iniciar_WoW_Server.bat') -Value $startBat -Encoding ASCII
    $stopBat = @'
@echo off
chcp 65001 >nul
"%WINDIR%\System32\wsl.exe" -d WoWServer -u root --exec /usr/local/bin/wowctl stop
pause
'@
    Set-Content -LiteralPath (Join-Path $Root 'Parar_WoW_Server.bat') -Value $stopBat -Encoding ASCII
    $statusBat = @'
@echo off
chcp 65001 >nul
"%WINDIR%\System32\wsl.exe" -d WoWServer -u root --exec /usr/local/bin/wowctl status
echo.
pause
'@
    Set-Content -LiteralPath (Join-Path $Root 'Estado_WoW_Server.bat') -Value $statusBat -Encoding ASCII
    $logsBat = @'
@echo off
chcp 65001 >nul
"%WINDIR%\System32\wsl.exe" -d WoWServer -u root --exec /usr/local/bin/wowctl logs
pause
'@
    Set-Content -LiteralPath (Join-Path $Root 'Ver_Logs_WoW.bat') -Value $logsBat -Encoding ASCII
    $accountBat = @'
@echo off
chcp 65001 >nul
echo Cuando aparezca la consola de worldserver, escribe:
echo account create USUARIO CONTRASENA CONTRASENA
echo Sustituye USUARIO y CONTRASENA por los que quieras usar.
echo Para salir sin apagar el servidor pulsa Ctrl+P y luego Ctrl+Q. No pulses Ctrl+C.
echo.
pause
"%WINDIR%\System32\wsl.exe" -d WoWServer -u root --exec /usr/local/bin/wowctl account
pause
'@
    Set-Content -LiteralPath (Join-Path $Root 'Crear_Cuenta_WoW.bat') -Value $accountBat -Encoding ASCII
    $ipBat = @'
@echo off
chcp 65001 >nul
set /p WOWIP=Direccion del reino (127.0.0.1 solo en este PC; IP local para LAN; IP publica o DNS para Internet):
if "%WOWIP%"=="" goto end
"%WINDIR%\System32\wsl.exe" -d WoWServer -u root --exec /usr/local/bin/wowctl set-ip "%WOWIP%"
pause
:end
'@
    Set-Content -LiteralPath (Join-Path $Root 'Configurar_IP_WoW.bat') -Value $ipBat -Encoding ASCII
    $backupBat = @'
@echo off
chcp 65001 >nul
"%WINDIR%\System32\wsl.exe" -d WoWServer -u root --exec /usr/local/bin/wowctl backup
pause
'@
    Set-Content -LiteralPath (Join-Path $Root 'Crear_Copia_Seguridad.bat') -Value $backupBat -Encoding ASCII

    $autoScript = @'
$ErrorActionPreference = 'Stop'
$root = 'C:\WoWServer'
$log = Join-Path $root 'logs\inicio-automatico.log'
New-Item -ItemType Directory -Path (Split-Path $log) -Force | Out-Null
try {
    "$(Get-Date -Format s) iniciando WSL2/Docker" | Out-File -FilePath $log -Append -Encoding utf8
    & "$env:WINDIR\System32\wsl.exe" -d WoWServer -u root --exec bash -c '/usr/local/bin/wowctl start 2>&1' | Out-File -FilePath $log -Append -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw "No se pudo iniciar el servidor (WSL: $LASTEXITCODE)." }
    Start-Sleep -Seconds 5
    $ipText = & "$env:WINDIR\System32\wsl.exe" -d WoWServer -u root --exec bash -c 'hostname -I 2>/dev/null'
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo consultar la IP de WSL.' }
    $wslIp = (($ipText -join ' ') -split '\s+' | Where-Object { $_ -match '^\d+\.\d+\.\d+\.\d+$' } | Select-Object -First 1)
    if ($wslIp) {
        Set-Content -LiteralPath (Join-Path $root 'logs\wsl-ip.txt') -Value $wslIp -Encoding ASCII
        & "$env:WINDIR\System32\schtasks.exe" /Run /TN 'AzerothCore-WoW-Network' | Out-File -FilePath $log -Append -Encoding utf8
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo solicitar la configuracion de red LAN.' }
    } else { throw 'No se encontro una direccion IPv4 de WSL.' }
} catch {
    "$(Get-Date -Format s) error: $($_.Exception.Message)" | Out-File -FilePath $log -Append -Encoding utf8
    exit 1
}
'@
    $autoScript = $autoScript.Replace('C:\WoWServer', $Root)
    Set-Content -LiteralPath (Join-Path $Root 'AutoStart_WoW.ps1') -Value $autoScript -Encoding UTF8

    # The elevated task only configures fixed ports. Never execute user-writable WSL code elevated at logon.
    $networkScript = @'
$env:PSModulePath = $PSHOME + '\Modules'
$ErrorActionPreference = 'Stop'
$root = 'C:\WoWServer'
$log = Join-Path $root 'network-logs\red-lan.log'
$netsh = Join-Path ([Environment]::SystemDirectory) 'netsh.exe'
try {
    $address = (Get-Content -LiteralPath (Join-Path $root 'logs\wsl-ip.txt') -Raw).Trim()
    $parsed = $null
    if ($address -notmatch '^\d{1,3}(\.\d{1,3}){3}$' -or -not [Net.IPAddress]::TryParse($address, [ref]$parsed) -or $parsed.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) {
        throw 'La direccion WSL no es una IPv4 valida.'
    }
    foreach ($port in @(3724,8085)) {
        & $netsh interface portproxy delete v4tov4 "listenaddress=0.0.0.0" "listenport=$port" *> $null
        & $netsh interface portproxy add v4tov4 "listenaddress=0.0.0.0" "listenport=$port" "connectaddress=$address" "connectport=$port" | Out-File -FilePath $log -Append -Encoding utf8
        if ($LASTEXITCODE -ne 0) { throw "No se pudo reenviar el puerto $port." }
    }
    "$(Get-Date -Format s) red LAN configurada: $address" | Out-File -FilePath $log -Append -Encoding utf8
} catch {
    "$(Get-Date -Format s) error: $($_.Exception.Message)" | Out-File -FilePath $log -Append -Encoding utf8
    exit 1
}
'@
    $networkScript = $networkScript.Replace('C:\WoWServer', $Root)
    Set-Content -LiteralPath (Join-Path $Root 'Network_WoW.ps1') -Value $networkScript -Encoding UTF8

    # Restrict inbound game traffic to the local subnet and private Windows profile.
    try {
        if (-not (Get-NetFirewallRule -DisplayName 'AzerothCore WoW LAN' -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName 'AzerothCore WoW LAN' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 3724,8085 -Profile Private -RemoteAddress LocalSubnet | Out-Null
        }
        if (-not (Get-NetFirewallRule -DisplayName 'AzerothCore Accounts LAN' -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName 'AzerothCore Accounts LAN' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8787 -Profile Private -RemoteAddress LocalSubnet | Out-Null
        }
    } catch { Write-Host 'No se pudieron crear las reglas de firewall automáticamente; el servidor seguirá siendo accesible localmente.' -ForegroundColor Yellow }
    try { Set-Service -Name iphlpsvc -StartupType Automatic -ErrorAction SilentlyContinue } catch {}
    try { Start-Service -Name iphlpsvc -ErrorAction SilentlyContinue } catch {}
    try {
        $taskName = 'AzerothCore-WoW-AutoStart'
        $taskAction = New-ScheduledTaskAction -Execute "$SystemDir\WindowsPowerShell\v1.0\powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$Root\AutoStart_WoW.ps1`""
        $taskTrigger = New-ScheduledTaskTrigger -AtLogOn -User (whoami)
        $networkAction = New-ScheduledTaskAction -Execute "$SystemDir\WindowsPowerShell\v1.0\powershell.exe" -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$Root\Network_WoW.ps1`""
        $networkPrincipal = New-ScheduledTaskPrincipal -UserId (whoami) -LogonType Interactive -RunLevel Highest
        Register-ScheduledTask -TaskName 'AzerothCore-WoW-Network' -Action $networkAction -Principal $networkPrincipal -Force | Out-Null
        $taskPrincipal = New-ScheduledTaskPrincipal -UserId (whoami) -LogonType Interactive -RunLevel Limited
        Register-ScheduledTask -TaskName $taskName -Action $taskAction -Trigger $taskTrigger -Principal $taskPrincipal -Force | Out-Null
    } catch {
        Write-Host 'No se pudo registrar el autoarranque. Puedes iniciar el servidor con Iniciar_WoW_Server.bat.' -ForegroundColor Yellow
    }
    try {
        & "$PSHOME\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root 'AutoStart_WoW.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'La preparacion inicial de red fallo; consulta inicio-automatico.log.' }
    } catch { Write-Host 'El autoarranque inicial no pudo preparar la red LAN; el acceso local sigue disponible.' -ForegroundColor Yellow }

    $readme = @'
AZEROTHCORE WOTLK 3.3.5a - INSTALACION AUTOMATICA
================================================

CARPETA: C:\WoWServer

IMPORTANTE SOBRE TU EQUIPO
- Con 4 GB de RAM, úsalo para pruebas o muy pocos jugadores. Puede ir lento.
- Se limita WSL2 a 2 GB de RAM y 4 GB de swap para dejar recursos a Windows. La tarea opcional TypeScript queda desactivada; Lua sigue disponible.
- La primera descarga usa varios GB y la importación de datos puede tardar bastante.
- Este paquete NO incluye ni descarga el cliente comercial de World of Warcraft.

USO BASICO
1. Abre C:\WoWServer\Estado_WoW_Server.bat para ver el estado.
2. Abre C:\WoWServer\Ver_Logs_WoW.bat para ver registros.
3. Crea una cuenta ejecutando C:\WoWServer\Crear_Cuenta_WoW.bat.
   Cuando aparezca la consola escribe: account create USUARIO CONTRASENA CONTRASENA
   Para desconectarte usa Ctrl+P y luego Ctrl+Q. No uses Ctrl+C.
4. Si el servidor ya está listo, ajusta la dirección del reino con Configurar_IP_WoW.bat:
   - 127.0.0.1: solo jugar desde el mismo servidor.
   - IP local, por ejemplo 192.168.1.50: PCs de tu red local.
   - IP pública o DNS: jugadores desde Internet (además debes configurar el router).
5. Para detenerlo, usa Parar_WoW_Server.bat. Para iniciarlo, Iniciar_WoW_Server.bat.
6. Crea copias de la base de datos con Crear_Copia_Seguridad.bat.

CLIENTE DEL JUEGO
- Usa una copia legítima compatible con WoW 3.3.5a (build 12340).
- Configura su realmlist para apuntar a la dirección que hayas establecido.
- No publiques ni distribuyas archivos del cliente que no tengas derecho a redistribuir.

ACCESO DESDE INTERNET
- El instalador solo abre el firewall de Windows para la red local privada.
- No configura el router. Para jugar desde fuera de tu casa necesitarás redirección TCP
  de los puertos 3724 y 8085 en el router y una dirección IP pública/DNS compatible.
- No expongas los puertos de MySQL (63306) ni SOAP (7878); se quedan en localhost.

GESTION
- C:\WoWServer\Iniciar_WoW_Server.bat
- C:\WoWServer\Parar_WoW_Server.bat
- C:\WoWServer\Estado_WoW_Server.bat
- C:\WoWServer\Ver_Logs_WoW.bat
- C:\WoWServer\Crear_Cuenta_WoW.bat
- C:\WoWServer\Configurar_IP_WoW.bat
- C:\WoWServer\Crear_Copia_Seguridad.bat

DIAGNOSTICO
- Instalación Windows: C:\WoWServer\logs\instalacion.log
- Inicio automático: C:\WoWServer\logs\inicio-automatico.log
- Docker dentro de WSL: /var/log/dockerd.log
- Comandos WSL: wsl -d WoWServer -u root --exec /usr/local/bin/wowctl status

El servidor se crea con AzerothCore y las imágenes precompiladas del proyecto acore-docker.
Consulta sus licencias/documentación antes de redistribuir o publicar un servidor.
'@
    Set-Content -LiteralPath (Join-Path $Root 'LEEME.txt') -Value $readme -Encoding UTF8

    # Mark completion, remove the large tarball only after successful import/bootstrap.
    try { Remove-Item -LiteralPath $ubuntuArchive -Force -ErrorAction SilentlyContinue } catch {}
    Write-Host "`nINSTALACIÓN BASE COMPLETADA." -ForegroundColor Green
    Write-Host "Los contenedores ya están lanzados, pero la primera importación puede continuar en segundo plano." -ForegroundColor Yellow
    Write-Host "Siguiente paso: abrir C:\WoWServer\Estado_WoW_Server.bat y esperar a que worldserver esté activo."
    Write-Host "Después usa C:\WoWServer\Crear_Cuenta_WoW.bat y, si hace falta, Configurar_IP_WoW.bat."
    Write-Host "Guía: C:\WoWServer\LEEME.txt"
    Pause-And-Exit 0
} catch {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Consulta el registro: $LogFile" -ForegroundColor Yellow
    Pause-And-Exit 1
}
