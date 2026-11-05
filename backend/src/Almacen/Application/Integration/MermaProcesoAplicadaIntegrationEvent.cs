using Millet.Almacen.Domain.Mermas;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>
/// EventType <c>almacen.merma_proceso.aplicada.v1</c> (A4.4). Baja de producción en proceso: desperdicio de una mesa de
/// corte o merma por etapa ya autorizada. No mueve existencia de Almacén; el valor (<c>ValorMxn</c>) se absorbe en el costo
/// del PT del periodo (P-A4.1), no va a gasto aparte. Consumidor previsto: <b>Contabilidad</b> (póliza, C1.5).
/// <c>Tipo</c> = DESPERDICIO_MESA | MERMA; <c>Mesa</c> solo en el desperdicio.
/// </summary>
public sealed record MermaProcesoAplicadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid MermaId,
    Guid SucursalId,
    string Tipo,
    DateOnly Fecha,
    string? Mesa,
    string Etapa,
    string Motivo,
    Guid ArticuloId,
    decimal? Kg,
    decimal M2,
    decimal ValorMxn,
    Guid? AutorizadoPor)
    : IntegrationEvent("almacen.merma_proceso.aplicada.v1", EmpresaId, OcurridoEn)
{
    public static MermaProcesoAplicadaIntegrationEvent De(MermaProceso m) => new(
        m.EmpresaId, DateTimeOffset.UtcNow, m.Id, m.SucursalId, m.Tipo, m.Fecha, m.Mesa, m.Etapa, m.Motivo, m.ArticuloId,
        m.Kg, m.M2, m.ValorMxn, m.AutorizadoPor);
}
