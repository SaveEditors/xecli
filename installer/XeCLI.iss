#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif

#ifndef ReleaseDir
  #error "ReleaseDir define is required."
#endif

#ifndef OutputDir
  #define OutputDir "out\\installer"
#endif
#ifndef DotNetRuntimeVersion
  #define DotNetRuntimeVersion "10.0.5"
#endif
#ifndef DotNetRuntimeInstaller
  #error "DotNetRuntimeInstaller define is required."
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
OutputBaseFilename=XeCLI-{#AppVersion}-setup-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dark windows11 includetitlebar
WizardImageFile=assets\wizard-side.png
WizardSmallImageFile=assets\wizard-small.png
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
ChangesEnvironment=yes
UninstallDisplayIcon={app}\rgh.exe

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
en.AddToPathTask=Add rgh to PATH
es.AddToPathTask=Agregar rgh al PATH
en.SupportUsButton=Support Us
es.SupportUsButton=Apoyanos
en.InstallDotNetRuntimeStatus=Installing .NET runtime {#DotNetRuntimeVersion} (x64)...
es.InstallDotNetRuntimeStatus=Instalando .NET runtime {#DotNetRuntimeVersion} (x64)...
en.InstallSummary=XeCLI is a powerful terminal-first toolkit built for Xbox 360 RGH and JTAG users. It brings together everything you need for live console work — console discovery and control, memory inspection and debugging, file system operations through XBDM and FTP, save and profile editing, avatar management, homebrew and dashboard staging, FATX disk handling, XEX dumping, reverse engineering helpers for Ghidra and IDA, and XeLL-backed NAND and keyvault tools. All of it is wrapped in one consistent, fast command-line experience named rgh. Whether you're doing quick checks, heavy debugging, content transfers, or full automation scripts, XeCLI keeps your workflow smooth and reliable on modified Xbox 360 consoles.
es.InstallSummary=XeCLI es un toolkit potente orientado a terminal para usuarios de Xbox 360 RGH y JTAG.%n%nReune todo lo necesario para trabajo en consola en vivo: descubrimiento y control de consola, inspeccion de memoria y depuracion, operaciones de sistema de archivos mediante XBDM y FTP, edicion de saves y perfiles, gestion de avatares, staging de homebrew y dashboard, manejo de discos FATX, volcado de XEX, ayudas de ingenieria inversa para Ghidra e IDA, y herramientas de NAND y keyvault con XeLL.%n%nTodo esta unificado en una experiencia de linea de comandos consistente y rapida llamada rgh. Ya sea para comprobaciones rapidas, depuracion intensiva, transferencias de contenido o scripts de automatizacion completos, XeCLI mantiene tu flujo de trabajo fluido y confiable en consolas Xbox 360 modificadas.

[Tasks]
Name: "modifypath"; Description: "{cm:AddToPathTask}"; Flags: checkedonce

[Files]
Source: "{#ReleaseDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#DotNetRuntimeInstaller}"; DestDir: "{tmp}"; DestName: "dotnet-runtime-{#DotNetRuntimeVersion}-win-x64.exe"; Flags: ignoreversion deleteafterinstall

[Run]
Filename: "{tmp}\dotnet-runtime-{#DotNetRuntimeVersion}-win-x64.exe"; \
    Parameters: "/install /quiet /norestart"; \
    StatusMsg: "{cm:InstallDotNetRuntimeStatus}"; \
    Flags: waituntilterminated runhidden; \
    Check: NeedsDotNetRuntimeInstall

[Code]
const
  PathTaskName = 'modifypath';
  SupportUrl = 'https://ko-fi.com/saveeditors';
  MachineEnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  UserEnvironmentKey = 'Environment';

var
  SupportButton: TNewButton;

function HasDotNetRuntime(const VersionPrefix: String): Boolean;
var
  RuntimeVersions: TArrayOfString;
  I: Integer;
begin
  Result := false;
  if not RegGetSubkeyNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App', RuntimeVersions) then
    exit;

  for I := 0 to GetArrayLength(RuntimeVersions) - 1 do
  begin
    if Pos(VersionPrefix + '.', RuntimeVersions[I]) = 1 then
    begin
      Result := true;
      exit;
    end;
  end;
end;

function NeedsDotNetRuntimeInstall: Boolean;
begin
  Result := not HasDotNetRuntime('10.0');
end;

function GetDefaultDir(Param: String): String;
begin
  if IsAdminInstallMode then
    Result := ExpandConstant('{autopf}\XeCLI')
  else
    Result := ExpandConstant('{localappdata}\Programs\XeCLI');
end;

function NormalizePathValue(const Value: String): String;
begin
  Result := Uppercase(RemoveBackslashUnlessRoot(Trim(Value)));
end;

function PathContainsEntry(const PathValue, Entry: String): Boolean;
begin
  Result := Pos(';' + NormalizePathValue(Entry) + ';', ';' + NormalizePathValue(PathValue) + ';') > 0;
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
  PathValue := PathValue + RemoveBackslashUnlessRoot(Entry);
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

procedure RemovePathEntry(const RootKey: Integer; const Subkey, Entry: String);
var
  PathValue: String;
  Updated: String;
begin
  if not RegQueryStringValue(RootKey, Subkey, 'Path', PathValue) then
    exit;

  Updated := RemovePathSegment(PathValue, Entry);
  if Updated <> PathValue then
    WritePathValue(RootKey, Subkey, Updated);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  InstallDir: String;
begin
  if (CurStep <> ssPostInstall) or not WizardIsTaskSelected(PathTaskName) then
    exit;

  InstallDir := ExpandConstant('{app}');
  if IsAdminInstallMode then
    AddPathEntry(HKLM, MachineEnvironmentKey, InstallDir)
  else
    AddPathEntry(HKCU, UserEnvironmentKey, InstallDir);
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
  WizardForm.WelcomeLabel2.Caption := ExpandConstant('{cm:InstallSummary}');
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

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  InstallDir: String;
begin
  if CurUninstallStep <> usUninstall then
    exit;

  InstallDir := ExpandConstant('{app}');
  RemovePathEntry(HKCU, UserEnvironmentKey, InstallDir);
  if IsAdminLoggedOn then
    RemovePathEntry(HKLM, MachineEnvironmentKey, InstallDir);
end;
