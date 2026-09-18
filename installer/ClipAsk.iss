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
SetupIconFile=..\assets\brand\clipask.ico
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

[Code]
var
  RemoveUserDataOnUninstall: Boolean;

function InitializeUninstall(): Boolean;
var
  OptionsForm: TSetupForm;
  HeadingLabel: TNewStaticText;
  DetailLabel: TNewStaticText;
  RemoveDataCheckBox: TNewCheckBox;
  ContinueButton: TNewButton;
  CancelButton: TNewButton;
begin
  Result := True;
  RemoveUserDataOnUninstall := False;

  { A silent uninstall remains non-destructive unless a future explicit command-line
    contract is added. This prompt is only for an interactive user choice. }
  if UninstallSilent then
    Exit;

  OptionsForm := CreateCustomForm(ScaleX(440), ScaleY(210), False, True);
  try
    OptionsForm.Caption := 'Uninstall ClipAsk';

    HeadingLabel := TNewStaticText.Create(OptionsForm);
    HeadingLabel.Parent := OptionsForm;
    HeadingLabel.Left := ScaleX(20);
    HeadingLabel.Top := ScaleY(18);
    HeadingLabel.Width := ScaleX(400);
    HeadingLabel.Height := ScaleY(22);
    HeadingLabel.AutoSize := False;
    HeadingLabel.Font.Style := [fsBold];
    HeadingLabel.Caption := 'Keep or remove your ClipAsk account data';

    DetailLabel := TNewStaticText.Create(OptionsForm);
    DetailLabel.Parent := OptionsForm;
    DetailLabel.Left := ScaleX(20);
    DetailLabel.Top := ScaleY(48);
    DetailLabel.Width := ScaleX(400);
    DetailLabel.Height := ScaleY(48);
    DetailLabel.AutoSize := False;
    DetailLabel.WordWrap := True;
    DetailLabel.Caption :=
      'By default, ClipAsk keeps your sign-in and preferences so a future reinstall can reuse them. Screenshots saved elsewhere are never deleted.';

    RemoveDataCheckBox := TNewCheckBox.Create(OptionsForm);
    RemoveDataCheckBox.Parent := OptionsForm;
    RemoveDataCheckBox.Left := ScaleX(20);
    RemoveDataCheckBox.Top := ScaleY(104);
    RemoveDataCheckBox.Width := ScaleX(400);
    RemoveDataCheckBox.Height := ScaleY(38);
    RemoveDataCheckBox.Caption :=
      'Sign out of ChatGPT and remove ClipAsk settings and local data';
    RemoveDataCheckBox.Checked := False;

    ContinueButton := TNewButton.Create(OptionsForm);
    ContinueButton.Parent := OptionsForm;
    ContinueButton.Left := ScaleX(248);
    ContinueButton.Top := ScaleY(164);
    ContinueButton.Width := ScaleX(82);
    ContinueButton.Height := ScaleY(27);
    ContinueButton.Caption := 'Continue';
    ContinueButton.Default := True;
    ContinueButton.ModalResult := mrOk;

    CancelButton := TNewButton.Create(OptionsForm);
    CancelButton.Parent := OptionsForm;
    CancelButton.Left := ScaleX(338);
    CancelButton.Top := ScaleY(164);
    CancelButton.Width := ScaleX(82);
    CancelButton.Height := ScaleY(27);
    CancelButton.Caption := 'Cancel';
    CancelButton.Cancel := True;
    CancelButton.ModalResult := mrCancel;

    OptionsForm.ActiveControl := ContinueButton;
    Result := OptionsForm.ShowModal = mrOk;
    if Result then
      RemoveUserDataOnUninstall := RemoveDataCheckBox.Checked;
  finally
    OptionsForm.Free;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  SignOutStarted: Boolean;
  SignOutSucceeded: Boolean;
  StateDirectory: String;
begin
  if not RemoveUserDataOnUninstall then
    Exit;

  if CurUninstallStep = usUninstall then
  begin
    ResultCode := -1;
    SignOutSucceeded := False;
    if FileExists(ExpandConstant('{app}\ClipAsk.exe')) then
    begin
      SignOutStarted := Exec(
        ExpandConstant('{app}\ClipAsk.exe'),
        '--uninstall-sign-out',
        ExpandConstant('{app}'),
        SW_HIDE,
        ewWaitUntilTerminated,
        ResultCode);
      SignOutSucceeded := SignOutStarted and (ResultCode = 0);
    end;

    if not SignOutSucceeded then
    begin
      Log('ClipAsk could not confirm ChatGPT sign-out during uninstall.');
      if not UninstallSilent then
        MsgBox(
          'ClipAsk could not confirm that the ChatGPT sign-in was removed. Local ClipAsk data will still be deleted. You may also remove the ClipAsk entry from Windows Credential Manager.',
          mbError,
          MB_OK);
    end;
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    StateDirectory := ExpandConstant('{localappdata}\ClipAsk');
    if DelTree(StateDirectory, True, True, True) then
      Log('Removed ClipAsk local state: ' + StateDirectory)
    else
      Log('ClipAsk local state could not be completely removed: ' + StateDirectory);
  end;
end;
