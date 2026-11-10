param([string]$OutputPath)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'No se encontró el compilador de .NET Framework. Instala .NET Framework 4.8 Developer Pack.'
}

$sources = @(
    (Join-Path $PSScriptRoot 'Program.cs'),
    (Join-Path $PSScriptRoot 'Models.cs'),
    (Join-Path $PSScriptRoot 'LauncherServer.cs'),
    (Join-Path $PSScriptRoot 'LauncherForm.cs')
)
$artwork = Join-Path $PSScriptRoot 'Assets\Batzoki-Northrend.png'
$icon = Join-Path $PSScriptRoot 'Assets\AzerotBatzoki.ico'
if (-not (Test-Path -LiteralPath $artwork -PathType Leaf)) {
    throw "No se encuentra la ilustración del launcher: $artwork"
}
if (-not (Test-Path -LiteralPath $icon -PathType Leaf)) {
    throw "No se encuentra el icono del launcher: $icon"
}
$output = if ([string]::IsNullOrWhiteSpace($OutputPath)) { Join-Path $PSScriptRoot 'AzerotBatzoki.exe' } else { [System.IO.Path]::GetFullPath($OutputPath) }
$references = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.IO.Compression.dll',
    '/reference:System.Runtime.Serialization.dll',
    '/reference:System.Security.dll',
    '/reference:System.Windows.Forms.dll'
)
$resource = '/resource:' + $artwork + ',AzerothCoreLauncher.Batzoki-Northrend.png'
$iconResource = '/win32icon:' + $icon
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', ('/out:' + $output), $resource, $iconResource) + $references + $sources
& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Falló la compilación C# (código $LASTEXITCODE)."
}

$testProcess = Start-Process -FilePath $output -ArgumentList '--self-test' -Wait -PassThru -NoNewWindow
if ($testProcess.ExitCode -ne 0) {
    throw "Fallaron las comprobaciones internas (código $($testProcess.ExitCode))."
}
Write-Output "Compilación correcta: $output"
