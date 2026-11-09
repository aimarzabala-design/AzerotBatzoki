param([string]$InstallerPath = (Join-Path $PSScriptRoot 'installer.ps1'))
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($InstallerPath, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
$installerText = Get-Content -LiteralPath $InstallerPath -Raw
if ($installerText -notmatch "New-NetFirewallRule\s+-DisplayName 'AzerothCore Accounts LAN'.*?-LocalPort 8787.*?-Profile Any.*?-RemoteAddress LocalSubnet" -or
    $installerText -notmatch "New-NetFirewallRule\s+-DisplayName 'AzerothCore WoW LAN'.*?-LocalPort 3724,8085.*?-Profile Any.*?-RemoteAddress LocalSubnet" -or
    $installerText -notmatch "Set-NetFirewallRule\s+-InputObject .*?-Profile Any") {
    throw 'Las reglas del juego y las cuentas deben cubrir cualquier perfil, limitadas a la subred local'
}
# Load only the ACL builders and capture their result in memory; no system ACL is changed.
function Set-Acl { param($LiteralPath, $AclObject) $script:CapturedAcl = $AclObject }
foreach ($name in @('Protect-InstallerDirectory', 'Protect-SecretFile')) {
    $function = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)
    if ($function) { . ([scriptblock]::Create($function.Extent.Text)) }
}
if (Get-Command Protect-InstallerDirectory -ErrorAction SilentlyContinue) {
    Protect-InstallerDirectory 'unused-test-path'
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $rules = $script:CapturedAcl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier])
    $userRule = $rules | Where-Object { $_.IdentityReference.Value -eq $currentSid }
    if (-not $script:CapturedAcl.AreAccessRulesProtected -or $userRule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Write) { throw 'La carpeta de scripts permite escritura sin elevar' }
    Protect-SecretFile 'unused-test-path'
    $rules = $script:CapturedAcl.GetAccessRules($true, $false, [Security.Principal.SecurityIdentifier])
    if ($rules.Count -ne 2 -or ($rules | Where-Object { $_.IdentityReference.Value -notin @('S-1-5-18', 'S-1-5-32-544') })) { throw 'Credenciales accesibles fuera de Administradores/SYSTEM' }
    Write-Host 'PASS: ACL protegidas y SID independientes del idioma (en memoria)'
}
foreach ($name in @('$autoScript', '$networkScript')) {
    $node = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text -eq $name -and $n.Right.Extent.Text.StartsWith("@'") }, $true)
    if ($node) {
        $t = $null; $e = $null
        $generated = [System.Management.Automation.Language.Parser]::ParseInput($node.Right.Expression.Value, [ref]$t, [ref]$e)
        if ($e.Count) { throw ($e | Out-String) }
        if ($name -eq '$networkScript') {
            $condition = $generated.Find({param($n) $n -is [System.Management.Automation.Language.IfStatementAst] -and $n.Extent.Text.Contains('TryParse')}, $true).Clauses[0].Item1.Extent.Text
            foreach ($case in @(@('172.20.10.2', $false), @('999.2.3.4', $true), @('1.2.3.4;whoami', $true), @('::1', $true))) {
                $address = $case[0]; $parsed = $null
                if ([bool](Invoke-Expression $condition) -ne $case[1]) { throw "Validacion IPv4 incorrecta: $address" }
            }
            Write-Host 'PASS: red elevada rechaza direcciones invalidas y metacaracteres'
        }
    }
}
$bootstrapNode = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text -eq '$bootstrap' -and $n.Right.Extent.Text.StartsWith("@'" ) }, $true)
$bootstrap = $bootstrapNode.Right.Expression.Value -replace "`r`n", "`n"
$match = [regex]::Match($bootstrap, "(?s)cat >/usr/local/bin/wowctl <<'EOF'\n(.*?)\nEOF")
if (-not $match.Success) { throw 'No se encontro wowctl' }
$testDir = Join-Path $PSScriptRoot '.test-work'
New-Item -ItemType Directory -Path (Join-Path $testDir 'bin'),(Join-Path $testDir 'server') -Force | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$wowctl = $match.Groups[1].Value.Replace('ROOT=/opt/azerothcore', 'ROOT=server').Replace('cd "$ROOT"', 'cd "$ROOT"; ROOT=.')
# Exercise backup and validation only: never run daemon discovery/startup in tests.
$wowctl = [regex]::Replace($wowctl, '(?s)ensure_docker\(\) \{.*?\n\}\n', "ensure_docker() { :; }`n")
[IO.File]::WriteAllText((Join-Path $testDir 'wowctl'), $wowctl, $utf8)
[IO.File]::WriteAllText((Join-Path $testDir 'bootstrap.sh'), $bootstrap, $utf8)
$checks = @'
set -euo pipefail
docker() {
  if [[ "$*" == *mysqldump* ]]; then
    echo 'SQL de prueba'
    return "${DUMP_EXIT:-0}"
  fi
  return 0
}
export -f docker
echo 'DOCKER_DB_ROOT_PASSWORD=abc' >server/.env
bash -n wowctl
bash -n bootstrap.sh
if bash wowctl set-ip "bad';DROP TABLE realmlist;--"; then echo 'Acepto IP invalida'; exit 1; fi
mkdir -p server/backups
rm -f server/backups/mysql-*.sql server/backups/*.partial
if DUMP_EXIT=7 bash wowctl backup; then echo 'No detecto fallo de mysqldump'; exit 1; fi
if compgen -G 'server/backups/*.sql' >/dev/null; then echo 'ERROR: copia SQL final publicada aunque mysqldump fallo'; exit 1; fi
bash wowctl backup
grep -q 'SQL de prueba' server/backups/*.sql
echo 'PASS: wowctl rechaza inyeccion y solo publica backups completos'
'@
[IO.File]::WriteAllText((Join-Path $testDir 'checks.sh'), $checks.Replace("`r`n", "`n"), $utf8)
$bash = 'C:\Program Files\Git\bin\bash.exe'
if (-not (Test-Path -LiteralPath $bash)) { throw 'Estas pruebas requieren Git Bash' }
Push-Location $testDir
try {
    & $bash --noprofile --norc checks.sh
    if ($LASTEXITCODE -ne 0) { throw 'Fallo la prueba funcional de wowctl' }
} finally { Pop-Location }
Write-Host 'PASS: sintaxis PowerShell y pruebas funcionales aisladas'
