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
    [switch]$Publier,
    # Clé de publication de l'application Android (voir README, « Signature de l'application Android »).
    [string]$CleAndroid = (Join-Path $env:USERPROFILE "AirsoftPlanner-signature\airsoftplanner.keystore"),
    # Construit l'APK avec la clé de développement du poste (essais seulement : jamais pour une version publiée).
    [switch]$SansCleAndroid
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

# 2. Application Android, signée avec la clé de publication (les mises à jour doivent toujours l'être avec la même).
$signing = @()
if (-not $SansCleAndroid) {
    if (-not (Test-Path $CleAndroid)) {
        throw "Clé de publication Android introuvable : $CleAndroid (voir README, « Signature de l'application Android »), ou -SansCleAndroid pour un essai."
    }
    if (-not $env:AIRSOFTPLANNER_KEYSTORE_PASS) {
        $secure = Read-Host "Mot de passe de la clé Android" -AsSecureString
        $env:AIRSOFTPLANNER_KEYSTORE_PASS = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    }
    # Mot de passe transmis par variable d'environnement (« env: ») : absent de la ligne de commande et des journaux.
    $signing = @("-p:AndroidKeyStore=true", "-p:AndroidSigningKeyStore=$CleAndroid", "-p:AndroidSigningKeyAlias=airsoftplanner",
        "-p:AndroidSigningStorePass=env:AIRSOFTPLANNER_KEYSTORE_PASS", "-p:AndroidSigningKeyPass=env:AIRSOFTPLANNER_KEYSTORE_PASS")
}
dotnet build (Join-Path $root "src\AirsoftPlanner.Mobile\AirsoftPlanner.Mobile.csproj") -c Release @signing
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
Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "LICENSE.txt")
Copy-Item (Join-Path $root "NOTICE") (Join-Path $stage "NOTICE.txt")

$zip = Join-Path $root "artifacts\$name.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Write-Host "Archive prête : $zip"

if ($Publier) {
    # Les logiciels installés trouvent cette version (« Rechercher une mise à jour ») dès que le dépôt est public.
    $apkRelease = Join-Path $root "artifacts\AirsoftPlanner-$version.apk"
    Copy-Item $apk $apkRelease -Force
    # Notes de version : docs/versions/<version>.md s'il existe.
    $notes = Join-Path $root "docs\versions\$version.md"
    $notesArgs = if (Test-Path $notes) { @("--notes-file", $notes) } else { @("--notes", "Airsoft Planner $version") }
    # GitHub CLI : dans le PATH, sinon à son emplacement d'installation (winget install GitHub.cli, puis gh auth login).
    $gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
    if (-not $gh) { $gh = @("$env:ProgramFiles\GitHub CLI\gh.exe", "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1 }
    if (-not $gh) { throw "GitHub CLI (gh) introuvable : winget install GitHub.cli, puis gh auth login." }
    & $gh release create "v$version" $zip $setup $apkRelease --repo Tepan-Games/AirsoftPlanner --title "Airsoft Planner $version" @notesArgs
    if ($LASTEXITCODE -ne 0) { throw "La publication sur GitHub a échoué." }
    Write-Host "Version v$version publiée sur GitHub."
}
