; Programme d'installation Windows d'Airsoft Planner (Inno Setup 6), produit par scripts\release.ps1 :
;   ISCC.exe /DAppVersion=0.9.0 /DSourceDir=artifacts\AirsoftPlanner-win-x64 /Oartifacts scripts\AirsoftPlanner.iss
; Installation pour l'utilisateur courant (aucun droit administrateur), raccourcis, fichiers .aop ouverts par
; double-clic, désinstallation depuis Paramètres › Applications.
; Mise à jour depuis le logiciel : /VERYSILENT /SUPPRESSMSGBOXES /RELANCER=1 (le logiciel redémarre ensuite).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\AirsoftPlanner-win-x64"
#endif

[Setup]
AppId={{6B0E2C5A-3F7D-4C1B-9A57-2E4D8F1A7C93}
AppName=Airsoft Planner
AppVersion={#AppVersion}
AppVerName=Airsoft Planner {#AppVersion}
AppPublisher=Tepan Games
AppPublisherURL=https://github.com/Tepan-Games/AirsoftPlanner
AppSupportURL=https://github.com/Tepan-Games/AirsoftPlanner
AppUpdatesURL=https://github.com/Tepan-Games/AirsoftPlanner/releases
VersionInfoVersion={#AppVersion}
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\AirsoftPlanner
DisableProgramGroupPage=yes
DisableDirPage=auto
ChangesAssociations=yes
CloseApplications=force
SetupIconFile=..\src\AirsoftPlanner.App\Assets\airsoftplanner.ico
UninstallDisplayIcon={app}\AirsoftPlanner.exe
UninstallDisplayName=Airsoft Planner
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=AirsoftPlanner-{#AppVersion}-Setup

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"

[CustomMessages]
fr.AopFile=Opération Airsoft Planner
en.AopFile=Airsoft Planner operation
de.AopFile=Airsoft-Planner-Operation
es.AopFile=Operación de Airsoft Planner
it.AopFile=Operazione Airsoft Planner
fr.Comment=Préparer, mener et analyser une OP d'airsoft
en.Comment=Prepare, run and review an airsoft operation
de.Comment=Airsoft-Operationen vorbereiten, durchführen und auswerten
es.Comment=Preparar, dirigir y analizar una operación de airsoft
it.Comment=Preparare, condurre e analizzare un'operazione di softair

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Airsoft Planner"; Filename: "{app}\AirsoftPlanner.exe"; Comment: "{cm:Comment}"
Name: "{autodesktop}\Airsoft Planner"; Filename: "{app}\AirsoftPlanner.exe"; Comment: "{cm:Comment}"; Tasks: desktopicon

[Registry]
; Fichiers .aop : ouverture par double-clic (clés de l'utilisateur, retirées à la désinstallation).
Root: HKA; Subkey: "Software\Classes\.aop"; ValueType: string; ValueName: ""; ValueData: "AirsoftPlanner.Operation"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\AirsoftPlanner.Operation"; ValueType: string; ValueName: ""; ValueData: "{cm:AopFile}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\AirsoftPlanner.Operation\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\AirsoftPlanner.exe"",0"
Root: HKA; Subkey: "Software\Classes\AirsoftPlanner.Operation\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\AirsoftPlanner.exe"" ""%1"""

[Run]
Filename: "{app}\AirsoftPlanner.exe"; Description: "{cm:LaunchProgram,Airsoft Planner}"; Flags: nowait postinstall skipifsilent
; Mise à jour lancée depuis le logiciel : il redémarre dans sa nouvelle version.
Filename: "{app}\AirsoftPlanner.exe"; Flags: nowait; Check: IsRelaunch

[Code]
function IsRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:RELANCER|0}') = '1';
end;

function InitializeSetup: Boolean;
begin
  // Mise à jour : laisser au logiciel le temps de se fermer avant de remplacer ses fichiers.
  if IsRelaunch then
    Sleep(3000);
  Result := True;
end;
