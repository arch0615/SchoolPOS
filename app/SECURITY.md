# SchoolPOS — Controles de seguridad (revisión)

Resumen de los controles de seguridad implementados y su cobertura de pruebas.
Referencias a los requisitos no funcionales en `../requirements.md`.

## Autenticación y contraseñas (NFR-6)
- **Hash de contraseñas**: PBKDF2-SHA256, 100 000 iteraciones, sal aleatoria de 16 bytes,
  comparación en tiempo constante (`Pbkdf2PasswordHasher`). Nunca se almacena la contraseña en claro.
  Aplica a operadores del POS (`AuthService`) y tutores del portal (`GuardianService`).
- **Bloqueo por intentos** (FR-WP-3): la cuenta se bloquea 15 min tras 5 intentos fallidos; el
  bloqueo se limpia al iniciar sesión correctamente o al restablecer la contraseña. Aplica tanto a
  **tutores del portal** (`GuardianService`) como a **operadores del POS** (`AuthService`) — la
  misma cuenta de operador abre la consola web de la tienda, así que está expuesta desde internet,
  no solo desde la LAN de la escuela.
- **Mensajes genéricos**: login y recuperación no revelan si un correo/usuario existe
  (evita enumeración de cuentas).
- **Recuperación de contraseña** (FR-WP-4): token aleatorio de 256 bits, **se guarda solo su hash**,
  **de un solo uso**, caduca en 1 hora.

## Pagos y webhooks (NFR-3)
- **Sin datos de tarjeta**: el cobro se delega por completo a Mercado Pago; el sistema nunca ve ni
  almacena datos de tarjeta.
- **Confirmación server-side**: el saldo se acredita **solo tras verificar el webhook**, nunca por la
  redirección del navegador. La verificación (`MercadoPagoGateway`):
  1. valida la firma `x-signature` (HMAC-SHA256 sobre el manifiesto `id;request-id;ts`, comparación
     en tiempo constante);
  2. **consulta el pago server-side** para leer su estado real (no se confía en el cuerpo del webhook).
  - Una firma inválida o ausente **no dispara ninguna llamada a la API** y no acredita nada.
- **Idempotencia**: cada recarga se aplica una sola vez (dedupe por `gateway_ref` + bandera
  `AppliedLocally`); webhooks duplicados no duplican el abono.

## Integridad financiera (NFR-1, NFR-4)
- **Sin doble gasto**: cada cargo usa un `UPDATE` condicional atómico a nivel de base de datos
  (`WHERE Balance + OverdraftLimit >= importe`); cargos concurrentes nunca sobregiran ni pierden
  actualizaciones (probado con concurrencia real).
- **Dinero en `decimal`** con redondeo comercial; libros mayores inmutables (solo inserción).
- **Bitácora** (FR-ADM-4): acciones sensibles (ajustes de saldo, devoluciones) quedan auditadas con
  estado antes/después.
- **Arqueo de caja**: toda venta en efectivo se liga a la sesión de caja abierta del operador, así
  que el efectivo esperado incluye lo vendido. El POS rechaza cobrar en efectivo sin caja abierta —
  si no, las ventas quedaban fuera del arqueo y aparecían como sobrante, tapando un faltante real.
  Las **devoluciones en efectivo** se asientan como egreso de esa misma caja (y también exigen una
  caja abierta), de modo que el dinero que sale del cajón queda registrado.
- **Devoluciones** (FR-SAL-5): restringidas al administrador — quien cobra no revierte su propio
  cobro — y auditadas con operador, venta e importe.

## Secretos en reposo
- **Tokens OAuth de la pasarela** (`SchoolPaymentAccount`): se guardan **cifrados** con ASP.NET
  Data Protection (`ISecretProtector`, prefijo `dp1:`). Con muchas escuelas en la misma base de
  datos de la nube, guardarlos en claro haría que una sola lectura permitiera cobrar a nombre de
  todas. Valores heredados sin prefijo se siguen leyendo y se re-cifran al siguiente guardado.
- Si el token no se puede descifrar (anillo de llaves perdido o rotado), la cuenta se lee como
  **no conectada** y la escuela debe reconectar por OAuth — nunca se propaga la excepción.
- **Operación**: el anillo de llaves debe persistir y compartirse entre instancias
  (`DataProtection:KeyRingPath`). Ver `deploy/INSTALL.md` §9.

## Control de acceso
- **POS**: roles cajero/almacén/administrador; pantallas sensibles (inventario, reportes, bitácora,
  descuentos) restringidas por rol. Tesorería la abre cualquier operador **para su propia caja**;
  el histórico de arqueos de la escuela sigue siendo solo del administrador.
- **Tarifa de comisión**: la fija el proveedor desde su panel (`/Vendor/Schools`). El POS la muestra
  pero no la edita — es una condición del contrato, no un ajuste de la escuela.
- **Portal**: cookies de autenticación; el panel del proveedor (comisiones) exige una política
  `Vendor` separada de las cuentas de padres.
- **Aislamiento entre escuelas**: el portal es multi-escuela. La escuela del tutor se fija al
  registrarse y viaja en la cookie (claim `school_id`); todas las operaciones con alcance de
  escuela (vincular alumno por matrícula, crear recarga) la leen de ahí, **nunca** de la
  configuración. Así un tutor no puede vincular un alumno de otra escuela reutilizando su
  matrícula (las matrículas solo son únicas dentro de una escuela), ni recargar con la tasa de
  comisión o la moneda de una escuela ajena.

## Cobertura de pruebas (relevantes a seguridad)
- Firma de webhook: válida / id alterado / secreto incorrecto / malformada / secreto vacío.
- Gateway: firma inválida o ausente → `null` sin llamar a la API.
- Recuperación: token correcto / incorrecto / caducado / reutilizado (un solo uso); cambio de
  contraseña exige la actual.
- Bloqueo tras 5 intentos y expiración del bloqueo — tutores **y** operadores del POS.
- Tokens OAuth: no se guardan en claro, se leen de vuelta, valor heredado sigue sirviendo, y un
  token indescifrable se comporta como "no conectada".
- Concurrencia / no-doble-gasto (SQLite ejecutable + SQL Server real gated); Kardex reconcilia con
  existencias bajo ajuste por conteo concurrente con ventas.

## Pendiente / recomendaciones
- HTTPS/HSTS en producción (ya activado fuera de Development).
- Protección de datos de menores (LFPDPPP): revisar retención y consentimiento.
- **Rotación** de los tokens de Mercado Pago (ya no se guardan en claro, pero no hay rotación
  programada) y respaldo del anillo de llaves de Data Protection.
- Rate-limiting del endpoint de webhook y de login.
- La prueba de concurrencia contra SQL Server real existe pero está gated
  (`SCHOOLPOS_SQLSERVER_TESTS`): conviene ejecutarla en CI, ya que producción es SQL Server y la
  garantía de no-doble-gasto depende del aislamiento del proveedor.
