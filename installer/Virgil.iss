; Inno Setup 6. Binaires exclusivement dans Program Files.
#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef PublishDir
  #define PublishDir SourcePath + "..\artifacts\Virgil2"
#endif
#if !FileExists(PublishDir + "\Virgil.ElevatedHelper.exe")
  #error Publication incomplete : Virgil.ElevatedHelper.exe absent
#endif

[Setup]
AppId={{79A190B0-5849-4E86-909F-81950C48A7D1}
AppName=Virgil
AppVersion={#AppVersion}
PrivilegesRequired=admin
DefaultDirName={autopf}\Virgil
DisableDirPage=yes
UsePreviousAppDir=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DefaultGroupName=Virgil
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=Virgil-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
AppMutex=Local\Virgil.SingleInstance.v2,Global\Virgil.Update.v2
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\Virgil.App.exe

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le bureau"; Flags: unchecked

[Files]
; La sortie complète inclut explicitement le helper, sans copie en dossier utilisateur.
Source: "{#PublishDir}\Virgil.ElevatedHelper.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "Virgil.ElevatedHelper.exe"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Virgil"; Filename: "{app}\Virgil.App.exe"
Name: "{autodesktop}\Virgil"; Filename: "{app}\Virgil.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Virgil.App.exe"; Description: "Lancer Virgil"; Flags: nowait postinstall unchecked runasoriginaluser skipifsilent

[Code]
var
  DeleteUserData: Boolean;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  { Refuse également /DIR et les anciennes installations hors Program Files. }
  if CompareText(RemoveBackslash(ExpandConstant('{app}')),
    RemoveBackslash(ExpandConstant('{autopf}\Virgil'))) <> 0 then
    Result := 'Installation refusée : les binaires et le helper doivent rester dans Program Files\Virgil.';
end;

function InitializeUninstall(): Boolean;
var
  Form: TSetupForm;
  Choice: TNewCheckBox;
  Info: TNewStaticText;
  ContinueButton, CancelButton: TNewButton;
begin
  Result := True;
  DeleteUserData := False;
  if UninstallSilent then exit;
  Form := CreateCustomForm(540, 220, False, True);
  try
    Form.Caption := 'Désinstallation de Virgil';
    Info := TNewStaticText.Create(Form);
    Info.Parent := Form;
    Info.SetBounds(ScaleX(16), ScaleY(16), ScaleX(508), ScaleY(100));
    Info.AutoSize := False;
    Info.WordWrap := True;
    Info.Caption := 'Les paramètres, rapports et logs sont conservés par défaut.' + #13#10 +
      'La quarantaine contient vos fichiers et ne sera JAMAIS supprimée. Emplacement pour chaque compte : %LOCALAPPDATA%\Virgil\Quarantine.' + #13#10 +
      'La suppression optionnelle concerne uniquement le compte exécutant ce désinstalleur, pas les autres utilisateurs.';
    Choice := TNewCheckBox.Create(Form);
    Choice.Parent := Form;
    Choice.SetBounds(ScaleX(16), ScaleY(122), ScaleX(508), ScaleY(36));
    Choice.Caption := 'Supprimer explicitement mes paramètres, rapports et logs';
    Choice.Checked := False;
    ContinueButton := TNewButton.Create(Form);
    ContinueButton.Parent := Form;
    ContinueButton.SetBounds(ScaleX(310), ScaleY(178), ScaleX(105), ScaleY(28));
    ContinueButton.Caption := 'Continuer';
    ContinueButton.ModalResult := mrOk;
    CancelButton := TNewButton.Create(Form);
    CancelButton.Parent := Form;
    CancelButton.SetBounds(ScaleX(425), ScaleY(178), ScaleX(100), ScaleY(28));
    CancelButton.Caption := 'Annuler';
    CancelButton.ModalResult := mrCancel;
    Result := Form.ShowModal = mrOk;
    DeleteUserData := Result and Choice.Checked;
  finally
    Form.Free;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DeleteUserData then
  begin
    { Chemins de VirgilPaths et ReportHistoryService ; jamais le parent Virgil. }
    DeleteFile(ExpandConstant('{userappdata}\Virgil\settings.json'));
    DelTree(ExpandConstant('{userappdata}\Virgil\reports'), True, True, True);
    DelTree(ExpandConstant('{localappdata}\Virgil\logs'), True, True, True);
  end;
end;
