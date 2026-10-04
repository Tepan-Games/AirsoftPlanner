# Archive de distribution complète : logiciel Windows (avec installation), application Android,
# documentation PDF et fichier d'OP d'exemple.
#
#   powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Exemple "chemin\vers\exemple.aop"
#
# Résultat : artifacts\AirsoftPlanner-<version>.zip
param(
    [Parameter(Mandatory = $true)][string]$Exemple,
    [string]$Documentation = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
[xml]$csproj = Get-Content (Join-Path $root "src\AirsoftPlanner.App\AirsoftPlanner.App.csproj")
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $Documentation) { $Documentation = Join-Path $root "docs\Airsoft Planner - Guide d'utilisation.pdf" }

# 1. Logiciel Windows autonome
& (Join-Path $PSScriptRoot "publish.ps1")
$windows = Join-Path $root "artifacts\AirsoftPlanner-win-x64"

# 2. Application Android
dotnet build (Join-Path $root "src\AirsoftPlanner.Mobile\AirsoftPlanner.Mobile.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "La compilation de l'application Android a échoué." }
$apk = Join-Path $root "src\AirsoftPlanner.Mobile\bin\Release\net10.0-android\com.tepangames.airsoftplanner-Signed.apk"

# 3. Assemblage
$name = "AirsoftPlanner-$version"
$stage = Join-Path $root "artifacts\$name"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
$win = New-Item -ItemType Directory (Join-Path $stage "1 - Logiciel Windows")
$android = New-Item -ItemType Directory (Join-Path $stage "2 - Application Android")
$example = New-Item -ItemType Directory (Join-Path $stage "4 - Exemple")
Copy-Item (Join-Path $windows "*") $win -Recurse
Copy-Item (Join-Path $PSScriptRoot "Installer.ps1"), (Join-Path $PSScriptRoot "Installer.cmd") $win
Copy-Item $apk (Join-Path $android "AirsoftPlanner.apk")
Copy-Item $Documentation (Join-Path $stage "3 - Guide d'utilisation.pdf")
Copy-Item $Exemple (Join-Path $example "OP d'exemple.aop")
Copy-Item (Join-Path $PSScriptRoot "LISEZ-MOI.txt") $stage

$zip = Join-Path $root "artifacts\$name.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Host "Archive prête : $zip"
