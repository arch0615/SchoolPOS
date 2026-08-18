# SchoolPOS — Solución (código)

Punto de venta + saldo en línea para tiendas escolares (México). Ver la especificación en
la carpeta raíz: `../requirements.md`, `../workplan.md`, `../decisions.md`.

**Stack:** C# / .NET 8 · SQL Server (DB local por escuela) · WPF (POS de escritorio) ·
ASP.NET Core (portal en la nube) · Mercado Pago (split) · UI en español · MXN/USD.

## Estructura

```
app/
├─ SchoolPOS.sln                     Solución completa (incluye el POS WPF, solo Windows)
├─ SchoolPOS.CrossPlatform.slnf      Filtro sin WPF — para compilar/probar en Linux/CI
├─ Directory.Build.props             Convenciones compartidas (nullable, es-MX, warnings=errors)
├─ src/
│  ├─ SchoolPOS.Domain/              Entidades, enums, Money, abstracciones (sin dependencias)
│  ├─ SchoolPOS.Data/                EF Core: DbContext, migraciones, BalanceService
│  ├─ SchoolPOS.Portal.Web/          Portal web (ASP.NET Core) — recargas
│  ├─ SchoolPOS.Sync.Agent/          Agente de sincronización nube ↔ DB local
│  └─ SchoolPOS.Pos.Desktop/         POS WPF (net8.0-windows) — SOLO compila en Windows
└─ tests/
   ├─ SchoolPOS.Domain.Tests/
   └─ SchoolPOS.Data.Tests/          Pruebas del libro mayor (SQLite en memoria)
```

## Compilar y probar

En Linux/CI (sin WPF) usa el filtro de solución:

```bash
dotnet build SchoolPOS.CrossPlatform.slnf
dotnet test  SchoolPOS.CrossPlatform.slnf
```

En Windows puedes abrir `SchoolPOS.sln` completo (incluye el POS WPF).

## Base de datos

Hay **dos juegos de migraciones**, uno por proveedor, porque el DDL que genera EF es específico
del proveedor (las de SQL Server traen `nvarchar`/`uniqueidentifier`, inservibles en SQLite) y EF
descubre *todas* las migraciones del ensamblado: no pueden convivir en uno solo.

| Proveedor | Dónde | Lo usa |
|---|---|---|
| SQL Server | `src/SchoolPOS.Data/Migrations` | Portal (nube) y escuelas con varias cajas |
| SQLite | `src/SchoolPOS.Data.Migrations.Sqlite/Migrations` | Instalador de una sola caja |

Al cambiar el modelo hay que agregar la migración a **los dos**, o el proveedor que se quede atrás
fallará al arrancar:

```bash
# SQL Server
dotnet ef migrations add <Nombre> --project src/SchoolPOS.Data -o Migrations

# SQLite
dotnet ef migrations add <Nombre> \
    --project src/SchoolPOS.Data.Migrations.Sqlite \
    --startup-project src/SchoolPOS.Data.Migrations.Sqlite -o Migrations
```

Los hosts aplican las migraciones al arrancar (`Database.Migrate()`), así que una escuela ya
instalada recibe los cambios de esquema al actualizar la aplicación, conservando sus datos.
Cualquier host que corra sobre SQLite debe **referenciar** `SchoolPOS.Data.Migrations.Sqlite`: el
ensamblado tiene que estar presente en tiempo de ejecución para poder aplicarlas.

La cadena de conexión real se inyecta por escuela vía `AddSchoolPosData(...)`
(ver `SchoolPOS.Data/DependencyInjection.cs`). Las cadenas de diseño —solo para generar
migraciones— están en `SchoolDbContextFactory` (SQL Server) y `SqliteDesignTimeFactory` (SQLite).

## Estado actual

Las cuatro piezas están en pie: **POS WPF**, **portal web**, **agente de sincronización** y
**proveedor** (comisiones + CFDI). 101 pruebas verdes (+1 gated sobre SQL Server real).

- **Saldo (núcleo):** libro mayor inmutable con `UPDATE` condicional atómico — sin sobregiro ni
  doble gasto bajo concurrencia; `SUM(Amount) == Account.Balance` reconcilia por construcción.
  Recargas idempotentes (dedupe por `gateway_ref` + bandera `AppliedLocally`).
- **Inventario:** Kardex atómico que reconcilia con las existencias, incluso con ajuste por conteo
  concurrente con ventas. **Ventas** y **compras** componen su transacción con la del saldo.
- **Pagos:** Mercado Pago marketplace (split por `marketplace_fee`), webhook verificado por firma y
  reconsultado server-side. Tokens OAuth de cada escuela **cifrados en reposo**.
- **Multi-escuela:** una sola instalación del portal atiende a todas las escuelas. El tutor elige la
  suya al registrarse y esa escuela viaja en su sesión; el alta de escuelas se hace desde el panel
  del proveedor (`/Vendor/AddSchool`) o con `tools/SchoolPOS.Provision`.
- **Sincronización:** el agente baja recargas confirmadas al ledger local y sube el consumo por
  lotes, leyendo solo lo pendiente (marca por asiento).

### Siguiente

- Ejecutar en CI la prueba de concurrencia contra SQL Server real (`SCHOOLPOS_SQLSERVER_TESTS`):
  producción es SQL Server y hoy la garantía se prueba sobre SQLite.
- Rate-limiting del webhook y del login; rotación programada de los tokens de Mercado Pago.
- `Money` está definido y probado pero no se usa en los servicios (usan `decimal` directo):
  adoptarlo en las fronteras o retirarlo.
