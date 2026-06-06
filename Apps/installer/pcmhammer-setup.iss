; PCM Hammer — Inno Setup installer script
; Requires Inno Setup 6.x  (https://jrsoftware.org/isinfo.php)
;
; Build (from the repo root):
;   iscc Apps\installer\pcmhammer-setup.iss
;
; The payload is taken from a staging layout (see WinFormsRoot / CliRoot below).
; In CI those folders are produced by the build job. For a local build use
; Apps\installer\build-installer.ps1, which stages from each project's bin\Release
; and then invokes this script.
;
; Overridable defines (pass with iscc /D<name>=<value>):
;   AppVersion     version shown in Add/Remove Programs (default 0.0.0)
;   WinFormsRoot   folder containing PcmHammer\, PcmLogger\, VpwExplorer\
;   CliRoot        folder containing pcmhammer-cli.exe

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

; Defaults match the CI staging layout (TemporaryArtifactStorage at the repo root).
; Paths are relative to this .iss file (Apps\installer).
#ifndef WinFormsRoot
  #define WinFormsRoot "..\..\TemporaryArtifactStorage\WinForms"
#endif
#ifndef CliRoot
  #define CliRoot "..\..\TemporaryArtifactStorage\CLI"
#endif

#define AppName      "PCM Hammer"
#define AppPublisher "PCMHacking.net"
#define AppURL       "https://pcmhacking.net"
#define AppIcon      "..\UI\WindowsForms\PcmHammer\0411_256px.ico"

[Setup]
; AppId uniquely identifies the product for upgrades/uninstall — do not change it
; between releases or upgrades will install side-by-side instead of replacing.
AppId={{6E3D2F1A-9C4B-4E7A-9D2E-7A1B5C9F0E22}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}

; Default install location: Program Files\PcmHammer (privileged; requires admin).
DefaultDirName={autopf}\PcmHammer
DefaultGroupName=PCM Hammer
DisableProgramGroupPage=auto
AllowNoIcons=yes

PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible

; Produces output\setup.exe
OutputDir=output
OutputBaseFilename=setup
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\PcmHammer\PcmHammer.exe
UninstallDisplayName={#AppName}

Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full";   Description: "Full installation (GUI apps + command-line tool)"
Name: "cli";    Description: "Command-line tool only"
Name: "custom"; Description: "Custom installation"; Flags: iscustom

[Components]
Name: "gui"; Description: "GUI applications (PCM Hammer, PCM Logger, VPW Explorer)"; Types: full
Name: "cli"; Description: "Command-line tool (pcmhammer-cli.exe)";                   Types: full cli

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut for PCM Hammer"; GroupDescription: "Additional icons:"; Components: gui

[Files]
; --- GUI applications (each in its own subfolder; kernels live with PcmHammer) ---
Source: "{#WinFormsRoot}\PcmHammer\*";   DestDir: "{app}\PcmHammer";   Components: gui; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WinFormsRoot}\PcmLogger\*";   DestDir: "{app}\PcmLogger";   Components: gui; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#WinFormsRoot}\VpwExplorer\*"; DestDir: "{app}\VpwExplorer"; Components: gui; Flags: ignoreversion recursesubdirs createallsubdirs

; --- Command-line tool ---
Source: "{#CliRoot}\pcmhammer-cli.exe"; DestDir: "{app}\Cli"; Components: cli; Flags: ignoreversion
; The CLI loads kernels from disk (default: its working directory). Ship the kernels
; next to it so it works out of the box from {app}\Cli.
Source: "{#WinFormsRoot}\PcmHammer\Kernel-*.bin"; DestDir: "{app}\Cli"; Components: cli; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#WinFormsRoot}\PcmHammer\Loader-*.bin"; DestDir: "{app}\Cli"; Components: cli; Flags: ignoreversion skipifsourcedoesntexist

; --- Shared shortcut icon ---
Source: "{#AppIcon}"; DestDir: "{app}"; DestName: "pcmhammer.ico"; Flags: ignoreversion

[Icons]
Name: "{group}\PCM Hammer";   Filename: "{app}\PcmHammer\PcmHammer.exe";     IconFilename: "{app}\pcmhammer.ico"; Components: gui
Name: "{group}\PCM Logger";   Filename: "{app}\PcmLogger\PcmLogger.exe";     IconFilename: "{app}\pcmhammer.ico"; Components: gui
Name: "{group}\VPW Explorer"; Filename: "{app}\VpwExplorer\VpwExplorer.exe"; IconFilename: "{app}\pcmhammer.ico"; Components: gui
Name: "{group}\Uninstall PCM Hammer"; Filename: "{uninstallexe}"
Name: "{autodesktop}\PCM Hammer"; Filename: "{app}\PcmHammer\PcmHammer.exe"; IconFilename: "{app}\pcmhammer.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\PcmHammer\PcmHammer.exe"; Description: "Launch PCM Hammer"; Flags: nowait postinstall skipifsilent; Components: gui

[UninstallDelete]
; Remove the install tree if empty after uninstall. (Per-user settings live in
; %LOCALAPPDATA% and are intentionally left in place.)
Type: dirifempty; Name: "{app}\PcmHammer"
Type: dirifempty; Name: "{app}\PcmLogger"
Type: dirifempty; Name: "{app}\VpwExplorer"
Type: dirifempty; Name: "{app}\Cli"
Type: dirifempty; Name: "{app}"
