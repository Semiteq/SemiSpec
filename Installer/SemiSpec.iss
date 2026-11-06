; Build with: iscc.exe /DAppVersion=3.0.0 Installer\SemiSpec.iss
; Run `dotnet publish SemiSpec/SemiSpec.UI/SemiSpec.UI.csproj -c Release` first.

#ifndef AppVersion
  #error AppVersion must be defined. Pass it as: iscc.exe /DAppVersion=3.0.0 Installer\SemiSpec.iss
#endif

#define AppName      "SemiSpec"
#define AppPublisher "Semiteq, Inc."
#define AppURL       "https://semiteq.ru"
#define AppExeName   "SemiSpec.exe"
; The AppId of the 2.x installer, kept through the rename from SpectrumVisualizer: setup upgrades in place
#define AppId        "{{02EA7FE7-4B06-4B9C-93ED-2B49AE013EAA}"

; Paths relative to the location of this .iss file (Installer/)
#define SrcBinDir    "..\SemiSpec\Artifacts\publish\SemiSpec.UI\release_win-x64"
#define AppIconFile  "..\SemiSpec\SemiSpec.UI\icon.ico"

; The log folder of every Semi application on the site: docs/architecture/overview.md, Release
#define LogRoot      "C:\DISTR\Logs\SemiSpec"

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

; An upgrade keeps the folder the previous install recorded (UsePreviousAppDir), the old name included
DefaultDirName={autopf}\{#AppName}
; Keep yes: [InstallDelete] empties {app}, so the user must not choose the folder
DisableDirPage=yes
DisableProgramGroupPage=yes

OutputDir=Output
OutputBaseFilename=SemiSpecInstaller

Compression=lzma2/ultra64
SolidCompression=yes

WizardStyle=modern
SetupIconFile={#AppIconFile}
WizardImageFile=wizard-large-164.bmp,wizard-large-205.bmp,wizard-large-246.bmp,wizard-large-328.bmp
WizardSmallImageFile=wizard-small-55.bmp,wizard-small-69.bmp,wizard-small-83.bmp,wizard-small-110.bmp
UninstallDisplayIcon={app}\{#AppExeName}

; Program Files and C:\DISTR both need admin rights
PrivilegesRequired=admin
CloseApplications=yes
; The 2.x installer recorded English; the language dialog preselects the Windows language instead
UsePreviousLanguage=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; No 2.x file outlives the upgrade. The spectra live under %LOCALAPPDATA% and the logs under {#LogRoot}, outside {app}
Type: filesandordirs; Name: "{app}\*"
; The old shortcuts point at the removed exe; [Icons] recreates the chosen ones.
; SpectrumVisualizer.lnk is the name the 2.x and 3.0 installers gave them
Type: files; Name: "{autoprograms}\{#AppName}.lnk"
Type: files; Name: "{autodesktop}\{#AppName}.lnk"
Type: files; Name: "{autoprograms}\SpectrumVisualizer.lnk"
Type: files; Name: "{autodesktop}\SpectrumVisualizer.lnk"

[Dirs]
; The operator, not the installing admin, writes the log; uninstall keeps it
Name: "{#LogRoot}"; Permissions: users-modify; Flags: uninsneveruninstall

[Files]
Source: "{#SrcBinDir}\*"; Excludes: "*.pdb,SemiSpec.xml,SemiSpec.*.xml"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser
