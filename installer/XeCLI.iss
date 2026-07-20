#ifndef AppVersion
  #error "AppVersion define is required."
#endif

#ifndef ReleaseDir
  #error "ReleaseDir define is required."
#endif

#ifndef TargetRuntime
  #error "TargetRuntime define is required."
#endif

#if TargetRuntime == "win-x64"
  #define OutputArchitectureName "win-x64"
#elif TargetRuntime == "win-x86"
  #define OutputArchitectureName "win-x86-legacy"
#else
  #error "TargetRuntime must be win-x64 or win-x86."
#endif

#ifndef OutputDir
  #define OutputDir "out\\installer"
#endif

[Setup]
AppId={{E4E0C9B1-68CE-4D99-8A6A-B825D1C615B1}
AppName=XeCLI
AppVersion={#AppVersion}
#if TargetRuntime == "win-x86"
AppVerName=XeCLI {#AppVersion} (Legacy 32-bit)
#else
AppVerName=XeCLI {#AppVersion}
#endif
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
OutputBaseFilename=XeCLI-{#AppVersion}-{#OutputArchitectureName}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dark windows11 includetitlebar
WizardImageFile=assets\wizard-side.png
WizardSmallImageFile=assets\wizard-small.png
MinVersion=10.0.14393
CloseApplications=yes
RestartApplications=no
#if TargetRuntime == "win-x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
ArchitecturesAllowed=x86compatible
#endif
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage
ChangesEnvironment=yes
UninstallDisplayIcon={app}\XeTerminal.exe
#ifdef XeCliAuthenticode
SignTool=xecli
SignedUninstaller=yes
SignToolRetryCount=3
SignToolMinimumTimeBetween=1000
SignToolRunMinimized=yes
#endif

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
en.AddToPathTask=Add rgh to PATH
es.AddToPathTask=Agregar rgh al PATH
en.DesktopShortcutTask=Create a desktop shortcut for XeCLI
es.DesktopShortcutTask=Crear un acceso directo en el escritorio para XeCLI
en.LaunchXeTerminal=Launch XeCLI
es.LaunchXeTerminal=Iniciar XeCLI
en.SupportUsButton=Support Us
es.SupportUsButton=Apóyanos
en.InstallSupportHint=Setup will install the XeCLI desktop application and the rgh command-line tool. Select Next to review the destination and installation options.
es.InstallSupportHint=El instalador incluirá la aplicación de escritorio XeCLI y la herramienta de línea de comandos rgh. Selecciona Siguiente para revisar la ubicación y las opciones de instalación.
#if TargetRuntime == "win-x86"
en.ArchitectureNotice=Legacy 32-bit compatibility release. Use the 64-bit installer on modern Windows. This package is self-contained, so no separate .NET installation is required.
es.ArchitectureNotice=Versión heredada de 32 bits para compatibilidad. Usa el instalador de 64 bits en Windows moderno. Este paquete es autocontenido, por lo que no se requiere instalar .NET por separado.
#else
en.ArchitectureNotice=Recommended 64-bit release. This package is self-contained, so no separate .NET installation is required.
es.ArchitectureNotice=Versión recomendada de 64 bits. Este paquete es autocontenido, por lo que no se requiere instalar .NET por separado.
#endif
en.InstallSummary=XeCLI brings desktop and command-line tools for Xbox 360 RGH and JTAG development into one focused Windows toolkit. Use it for diagnostics, file management, memory inspection, and reverse-engineering workflows.
es.InstallSummary=XeCLI reúne herramientas gráficas y de línea de comandos para el desarrollo en consolas Xbox 360 RGH y JTAG en un conjunto integrado para Windows. Úsalo para diagnóstico, gestión de archivos, inspección de memoria e ingeniería inversa.
en.UserDataRetained=XeCLI settings and cache were retained. Captures, configured log paths, and any XECLI_HOME directory were also left untouched. You can optionally remove these profile folders manually:
es.UserDataRetained=La configuración y la caché de XeCLI se conservaron. Las capturas, las rutas de registro configuradas y cualquier directorio XECLI_HOME también permanecen. Puedes eliminar manualmente estas carpetas de perfil si lo deseas:
en.UnsafeInstallDir=Choose a dedicated XeCLI installation folder. Drive roots, Windows directories, and profile data roots are not valid installation targets.
es.UnsafeInstallDir=Elige una carpeta de instalación dedicada para XeCLI. Las raíces de unidad, los directorios de Windows y las raíces de datos del perfil no son destinos válidos.
en.InstallRelocationBlocked=XeCLI is already installed in another or ambiguous location. Uninstall the existing copy without removing its settings, then rerun this installer for the new location.
es.InstallRelocationBlocked=XeCLI ya está instalado en otra ubicación o en una ubicación ambigua. Desinstala la copia existente sin eliminar su configuración y vuelve a ejecutar este instalador para la nueva ubicación.
en.InstallOwnedManifestRequired=The existing XeCLI installation cannot be upgraded safely because its file ownership manifest is missing. Uninstall the existing XeCLI copy while retaining its settings, then rerun this installer.
es.InstallOwnedManifestRequired=La instalación existente de XeCLI no se puede actualizar de forma segura porque falta su manifiesto de archivos. Desinstala la copia existente de XeCLI conservando su configuración y vuelve a ejecutar este instalador.
en.InstallOwnedPreparationFailed=Setup could not validate and preserve the existing XeCLI ownership inventory. No new files were installed. Review the setup log and rerun setup.
es.InstallOwnedPreparationFailed=El instalador no pudo validar y conservar el inventario de propiedad existente de XeCLI. No se instalaron archivos nuevos. Revisa el registro y vuelve a ejecutar el instalador.
en.InstallOwnedCleanupFailed=Setup could not complete post-install integrity validation or stale-file cleanup. Ownership and hash evidence were retained. Review the setup log, resolve the reported file issue, and rerun setup.
es.InstallOwnedCleanupFailed=El instalador no pudo completar la validación de integridad posterior a la instalación o la limpieza de archivos obsoletos. Se conservaron las pruebas de propiedad y hashes. Revisa el registro del instalador, resuelve el problema indicado y vuelve a ejecutar el instalador.
en.InstallPortableDestinationBlocked=The selected folder contains a XeCLI portable installation. Setup will not convert it or remove its UserData. Choose a different empty folder for the installed version. The portable folder will remain unchanged.
es.InstallPortableDestinationBlocked=La carpeta seleccionada contiene una instalación portátil de XeCLI. El instalador no la convertirá ni eliminará su carpeta UserData. Elige otra carpeta vacía para la versión instalada. La carpeta portátil permanecerá sin cambios.
en.InstallNonEmptyDestinationBlocked=The selected folder is not empty and is not the registered XeCLI installation. To protect its contents, choose a different empty folder.
es.InstallNonEmptyDestinationBlocked=La carpeta seleccionada no está vacía y no es la instalación registrada de XeCLI. Para proteger su contenido, elige otra carpeta vacía.
en.InstallDestinationInspectionFailed=Setup could not safely inspect the selected destination. No files were installed. Choose an accessible, empty folder and run Setup again.
es.InstallDestinationInspectionFailed=El instalador no pudo inspeccionar de forma segura el destino seleccionado. No se instaló ningún archivo. Elige una carpeta accesible y vacía y vuelve a ejecutar el instalador.

[Tasks]
Name: "modifypath"; Description: "{cm:AddToPathTask}"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:DesktopShortcutTask}"; Flags: unchecked

[Files]
Source: "{#ReleaseDir}\*"; DestDir: "{app}"; Excludes: "xecli.portable,release-manifest.json,UserData\*,logs\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ReleaseDir}\release-manifest.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\XeCLI\XeCLI"; Filename: "{app}\XeTerminal.exe"; WorkingDir: "{app}"; IconFilename: "{app}\XeTerminal.exe"; AppUserModelID: "SaveEditors.XeCLI.XeTerminal"
Name: "{autodesktop}\XeCLI"; Filename: "{app}\XeTerminal.exe"; WorkingDir: "{app}"; IconFilename: "{app}\XeTerminal.exe"; AppUserModelID: "SaveEditors.XeCLI.XeTerminal"; Tasks: desktopicon

[Run]
Filename: "{app}\XeTerminal.exe"; Description: "{cm:LaunchXeTerminal}"; Flags: nowait postinstall skipifsilent

[Code]
const
  PathTaskName = 'modifypath';
  SupportUrl = 'https://ko-fi.com/xecli';
  UninstallRegistryKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#Copy(SetupSetting("AppId"), 2)}_is1';
  UserPathsRegistryKey = 'Software\SaveEditors\XeCLI\Installer';
  UninstallRegistryInstallLocationValueName = 'InstallLocation';
  UninstallRegistryAppPathValueName = 'Inno Setup: App Path';
  UninstallRegistryUninstallStringValueName = 'UninstallString';
  UninstallRegistryDisplayVersionValueName = 'DisplayVersion';
  OwnedInstallRegistryUserProfileValueName = 'XeCLI User Profile';
  OwnedInstallRegistryUserAppDataValueName = 'XeCLI User AppData';
  OwnedInstallRegistryLocalAppDataValueName = 'XeCLI Local AppData';
  MachineEnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  UserEnvironmentKey = 'Environment';
  OwnedFilesManifestName = 'xecli-owned-files.txt';
  OwnedHashesManifestName = 'xecli-owned-hashes.sha256';
  PreviousOwnedFilesManifestName = 'xecli-owned-files.previous.txt';
  PreviousOwnedFilesManifestTempName = 'xecli-owned-files.previous.tmp';
  ReleaseManifestName = 'release-manifest.json';
  PortableMarkerName = 'xecli.portable';
  LegacyInstallMarkerName = '.xecli-install';
  LegacyInstallMarkerValueV1 = 'XeCLI|E4E0C9B1-68CE-4D99-8A6A-B825D1C615B1';
  LegacyInstallMarkerValueX64 = 'XeCLI|E4E0C9B1-68CE-4D99-8A6A-B825D1C615B1|win-x64';
  LegacyInstallMarkerValueX86 = 'XeCLI|E4E0C9B1-68CE-4D99-8A6A-B825D1C615B1|win-x86';
  PortableUserDataDirectoryName = 'UserData';
  LegacyLocalLogsDirectoryName = 'logs';
  ErrorFileNotFound = 2;
  ErrorPathNotFound = 3;
  ErrorAccessDenied = 5;
  ErrorNoMoreFiles = 18;
  ErrorDirNotEmpty = 145;
  FileAttributeDirectory = $10;
  FileAttributeReparsePoint = $400;
  InvalidFileAttributes = -1;
  MoveFileReplaceExisting = $1;
  MoveFileWriteThrough = $8;

var
  SupportButton: TNewButton;
  CurrentUserProfileDir: String;
  CurrentUserAppDataDir: String;
  CurrentUserLocalAppDataDir: String;
  CurrentUserPathsResolved: Boolean;
  PreparedUpgrade: Boolean;
  PreparedUpgradeCleanupFailed: Boolean;
  PreparedUpgradeInstallDir: String;
  PreparedOldOwnedFiles: TArrayOfString;
  PreparedOldParentDirectories: TArrayOfString;

function WindowsGetFileAttributes(FileName: String): Integer;
  external 'GetFileAttributesW@kernel32.dll stdcall';
function WindowsDeleteFile(FileName: String): Boolean;
  external 'DeleteFileW@kernel32.dll stdcall';
function WindowsRemoveDirectory(DirectoryName: String): Boolean;
  external 'RemoveDirectoryW@kernel32.dll stdcall';
function WindowsMoveFileEx(ExistingFileName, NewFileName: String;
  Flags: Integer): Boolean;
  external 'MoveFileExW@kernel32.dll stdcall';
function WindowsGetLastError: Integer;
  external 'GetLastError@kernel32.dll stdcall';

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

function BuildUserPathQueryScript: String;
begin
  Result :=
    'param([Parameter(Mandatory=$true)][string]$OutputPath)' + #13#10 +
    '$ErrorActionPreference = ''Stop''' + #13#10 +
    '$paths = @(' + #13#10 +
    '  [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile),' + #13#10 +
    '  [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData),' + #13#10 +
    '  [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)' + #13#10 +
    ')' + #13#10 +
    'foreach ($path in $paths) {' + #13#10 +
    '  if ([string]::IsNullOrWhiteSpace($path) -or -not [IO.Path]::IsPathRooted($path)) { exit 2 }' + #13#10 +
    '}' + #13#10 +
    '[IO.File]::WriteAllLines($OutputPath, $paths, [Text.UTF8Encoding]::new($false))' + #13#10;
end;

function TryQueryOriginalUserPaths(var ProfileDir, AppDataDir, LocalAppDataDir: String): Boolean;
var
  ScriptPath: String;
  OutputPath: String;
  Paths: TArrayOfString;
  ResultCode: Integer;
begin
  Result := false;
  ScriptPath := ExpandConstant('{tmp}\xecli-query-user-paths.ps1');
  OutputPath := ExpandConstant('{tmp}\xecli-user-paths.txt');
  DeleteFile(OutputPath);

  if not SaveStringToFile(ScriptPath, BuildUserPathQueryScript, false) then
  begin
    Log('Unable to stage the XeCLI current-user path query script.');
    exit;
  end;

  ResultCode := -1;
  if not ExecAsOriginalUser(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath +
    '" -OutputPath "' + OutputPath + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Log(Format('Unable to query XeCLI paths as the original user (error %d).', [ResultCode]));
    exit;
  end;

  if ResultCode <> 0 then
  begin
    Log(Format('XeCLI current-user path query exited with code %d.', [ResultCode]));
    exit;
  end;

  if not LoadStringsFromFile(OutputPath, Paths) or (GetArrayLength(Paths) <> 3) then
  begin
    Log('XeCLI current-user path query returned an unexpected result.');
    exit;
  end;

  ProfileDir := Trim(Paths[0]);
  AppDataDir := Trim(Paths[1]);
  LocalAppDataDir := Trim(Paths[2]);
  Result := (ProfileDir <> '') and (AppDataDir <> '') and (LocalAppDataDir <> '');
  DeleteFile(OutputPath);
  DeleteFile(ScriptPath);
end;

procedure ResolveCurrentUserPaths;
var
  OriginalProfileDir: String;
  OriginalAppDataDir: String;
  OriginalLocalAppDataDir: String;
begin
  CurrentUserProfileDir := Trim(GetEnv('USERPROFILE'));
  CurrentUserAppDataDir := Trim(ExpandConstant('{userappdata}'));
  CurrentUserLocalAppDataDir := Trim(ExpandConstant('{localappdata}'));
  CurrentUserPathsResolved :=
    (CurrentUserProfileDir <> '') and
    (CurrentUserAppDataDir <> '') and
    (CurrentUserLocalAppDataDir <> '');

  if IsAdminInstallMode then
  begin
    CurrentUserPathsResolved := TryQueryOriginalUserPaths(
      OriginalProfileDir, OriginalAppDataDir, OriginalLocalAppDataDir);
    if CurrentUserPathsResolved then
    begin
      CurrentUserProfileDir := OriginalProfileDir;
      CurrentUserAppDataDir := OriginalAppDataDir;
      CurrentUserLocalAppDataDir := OriginalLocalAppDataDir;
    end
    else
      Log('Unable to resolve the original user data paths; retention paths will use uninstall-time values.');
  end;
end;

function BuildLanguageMergeScript(const LanguageCode: String): String;
begin
  Result :=
    '$ErrorActionPreference = ''Stop''' + #13#10 +
    '$stateRoot = if ([string]::IsNullOrWhiteSpace($env:XECLI_HOME)) { Join-Path $env:APPDATA ''XeCLI'' } else { [IO.Path]::GetFullPath($env:XECLI_HOME) }' + #13#10 +
    '$configPath = Join-Path $stateRoot ''config.json''' + #13#10 +
    '$configDir = Split-Path -Path $configPath -Parent' + #13#10 +
    'New-Item -ItemType Directory -Force -Path $configDir | Out-Null' + #13#10 +
    '$config = [pscustomobject]@{}' + #13#10 +
    'if (Test-Path -LiteralPath $configPath) {' + #13#10 +
    '  try { $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json } catch { exit 2 }' + #13#10 +
    '  if ($config -isnot [pscustomobject]) { exit 3 }' + #13#10 +
    '}' + #13#10 +
    '$changed = $false' + #13#10 +
    'if ($null -eq $config.PSObject.Properties[''UiLanguage'']) {' + #13#10 +
    '  $config | Add-Member -NotePropertyName UiLanguage -NotePropertyValue ''' + LanguageCode + '''' + #13#10 +
    '  $changed = $true' + #13#10 +
    '}' + #13#10 +
    'if ($null -eq $config.PSObject.Properties[''PathPromptHandled'']) {' + #13#10 +
    '  $config | Add-Member -NotePropertyName PathPromptHandled -NotePropertyValue $true' + #13#10 +
    '  $changed = $true' + #13#10 +
    '}' + #13#10 +
    'if (-not $changed) { exit 0 }' + #13#10 +
    '$tempPath = $configPath + ''.tmp''' + #13#10 +
    '$config | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $tempPath -Encoding utf8' + #13#10 +
    'Move-Item -LiteralPath $tempPath -Destination $configPath -Force' + #13#10;
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
  begin
    Log('Unable to stage the optional XeCLI language configuration script.');
    exit;
  end;

  if not ExecAsOriginalUser(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Log('Unable to start the optional XeCLI language configuration script as the original user.');
    exit;
  end;

  if ResultCode <> 0 then
    Log(Format('Optional XeCLI language configuration exited with code %d.', [ResultCode]));
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

function IsEqualPath(const Left, Right: String): Boolean;
begin
  Result := NormalizePathValue(Left) = NormalizePathValue(Right);
end;

function IsPathInside(const Candidate, Parent: String): Boolean;
var
  CandidatePath: String;
  ParentPath: String;
begin
  CandidatePath := AddBackslash(NormalizePathValue(Candidate));
  ParentPath := AddBackslash(NormalizePathValue(Parent));
  Result := (ParentPath <> '\') and (Pos(ParentPath, CandidatePath) = 1);
end;

function IsSafeInstallDir(const InstallDir: String): Boolean;
var
  NormalizedDir: String;
  DriveRoot: String;
begin
  Result := false;
  NormalizedDir := NormalizePathSegment(InstallDir);
  if NormalizedDir = '' then
    exit;

  DriveRoot := AddBackslash(ExtractFileDrive(NormalizedDir));
  if (DriveRoot = '\') or IsEqualPath(NormalizedDir, DriveRoot) then
    exit;
  if IsPathInside(NormalizedDir, ExpandConstant('{win}')) then
    exit;
  if IsEqualPath(NormalizedDir, Trim(GetEnv('USERPROFILE'))) or
     IsEqualPath(NormalizedDir, ExpandConstant('{userappdata}')) or
     IsEqualPath(NormalizedDir, ExpandConstant('{localappdata}')) or
     IsEqualPath(NormalizedDir, ExpandConstant('{autopf}')) then
    exit;
  if IsEqualPath(NormalizedDir, CurrentUserProfileDir) or
     IsEqualPath(NormalizedDir, CurrentUserAppDataDir) or
     IsEqualPath(NormalizedDir, CurrentUserLocalAppDataDir) then
    exit;

  Result := true;
end;

function TryCanonicalizeAbsolutePath(const Value: String; var CanonicalPath: String): Boolean;
var
  NormalizedPath: String;
begin
  Result := false;
  CanonicalPath := '';
  NormalizedPath := NormalizePathSegment(Value);
  if NormalizedPath = '' then
    exit;

  if not (((Length(NormalizedPath) >= 3) and
           (NormalizedPath[2] = ':') and
           (NormalizedPath[3] = '\')) or
          (Copy(NormalizedPath, 1, 2) = '\\')) then
    exit;

  CanonicalPath := NormalizePathSegment(ExpandFileName(NormalizedPath));
  Result := (CanonicalPath <> '') and (ExtractFileDrive(CanonicalPath) <> '');
end;

function ExistingRecordIdentityListContains(const RecordIdentities: TArrayOfString;
  const Identity: String): Boolean;
var
  I: Integer;
begin
  Result := false;
  for I := 0 to GetArrayLength(RecordIdentities) - 1 do
    if CompareText(RecordIdentities[I], Identity) = 0 then
    begin
      Result := true;
      exit;
    end;
end;

procedure ReadExistingInstallPathFromView(const RootKey: Integer;
  const RootScope: String; var ExistingInstallPaths,
  ExistingRecordIdentities: TArrayOfString; var Ambiguous: Boolean);
var
  InstallPath: String;
  CanonicalInstallPath: String;
  CandidateCanonicalPath: String;
  UninstallString: String;
  DisplayVersion: String;
  RecordIdentity: String;
  FoundPathValue: Boolean;
  RecordIndex: Integer;
begin
  if not RegKeyExists(RootKey, UninstallRegistryKey) then
    exit;

  FoundPathValue := false;
  CanonicalInstallPath := '';
  if RegValueExists(RootKey, UninstallRegistryKey,
    UninstallRegistryInstallLocationValueName) then
  begin
    FoundPathValue := true;
    if not RegQueryStringValue(RootKey, UninstallRegistryKey,
         UninstallRegistryInstallLocationValueName, InstallPath) or
       not TryCanonicalizeAbsolutePath(InstallPath, CandidateCanonicalPath) then
    begin
      Ambiguous := true;
      exit;
    end;
    CanonicalInstallPath := CandidateCanonicalPath;
  end;

  if RegValueExists(RootKey, UninstallRegistryKey,
    UninstallRegistryAppPathValueName) then
  begin
    FoundPathValue := true;
    if not RegQueryStringValue(RootKey, UninstallRegistryKey,
         UninstallRegistryAppPathValueName, InstallPath) or
       not TryCanonicalizeAbsolutePath(InstallPath, CandidateCanonicalPath) then
    begin
      Ambiguous := true;
      exit;
    end;
    if (CanonicalInstallPath <> '') and
       not IsEqualPath(CanonicalInstallPath, CandidateCanonicalPath) then
    begin
      Ambiguous := true;
      exit;
    end;
    CanonicalInstallPath := CandidateCanonicalPath;
  end;

  if not FoundPathValue then
  begin
    Ambiguous := true;
    exit;
  end;
  if not RegQueryStringValue(RootKey, UninstallRegistryKey,
       UninstallRegistryUninstallStringValueName, UninstallString) or
     (Trim(UninstallString) = '') or
     not RegQueryStringValue(RootKey, UninstallRegistryKey,
       UninstallRegistryDisplayVersionValueName, DisplayVersion) or
     (Trim(DisplayVersion) = '') then
  begin
    Ambiguous := true;
    exit;
  end;

  RecordIdentity := Lowercase(RootScope) + #1 +
    NormalizePathValue(CanonicalInstallPath) + #1 +
    Lowercase(Trim(UninstallString)) + #1 +
    Lowercase(Trim(DisplayVersion));
  if ExistingRecordIdentityListContains(ExistingRecordIdentities,
    RecordIdentity) then
    exit;

  RecordIndex := GetArrayLength(ExistingRecordIdentities);
  SetArrayLength(ExistingRecordIdentities, RecordIndex + 1);
  ExistingRecordIdentities[RecordIndex] := RecordIdentity;
  AddUniquePathEntry(ExistingInstallPaths, CanonicalInstallPath);
  if GetArrayLength(ExistingRecordIdentities) > 1 then
    Ambiguous := true;
end;

procedure ReadExistingInstallPaths(var ExistingInstallPaths: TArrayOfString;
  var Ambiguous: Boolean);
var
  ExistingRecordIdentities: TArrayOfString;
begin
  SetArrayLength(ExistingInstallPaths, 0);
  SetArrayLength(ExistingRecordIdentities, 0);
  Ambiguous := false;

  ReadExistingInstallPathFromView(HKCU32, 'HKCU', ExistingInstallPaths,
    ExistingRecordIdentities, Ambiguous);
  ReadExistingInstallPathFromView(HKLM32, 'HKLM32', ExistingInstallPaths,
    ExistingRecordIdentities, Ambiguous);
  if IsWin64 then
  begin
    ReadExistingInstallPathFromView(HKCU64, 'HKCU', ExistingInstallPaths,
      ExistingRecordIdentities, Ambiguous);
    ReadExistingInstallPathFromView(HKLM64, 'HKLM64', ExistingInstallPaths,
      ExistingRecordIdentities, Ambiguous);
  end;
end;

function IsCurrentUserInstallTarget(const InstallDir: String): Boolean;
var
  CurrentDir: String;
  UserProgramsDir: String;
begin
  CurrentDir := AddBackslash(NormalizePathValue(InstallDir));
  UserProgramsDir := AddBackslash(NormalizePathValue(ExpandConstant('{localappdata}\Programs')));
  Result := Pos(UserProgramsDir, CurrentDir) = 1;
end;

function ShouldUseMachinePathTarget(const CurrentInstallDir: String): Boolean;
begin
  Result := IsAdminInstallMode and not IsCurrentUserInstallTarget(CurrentInstallDir);
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

function IsMissingPathError(const ErrorCode: Integer): Boolean;
begin
  Result := (ErrorCode = ErrorFileNotFound) or
    (ErrorCode = ErrorPathNotFound);
end;

function TryValidateNoReparsePath(const PathValue, Context: String): Boolean;
var
  CurrentPath: String;
  ParentPath: String;
  Attributes: Integer;
  ErrorCode: Integer;
  InspectionFailed: Boolean;
  InspectionErrorCode: Integer;
  InspectionFailurePath: String;
begin
  Result := false;
  InspectionFailed := false;
  InspectionErrorCode := 0;
  InspectionFailurePath := '';
  CurrentPath := NormalizePathSegment(PathValue);
  while CurrentPath <> '' do
  begin
    Attributes := WindowsGetFileAttributes(CurrentPath);
    if Attributes = InvalidFileAttributes then
    begin
      ErrorCode := WindowsGetLastError;
      if not IsMissingPathError(ErrorCode) then
      begin
        if not InspectionFailed then
        begin
          InspectionFailed := true;
          InspectionErrorCode := ErrorCode;
          InspectionFailurePath := CurrentPath;
        end;
      end;
    end
    else if (Attributes and FileAttributeReparsePoint) <> 0 then
    begin
      Log(Format('Blocked XeCLI file operation because %s contains a reparse point: %s', [Context, CurrentPath]));
      exit;
    end;

    ParentPath := NormalizePathSegment(ExtractFileDir(CurrentPath));
    if (ParentPath = '') or IsEqualPath(ParentPath, CurrentPath) then
      break;
    CurrentPath := ParentPath;
  end;

  if InspectionFailed then
  begin
    Log(Format('Blocked XeCLI file operation because attributes for %s could not be inspected (error %d): %s', [Context, InspectionErrorCode, InspectionFailurePath]));
    exit;
  end;
  Result := true;
end;

function TryInspectInstallDestination(const InstallDir: String;
  var IsEmpty, HasPortableMarker,
  HasLegacyInstallMarkerOnly: Boolean): Boolean;
var
  Attributes: Integer;
  ErrorCode: Integer;
  EnumerationError: Integer;
  EntryCount: Integer;
  OnlyEntryName: String;
  MarkerPath: String;
  LegacyMarkerValue: AnsiString;
  FindRec: TFindRec;
begin
  Result := false;
  IsEmpty := false;
  HasPortableMarker := false;
  HasLegacyInstallMarkerOnly := false;

  if not TryValidateNoReparsePath(InstallDir,
    'the selected installation directory') then
    exit;

  Attributes := WindowsGetFileAttributes(InstallDir);
  if Attributes = InvalidFileAttributes then
  begin
    ErrorCode := WindowsGetLastError;
    if IsMissingPathError(ErrorCode) then
    begin
      IsEmpty := true;
      Result := true;
    end
    else
      Log(Format('Blocked XeCLI installation because the selected destination could not be inspected (error %d): %s', [ErrorCode, InstallDir]));
    exit;
  end;
  if (Attributes and FileAttributeDirectory) = 0 then
  begin
    Log(Format('Blocked XeCLI installation because the selected destination is not a directory: %s', [InstallDir]));
    exit;
  end;

  MarkerPath := NormalizePathSegment(
    AddBackslash(InstallDir) + PortableMarkerName);
  Attributes := WindowsGetFileAttributes(MarkerPath);
  if Attributes <> InvalidFileAttributes then
  begin
    HasPortableMarker := true;
    Result := true;
    exit;
  end;
  ErrorCode := WindowsGetLastError;
  if not IsMissingPathError(ErrorCode) then
  begin
    Log(Format('Blocked XeCLI installation because the portable marker path could not be inspected (error %d): %s', [ErrorCode, MarkerPath]));
    exit;
  end;

  EntryCount := 0;
  OnlyEntryName := '';
  if FindFirst(AddBackslash(InstallDir) + '*', FindRec) then
  begin
    EnumerationError := ErrorNoMoreFiles;
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          EntryCount := EntryCount + 1;
          if EntryCount = 1 then
            OnlyEntryName := FindRec.Name;
        end;
        if not FindNext(FindRec) then
        begin
          EnumerationError := WindowsGetLastError;
          break;
        end;
      until false;
    finally
      FindClose(FindRec);
    end;

    if EnumerationError <> ErrorNoMoreFiles then
    begin
      Log(Format('Blocked XeCLI installation because the selected destination could not be enumerated completely (error %d): %s', [EnumerationError, InstallDir]));
      exit;
    end;
  end
  else
  begin
    ErrorCode := WindowsGetLastError;
    if (ErrorCode <> ErrorFileNotFound) and
       (ErrorCode <> ErrorNoMoreFiles) then
    begin
      Log(Format('Blocked XeCLI installation because the selected destination could not be enumerated (error %d): %s', [ErrorCode, InstallDir]));
      exit;
    end;
  end;

  IsEmpty := EntryCount = 0;
  if (EntryCount = 1) and
     (CompareText(OnlyEntryName, LegacyInstallMarkerName) = 0) then
  begin
    MarkerPath := NormalizePathSegment(
      AddBackslash(InstallDir) + LegacyInstallMarkerName);
    Attributes := WindowsGetFileAttributes(MarkerPath);
    if (Attributes <> InvalidFileAttributes) and
       ((Attributes and FileAttributeDirectory) = 0) and
       ((Attributes and FileAttributeReparsePoint) = 0) and
       LoadStringFromFile(MarkerPath, LegacyMarkerValue) then
      HasLegacyInstallMarkerOnly :=
        (LegacyMarkerValue = LegacyInstallMarkerValueV1) or
        (LegacyMarkerValue = LegacyInstallMarkerValueX64) or
        (LegacyMarkerValue = LegacyInstallMarkerValueX86);
  end;

  Result := true;
end;

function HasInvalidOwnedPathCharacter(const Value: String): Boolean;
var
  I: Integer;
  CharacterCode: Integer;
begin
  Result := (Pos('*', Value) > 0) or
    (Pos('?', Value) > 0) or
    (Pos('"', Value) > 0) or
    (Pos('<', Value) > 0) or
    (Pos('>', Value) > 0) or
    (Pos('|', Value) > 0) or
    (Pos(':', Value) > 0) or
    (Pos('\', Value) > 0);
  if Result then
    exit;

  for I := 1 to Length(Value) do
  begin
    CharacterCode := Ord(Value[I]);
    if (CharacterCode < 32) or (CharacterCode = 127) then
    begin
      Result := true;
      exit;
    end;
  end;
end;

function TryNormalizeOwnedRelativePath(const RawPath: String;
  var NormalizedPath: String): Boolean;
var
  Remaining: String;
  Segment: String;
  SeparatorIndex: Integer;
begin
  Result := false;
  NormalizedPath := '';
  if (RawPath = '') or (RawPath <> Trim(RawPath)) or
     (RawPath[1] = '/') or (Copy(RawPath, 1, 2) = '//') or
     HasInvalidOwnedPathCharacter(RawPath) then
    exit;

  Remaining := RawPath;
  while Remaining <> '' do
  begin
    SeparatorIndex := Pos('/', Remaining);
    if SeparatorIndex = 0 then
    begin
      Segment := Remaining;
      Remaining := '';
    end
    else
    begin
      Segment := Copy(Remaining, 1, SeparatorIndex - 1);
      Delete(Remaining, 1, SeparatorIndex);
    end;

    if (Segment = '') or (Segment = '.') or (Segment = '..') or
       (Segment[Length(Segment)] = '.') or
       (Segment[Length(Segment)] = ' ') then
      exit;

    if NormalizedPath <> '' then
      NormalizedPath := NormalizedPath + '\';
    NormalizedPath := NormalizedPath + Segment;
  end;

  Result := NormalizedPath <> '';
end;

function IsProtectedInstallStateRelativePath(
  const RelativePath: String): Boolean;
begin
  Result :=
    (CompareText(RelativePath, PortableMarkerName) = 0) or
    (CompareText(RelativePath, PortableUserDataDirectoryName) = 0) or
    (CompareText(Copy(RelativePath, 1,
      Length(PortableUserDataDirectoryName) + 1),
      PortableUserDataDirectoryName + '\') = 0) or
    (CompareText(RelativePath, LegacyLocalLogsDirectoryName) = 0) or
    (CompareText(Copy(RelativePath, 1,
      Length(LegacyLocalLogsDirectoryName) + 1),
      LegacyLocalLogsDirectoryName + '\') = 0);
end;

procedure AddOwnedParentDirectories(const TargetPath, CanonicalApp: String;
  var ParentDirectories: TArrayOfString);
var
  ParentPath: String;
begin
  ParentPath := NormalizePathSegment(ExtractFileDir(TargetPath));
  while (ParentPath <> '') and not IsEqualPath(ParentPath, CanonicalApp) and
    IsPathInside(ParentPath, CanonicalApp) do
  begin
    AddUniquePathEntry(ParentDirectories, ParentPath);
    ParentPath := NormalizePathSegment(ExtractFileDir(ParentPath));
  end;
end;

procedure SortPathsDeepestFirst(var Paths: TArrayOfString);
var
  I: Integer;
  J: Integer;
  PathCount: Integer;
  Temporary: String;
begin
  PathCount := GetArrayLength(Paths);
  if PathCount < 2 then
    exit;

  for I := 0 to PathCount - 2 do
    for J := I + 1 to PathCount - 1 do
      if (Length(Paths[J]) > Length(Paths[I])) or
         ((Length(Paths[J]) = Length(Paths[I])) and
          (CompareText(Paths[J], Paths[I]) > 0)) then
      begin
        Temporary := Paths[I];
        Paths[I] := Paths[J];
        Paths[J] := Temporary;
      end;
end;

function TryLoadOwnedFileInventory(const CanonicalApp, InventoryPath: String;
  const RequireFilesPresent, RequireReleaseManifest: Boolean;
  var OwnedFiles, ParentDirectories: TArrayOfString;
  var OwnedManifestIndex, ReleaseManifestIndex: Integer): Boolean;
var
  ManifestPath: String;
  SourceInventoryPath: String;
  RawPaths: TArrayOfString;
  NormalizedRelativePath: String;
  ExpectedTargetPath: String;
  CanonicalTargetPath: String;
  Attributes: Integer;
  ErrorCode: Integer;
  I: Integer;
  TargetIndex: Integer;
begin
  Result := false;
  SetArrayLength(OwnedFiles, 0);
  SetArrayLength(ParentDirectories, 0);
  OwnedManifestIndex := -1;
  ReleaseManifestIndex := -1;
  ManifestPath := NormalizePathSegment(AddBackslash(CanonicalApp) + OwnedFilesManifestName);
  SourceInventoryPath := NormalizePathSegment(InventoryPath);

  if not TryValidateNoReparsePath(SourceInventoryPath,
    'the ownership inventory path') then
    exit;
  Attributes := WindowsGetFileAttributes(SourceInventoryPath);
  if Attributes = InvalidFileAttributes then
  begin
    ErrorCode := WindowsGetLastError;
    Log(Format('Blocked XeCLI upgrade because an ownership inventory cannot be read (error %d): %s', [ErrorCode, SourceInventoryPath]));
    exit;
  end;
  if ((Attributes and FileAttributeDirectory) <> 0) or
     ((Attributes and FileAttributeReparsePoint) <> 0) then
  begin
    Log(Format('Blocked XeCLI upgrade because an ownership inventory is not a regular file: %s', [SourceInventoryPath]));
    exit;
  end;
  if not LoadStringsFromFile(SourceInventoryPath, RawPaths) or
     (GetArrayLength(RawPaths) = 0) then
  begin
    Log(Format('Blocked XeCLI upgrade because an ownership inventory is empty or unreadable: %s', [SourceInventoryPath]));
    exit;
  end;

  for I := 0 to GetArrayLength(RawPaths) - 1 do
  begin
    if not TryNormalizeOwnedRelativePath(RawPaths[I], NormalizedRelativePath) then
    begin
      Log(Format('Blocked XeCLI upgrade because ownership manifest line %d is not a safe relative path.', [I + 1]));
      exit;
    end;
    if (CompareText(NormalizedRelativePath,
         PreviousOwnedFilesManifestName) = 0) or
       (CompareText(NormalizedRelativePath,
         PreviousOwnedFilesManifestTempName) = 0) then
    begin
      Log(Format('Blocked XeCLI upgrade because ownership manifest line %d names reserved upgrade evidence.', [I + 1]));
      exit;
    end;
    if IsProtectedInstallStateRelativePath(NormalizedRelativePath) then
    begin
      Log(Format('Blocked XeCLI upgrade because ownership manifest line %d names protected portable or user state.', [I + 1]));
      exit;
    end;

    ExpectedTargetPath := NormalizePathSegment(
      AddBackslash(CanonicalApp) + NormalizedRelativePath);
    if not TryCanonicalizeAbsolutePath(ExpectedTargetPath, CanonicalTargetPath) or
       IsEqualPath(CanonicalTargetPath, CanonicalApp) or
       not IsPathInside(CanonicalTargetPath, CanonicalApp) or
       not IsEqualPath(ExpectedTargetPath, CanonicalTargetPath) then
    begin
      Log(Format('Blocked XeCLI upgrade because ownership manifest line %d does not resolve strictly beneath the install directory.', [I + 1]));
      exit;
    end;
    if PathEntryListContains(OwnedFiles, CanonicalTargetPath) then
    begin
      Log(Format('Blocked XeCLI upgrade because ownership manifest line %d duplicates another target.', [I + 1]));
      exit;
    end;
    if not TryValidateNoReparsePath(CanonicalTargetPath,
      Format('ownership manifest line %d', [I + 1])) then
      exit;

    Attributes := WindowsGetFileAttributes(CanonicalTargetPath);
    if Attributes <> InvalidFileAttributes then
    begin
      if (Attributes and FileAttributeDirectory) <> 0 then
      begin
        Log(Format('Blocked XeCLI upgrade because ownership manifest line %d names a directory.', [I + 1]));
        exit;
      end;
    end
    else
    begin
      ErrorCode := WindowsGetLastError;
      if RequireFilesPresent and IsMissingPathError(ErrorCode) then
      begin
        Log(Format('Blocked XeCLI upgrade because newly installed ownership manifest line %d is missing: %s', [I + 1, CanonicalTargetPath]));
        exit;
      end
      else if not IsMissingPathError(ErrorCode) then
      begin
        Log(Format('Blocked XeCLI upgrade because ownership manifest line %d cannot be inspected (error %d).', [I + 1, ErrorCode]));
        exit;
      end;
    end;

    TargetIndex := GetArrayLength(OwnedFiles);
    SetArrayLength(OwnedFiles, TargetIndex + 1);
    OwnedFiles[TargetIndex] := CanonicalTargetPath;
    AddOwnedParentDirectories(CanonicalTargetPath, CanonicalApp,
      ParentDirectories);
    if IsEqualPath(CanonicalTargetPath, ManifestPath) then
      OwnedManifestIndex := TargetIndex;
    if IsEqualPath(CanonicalTargetPath,
      NormalizePathSegment(AddBackslash(CanonicalApp) + ReleaseManifestName)) then
      ReleaseManifestIndex := TargetIndex;
  end;

  if OwnedManifestIndex < 0 then
  begin
    Log('Blocked XeCLI upgrade because the ownership manifest does not own itself.');
    exit;
  end;
  if RequireReleaseManifest and (ReleaseManifestIndex < 0) then
  begin
    Log('Blocked XeCLI upgrade because the new ownership manifest does not own release-manifest.json.');
    exit;
  end;

  SortPathsDeepestFirst(ParentDirectories);
  Result := true;
end;

function IsSha256Hex(const Value: String): Boolean;
var
  I: Integer;
begin
  Result := Length(Value) = 64;
  if not Result then
    exit;
  for I := 1 to Length(Value) do
    if Pos(Lowercase(Value[I]), '0123456789abcdef') = 0 then
    begin
      Result := false;
      exit;
    end;
end;

function TryParseOwnedHashRecord(const HashRecord: String;
  var ExpectedHash, RelativePath: String): Boolean;
begin
  Result := false;
  ExpectedHash := '';
  RelativePath := '';
  if (Length(HashRecord) < 67) or
     (HashRecord[65] <> ' ') or (HashRecord[66] <> '*') then
    exit;

  ExpectedHash := Copy(HashRecord, 1, 64);
  RelativePath := Copy(HashRecord, 67, Length(HashRecord) - 66);
  Result := IsSha256Hex(ExpectedHash) and (RelativePath <> '');
end;

function TryValidateOwnedFileHashes(const CanonicalApp: String;
  const NewOwnedFiles: TArrayOfString): Boolean;
var
  HashManifestPath: String;
  RawHashRecords: TArrayOfString;
  HashedFiles: TArrayOfString;
  ExpectedHashes: TArrayOfString;
  ExpectedHash: String;
  ActualHash: String;
  RawRelativePath: String;
  NormalizedRelativePath: String;
  ExpectedTargetPath: String;
  CanonicalTargetPath: String;
  Attributes: Integer;
  ErrorCode: Integer;
  I: Integer;
  TargetIndex: Integer;
begin
  Result := false;
  SetArrayLength(HashedFiles, 0);
  SetArrayLength(ExpectedHashes, 0);
  HashManifestPath := NormalizePathSegment(
    AddBackslash(CanonicalApp) + OwnedHashesManifestName);
  if not PathEntryListContains(NewOwnedFiles, HashManifestPath) then
  begin
    Log('Blocked XeCLI post-install validation because the ownership manifest does not own the hash inventory.');
    exit;
  end;
  if not TryValidateNoReparsePath(HashManifestPath,
    'the owned-file hash inventory path') then
    exit;

  Attributes := WindowsGetFileAttributes(HashManifestPath);
  if Attributes = InvalidFileAttributes then
  begin
    ErrorCode := WindowsGetLastError;
    Log(Format('Blocked XeCLI post-install validation because the hash inventory cannot be read (error %d): %s', [ErrorCode, HashManifestPath]));
    exit;
  end;
  if ((Attributes and FileAttributeDirectory) <> 0) or
     ((Attributes and FileAttributeReparsePoint) <> 0) then
  begin
    Log(Format('Blocked XeCLI post-install validation because the hash inventory is not a regular file: %s', [HashManifestPath]));
    exit;
  end;
  if not LoadStringsFromFile(HashManifestPath, RawHashRecords) or
     (GetArrayLength(RawHashRecords) = 0) then
  begin
    Log(Format('Blocked XeCLI post-install validation because the hash inventory is empty or unreadable: %s', [HashManifestPath]));
    exit;
  end;

  for I := 0 to GetArrayLength(RawHashRecords) - 1 do
  begin
    if not TryParseOwnedHashRecord(RawHashRecords[I], ExpectedHash,
      RawRelativePath) or
       not TryNormalizeOwnedRelativePath(RawRelativePath,
         NormalizedRelativePath) then
    begin
      Log(Format('Blocked XeCLI post-install validation because hash inventory line %d is malformed or is not a safe relative path.', [I + 1]));
      exit;
    end;
    if (CompareText(NormalizedRelativePath, OwnedHashesManifestName) = 0) or
       (CompareText(NormalizedRelativePath,
          PreviousOwnedFilesManifestName) = 0) or
       (CompareText(NormalizedRelativePath,
          PreviousOwnedFilesManifestTempName) = 0) then
    begin
      Log(Format('Blocked XeCLI post-install validation because hash inventory line %d names excluded metadata.', [I + 1]));
      exit;
    end;

    ExpectedTargetPath := NormalizePathSegment(
      AddBackslash(CanonicalApp) + NormalizedRelativePath);
    if not TryCanonicalizeAbsolutePath(ExpectedTargetPath,
         CanonicalTargetPath) or
       IsEqualPath(CanonicalTargetPath, CanonicalApp) or
       not IsPathInside(CanonicalTargetPath, CanonicalApp) or
       not IsEqualPath(ExpectedTargetPath, CanonicalTargetPath) then
    begin
      Log(Format('Blocked XeCLI post-install validation because hash inventory line %d does not resolve strictly beneath the install directory.', [I + 1]));
      exit;
    end;
    if PathEntryListContains(HashedFiles, CanonicalTargetPath) then
    begin
      Log(Format('Blocked XeCLI post-install validation because hash inventory line %d duplicates another target.', [I + 1]));
      exit;
    end;
    if not TryValidateNoReparsePath(CanonicalTargetPath,
      Format('hash inventory line %d', [I + 1])) then
      exit;

    Attributes := WindowsGetFileAttributes(CanonicalTargetPath);
    if Attributes = InvalidFileAttributes then
    begin
      ErrorCode := WindowsGetLastError;
      Log(Format('Blocked XeCLI post-install validation because a current owned file is missing or unreadable (error %d): %s', [ErrorCode, CanonicalTargetPath]));
      exit;
    end;
    if ((Attributes and FileAttributeDirectory) <> 0) or
       ((Attributes and FileAttributeReparsePoint) <> 0) then
    begin
      Log(Format('Blocked XeCLI post-install validation because a current owned target is not a regular file: %s', [CanonicalTargetPath]));
      exit;
    end;

    TargetIndex := GetArrayLength(HashedFiles);
    SetArrayLength(HashedFiles, TargetIndex + 1);
    SetArrayLength(ExpectedHashes, TargetIndex + 1);
    HashedFiles[TargetIndex] := CanonicalTargetPath;
    ExpectedHashes[TargetIndex] := ExpectedHash;
  end;

  if GetArrayLength(HashedFiles) <> GetArrayLength(NewOwnedFiles) - 1 then
  begin
    Log('Blocked XeCLI post-install validation because the hash and ownership inventory counts do not have exact parity.');
    exit;
  end;
  for I := 0 to GetArrayLength(NewOwnedFiles) - 1 do
    if not IsEqualPath(NewOwnedFiles[I], HashManifestPath) and
       not PathEntryListContains(HashedFiles, NewOwnedFiles[I]) then
    begin
      Log(Format('Blocked XeCLI post-install validation because an owned file is absent from the hash inventory: %s', [NewOwnedFiles[I]]));
      exit;
    end;
  for I := 0 to GetArrayLength(HashedFiles) - 1 do
    if not PathEntryListContains(NewOwnedFiles, HashedFiles[I]) then
    begin
      Log(Format('Blocked XeCLI post-install validation because the hash inventory names an unowned file: %s', [HashedFiles[I]]));
      exit;
    end;

  for I := 0 to GetArrayLength(HashedFiles) - 1 do
  begin
    try
      ActualHash := GetSHA256OfFile(HashedFiles[I]);
    except
      Log(Format('Blocked XeCLI post-install validation because an owned file could not be hashed: %s (%s)', [HashedFiles[I], GetExceptionMessage]));
      exit;
    end;
    if CompareText(ActualHash, ExpectedHashes[I]) <> 0 then
    begin
      Log(Format('Blocked XeCLI stale-file cleanup because a current owned file failed SHA256 validation: %s', [HashedFiles[I]]));
      exit;
    end;
  end;

  Result := true;
end;

function DeleteOwnedFile(const FilePath: String): Boolean;
var
  Attributes: Integer;
  ErrorCode: Integer;
begin
  Result := false;
  if not TryValidateNoReparsePath(FilePath, 'an owned file selected for deletion') then
    exit;

  Attributes := WindowsGetFileAttributes(FilePath);
  if Attributes = InvalidFileAttributes then
  begin
    ErrorCode := WindowsGetLastError;
    if IsMissingPathError(ErrorCode) then
      Result := true
    else
      Log(Format('Unable to inspect an owned XeCLI file before deletion (error %d): %s', [ErrorCode, FilePath]));
    exit;
  end;
  if ((Attributes and FileAttributeDirectory) <> 0) or
     ((Attributes and FileAttributeReparsePoint) <> 0) then
  begin
    Log(Format('Refusing to delete an owned XeCLI path that is no longer a regular file: %s', [FilePath]));
    exit;
  end;

  if WindowsDeleteFile(FilePath) then
  begin
    Log(Format('Removed installer-owned XeCLI file: %s', [FilePath]));
    Result := true;
  end
  else
  begin
    ErrorCode := WindowsGetLastError;
    if IsMissingPathError(ErrorCode) then
      Result := true
    else
      Log(Format('Unable to remove installer-owned XeCLI file (error %d): %s', [ErrorCode, FilePath]));
  end;
end;

function RemoveEmptyOwnedParentDirectory(const DirectoryPath: String): Boolean;
var
  ErrorCode: Integer;
begin
  Result := false;
  if not TryValidateNoReparsePath(DirectoryPath,
    'an owned parent directory selected for removal') then
    exit;

  if WindowsRemoveDirectory(DirectoryPath) then
  begin
    Log(Format('Removed empty installer-owned XeCLI directory: %s', [DirectoryPath]));
    Result := true;
    exit;
  end;

  ErrorCode := WindowsGetLastError;
  if IsMissingPathError(ErrorCode) then
    Result := true
  else if ErrorCode = ErrorDirNotEmpty then
  begin
    Log(Format('Retained non-empty XeCLI directory containing unlisted files: %s', [DirectoryPath]));
    Result := true;
  end
  else
    Log(Format('Unable to remove an empty installer-owned XeCLI directory (error %d): %s', [ErrorCode, DirectoryPath]));
end;

procedure ResetPreparedUpgradeState;
begin
  PreparedUpgrade := false;
  PreparedUpgradeInstallDir := '';
  SetArrayLength(PreparedOldOwnedFiles, 0);
  SetArrayLength(PreparedOldParentDirectories, 0);
end;

procedure MergePathEntries(const SourcePaths: TArrayOfString;
  var DestinationPaths: TArrayOfString);
var
  I: Integer;
begin
  for I := 0 to GetArrayLength(SourcePaths) - 1 do
    AddUniquePathEntry(DestinationPaths, SourcePaths[I]);
end;

procedure RebuildOwnedParentDirectories(const CanonicalApp: String;
  const OwnedFiles: TArrayOfString; var ParentDirectories: TArrayOfString);
var
  I: Integer;
begin
  SetArrayLength(ParentDirectories, 0);
  for I := 0 to GetArrayLength(OwnedFiles) - 1 do
    AddOwnedParentDirectories(OwnedFiles[I], CanonicalApp,
      ParentDirectories);
  SortPathsDeepestFirst(ParentDirectories);
end;

function BuildOwnedInventoryEvidence(const CanonicalApp: String;
  const OwnedFiles: TArrayOfString; var EvidenceText: String): Boolean;
var
  AppPrefix: String;
  RelativePath: String;
  NormalizedRelativePath: String;
  I: Integer;
begin
  Result := false;
  EvidenceText := '';
  AppPrefix := AddBackslash(NormalizePathSegment(CanonicalApp));
  for I := 0 to GetArrayLength(OwnedFiles) - 1 do
  begin
    if not IsPathInside(OwnedFiles[I], CanonicalApp) then
    begin
      Log('Blocked XeCLI upgrade because prepared ownership evidence escaped the install directory.');
      exit;
    end;

    RelativePath := Copy(OwnedFiles[I], Length(AppPrefix) + 1,
      Length(OwnedFiles[I]) - Length(AppPrefix));
    StringChangeEx(RelativePath, '\', '/', True);
    if not TryNormalizeOwnedRelativePath(RelativePath,
      NormalizedRelativePath) then
    begin
      Log('Blocked XeCLI upgrade because prepared ownership evidence could not be serialized safely.');
      exit;
    end;
    EvidenceText := EvidenceText + RelativePath + #10;
  end;

  Result := EvidenceText <> '';
end;

function TryMergeOwnedInventoryFile(const CanonicalApp, InventoryPath: String;
  var OwnedFiles: TArrayOfString): Boolean;
var
  Attributes: Integer;
  ErrorCode: Integer;
  InventoryFiles: TArrayOfString;
  ParentDirectories: TArrayOfString;
  OwnedManifestIndex: Integer;
  ReleaseManifestIndex: Integer;
begin
  Result := false;
  if not TryValidateNoReparsePath(InventoryPath,
    'an ownership evidence path') then
    exit;

  Attributes := WindowsGetFileAttributes(InventoryPath);
  if Attributes = InvalidFileAttributes then
  begin
    ErrorCode := WindowsGetLastError;
    if IsMissingPathError(ErrorCode) then
      Result := true
    else
      Log(Format('Blocked XeCLI upgrade because ownership evidence could not be inspected (error %d): %s', [ErrorCode, InventoryPath]));
    exit;
  end;

  if not TryLoadOwnedFileInventory(CanonicalApp, InventoryPath, false,
    false, InventoryFiles, ParentDirectories, OwnedManifestIndex,
    ReleaseManifestIndex) then
    exit;
  MergePathEntries(InventoryFiles, OwnedFiles);
  Result := true;
end;

function PathEntryListsMatch(const LeftPaths,
  RightPaths: TArrayOfString): Boolean;
var
  I: Integer;
begin
  Result := false;
  if GetArrayLength(LeftPaths) <> GetArrayLength(RightPaths) then
    exit;
  for I := 0 to GetArrayLength(LeftPaths) - 1 do
    if not PathEntryListContains(RightPaths, LeftPaths[I]) then
      exit;
  Result := true;
end;

function TryPrepareExistingOwnedFiles(const CanonicalApp: String): Boolean;
var
  CurrentManifestPath: String;
  EvidencePath: String;
  TempEvidencePath: String;
  EvidenceText: String;
  CurrentOwnedFiles: TArrayOfString;
  CurrentParentDirectories: TArrayOfString;
  ValidatedEvidenceFiles: TArrayOfString;
  ValidatedEvidenceParents: TArrayOfString;
  OwnedManifestIndex: Integer;
  ReleaseManifestIndex: Integer;
  Attributes: Integer;
  ErrorCode: Integer;
begin
  Result := false;
  if not TryValidateNoReparsePath(CanonicalApp,
    'the canonical install directory') then
    exit;

  CurrentManifestPath := NormalizePathSegment(
    AddBackslash(CanonicalApp) + OwnedFilesManifestName);
  EvidencePath := NormalizePathSegment(
    AddBackslash(CanonicalApp) + PreviousOwnedFilesManifestName);
  TempEvidencePath := NormalizePathSegment(
    AddBackslash(CanonicalApp) + PreviousOwnedFilesManifestTempName);

  if not TryLoadOwnedFileInventory(CanonicalApp, CurrentManifestPath,
    false, false, CurrentOwnedFiles, CurrentParentDirectories,
    OwnedManifestIndex, ReleaseManifestIndex) then
    exit;
  MergePathEntries(CurrentOwnedFiles, PreparedOldOwnedFiles);
  if not TryMergeOwnedInventoryFile(CanonicalApp, EvidencePath,
       PreparedOldOwnedFiles) or
     not TryMergeOwnedInventoryFile(CanonicalApp, TempEvidencePath,
       PreparedOldOwnedFiles) then
    exit;
  RebuildOwnedParentDirectories(CanonicalApp, PreparedOldOwnedFiles,
    PreparedOldParentDirectories);

  if not BuildOwnedInventoryEvidence(CanonicalApp, PreparedOldOwnedFiles,
    EvidenceText) then
    exit;
  if not TryValidateNoReparsePath(TempEvidencePath,
    'the temporary ownership evidence path') then
    exit;
  Attributes := WindowsGetFileAttributes(TempEvidencePath);
  if (Attributes <> InvalidFileAttributes) and
     (((Attributes and FileAttributeDirectory) <> 0) or
      ((Attributes and FileAttributeReparsePoint) <> 0)) then
  begin
    Log(Format('Blocked XeCLI upgrade because temporary ownership evidence is not a regular file: %s', [TempEvidencePath]));
    exit;
  end;
  if (Attributes = InvalidFileAttributes) then
  begin
    ErrorCode := WindowsGetLastError;
    if not IsMissingPathError(ErrorCode) then
    begin
      Log(Format('Blocked XeCLI upgrade because temporary ownership evidence could not be inspected (error %d): %s', [ErrorCode, TempEvidencePath]));
      exit;
    end;
  end;

  if not SaveStringToFile(TempEvidencePath, EvidenceText, false) then
  begin
    Log(Format('Blocked XeCLI upgrade because ownership evidence could not be written: %s', [TempEvidencePath]));
    exit;
  end;
  if not TryLoadOwnedFileInventory(CanonicalApp, TempEvidencePath,
       false, false, ValidatedEvidenceFiles, ValidatedEvidenceParents,
       OwnedManifestIndex, ReleaseManifestIndex) or
     not PathEntryListsMatch(PreparedOldOwnedFiles,
       ValidatedEvidenceFiles) then
  begin
    Log('Blocked XeCLI upgrade because stored ownership evidence did not match the prepared inventory.');
    exit;
  end;

  if not WindowsMoveFileEx(TempEvidencePath, EvidencePath,
    MoveFileReplaceExisting or MoveFileWriteThrough) then
  begin
    ErrorCode := WindowsGetLastError;
    Log(Format('Blocked XeCLI upgrade because ownership evidence could not be committed (error %d): %s', [ErrorCode, EvidencePath]));
    exit;
  end;
  if not TryLoadOwnedFileInventory(CanonicalApp, EvidencePath,
       false, false, ValidatedEvidenceFiles, ValidatedEvidenceParents,
       OwnedManifestIndex, ReleaseManifestIndex) or
     not PathEntryListsMatch(PreparedOldOwnedFiles,
       ValidatedEvidenceFiles) then
  begin
    Log('Blocked XeCLI upgrade because committed ownership evidence could not be validated.');
    exit;
  end;

  PreparedUpgradeInstallDir := CanonicalApp;
  PreparedUpgrade := true;
  Result := true;
end;

function DeleteUpgradeEvidenceFile(const FilePath: String): Boolean;
var
  Attributes: Integer;
  ErrorCode: Integer;
begin
  Result := false;
  if not TryValidateNoReparsePath(FilePath,
    'an ownership evidence file selected for deletion') then
    exit;
  Attributes := WindowsGetFileAttributes(FilePath);
  if Attributes = InvalidFileAttributes then
  begin
    ErrorCode := WindowsGetLastError;
    if IsMissingPathError(ErrorCode) then
      Result := true
    else
      Log(Format('Unable to inspect XeCLI ownership evidence before deletion (error %d): %s', [ErrorCode, FilePath]));
    exit;
  end;
  if ((Attributes and FileAttributeDirectory) <> 0) or
     ((Attributes and FileAttributeReparsePoint) <> 0) then
  begin
    Log(Format('Refusing to delete XeCLI ownership evidence that is not a regular file: %s', [FilePath]));
    exit;
  end;
  if WindowsDeleteFile(FilePath) then
    Result := true
  else
  begin
    ErrorCode := WindowsGetLastError;
    if IsMissingPathError(ErrorCode) then
      Result := true
    else
      Log(Format('Unable to remove XeCLI ownership evidence (error %d): %s', [ErrorCode, FilePath]));
  end;
end;

function RemoveStaleOwnedFiles(const CanonicalApp: String;
  const NewOwnedFiles: TArrayOfString): Boolean;
var
  StaleFiles: TArrayOfString;
  StaleParentDirectories: TArrayOfString;
  EvidencePath: String;
  TempEvidencePath: String;
  I: Integer;
  StaleIndex: Integer;
begin
  Result := false;
  SetArrayLength(StaleFiles, 0);
  SetArrayLength(StaleParentDirectories, 0);
  for I := 0 to GetArrayLength(PreparedOldOwnedFiles) - 1 do
  begin
    if PathEntryListContains(NewOwnedFiles, PreparedOldOwnedFiles[I]) then
      continue;
    StaleIndex := GetArrayLength(StaleFiles);
    SetArrayLength(StaleFiles, StaleIndex + 1);
    StaleFiles[StaleIndex] := PreparedOldOwnedFiles[I];
    AddOwnedParentDirectories(PreparedOldOwnedFiles[I], CanonicalApp,
      StaleParentDirectories);
  end;
  SortPathsDeepestFirst(StaleParentDirectories);

  for I := 0 to GetArrayLength(StaleFiles) - 1 do
    if not DeleteOwnedFile(StaleFiles[I]) then
      exit;
  for I := 0 to GetArrayLength(StaleParentDirectories) - 1 do
    if not RemoveEmptyOwnedParentDirectory(StaleParentDirectories[I]) then
      exit;

  EvidencePath := NormalizePathSegment(
    AddBackslash(CanonicalApp) + PreviousOwnedFilesManifestName);
  TempEvidencePath := NormalizePathSegment(
    AddBackslash(CanonicalApp) + PreviousOwnedFilesManifestTempName);
  if not DeleteUpgradeEvidenceFile(EvidencePath) or
     not DeleteUpgradeEvidenceFile(TempEvidencePath) then
    exit;

  ResetPreparedUpgradeState;
  Result := true;
end;

function CompletePreparedOwnedFileUpgrade(const InstallDir: String): Boolean;
var
  CanonicalApp: String;
  NewOwnedFiles: TArrayOfString;
  NewParentDirectories: TArrayOfString;
  OwnedManifestIndex: Integer;
  ReleaseManifestIndex: Integer;
  ManifestPath: String;
begin
  Result := false;
  if not TryCanonicalizeAbsolutePath(InstallDir, CanonicalApp) then
  begin
    Log('Blocked XeCLI post-install validation because the install directory is not canonical.');
    exit;
  end;
  if PreparedUpgrade and
     not IsEqualPath(CanonicalApp, PreparedUpgradeInstallDir) then
  begin
    Log('Blocked XeCLI stale-file cleanup because the post-install directory differs from the prepared upgrade directory.');
    exit;
  end;

  ManifestPath := NormalizePathSegment(
    AddBackslash(CanonicalApp) + OwnedFilesManifestName);
  if not TryLoadOwnedFileInventory(CanonicalApp, ManifestPath, true,
       true, NewOwnedFiles, NewParentDirectories, OwnedManifestIndex,
       ReleaseManifestIndex) then
    exit;
  if not TryValidateOwnedFileHashes(CanonicalApp, NewOwnedFiles) then
    exit;
  if PreparedUpgrade then
    Result := RemoveStaleOwnedFiles(CanonicalApp, NewOwnedFiles)
  else
    Result := true;
end;

function InitializeSetup: Boolean;
begin
  PreparedUpgradeCleanupFailed := false;
  ResetPreparedUpgradeState;
  ResolveCurrentUserPaths;
  Result := true;
#if TargetRuntime == "win-x86"
  if IsWin64 and
    (CompareText(ExpandConstant('{param:FORCEX86|0}'), '1') <> 0) and
    (CompareText(ExpandConstant('{param:FORCEX86|0}'), 'true') <> 0) then
  begin
    Log('Blocked the legacy 32-bit XeCLI installer on 64-bit Windows.');
    if not WizardSilent then
      MsgBox('This is the legacy 32-bit XeCLI installer. Use the win-x64 installer on 64-bit Windows, or pass /FORCEX86=1 only for compatibility testing.', mbError, MB_OK);
    Result := false;
  end;
#endif
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingInstallPaths: TArrayOfString;
  ExistingInstallStateAmbiguous: Boolean;
  TargetInstallDir: String;
  DestinationIsEmpty: Boolean;
  HasPortableMarker: Boolean;
  HasLegacyInstallMarkerOnly: Boolean;
begin
  Result := '';
  ResetPreparedUpgradeState;
  if not TryCanonicalizeAbsolutePath(WizardDirValue, TargetInstallDir) or
     not IsSafeInstallDir(TargetInstallDir) then
  begin
    Result := ExpandConstant('{cm:UnsafeInstallDir}');
    exit;
  end;

  if not TryInspectInstallDestination(TargetInstallDir,
       DestinationIsEmpty, HasPortableMarker,
       HasLegacyInstallMarkerOnly) then
  begin
    Result := ExpandConstant('{cm:InstallDestinationInspectionFailed}');
    exit;
  end;
  if HasPortableMarker then
  begin
    Log('Blocked XeCLI installation because the selected destination contains xecli.portable. Portable UserData was left untouched.');
    Result := ExpandConstant('{cm:InstallPortableDestinationBlocked}');
    exit;
  end;

  ReadExistingInstallPaths(ExistingInstallPaths,
    ExistingInstallStateAmbiguous);
  if ExistingInstallStateAmbiguous or
     (GetArrayLength(ExistingInstallPaths) > 1) or
     ((GetArrayLength(ExistingInstallPaths) = 1) and
      not IsEqualPath(ExistingInstallPaths[0], TargetInstallDir)) then
  begin
    Log('Blocked XeCLI installation relocation because the existing install location is different or ambiguous.');
    Result := ExpandConstant('{cm:InstallRelocationBlocked}');
    exit;
  end;

  if GetArrayLength(ExistingInstallPaths) = 1 then
  begin
    if not FileExists(AddBackslash(TargetInstallDir) + OwnedFilesManifestName) then
    begin
      Log('Blocked XeCLI same-directory upgrade because xecli-owned-files.txt is missing or unreadable.');
      Result := ExpandConstant('{cm:InstallOwnedManifestRequired}');
      exit;
    end;

    if not TryPrepareExistingOwnedFiles(TargetInstallDir) then
    begin
      Log('Blocked XeCLI same-directory upgrade because the old ownership inventory could not be preserved.');
      Result := ExpandConstant('{cm:InstallOwnedPreparationFailed}');
      exit;
    end;
  end;
  if (GetArrayLength(ExistingInstallPaths) = 0) and
     not DestinationIsEmpty and
     not HasLegacyInstallMarkerOnly then
  begin
    Log('Blocked XeCLI installation because the unregistered destination is not empty. Existing files were left untouched.');
    Result := ExpandConstant('{cm:InstallNonEmptyDestinationBlocked}');
    exit;
  end;
end;

procedure RevalidateInstallDestinationBeforeWrite;
var
  TargetInstallDir: String;
  DestinationIsEmpty: Boolean;
  HasPortableMarker: Boolean;
  HasLegacyInstallMarkerOnly: Boolean;
  LegacyInstallMarkerPath: String;
begin
  if not TryCanonicalizeAbsolutePath(ExpandConstant('{app}'),
       TargetInstallDir) or
     not IsSafeInstallDir(TargetInstallDir) or
     not TryInspectInstallDestination(TargetInstallDir,
       DestinationIsEmpty, HasPortableMarker,
       HasLegacyInstallMarkerOnly) then
    RaiseException(ExpandConstant('{cm:InstallDestinationInspectionFailed}'));

  if HasPortableMarker then
  begin
    Log('Aborted XeCLI installation before file extraction because xecli.portable appeared after destination validation. Portable UserData was left untouched.');
    RaiseException(ExpandConstant('{cm:InstallPortableDestinationBlocked}'));
  end;
  if not PreparedUpgrade and HasLegacyInstallMarkerOnly then
  begin
    LegacyInstallMarkerPath := NormalizePathSegment(
      AddBackslash(TargetInstallDir) + LegacyInstallMarkerName);
    if not DeleteFile(LegacyInstallMarkerPath) then
    begin
      Log(Format('Aborted XeCLI installation because the sole legacy install marker could not be removed: %s', [LegacyInstallMarkerPath]));
      RaiseException(ExpandConstant('{cm:InstallDestinationInspectionFailed}'));
    end;

    if not TryInspectInstallDestination(TargetInstallDir,
         DestinationIsEmpty, HasPortableMarker,
         HasLegacyInstallMarkerOnly) or
       not DestinationIsEmpty or HasPortableMarker or
       HasLegacyInstallMarkerOnly then
    begin
      Log('Aborted XeCLI installation because the destination changed while removing the sole legacy install marker.');
      RaiseException(ExpandConstant('{cm:InstallNonEmptyDestinationBlocked}'));
    end;
    Log('Removed the sole legacy XeCLI install marker before installing v2 files.');
  end;
  if not PreparedUpgrade and not DestinationIsEmpty then
  begin
    Log('Aborted XeCLI installation before file extraction because the fresh-install destination became non-empty. Existing files were left untouched.');
    RaiseException(ExpandConstant('{cm:InstallNonEmptyDestinationBlocked}'));
  end;
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
  UseMachinePathTarget: Boolean;
begin
  SetArrayLength(OwnedInstallDirs, 0);
  AddUniquePathEntry(OwnedInstallDirs, CurrentInstallDir);
  UseMachinePathTarget := ShouldUseMachinePathTarget(CurrentInstallDir);
  SyncXeCLIPathValue(HKCU, UserEnvironmentKey, OwnedInstallDirs, CurrentInstallDir, ShouldAddCurrentEntry and not UseMachinePathTarget);
  if IsAdminInstallMode then
  begin
    if IsWin64 then
      SyncXeCLIPathValue(HKLM64, MachineEnvironmentKey, OwnedInstallDirs, CurrentInstallDir, ShouldAddCurrentEntry and UseMachinePathTarget)
    else
      SyncXeCLIPathValue(HKLM, MachineEnvironmentKey, OwnedInstallDirs, CurrentInstallDir, ShouldAddCurrentEntry and UseMachinePathTarget);
  end;
end;

procedure ClearStoredCurrentUserPaths;
begin
  RegDeleteValue(HKA, UserPathsRegistryKey, OwnedInstallRegistryUserProfileValueName);
  RegDeleteValue(HKA, UserPathsRegistryKey, OwnedInstallRegistryUserAppDataValueName);
  RegDeleteValue(HKA, UserPathsRegistryKey, OwnedInstallRegistryLocalAppDataValueName);
  RegDeleteKeyIfEmpty(HKA, UserPathsRegistryKey);
end;

procedure StoreCurrentUserPaths;
var
  Stored: Boolean;
begin
  ClearStoredCurrentUserPaths;
  if not CurrentUserPathsResolved then
    exit;

  Stored :=
    RegWriteStringValue(HKA, UserPathsRegistryKey,
      OwnedInstallRegistryUserProfileValueName, NormalizePathSegment(CurrentUserProfileDir)) and
    RegWriteStringValue(HKA, UserPathsRegistryKey,
      OwnedInstallRegistryUserAppDataValueName, NormalizePathSegment(CurrentUserAppDataDir)) and
    RegWriteStringValue(HKA, UserPathsRegistryKey,
      OwnedInstallRegistryLocalAppDataValueName, NormalizePathSegment(CurrentUserLocalAppDataDir));
  if not Stored then
  begin
    ClearStoredCurrentUserPaths;
    Log('Unable to store the XeCLI current-user data paths.');
  end;
end;

procedure ReportRetainedUserData;
var
  AppDataRoot: String;
  LocalAppDataRoot: String;
  AppDataPath: String;
  LocalAppDataPath: String;
begin
  if not RegQueryStringValue(HKA, UserPathsRegistryKey,
       OwnedInstallRegistryUserAppDataValueName, AppDataRoot) or
     (Trim(AppDataRoot) = '') then
    AppDataRoot := ExpandConstant('{userappdata}');
  if not RegQueryStringValue(HKA, UserPathsRegistryKey,
       OwnedInstallRegistryLocalAppDataValueName, LocalAppDataRoot) or
     (Trim(LocalAppDataRoot) = '') then
    LocalAppDataRoot := ExpandConstant('{localappdata}');

  AppDataPath := NormalizePathSegment(AddBackslash(AppDataRoot) + 'XeCLI');
  LocalAppDataPath := NormalizePathSegment(
    AddBackslash(LocalAppDataRoot) + 'XeCLI');
  Log(Format('Retained XeCLI user data for optional manual removal: %s', [AppDataPath]));
  Log(Format('Retained XeCLI user data for optional manual removal: %s', [LocalAppDataPath]));
  if not UninstallSilent then
    MsgBox(ExpandConstant('{cm:UserDataRetained}') + #13#10 +
      AppDataPath + #13#10 + LocalAppDataPath, mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  InstallDir: String;
begin
  if CurStep = ssInstall then
  begin
    RevalidateInstallDestinationBeforeWrite;
    exit;
  end;
  if CurStep <> ssPostInstall then
    exit;

  InstallDir := ExpandConstant('{app}');
  if not CompletePreparedOwnedFileUpgrade(InstallDir) then
  begin
    PreparedUpgradeCleanupFailed := true;
    Log('XeCLI post-install integrity validation or stale owned-file cleanup did not complete; ownership and hash evidence were retained for retry.');
    RaiseException(ExpandConstant('{cm:InstallOwnedCleanupFailed}'));
  end;
  SyncXeCLIPathState(InstallDir, WizardIsTaskSelected(PathTaskName));

  ApplySelectedLanguage(GetCliLanguageCode(''));
  StoreCurrentUserPaths;
end;

function GetCustomSetupExitCode: Integer;
begin
  if PreparedUpgradeCleanupFailed then
    Result := 3
  else
    Result := 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  InstallDir: String;
begin
  if CurUninstallStep <> usUninstall then
    exit;

  InstallDir := ExpandConstant('{app}');
  SyncXeCLIPathState(InstallDir, false);
  ReportRetainedUserData;
  ClearStoredCurrentUserPaths;
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
  WizardForm.WelcomeLabel2.Caption := ExpandConstant('{cm:InstallSummary}') + #13#10#13#10 + ExpandConstant('{cm:ArchitectureNotice}') + #13#10#13#10 + ExpandConstant('{cm:InstallSupportHint}');
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

procedure CurPageChanged(CurPageID: Integer);
begin
  SupportButton.Visible := (CurPageID = wpWelcome) or (CurPageID = wpFinished);
end;
