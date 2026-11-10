$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('AB-update-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$fixture = Join-Path $testRoot 'Wow.exe'
& $compiler /nologo /target:winexe ("/out:"+$fixture) (Join-Path $PSScriptRoot 'FakeWow.cs')
if ($LASTEXITCODE -ne 0) { throw 'Falló compilación de fixture.' }
$probe = Join-Path $testRoot 'UpdateBenchmark.exe'
$sources = @('Program.cs','Models.cs','LauncherServer.cs','LauncherForm.cs') | ForEach-Object { Join-Path $sourceRoot $_ }
& $compiler /nologo /target:exe /main:AzerothCoreLauncher.UpdateBenchmark /define:FIXED ("/out:"+$probe) /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll /reference:System.IO.Compression.dll (Join-Path $PSScriptRoot 'UpdateBenchmark.cs') $sources
if ($LASTEXITCODE -ne 0) { throw 'Falló compilación de prueba.' }
& $probe (Join-Path $testRoot 'client') $fixture
if ($LASTEXITCODE -ne 0) { throw 'Falló prueba de actualización.' }
Write-Output "Carpeta sintética de prueba: $testRoot"
