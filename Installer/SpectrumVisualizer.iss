; SpectrumVisualizer Inno Setup script
; Build with: iscc.exe /DAppVersion=3.0.0 Installer\SpectrumVisualizer.iss
; Run `dotnet publish SpectrumVisualizer/SpectrumVisualizer.UI/SpectrumVisualizer.UI.csproj -c Release` first.

#ifndef AppVersion
  #error AppVersion must be defined. Pass it as: iscc.exe /DAppVersion=3.0.0 SpectrumVisualizer.iss
#endif

#define AppName      "SpectrumVisualizer"
#define AppPublisher "Semiteq, Inc."
#define AppURL       "semiteq.ru"
#define AppExeName   "SpectrumVisualizer.UI.exe"
; The AppId of the 2.x installer: setup upgrades that install in place
#define AppId        "{{02EA7FE7-4B06-4B9C-93ED-2B49AE013EAA}"

; Paths relative to the location of this .iss file (Installer/)
#define SrcBinDir    "..\SpectrumVisualizer\Artifacts\publish\SpectrumVisualizer.UI\release_win-x64"
#define AppIconFile  "..\SpectrumVisualizer\SpectrumVisualizer.UI\icon.ico"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
VersionInfoVersion={#AppVersion}

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes

OutputDir=Output
OutputBaseFilename=SpectrumVisualizerInstaller

Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

WizardStyle=modern
SetupIconFile={#AppIconFile}
UninstallDisplayIcon={app}\{#AppExeName}

PrivilegesRequired=admin
CloseApplications=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Empties the program folder before the copy, so no 2.x file outlives the upgrade;
; data and logs live under %LOCALAPPDATA%, outside this folder
Type: filesandordirs; Name: "{app}\*"
; The 2.x shortcuts point at the removed exe; [Icons] recreates the chosen ones
Type: files; Name: "{autoprograms}\{#AppName}.lnk"
Type: files; Name: "{autodesktop}\{#AppName}.lnk"

[Files]
Source: "{#SrcBinDir}\*"; Excludes: "*.pdb,SpectrumVisualizer.*.xml"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
