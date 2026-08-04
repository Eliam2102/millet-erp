using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// EventType <c>cuentas_por_pagar.deposito-viaticos.esperado.v1</c>
/// (GI-PR4, doc 12 §D4/Q3). Se publica al liberar una comprobación de
/// viáticos con <b>diferencia negativa</b>: el empleado gastó menos que
/// el anticipo y debe devolver la diferencia. Tesorería lo proyecta como
/// expectativa de depósito (RN-6) para conciliarlo cuando el depósito
/// del empleado aparezca en el banco.
/// Espejo: Tesoreria/Application/EventListeners/ContratosEspejoCxp.cs.
/// </summary>
public sealed record DepositoViaticosEsperadoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid SolicitudViaticosId,
    Guid EmpleadoId,
    decimal MontoEsperado,
    string Moneda)
    : IntegrationEvent(WireEventType, EmpresaId, OcurridoEn)
{
    public const string WireEventType = "cuentas_por_pagar.deposito-viaticos.esperado.v1";
}
