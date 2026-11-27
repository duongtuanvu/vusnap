; Inno Setup script for VuSnap. Built by .github/workflows/release.yml:
;   ISCC /DAppVersion=0.1.0 /DSourceDir=..\out\self-contained /DOutputDir=..\dist installer\VuSnap.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\self-contained"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "VuSnap"
#define AppExe "VuSnap.exe"

[Setup]
AppId={{F9D3A47E-6C21-4D8B-A174-1E83B2C85F60}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=vudt
AppPublisherURL=https://github.com/duongtuanvu/vusnap
AppSupportURL=https://github.com/duongtuanvu/vusnap/issues
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=VuSnap-{#AppVersion}-setup
; app-fit.ico has 16..256px frames; the old app.ico only had a single 256px frame, which looked blurry when Windows scaled it down
SetupIconFile=..\src\VuSnap\Assets\app-fit.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Per-user install by default (no admin prompt); the user can choose "for all users" in the dialog
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Remove the "start with Windows" entry (written by the app) on uninstall
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "{#AppName}"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
