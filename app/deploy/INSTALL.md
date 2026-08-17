# SchoolPOS — Guía de instalación por escuela (llave en mano)

Pasos para poner en marcha una escuela. La **DB local de la escuela es la fuente
única de verdad** del saldo; el **portal** (nube) recibe las recargas y el
**agente de sincronización** las baja al ledger local.

```
   NUBE                                     ESCUELA (LAN)
 ┌───────────────────┐                    ┌──────────────────────────────┐
 │  Portal (ASP.NET) │   sync agent       │  POS (WPF) ── LAN ──┐         │
 │  DB nube (SQL)    │◀──────────────────▶│  → consume saldo    ▼         │
 │  Mercado Pago     │  recargas ↓        │        ┌──────────────────────┐│
 └───────────────────┘  consumo ↑         │        │ SQL Server local     ││
                                          │        │ (fuente de verdad)   ││
                                          │        └──────────────────────┘│
                                          └──────────────────────────────┘
```

## 1. Prerrequisitos

| Componente | Dónde | Requisito |
|-----------|-------|-----------|
| **.NET 8 SDK/Runtime** | Nube + cada escuela | SDK para publicar; runtime para ejecutar |
| **SQL Server** | Nube (portal) | Instancia accesible (Azure SQL / SQL Server) |
| **SQL Server Express** | Cada escuela | Instancia local (`localhost\SQLEXPRESS`) |
| **Windows** | Caja(s) POS | Para el POS WPF + lector de código de barras / impresora |
| **Cuenta Mercado Pago** | Vendedor + escuela | App marketplace + OAuth de la escuela |

## 2. Base de datos de la nube (portal)

Crea una base vacía (p. ej. `SchoolPOS_Cloud`). El portal aplica las migraciones
automáticamente al arrancar (`Database.Migrate()` cuando `Database:Provider=SqlServer`).

## 3. Provisionar la escuela (DB local + admin)

En la caja de la escuela (o donde esté su SQL Server local), ejecuta el
provisionador. Aplica las migraciones y crea la escuela + el operador administrador.

**Windows (PowerShell):**
```powershell
cd app\deploy
.\provision-school.ps1 `
  -ConnectionString "Server=localhost\SQLEXPRESS;Database=SchoolPOS_ColegioX;Trusted_Connection=True;TrustServerCertificate=True;" `
  -SchoolName "Colegio X" -AdminUser admin -AdminPassword "CAMBIA-ESTO" -CommissionRate 0.05
```

**Linux/macOS (bash):**
```bash
cd app/deploy
CONN="Server=...;Database=SchoolPOS_ColegioX;User Id=sa;Password=...;TrustServerCertificate=True;" \
SCHOOL_NAME="Colegio X" ADMIN_PASSWORD="CAMBIA-ESTO" ./provision-school.sh
```

> **Anota el `SchoolId`** que imprime el comando: se usa en el POS y el portal.
> Para re-ejecutar sin duplicar, vuelve a pasar el mismo `--SchoolId` / `-SchoolId`.

**Datos fiscales (para facturar la comisión, FR-COM-5):** agrega `-Rfc`, `-LegalName`,
`-TaxRegime`, `-PostalCode`, `-CfdiUse` (PowerShell) o las variables `RFC`, `LEGAL_NAME`,
`TAX_REGIME`, `POSTAL_CODE`, `CFDI_USE` (bash). Sin estos datos el sistema **no puede
emitir el CFDI de comisión** de esa escuela. Se pueden completar después volviendo a
ejecutar con el mismo `SchoolId` (actualiza solo lo fiscal).

Los estudiantes (roster) se dan de alta después desde el POS (inventario/clientes)
o por carga inicial; el portal los vincula por matrícula.

## 4. Portal (nube)

Un solo portal atiende a **todas** las escuelas: el tutor elige la suya al registrarse y a partir
de ahí su escuela viaja en la sesión. No hay que desplegar un portal por escuela.

