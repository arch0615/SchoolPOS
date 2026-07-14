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

## Base de datos (SQL Server, por escuela)

Las migraciones viven en `src/SchoolPOS.Data/Migrations`. Para aplicarlas a una instancia local:

```bash
dotnet ef database update --project src/SchoolPOS.Data
# nueva migración:
dotnet ef migrations add <Nombre> --project src/SchoolPOS.Data -o Migrations
```

La cadena de conexión real se inyecta por escuela vía `AddSchoolPosData(connectionString)`
(ver `SchoolPOS.Data/DependencyInjection.cs`). La cadena de diseño (solo para generar
migraciones) está en `SchoolDbContextFactory`.

## Estado actual (Fase 1 completa — hito M1)

Núcleo del libro mayor de saldo — **fuente única de verdad** con transacciones atómicas,
y el **esquema completo de la Fase 1** (22 entidades) con dos migraciones.

- `Money` en `decimal` con redondeo comercial (NFR-4).
- **Saldo:** School, Student, Account, BalanceMovement (inmutable), TopUp (dedupe por
  `gateway_ref`), Guardian, User, AuditLog.
- **Inventario:** Category, Product (stock denormalizado), StockMovement (Kardex).
- **Ventas:** Sale, SaleLine.
- **Compras:** Supplier, PurchaseOrder, PurchaseOrderLine, GoodsReceipt (+Line), SupplierInvoice.
- **Tesorería:** CashSession, CashMovement.
- `BalanceService`: recarga (100% al estudiante, idempotente), cobro con saldo (UPDATE
  condicional atómico → sin sobregiro ni doble gasto), devolución y ajuste auditado.
- 14 pruebas verdes, incl. **recarga + venta + devolución reconcilian** (M1), Kardex
  reconcilia con existencias, y renglones de venta cuadran con el total.

### Siguiente

Fase 1.B restante: servicios de dominio de **inventario** (entrada/salida/ajuste con Kardex
atómico) y **ventas** (arma la venta, cobra por saldo, descuenta stock). Luego Fase 2
(POS WPF: ventas + inventario offline sobre la DB local).
