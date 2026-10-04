# Installation d'Airsoft Planner pour l'utilisateur Windows courant (aucun droit administrateur nécessaire) :
# copie dans %LOCALAPPDATA%\Programs\AirsoftPlanner, raccourcis menu Démarrer et bureau, association des fichiers .aop.
#
#   Installer.cmd              installe (ou met à jour)
#   Installer.cmd -Desinstaller  supprime le logiciel, les raccourcis et l'association
param([switch]$Desinstaller)

$ErrorActionPreference = "Stop"
$source = $PSScriptRoot
$target = Join-Path $env:LOCALAPPDATA "Programs\AirsoftPlanner"
$exe = Join-Path $target "AirsoftPlanner.exe"
$startMenu = Join-Path ([Environment]::GetFolderPath("Programs")) "Airsoft Planner.lnk"
$desktop = Join-Path ([Environment]::GetFolderPath("Desktop")) "Airsoft Planner.lnk"
$classes = "HKCU:\Software\Classes"

function Remove-Association {
    Remove-Item "$classes\.aop" -Recurse -ErrorAction SilentlyContinue
    Remove-Item "$classes\AirsoftPlanner.Operation" -Recurse -ErrorAction SilentlyContinue
}

if ($Desinstaller) {
    Get-Process AirsoftPlanner -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item $startMenu, $desktop -ErrorAction SilentlyContinue
    Remove-Association
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Airsoft Planner a été désinstallé (vos fichiers d'OP .aop et vos réglages sont conservés)."
    return
}

if (-not (Test-Path (Join-Path $source "AirsoftPlanner.exe"))) {
    throw "AirsoftPlanner.exe introuvable à côté de ce script."
}

# Mise à jour : le logiciel ne doit pas être ouvert pendant la copie.
Get-Process AirsoftPlanner -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force $target | Out-Null
Get-ChildItem $source -Exclude "Installer.ps1", "Installer.cmd" | Copy-Item -Destination $target -Recurse -Force
Copy-Item (Join-Path $source "Installer.ps1") (Join-Path $target "Desinstaller.ps1") -Force

$shell = New-Object -ComObject WScript.Shell
foreach ($path in @($startMenu, $desktop)) {
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $target
    $link.IconLocation = "$exe,0"
    $link.Description = "Préparer, mener et analyser une OP d'airsoft"
    $link.Save()
}

# Fichiers .aop : ouverture par double-clic.
Remove-Association
New-Item "$classes\.aop" -Force | Set-ItemProperty -Name "(default)" -Value "AirsoftPlanner.Operation"
New-Item "$classes\AirsoftPlanner.Operation" -Force | Set-ItemProperty -Name "(default)" -Value "Opération Airsoft Planner"
New-Item "$classes\AirsoftPlanner.Operation\DefaultIcon" -Force | Set-ItemProperty -Name "(default)" -Value "`"$exe`",0"
New-Item "$classes\AirsoftPlanner.Operation\shell\open\command" -Force | Set-ItemProperty -Name "(default)" -Value "`"$exe`" `"%1`""

Write-Host "Airsoft Planner est installé : menu Démarrer et bureau. Les fichiers .aop s'ouvrent par double-clic."
Write-Host "Pour le désinstaller : powershell -ExecutionPolicy Bypass -File `"$target\Desinstaller.ps1`" -Desinstaller"
