# Erstellt PropTax.App.exe (Windows x64, Single-File, self-contained) unter publish\win-x64 und führt den Selbsttest aus.
# Aufruf:  .\build\publish-windows.ps1 [-Version 1.0.1]
param([string]$Version = "")
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
$extra = @()
if ($Version) { $extra += "-p:Version=$Version" }
dotnet test -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests fehlgeschlagen" }
dotnet publish src/PropTax.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true @extra -o publish/win-x64
if ($LASTEXITCODE -ne 0) { throw "Publish fehlgeschlagen" }
Remove-Item publish/win-x64/*.pdb -ErrorAction SilentlyContinue
$p = Start-Process -FilePath publish/win-x64/PropTax.App.exe -ArgumentList '--selftest' -Wait -PassThru -NoNewWindow
if ($p.ExitCode -ne 0) { throw "Selbsttest fehlgeschlagen ($($p.ExitCode))" }
Get-Item publish/win-x64/PropTax.App.exe | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
