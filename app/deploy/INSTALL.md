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

1. Copia `deploy/config-templates/portal.appsettings.json` a
   `src/SchoolPOS.Portal.Web/appsettings.json` y completa:
   - `ConnectionStrings:Portal` → DB de la nube.
   - `Portal:SchoolId` → el SchoolId provisionado.
   - `Portal:SeedDemoData` → `false` en producción.
   - `Portal:VendorAccessCode` → código para el panel de comisiones.
   - `Payments:Provider` → `MercadoPago`; llena la sección `MercadoPago` (paso 7).
2. Publica y ejecuta:
   ```bash
   dotnet publish src/SchoolPOS.Portal.Web -c Release -o /srv/schoolpos-portal
   ASPNETCORE_URLS="https://0.0.0.0:443" dotnet /srv/schoolpos-portal/SchoolPOS.Portal.Web.dll
   ```
   Detrás de un proxy inverso con TLS. En no-Development ya aplica HSTS.

## 5. POS de escritorio (WPF, Windows)

1. En una máquina Windows, publica el POS (solo compila en Windows):
   ```powershell
   dotnet publish src\SchoolPOS.Pos.Desktop -c Release -r win-x64 --self-contained -o C:\SchoolPOS\POS
   ```
2. Copia `deploy/config-templates/pos.appsettings.json` junto al ejecutable y completa
   `Pos:SchoolId` y `ConnectionStrings:Local` (SQL Server local de la escuela).
3. Ejecuta `SchoolPOS.Pos.Desktop.exe` e inicia sesión con el operador administrador.
   El POS opera contra la DB local por LAN → **sigue vendiendo aunque no haya internet**.

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

## 8. Verificación

- [ ] `provision-school` imprime un `SchoolId` y crea el operador admin.
- [ ] El POS inicia sesión con el admin y registra una venta contra saldo.
- [ ] En el portal: registrar tutor → vincular alumno por matrícula → recargar → aprobar.
- [ ] El saldo recargado aparece en el POS tras un ciclo del agente de sincronización.
- [ ] Panel del proveedor (`/Vendor/Login`) muestra la comisión de la escuela.

## Notas de operación

- **Respaldos**: respalda la DB local de cada escuela (fuente de verdad) y la DB de la nube.
- **Secretos**: no subas `appsettings.json` con credenciales al repositorio (ya está en `.gitignore` por entorno). Usa variables de entorno o un gestor de secretos.
- **Actualizaciones de esquema**: nuevas migraciones se aplican re-ejecutando el provisionador (o el portal al arrancar).
- **Zona horaria**: los sellos de tiempo se guardan en UTC.
