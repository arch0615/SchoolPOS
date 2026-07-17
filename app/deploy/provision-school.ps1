<#
.SYNOPSIS
  Provisiona una escuela: aplica migraciones a SQL Server, crea la escuela y el
  operador administrador. Idempotente si se pasa -SchoolId.

.EXAMPLE
  .\provision-school.ps1 -ConnectionString "Server=localhost\SQLEXPRESS;Database=SchoolPOS_ColegioX;Trusted_Connection=True;TrustServerCertificate=True;" `
      -SchoolName "Colegio X" -AdminPassword "Secreta123"
#>
param(
    [Parameter(Mandatory = $true)][string]$ConnectionString,
    [Parameter(Mandatory = $true)][string]$SchoolName,
    [Parameter(Mandatory = $true)][string]$AdminPassword,
    [string]$AdminUser = "admin",
    [string]$Currency = "MXN",
    [string]$CommissionRate = "0.05",
    [string]$TaxRate = "0",
    [string]$SchoolId,
    [string]$Rfc,
    [string]$LegalName,
    [string]$TaxRegime,
    [string]$PostalCode,
    [string]$CfdiUse
)

$ErrorActionPreference = "Stop"
$toolPath = Join-Path $PSScriptRoot "..\tools\SchoolPOS.Provision"

$toolArgs = @(
    "--Provider", "SqlServer",
    "--ConnectionString", $ConnectionString,
    "--SchoolName", $SchoolName,
    "--AdminUser", $AdminUser,
    "--AdminPassword", $AdminPassword,
    "--Currency", $Currency,
    "--CommissionRate", $CommissionRate,
    "--TaxRate", $TaxRate
)
if ($SchoolId)   { $toolArgs += @("--SchoolId", $SchoolId) }
if ($Rfc)        { $toolArgs += @("--Rfc", $Rfc) }
if ($LegalName)  { $toolArgs += @("--LegalName", $LegalName) }
if ($TaxRegime)  { $toolArgs += @("--TaxRegime", $TaxRegime) }
if ($PostalCode) { $toolArgs += @("--PostalCode", $PostalCode) }
if ($CfdiUse)    { $toolArgs += @("--CfdiUse", $CfdiUse) }

dotnet run --project $toolPath -c Release -- @toolArgs
