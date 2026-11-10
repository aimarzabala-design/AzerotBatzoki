param([string]$Go = 'go.exe')
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & $Go test -v main.go main_test.go
    if ($LASTEXITCODE -ne 0) { throw 'Fallo go test' }
    & $Go vet main.go main_test.go
    if ($LASTEXITCODE -ne 0) { throw 'Fallo go vet' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-Installer.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Fallo Test-Installer' }
    & $Go build -trimpath -ldflags '-s -w' -o Instalar_WoW_Server.exe main.go
    if ($LASTEXITCODE -ne 0) { throw 'Fallo compilacion' }
    $result = & .\Instalar_WoW_Server.exe -CheckOnly
    if ($LASTEXITCODE -ne 0 -or ($result -join '') -notmatch (Get-FileHash .\installer.ps1).Hash) {
        throw 'El EXE no ha confirmado el hash de la fuente'
    }
    $result
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\LaunchWoW\Compilar.ps1
    if ($LASTEXITCODE -ne 0) { throw 'Fallo la compilacion del launcher C#' }
    $files = @('.gitattributes', '.gitignore', 'README.md', 'Instalar_WoW_Server.exe', 'installer.ps1', 'main.go', 'main_test.go', 'Test-Installer.ps1', 'Compilar.ps1', 'LEEME.md', 'LEEME.txt', 'CAMBIOS.txt', 'INFORME_REVISION.md', 'LaunchWoW\AzerotBatzoki.exe', 'LaunchWoW\Assets\LaunchWoW.exe', 'LaunchWoW\Program.cs', 'LaunchWoW\Models.cs', 'LaunchWoW\LauncherServer.cs', 'LaunchWoW\LauncherForm.cs', 'LaunchWoW\Compilar.ps1', 'LaunchWoW\Assets\Batzoki-Northrend.png', 'LaunchWoW\Assets\AzerotBatzoki.ico', 'LaunchWoW\Enable-DownloadHostFirewall.ps1')
    $sums = foreach ($file in $files) { ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $file) }
    Set-Content -LiteralPath SHA256SUMS-local.txt -Value $sums -Encoding ASCII
} finally { Pop-Location }
