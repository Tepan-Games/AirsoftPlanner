# Archive de distribution complète : programme d'installation Windows, application Android,
# documentation PDF et fichier d'OP d'exemple.
#
#   powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Exemple "chemin\vers\exemple.aop"
#
# Résultat : artifacts\AirsoftPlanner-<version>.zip et artifacts\AirsoftPlanner-<version>-Setup.exe
# (nécessite Inno Setup 6 : winget install JRSoftware.InnoSetup)
param(
    [Parameter(Mandatory = $true)][string]$Exemple,
    [string]$Documentation = "",
    # Publie la version sur GitHub (release v<version> avec l'archive, le Setup.exe et l'APK) : nécessite l'outil gh connecté.
    [switch]$Publier
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
[xml]$csproj = Get-Content (Join-Path $root "src\AirsoftPlanner.App\AirsoftPlanner.App.csproj")
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $Documentation) { $Documentation = Join-Path $root "docs\Airsoft Planner - Guide d'utilisation.pdf" }

# 1. Logiciel Windows autonome
& (Join-Path $PSScriptRoot "publish.ps1")
$windows = Join-Path $root "artifacts\AirsoftPlanner-win-x64"

# Programme d'installation (Inno Setup) : installation pour l'utilisateur, raccourcis, fichiers .aop,
# désinstallation depuis Paramètres › Applications, mises à jour silencieuses depuis le logiciel.
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 introuvable : winget install JRSoftware.InnoSetup" }
& $iscc /Q "/DAppVersion=$version" "/DSourceDir=$windows" "/O$(Join-Path $root 'artifacts')" (Join-Path $PSScriptRoot "AirsoftPlanner.iss")
if ($LASTEXITCODE -ne 0) { throw "La création du programme d'installation a échoué." }
$setup = Join-Path $root "artifacts\AirsoftPlanner-$version-Setup.exe"

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
Copy-Item $setup (Join-Path $win "Installer Airsoft Planner $version.exe")
Copy-Item $apk (Join-Path $android "AirsoftPlanner.apk")
Copy-Item $Documentation (Join-Path $stage "3 - Guide d'utilisation.pdf")
Copy-Item $Exemple (Join-Path $example "OP d'exemple.aop")
Copy-Item (Join-Path $PSScriptRoot "LISEZ-MOI.txt") $stage

$zip = Join-Path $root "artifacts\$name.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Host "Archive prête : $zip"

if ($Publier) {
    # Les logiciels installés trouvent cette version (« Rechercher une mise à jour ») dès que le dépôt est public.
    $apkRelease = Join-Path $root "artifacts\AirsoftPlanner-$version.apk"
    Copy-Item $apk $apkRelease -Force
    gh release create "v$version" $zip $setup $apkRelease --repo Tepan-Games/AirsoftPlanner --title "Airsoft Planner $version" --notes "Airsoft Planner $version"
    if ($LASTEXITCODE -ne 0) { throw "La publication sur GitHub a échoué." }
    Write-Host "Version v$version publiée sur GitHub."
}
