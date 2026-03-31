#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif

#ifndef ReleaseDir
  #error "ReleaseDir define is required."
#endif
#ifndef AppIdentity
  #define AppIdentity "E4E0C9B1-68CE-4D99-8A6A-B825D1C615B1"
#endif

#ifndef OutputDir
  #define OutputDir "out\\installer"
#endif

[Setup]
AppId={{E4E0C9B1-68CE-4D99-8A6A-B825D1C615B1}
AppName=XeCLI
AppVersion={#AppVersion}
AppVerName=XeCLI {#AppVersion}
AppPublisher=SaveEditors
AppPublisherURL=https://github.com/SaveEditors/xecli
AppSupportURL=https://github.com/SaveEditors/xecli/issues
AppUpdatesURL=https://github.com/SaveEditors/xecli/releases
SetupIconFile=assets\xecli.ico
DefaultDirName={code:GetDefaultDir}
DisableProgramGroupPage=yes
DisableWelcomePage=no
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=XeCLI-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dark windows11 includetitlebar
WizardImageFile=assets\wizard-side.png
WizardSmallImageFile=assets\wizard-small.png
ArchitecturesAllowed=x86compatible x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
ChangesEnvironment=yes
UninstallDisplayIcon={app}\XeTerminal.exe

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
en.AddToPathTask=Add rgh to PATH
es.AddToPathTask=Agregar rgh al PATH
en.DesktopShortcutTask=Create desktop shortcut for XeTerminal
es.DesktopShortcutTask=Crear acceso directo de escritorio para XeTerminal
en.LaunchXeTerminal=Launch XeTerminal
es.LaunchXeTerminal=Iniciar XeTerminal
en.SupportUsButton=Support Us
es.SupportUsButton=Apoyanos
en.InstallSupportHint=During installation, use the Details view to inspect file actions. For support, launch setup with /LOG=setup.log to capture a log.
es.InstallSupportHint=Durante la instalacion, usa la vista Details para inspeccionar las acciones y archivos. Para soporte, inicia el instalador con /LOG=setup.log para capturar un registro.
en.InstallSummary=XeCLI is a powerful terminal-first toolkit built for Xbox 360 RGH and JTAG users. It brings together everything you need for live console work.%n%nAll of it is wrapped in one consistent, fast command-line experience named rgh. Whether you're doing quick checks, heavy debugging, content transfers, or full automation scripts, XeCLI keeps your workflow smooth and reliable on modified Xbox 360 consoles.%n%nFeatures include: Console discovery & control, memory inspection & debugging, file system operations through XBDM/FTP, avatar management, homebrew staging, FATX disk handling, Live XEX dumping, reverse engineering helpers for Ghidra and IDA, and so much more!
es.InstallSummary=XeCLI es un toolkit potente orientado a terminal para usuarios de Xbox 360 RGH y JTAG.%n%nReune todo lo necesario para trabajo en consola en vivo: descubrimiento y control de consola, inspeccion de memoria y depuracion, operaciones de sistema de archivos mediante XBDM y FTP, edicion de saves y perfiles, gestion de avatares, staging de homebrew y dashboard, manejo de discos FATX, volcado de XEX, ayudas de ingenieria inversa para Ghidra e IDA, y herramientas de NAND y keyvault con XeLL.%n%nTodo esta unificado en una experiencia de linea de comandos consistente y rapida llamada rgh. Ya sea para comprobaciones rapidas, depuracion intensiva, transferencias de contenido o scripts de automatizacion completos, XeCLI mantiene tu flujo de trabajo fluido y confiable en consolas Xbox 360 modificadas.

[Tasks]
Name: "modifypath"; Description: "{cm:AddToPathTask}"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:DesktopShortcutTask}"; Flags: unchecked

[Files]
Source: "{#ReleaseDir}\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: Is64BitInstallMode
Source: "{#ReleaseDir}\win-x86\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: not Is64BitInstallMode

[Icons]
Name: "{autoprograms}\XeCLI\XeCLI Terminal"; Filename: "{app}\XeTerminal.exe"; WorkingDir: "{app}"; IconFilename: "{app}\XeTerminal.exe"
Name: "{autodesktop}\XeCLI Terminal"; Filename: "{app}\XeTerminal.exe"; WorkingDir: "{app}"; IconFilename: "{app}\XeTerminal.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\XeTerminal.exe"; Description: "{cm:LaunchXeTerminal}"; Flags: nowait postinstall skipifsilent

[Code]
const
  PathTaskName = 'modifypath';
  SupportUrl = 'https://ko-fi.com/xecli';
  OwnedInstallMarkerFileName = '.xecli-install';
  OwnedInstallRegistryKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1';
  OwnedInstallRegistryInstallLocationValueName = 'InstallLocation';
  OwnedInstallRegistryAppPathValueName = 'Inno Setup: App Path';
  OwnedInstallMarkerValue = 'XeCLI|{#AppIdentity}';
  MachineEnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  UserEnvironmentKey = 'Environment';

var
  SupportButton: TNewButton;
  PreviousOwnedInstallDirs: TArrayOfString;

function GetDefaultDir(Param: String): String;
begin
  if IsAdminInstallMode then
    Result := ExpandConstant('{autopf}\XeCLI')
  else
    Result := ExpandConstant('{localappdata}\Programs\XeCLI');
end;

function GetCliLanguageCode(Param: String): String;
begin
  if CompareText(ActiveLanguage, 'es') = 0 then
    Result := 'es'
  else
    Result := 'en';
end;

function BuildLanguageMergeScript(const LanguageCode: String): String;
begin
  Result :=
    '$ErrorActionPreference = ''Stop''' + #13#10 +
    '$configPath = Join-Path $env:APPDATA ''XeCLI\config.json''' + #13#10 +
    '$configDir = Split-Path -Path $configPath -Parent' + #13#10 +
    'New-Item -ItemType Directory -Force -Path $configDir | Out-Null' + #13#10 +
    '$config = [pscustomobject]@{}' + #13#10 +
    'if (Test-Path -LiteralPath $configPath) {' + #13#10 +
    '  try { $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json } catch { $config = [pscustomobject]@{} }' + #13#10 +
    '}' + #13#10 +
    '$config | Add-Member -NotePropertyName UiLanguage -NotePropertyValue ''' + LanguageCode + ''' -Force' + #13#10 +
    '$config | Add-Member -NotePropertyName PathPromptHandled -NotePropertyValue $true -Force' + #13#10 +
    '$config | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $configPath -Encoding utf8' + #13#10;
end;

procedure ApplySelectedLanguage(const LanguageCode: String);
var
  ScriptPath: String;
  ScriptText: String;
  ResultCode: Integer;
begin
  ScriptPath := ExpandConstant('{tmp}\xecli-apply-language.ps1');
  ScriptText := BuildLanguageMergeScript(LanguageCode);
  if not SaveStringToFile(ScriptPath, ScriptText, False) then
    RaiseException('Failed to stage the language configuration script.');

  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    RaiseException('Failed to start the language configuration script.');

  if ResultCode <> 0 then
    RaiseException(Format('Language configuration script failed with exit code %d.', [ResultCode]));
end;

function NormalizePathSegment(const Value: String): String;
begin
  Result := Trim(Value);
  if (Length(Result) >= 2) and (Result[1] = '"') and (Result[Length(Result)] = '"') then
    Result := Copy(Result, 2, Length(Result) - 2);
  StringChangeEx(Result, '/', '\', True);
  Result := RemoveBackslashUnlessRoot(Result);
end;

function NormalizePathValue(const Value: String): String;
begin
  Result := Uppercase(NormalizePathSegment(Value));
end;

function PathContainsEntry(const PathValue, Entry: String): Boolean;
begin
  Result := Pos(';' + NormalizePathValue(Entry) + ';', ';' + NormalizePathValue(PathValue) + ';') > 0;
end;

function PathEntryListContains(const PathEntries: TArrayOfString; const Entry: String): Boolean;
var
  I: Integer;
  EntryCount: Integer;
begin
  Result := false;
  EntryCount := GetArrayLength(PathEntries);
  if EntryCount = 0 then
    exit;

  for I := 0 to EntryCount - 1 do
  begin
    if NormalizePathValue(PathEntries[I]) = NormalizePathValue(Entry) then
    begin
      Result := true;
      exit;
    end;
  end;
end;

procedure AddUniquePathEntry(var PathEntries: TArrayOfString; const Entry: String);
var
  Index: Integer;
  NormalizedEntry: String;
begin
  NormalizedEntry := NormalizePathSegment(Entry);
  if NormalizedEntry = '' then
    exit;

  if PathEntryListContains(PathEntries, NormalizedEntry) then
    exit;

  Index := GetArrayLength(PathEntries);
  SetArrayLength(PathEntries, Index + 1);
  PathEntries[Index] := NormalizedEntry;
end;

function IsOwnedInstallDir(const InstallDir: String): Boolean;
var
  MarkerPath: String;
begin
  Result := false;
  if NormalizePathSegment(InstallDir) = '' then
    exit;

  MarkerPath := AddBackslash(NormalizePathSegment(InstallDir)) + OwnedInstallMarkerFileName;
  if not FileExists(MarkerPath) then
    exit;
  Result := true;
end;

procedure AddOwnedInstallDirFromUninstallValue(const RootKey: Integer; const ValueName: String; var OwnedInstallDirs: TArrayOfString);
var
  InstallDir: String;
begin
  if not RegQueryStringValue(RootKey, OwnedInstallRegistryKey, ValueName, InstallDir) then
    exit;

  InstallDir := NormalizePathSegment(InstallDir);
  if (InstallDir = '') or not IsOwnedInstallDir(InstallDir) then
    exit;

  AddUniquePathEntry(OwnedInstallDirs, InstallDir);
end;

procedure QueryPathValue(const RootKey: Integer; const Subkey: String; var PathValue: String);
begin
  if not RegQueryStringValue(RootKey, Subkey, 'Path', PathValue) then
    PathValue := '';
end;

procedure WritePathValue(const RootKey: Integer; const Subkey, PathValue: String);
begin
  if not RegWriteExpandStringValue(RootKey, Subkey, 'Path', PathValue) then
    Log(Format('Failed to update PATH in %s', [Subkey]));
end;

procedure AddPathEntry(const RootKey: Integer; const Subkey, Entry: String);
var
  PathValue: String;
begin
  QueryPathValue(RootKey, Subkey, PathValue);
  if PathContainsEntry(PathValue, Entry) then
    exit;

  if (PathValue <> '') and (PathValue[Length(PathValue)] <> ';') then
    PathValue := PathValue + ';';
  PathValue := PathValue + NormalizePathSegment(Entry);
  WritePathValue(RootKey, Subkey, PathValue);
end;

function RemovePathSegment(const PathValue, Entry: String): String;
var
  Remaining: String;
  Item: String;
  Updated: String;
  DelimiterIndex: Integer;
begin
  Remaining := PathValue;
  Updated := '';

  while Remaining <> '' do
  begin
    DelimiterIndex := Pos(';', Remaining);
    if DelimiterIndex = 0 then
    begin
      Item := Remaining;
      Remaining := '';
    end
    else
    begin
      Item := Copy(Remaining, 1, DelimiterIndex - 1);
      Delete(Remaining, 1, DelimiterIndex);
    end;

    Item := Trim(Item);
    if Item = '' then
      continue;

    if NormalizePathValue(Item) = NormalizePathValue(Entry) then
      continue;

    if Updated <> '' then
      Updated := Updated + ';';
    Updated := Updated + Item;
  end;

  Result := Updated;
end;

function RemoveOwnedInstallPathEntries(const PathValue: String; const OwnedInstallDirs: TArrayOfString): String;
var
  Remaining: String;
  Item: String;
  Updated: String;
  SeenEntries: TArrayOfString;
  DelimiterIndex: Integer;
begin
  Remaining := PathValue;
  Updated := '';
  SetArrayLength(SeenEntries, 0);

  while Remaining <> '' do
  begin
    DelimiterIndex := Pos(';', Remaining);
    if DelimiterIndex = 0 then
    begin
      Item := Remaining;
      Remaining := '';
    end
    else
    begin
      Item := Copy(Remaining, 1, DelimiterIndex - 1);
      Delete(Remaining, 1, DelimiterIndex);
    end;

    Item := NormalizePathSegment(Item);
    if Item = '' then
      continue;

    if PathEntryListContains(OwnedInstallDirs, Item) then
      continue;

    if PathEntryListContains(SeenEntries, Item) then
      continue;

    AddUniquePathEntry(SeenEntries, Item);
    if Updated <> '' then
      Updated := Updated + ';';
    Updated := Updated + Item;
  end;

  Result := Updated;
end;

function SetAuthoritativePathEntry(const PathValue, Entry: String): String;
var
  Updated: String;
begin
  Updated := PathValue;
  if NormalizePathSegment(Entry) = '' then
  begin
    Result := Updated;
    exit;
  end;

  if not PathContainsEntry(Updated, Entry) then
  begin
    if (Updated <> '') and (Updated[Length(Updated)] <> ';') then
      Updated := Updated + ';';
    Updated := Updated + NormalizePathSegment(Entry);
  end;

  Result := Updated;
end;

procedure CopyOwnedInstallDirs(const Source: TArrayOfString; var Dest: TArrayOfString);
var
  I: Integer;
  SourceCount: Integer;
begin
  SourceCount := GetArrayLength(Source);
  SetArrayLength(Dest, SourceCount);
  if SourceCount = 0 then
    exit;

  for I := 0 to SourceCount - 1 do
    Dest[I] := Source[I];
end;

procedure PopulateOwnedInstallDirsFromUninstallInfo(var OwnedInstallDirs: TArrayOfString);
begin
  SetArrayLength(OwnedInstallDirs, 0);
  AddOwnedInstallDirFromUninstallValue(HKCU, OwnedInstallRegistryInstallLocationValueName, OwnedInstallDirs);
  AddOwnedInstallDirFromUninstallValue(HKCU, OwnedInstallRegistryAppPathValueName, OwnedInstallDirs);
  AddOwnedInstallDirFromUninstallValue(HKLM, OwnedInstallRegistryInstallLocationValueName, OwnedInstallDirs);
  AddOwnedInstallDirFromUninstallValue(HKLM, OwnedInstallRegistryAppPathValueName, OwnedInstallDirs);
  if IsWin64 then
  begin
    AddOwnedInstallDirFromUninstallValue(HKLM64, OwnedInstallRegistryInstallLocationValueName, OwnedInstallDirs);
    AddOwnedInstallDirFromUninstallValue(HKLM64, OwnedInstallRegistryAppPathValueName, OwnedInstallDirs);
  end;
end;

function InitializeSetup: Boolean;
begin
  PopulateOwnedInstallDirsFromUninstallInfo(PreviousOwnedInstallDirs);
  Result := true;
end;

procedure SyncXeCLIPathValue(const RootKey: Integer; const Subkey: String; const OwnedInstallDirs: TArrayOfString; const CurrentInstallDir: String; const ShouldAddCurrentEntry: Boolean);
var
  PathValue: String;
  Updated: String;
begin
  QueryPathValue(RootKey, Subkey, PathValue);
  Updated := RemoveOwnedInstallPathEntries(PathValue, OwnedInstallDirs);
  if ShouldAddCurrentEntry then
    Updated := SetAuthoritativePathEntry(Updated, CurrentInstallDir);

  if Updated <> PathValue then
    WritePathValue(RootKey, Subkey, Updated);
end;

procedure SyncXeCLIPathState(const CurrentInstallDir: String; const ShouldAddCurrentEntry: Boolean);
var
  OwnedInstallDirs: TArrayOfString;
begin
  CopyOwnedInstallDirs(PreviousOwnedInstallDirs, OwnedInstallDirs);
  AddUniquePathEntry(OwnedInstallDirs, CurrentInstallDir);
  SyncXeCLIPathValue(HKCU, UserEnvironmentKey, OwnedInstallDirs, CurrentInstallDir, ShouldAddCurrentEntry and not IsAdminInstallMode);
  SyncXeCLIPathValue(HKLM64, MachineEnvironmentKey, OwnedInstallDirs, CurrentInstallDir, ShouldAddCurrentEntry and IsAdminInstallMode);
end;

procedure WriteOwnedInstallMarker(const InstallDir: String);
begin
  if not SaveStringToFile(AddBackslash(NormalizePathSegment(InstallDir)) + OwnedInstallMarkerFileName, OwnedInstallMarkerValue, false) then
    RaiseException('Failed to write the XeCLI install marker.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  InstallDir: String;
begin
  if CurStep <> ssPostInstall then
    exit;

  InstallDir := ExpandConstant('{app}');
  SyncXeCLIPathState(InstallDir, WizardIsTaskSelected(PathTaskName));

  ApplySelectedLanguage(GetCliLanguageCode(''));
  WriteOwnedInstallMarker(InstallDir);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  InstallDir: String;
begin
  if CurUninstallStep <> usUninstall then
    exit;

  InstallDir := ExpandConstant('{app}');
  SyncXeCLIPathState(InstallDir, false);
end;

procedure OpenSupportPage(Sender: TObject);
var
  ErrorCode: Integer;
begin
  ShellExecAsOriginalUser('open', SupportUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

procedure InitializeWizard;
var
  WelcomeBottom: Integer;
begin
  WizardForm.WelcomeLabel2.Caption := ExpandConstant('{cm:InstallSummary}') + #13#10#13#10 + ExpandConstant('{cm:InstallSupportHint}');
  WizardForm.WelcomeLabel2.AutoSize := False;
  WizardForm.WelcomeLabel2.WordWrap := True;
  WelcomeBottom := WizardForm.NextButton.Top - WizardForm.WelcomePage.Top - ScaleY(20);
  WizardForm.WelcomeLabel2.Height := WelcomeBottom - WizardForm.WelcomeLabel2.Top;

  SupportButton := TNewButton.Create(WizardForm);
  SupportButton.Parent := WizardForm;
  SupportButton.Caption := ExpandConstant('{cm:SupportUsButton}');
  SupportButton.Left := ScaleX(12);
  SupportButton.Top := WizardForm.CancelButton.Top;
  SupportButton.Width := ScaleX(96);
  SupportButton.Height := WizardForm.CancelButton.Height;
  SupportButton.Anchors := [akLeft, akBottom];
  SupportButton.OnClick := @OpenSupportPage;
end;
