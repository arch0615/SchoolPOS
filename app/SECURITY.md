# SchoolPOS — Controles de seguridad (revisión)

Resumen de los controles de seguridad implementados y su cobertura de pruebas.
Referencias a los requisitos no funcionales en `../requirements.md`.

## Autenticación y contraseñas (NFR-6)
- **Hash de contraseñas**: PBKDF2-SHA256, 100 000 iteraciones, sal aleatoria de 16 bytes,
  comparación en tiempo constante (`Pbkdf2PasswordHasher`). Nunca se almacena la contraseña en claro.
  Aplica a operadores del POS (`AuthService`) y tutores del portal (`GuardianService`).
- **Bloqueo por intentos** (portal, FR-WP-3): la cuenta del tutor se bloquea 15 min tras 5 intentos
  fallidos; el bloqueo se limpia al iniciar sesión correctamente o al restablecer la contraseña.
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

## Control de acceso
- **POS**: roles cajero/almacén/administrador; pantallas sensibles (inventario, reportes, bitácora,
  descuentos) restringidas por rol.
- **Portal**: cookies de autenticación; el panel del proveedor (comisiones) exige una política
  `Vendor` separada de las cuentas de padres.

## Cobertura de pruebas (relevantes a seguridad)
- Firma de webhook: válida / id alterado / secreto incorrecto / malformada / secreto vacío.
- Gateway: firma inválida o ausente → `null` sin llamar a la API.
- Recuperación: token correcto / incorrecto / caducado / reutilizado (un solo uso); cambio de
  contraseña exige la actual.
- Bloqueo tras 5 intentos y expiración del bloqueo.
- Concurrencia / no-doble-gasto (SQLite ejecutable + SQL Server real gated).

## Pendiente / recomendaciones
- HTTPS/HSTS en producción (ya activado fuera de Development).
- Protección de datos de menores (LFPDPPP): revisar retención y consentimiento.
- Rotación de secretos (tokens de Mercado Pago) y almacenamiento seguro (no en `appsettings`).
- Rate-limiting del endpoint de webhook y de login.
