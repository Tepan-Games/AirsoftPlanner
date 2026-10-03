# Produit une version autonome d'Airsoft Planner pour Windows (aucune installation de .NET nécessaire)
# et l'archive ZIP à distribuer aux orgas.
#
#   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
#
# Résultat : artifacts\AirsoftPlanner-<version>-win-x64.zip, contenant AirsoftPlanner.exe.
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\AirsoftPlanner.App\AirsoftPlanner.App.csproj"
[xml]$csproj = Get-Content $project
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$output = Join-Path $root "artifacts\AirsoftPlanner-$Runtime"
$zip = Join-Path $root "artifacts\AirsoftPlanner-$version-$Runtime.zip"

if (Test-Path $output) { Remove-Item -Recurse -Force $output }
dotnet publish $project -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -o $output
if ($LASTEXITCODE -ne 0) { throw "La publication a échoué." }

# Symboles de débogage des bibliothèques natives : inutiles aux utilisateurs.
Get-ChildItem $output -Filter *.pdb | Remove-Item -Force

# Nom de programme parlant pour les utilisateurs.
Rename-Item (Join-Path $output "AirsoftPlanner.App.exe") "AirsoftPlanner.exe"

if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zip
Write-Host "Version autonome prête : $zip"
