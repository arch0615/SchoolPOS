<#
.SYNOPSIS
    Publica el POS y compila el instalador de la escuela (.exe).

.DESCRIPTION
    Genera una publicación self-contained win-x64 (la escuela no necesita
    instalar .NET) y la empaqueta con Inno Setup en un único instalador.

.PARAMETER IsccPath
    Ruta a ISCC.exe (compilador de línea de comandos de Inno Setup 6).

.PARAMETER SignTool
    Comando de firma, si se cuenta con certificado. Ejemplo:
      -SignTool 'signtool sign /fd sha256 /f C:\cert.pfx /p CLAVE /tr http://timestamp.digicert.com /td sha256 $f'
    Sin este parámetro el instalador queda SIN FIRMAR y Windows SmartScreen
    mostrará una advertencia a cada usuario.

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -SignTool 'signtool sign /fd sha256 /f cert.pfx /p clave $f'
#>
[CmdletBinding()]
param(
    [string]$IsccPath = "C:\Tools\InnoSetup6\ISCC.exe",
    [string]$SignTool
)

$ErrorActionPreference = "Stop"

$here        = Split-Path -Parent $MyInvocation.MyCommand.Path
$appRoot     = Resolve-Path (Join-Path $here "..\..")          # …\app
$repoRoot    = Resolve-Path (Join-Path $appRoot "..")          # raíz del repo
$artifacts   = Join-Path $repoRoot "artifacts"
$payload     = Join-Path $artifacts "pos-publish"
$syncPayload = Join-Path $artifacts "sync-agent-publish"
$project     = Join-Path $appRoot "src\SchoolPOS.Pos.Desktop\SchoolPOS.Pos.Desktop.csproj"
$syncProject = Join-Path $appRoot "src\SchoolPOS.Sync.Agent\SchoolPOS.Sync.Agent.csproj"

if (-not (Test-Path $IsccPath)) {
    throw "No se encontró ISCC.exe en '$IsccPath'. Instala Inno Setup 6 o pasa -IsccPath."
}

Write-Host "» Limpiando publicaciones anteriores…" -ForegroundColor Cyan
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
if (Test-Path $syncPayload) { Remove-Item $syncPayload -Recurse -Force }
New-Item -ItemType Directory -Path $payload -Force | Out-Null
New-Item -ItemType Directory -Path $syncPayload -Force | Out-Null

Write-Host "» Publicando el POS (self-contained win-x64)…" -ForegroundColor Cyan
# self-contained: la escuela no instala .NET. Pesa más, pero elimina la causa
# número uno de "no abre" en una instalación sin soporte técnico.
& dotnet publish $project -c Release -r win-x64 --self-contained true -o $payload --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló ($LASTEXITCODE)." }

$sizeMb = (Get-ChildItem $payload -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("  publicación: {0:N0} MB" -f $sizeMb)

Write-Host "» Publicando el Agente de Sincronización (self-contained win-x64)…" -ForegroundColor Cyan
# Va en el mismo instalador que el POS: el instalador lo registra e inicia como servicio de
# Windows, así que la escuela nunca necesita instalarlo ni tocarlo por separado.
& dotnet publish $syncProject -c Release -r win-x64 --self-contained true -o $syncPayload --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish del agente falló ($LASTEXITCODE)." }

$syncSizeMb = (Get-ChildItem $syncPayload -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("  publicación: {0:N0} MB" -f $syncSizeMb)

Write-Host "» Compilando el instalador…" -ForegroundColor Cyan
$iss  = Join-Path $here "LoncherApp.iss"
$args = @($iss)
if ($SignTool) { $args += "/DSignTool=$SignTool" }

& $IsccPath @args
if ($LASTEXITCODE -ne 0) { throw "ISCC falló ($LASTEXITCODE)." }

$setup = Get-ChildItem $artifacts -Filter "LoncherApp-Setup-*.exe" |
         Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host ""
Write-Host "✔ Instalador listo:" -ForegroundColor Green
Write-Host ("  {0}  ({1:N0} MB)" -f $setup.FullName, ($setup.Length / 1MB))
if (-not $SignTool) {
    Write-Host "  AVISO: sin firmar — SmartScreen advertirá al usuario." -ForegroundColor Yellow
}