1. Copia `deploy/config-templates/portal.appsettings.json` a
   `src/SchoolPOS.Portal.Web/appsettings.json` y completa:
   - `ConnectionStrings:Portal` → DB de la nube.
   - `Portal:SeedDemoData` → `false` en producción (`Portal:SchoolId` solo se usa para sembrar
     datos de demostración; en producción es irrelevante).
   - `Portal:VendorAccessCode` → código para el panel de comisiones.
   - `Payments:Provider` → `MercadoPago`; llena la sección `MercadoPago` (paso 7).
   - `DataProtection:KeyRingPath` → carpeta persistente para el anillo de llaves (paso 9).
2. Publica y ejecuta:
   ```bash
   dotnet publish src/SchoolPOS.Portal.Web -c Release -o /srv/schoolpos-portal
   ASPNETCORE_URLS="https://0.0.0.0:443" dotnet /srv/schoolpos-portal/SchoolPOS.Portal.Web.dll
   ```
   Detrás de un proxy inverso con TLS. En no-Development ya aplica HSTS.

## 5. POS de escritorio (WPF, Windows)

### 5.A Instalador (recomendado — escuela de una sola caja)

La escuela **no necesita SQL Server, ni .NET, ni editar archivos**. Se genera un instalador
(`.exe`) y la escuela hace doble clic:

```powershell
cd app\deploy\installer
.\build-installer.ps1                 # publica el POS y compila el instalador
# → artifacts\LoncherApp-Setup-1.0.0.exe  (~53 MB)
```

Lo que ve la escuela: siguiente → instalar → al abrir por primera vez, un **asistente** que pide
el nombre de la escuela y el usuario/contraseña del administrador. El asistente crea la base de
datos y guarda la configuración; a partir del segundo arranque entra directo al acceso.

| | |
|---|---|
| Programa | `C:\Program Files\LoncherApp` |
| **Datos (respaldar esto)** | `C:\ProgramData\LoncherApp\schoolpos.db` |
| Configuración | `C:\ProgramData\LoncherApp\appsettings.json` |

- La base es **SQLite**, en la misma computadora. Es el modo *una sola caja*: **no** debe
  compartirse por red. Para varias cajas usa 5.B.
- El desinstalador **pregunta** antes de borrar los datos, y por omisión los conserva.
- El instalador **no está firmado** todavía: Windows mostrará "Windows protegió su PC" y hay que
  elegir *Más información → Ejecutar de todas formas*. Con un certificado se firma pasando
  `-SignTool` a `build-installer.ps1` (ver el encabezado del script).

### 5.B Instalación manual (varias cajas contra SQL Server)

1. En una máquina Windows, publica el POS (solo compila en Windows):
   ```powershell
   dotnet publish src\SchoolPOS.Pos.Desktop -c Release -r win-x64 --self-contained -o C:\SchoolPOS\POS
   ```
2. Crea `C:\ProgramData\LoncherApp\appsettings.json` con `Pos:SchoolId`,
   `Database:Provider = SqlServer` y `ConnectionStrings:Local` (SQL Server local de la escuela).
   Provisiona la base con `SchoolPOS.Provision` (paso 3).
3. Ejecuta `SchoolPOS.Pos.Desktop.exe` e inicia sesión con el operador administrador.
   El POS opera contra la DB local por LAN → **sigue vendiendo aunque no haya internet**.

> El asistente todavía no cubre este modo (la opción aparece deshabilitada); se configura a mano.

## 6. Agente de sincronización (por escuela)

1. Copia `deploy/config-templates/sync-agent.appsettings.json` a
   `src/SchoolPOS.Sync.Agent/appsettings.json` y completa `ConnectionStrings:Cloud`
   (DB nube) y `ConnectionStrings:Local` (DB de la escuela).
2. Publica y ejecuta como servicio (recomendado):
   ```powershell
   dotnet publish src\SchoolPOS.Sync.Agent -c Release -r win-x64 --self-contained -o C:\SchoolPOS\Sync
   # Registrar como servicio de Windows:
   sc.exe create SchoolPOSSync binPath= "C:\SchoolPOS\Sync\SchoolPOS.Sync.Agent.exe" start= auto
   sc.exe start SchoolPOSSync
   ```
   El agente baja recargas confirmadas al ledger local (idempotente) y sube el
   consumo a la nube. Si no hay internet, reintenta en el siguiente ciclo.

