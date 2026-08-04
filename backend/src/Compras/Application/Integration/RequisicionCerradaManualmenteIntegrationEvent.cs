using Millet.Compras.Domain;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration;

/// <summary>
/// Integration event: el jefe de almacén / almacenista cerró manualmente la
/// requisición (ADR-0043 R3) — el requisitante ya no necesita el material.
/// EventType <c>compras.requisicion.cerrada-manualmente.v1</c>.
///
/// <para>Distinto de <c>compras.requisicion.cerrada.v1</c> (cierre por surtido
/// completo): aquí <see cref="EstadoFinal"/> es
/// <see cref="EstadoRequisicion.CerradaSinSurtir"/> o
/// <see cref="EstadoRequisicion.CerradaSurtidaParcial"/>, y lleva motivo +
/// actor. El material no entregado queda como stock libre.</para>
/// </summary>
public sealed record RequisicionCerradaManualmenteIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RequisicionId,
    EstadoRequisicion EstadoFinal,
    Guid MotivoId,
    string? MotivoTexto,
    Guid ActorId)
    : IntegrationEvent("compras.requisicion.cerrada-manualmente.v1", EmpresaId, OcurridoEn);
