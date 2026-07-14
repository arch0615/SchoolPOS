#!/usr/bin/env bash
# Provisiona una escuela: aplica migraciones a SQL Server, crea la escuela y el
# operador administrador. Idempotente si se define SCHOOL_ID.
#
# Uso:
#   CONN="Server=localhost;Database=SchoolPOS_ColegioX;User Id=sa;Password=...;TrustServerCertificate=True;" \
#   SCHOOL_NAME="Colegio X" ADMIN_PASSWORD="Secreta123" ./provision-school.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TOOL="$SCRIPT_DIR/../tools/SchoolPOS.Provision"

: "${CONN:?Define CONN (cadena de conexión a SQL Server)}"
: "${ADMIN_PASSWORD:?Define ADMIN_PASSWORD}"

ARGS=(
  --Provider SqlServer
  --ConnectionString "$CONN"
  --SchoolName "${SCHOOL_NAME:-Colegio X}"
  --AdminUser "${ADMIN_USER:-admin}"
  --AdminPassword "$ADMIN_PASSWORD"
  --Currency "${CURRENCY:-MXN}"
  --CommissionRate "${COMMISSION_RATE:-0.05}"
  --TaxRate "${TAX_RATE:-0}"
)
if [[ -n "${SCHOOL_ID:-}" ]]; then
  ARGS+=(--SchoolId "$SCHOOL_ID")
fi

dotnet run --project "$TOOL" -c Release -- "${ARGS[@]}"