## 7. Mercado Pago (split de comisión)

1. Crea una **aplicación marketplace** en Mercado Pago (cuenta del proveedor).
2. Conecta la cuenta de la escuela por **OAuth** para obtener su *access token* de vendedor.
3. Configura en `portal.appsettings.json` → `MercadoPago`:
   `AccessToken` (token del vendedor), `WebhookSecret`, y `NotificationUrl`
   apuntando a `https://<tu-dominio>/api/payments/webhook`.
4. En el panel de Mercado Pago, registra ese webhook.
   La comisión viaja como `marketplace_fee` y se separa a la cuenta del proveedor.

## 8. Anillo de llaves (cifrado de los tokens de las escuelas)

Los *access/refresh token* de Mercado Pago de cada escuela se guardan **cifrados** en la DB de la
nube. La llave vive en un anillo de Data Protection que **debe persistir entre reinicios y
compartirse entre instancias**; si se pierde, los tokens dejan de poder descifrarse y **cada
escuela tiene que reconectar su cuenta por OAuth** (no se pierde dinero ni saldo, pero se caen los
cobros hasta reconectar).

```jsonc
// portal.appsettings.json
"DataProtection": { "KeyRingPath": "/var/lib/schoolpos/keys" }   // Windows: "C:\\SchoolPOS\\keys"
```

- La carpeta debe ser **legible/escribible solo por el usuario del servicio** y entrar en el
  respaldo, igual que la base de datos.
- Sin configurar, las llaves van al perfil del usuario — que en un contenedor o bajo un servicio
  con perfil efímero se borra en cada despliegue. Configúralo siempre en producción.
- Con **varias instancias** del portal (balanceo), todas deben apuntar a la misma ruta compartida.

## 9. Verificación

- [ ] `provision-school` imprime un `SchoolId` y crea el operador admin.
- [ ] El POS inicia sesión con el admin y registra una venta contra saldo.
- [ ] En el POS: abrir caja en Tesorería → vender en efectivo → cerrar caja; el efectivo esperado
      incluye esa venta (sin caja abierta, el cobro en efectivo se rechaza).
- [ ] En el POS (administrador): Devoluciones → elegir la venta → devolver una pieza; el stock
      regresa y el importe se reintegra (al saldo, o como egreso de la caja si fue en efectivo).
- [ ] En el portal: registrar tutor **eligiendo su escuela** → vincular alumno por matrícula →
      recargar → aprobar.
- [ ] Con dos escuelas dadas de alta, un tutor de la escuela A **no** puede vincular una matrícula
      de la escuela B.
- [ ] El saldo recargado aparece en el POS tras un ciclo del agente de sincronización.
- [ ] Panel del proveedor (`/Vendor/Login`) muestra la comisión de la escuela.

## Notas de operación

- **Respaldos**: respalda la DB local de cada escuela (fuente de verdad), la DB de la nube y la
  carpeta del anillo de llaves (paso 8).
- **Primera corrida del agente tras actualizar**: los asientos anteriores a esta versión no traen
  marca de sincronización, así que el agente los revisa una vez en lotes de 500 por ciclo (no se
  duplica nada: hay dedupe por Id). En una escuela con mucho historial la primera puesta al día
  puede tardar varios ciclos; después cada corrida solo mira lo nuevo.
- **Secretos**: no subas `appsettings.json` con credenciales al repositorio (ya está en `.gitignore` por entorno). Usa variables de entorno o un gestor de secretos.
- **Actualizaciones de esquema**: nuevas migraciones se aplican re-ejecutando el provisionador (o el portal al arrancar).
- **Zona horaria**: los sellos de tiempo se guardan en UTC y se presentan/filtran en hora de México
  (`MxTime`, en `SchoolPOS.Domain`, compartido por el POS y el portal para que no puedan divergir).
  El servidor resuelve `America/Mexico_City` (Linux) o `Central Standard Time (Mexico)` (Windows);
  si ninguna existe cae a UTC, así que verifica que la caja/servidor tenga la zona instalada.
