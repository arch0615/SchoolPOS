; ---------------------------------------------------------------------------
; LoncherApp — Punto de Venta · instalador para escuelas
;
; Objetivo: que una escuela sin personal técnico pueda instalar el POS sin
; tocar una terminal, sin instalar SQL Server y sin editar archivos de
; configuración. El instalador SOLO copia archivos y prepara permisos; toda la
; configuración (escuela, administrador, base de datos) la hace el asistente de
; primer arranque dentro de la aplicación, donde sí se le puede mostrar un
; error entendible y dejar reintentar.
;
; Compilar:  build-installer.ps1   (publica la app y luego llama a ISCC)
; ---------------------------------------------------------------------------

#define AppName        "LoncherApp Punto de Venta"
#define AppShortName   "LoncherApp"
#define AppVersion     "1.0.0"
#define AppPublisher   "LoncherApp"
#define AppExe         "SchoolPOS.Pos.Desktop.exe"
; Carpeta con la publicación self-contained (la genera build-installer.ps1).
#define PayloadDir     "..\..\..\artifacts\pos-publish"
#define AppIcon        "..\..\src\SchoolPOS.Pos.Desktop\Assets\AppIcon.ico"

; Agente de sincronización: mismo instalador, se registra como servicio de Windows (ver [Run]).
; No tiene ícono ni acceso directo — no es algo que la escuela abra, corre solo en segundo plano.
#define SyncExe        "SchoolPOS.Sync.Agent.exe"
#define SyncPayloadDir "..\..\..\artifacts\sync-agent-publish"
#define SyncServiceName "SchoolPOSSync"

[Setup]
AppId={{8E6F1C2A-4B7D-4E31-9A55-2C1D9F7B3A64}
; Sin esto, el instalador (LoncherApp-Setup-*.exe) muestra el ícono genérico de Inno Setup en vez
; del logotipo — es aparte de UninstallDisplayIcon más abajo, que ya usa el del propio {#AppExe}.
SetupIconFile={#AppIcon}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppShortName}
DefaultGroupName={#AppShortName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\..\..\artifacts
OutputBaseFilename=LoncherApp-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; La app se publica self-contained para win-x64: no hay que instalar .NET.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Escribe en Archivos de programa y ajusta permisos de ProgramData.
PrivilegesRequired=admin
MinVersion=10.0
DisableProgramGroupPage=yes

; --- Firma de código -------------------------------------------------------
; Sin firmar, Windows SmartScreen muestra "Windows protegió su PC" y la mayoría
; de los usuarios no técnicos se detienen ahí. Al conseguir el certificado,
; descomentar y compilar con:
;   ISCC.exe /DSignTool="signtool sign /fd sha256 /f cert.pfx /p CLAVE /tr http://timestamp.digicert.com /td sha256 $f"
; #ifdef SignTool
; SignTool=SignTool
; SignedUninstaller=yes
; #endif

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
; Toda la publicación self-contained. Se excluye appsettings.json: la
; configuración real vive en ProgramData y la escribe el asistente; enviar una
; plantilla con valores de ejemplo solo invita a que alguien la edite a mano.
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "appsettings.json"
; Agente de sincronización, en su propia subcarpeta. Su appsettings.json SÍ se incluye: son
; valores por omisión inofensivos (sin llave ni datos de la escuela), no una plantilla con
; secretos — la config real de la escuela vive en ProgramData (ver [Run] más abajo).
Source: "{#SyncPayloadDir}\*"; DestDir: "{app}\SyncAgent"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Datos de la instalación: configuración + base de datos SQLite.
; users-modify es imprescindible — el cajero normalmente NO es administrador, y
; tanto el asistente como cada venta necesitan escribir aquí.
Name: "{commonappdata}\{#AppShortName}"; Permissions: users-modify

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppShortName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#AppShortName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Registra e inicia el Agente de Sincronización como servicio de Windows (arranque automático,
; sobrevive reinicios, no requiere que nadie inicie sesión). "stop"/"delete" van primero y
; toleran error (con "&", no "&&") para que una reinstalación o actualización no falle si el
; servicio ya existía de una instalación anterior; el "timeout" le da un respiro al Administrador
; de control de servicios antes de volver a crearlo. Sin usuario/UI de por medio: PrivilegesRequired
; admin ya garantiza los permisos necesarios en este paso.
Filename: "{cmd}"; Parameters: "/c sc.exe stop {#SyncServiceName} & sc.exe delete {#SyncServiceName} & timeout /t 2 /nobreak >nul & sc.exe create {#SyncServiceName} binPath= ""{app}\SyncAgent\{#SyncExe}"" start= auto DisplayName= ""LoncherApp - Sincronización"" & sc.exe description {#SyncServiceName} ""Sincroniza el saldo de LoncherApp con la nube. Se instala junto con el POS; no requiere atención."" & sc.exe start {#SyncServiceName}"; Flags: runhidden; StatusMsg: "Configurando la sincronización con la nube…"
Filename: "{app}\{#AppExe}"; Description: "Abrir {#AppShortName} ahora"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Quita el servicio del agente antes de borrar sus archivos; si ya estaba detenido o no existía,
; el "&" (no "&&") deja seguir sin marcar la desinstalación como fallida.
Filename: "{cmd}"; Parameters: "/c sc.exe stop {#SyncServiceName} & sc.exe delete {#SyncServiceName}"; Flags: runhidden; RunOnceId: "RemoveSyncService"

[UninstallDelete]
; Los archivos del programa se van; los DATOS no se tocan aquí a propósito
; (ver CurUninstallStepChanged: se pregunta antes de borrar la base).
Type: filesandordirs; Name: "{app}"

[Code]
// Desinstalar no debe llevarse las ventas de la escuela sin avisar. Se pregunta
// explícitamente y por omisión se conservan los datos (incluida la llave de sincronización,
// que vive en el mismo directorio: así una reinstalación no la vuelve a pedir).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{commonappdata}\{#AppShortName}');
    if DirExists(DataDir) then
    begin
      if MsgBox('¿Desea borrar también los datos de la tienda (ventas, inventario, saldos y la llave de sincronización)?' + #13#10 + #13#10 +
                DataDir + #13#10 + #13#10 +
                'Esta acción no se puede deshacer. Elija "No" si va a reinstalar o si quiere conservar un respaldo.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
