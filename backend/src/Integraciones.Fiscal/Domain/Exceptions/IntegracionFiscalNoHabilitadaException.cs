using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.Integraciones.Fiscal.Domain.Exceptions;

/// <summary>
/// El adaptador del SDK de FiscalAPI no puede operar por configuración del
/// ambiente (<c>IntegracionesFiscal:Sdk:Disabled=true</c> o <c>TenantKey</c>
/// ausente). No es un fallo del proveedor ni de la configuración PAC de la
/// empresa. Mapea a HTTP 422 (ADR-0010).
/// </summary>
public sealed class IntegracionFiscalNoHabilitadaException(string motivo) : DomainException(motivo)
{
    public override string Code => "INTEGRACION_FISCAL_NO_HABILITADA";
}
