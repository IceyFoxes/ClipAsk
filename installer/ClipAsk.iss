#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

#ifndef MySourceDir
  #define MySourceDir "..\artifacts\publish\ClipAsk-0.1.0-win-x64"
#endif

#ifndef MyOutputDir
  #define MyOutputDir "..\artifacts\releases"
#endif

[Setup]
AppId={{4B1871A5-A41A-4CA7-8CD3-D4A49C4DCAD8}
AppName=ClipAsk
AppVersion={#MyAppVersion}
AppPublisher=IceyFoxes
AppPublisherURL=https://github.com/IceyFoxes/ClipAsk
AppSupportURL=https://github.com/IceyFoxes/ClipAsk/issues
AppUpdatesURL=https://github.com/IceyFoxes/ClipAsk/releases
DefaultDirName={localappdata}\Programs\ClipAsk
DefaultGroupName=ClipAsk
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir={#MyOutputDir}
OutputBaseFilename=ClipAsk-{#MyAppVersion}-win-x64-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\ClipAsk.exe
CloseApplications=yes
CloseApplicationsFilter=ClipAsk.exe
RestartApplications=no
SetupLogging=yes

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\ClipAsk"; Filename: "{app}\ClipAsk.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "ClipAsk"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\ClipAsk.exe"; Description: "Launch ClipAsk"; Flags: nowait postinstall skipifsilent
