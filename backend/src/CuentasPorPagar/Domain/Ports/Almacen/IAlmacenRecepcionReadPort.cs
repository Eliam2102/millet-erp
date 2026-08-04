namespace Millet.CuentasPorPagar.Domain.Ports.Almacen;

/// <summary>
/// Puerto de lectura sobre el módulo Almacén para validar three-way
/// match (OC + Recepción + Factura). CxP lo consume como insumo
/// informativo: si la recepción no existe, la captura no se bloquea en
/// v1 (§1.2 punto último del 01-diseno).
///
/// <para>
/// F0-PR1 introduce el contrato + stub <c>NoOpAlmacenRecepcionReadPort</c>
/// (devuelve <c>null</c>). PLATFORM-TODO(&lt;AlmacenRecepcion&gt;):
/// adapter real cuando el módulo Almacén esté en runtime.
/// </para>
/// </summary>
public interface IAlmacenRecepcionReadPort
{
    Task<RecepcionOcDto?> ObtenerRecepcionDeOcAsync(Guid ordenCompraId, CancellationToken cancellationToken);
}

public sealed record RecepcionOcDto(
    Guid RecepcionId,
    Guid OrdenCompraId,
    DateOnly FechaRecepcion,
    bool FacturaPendiente,
    IReadOnlyList<LineaRecepcionDto> Lineas);

public sealed record LineaRecepcionDto(
    Guid LineaOcId,
    decimal CantidadRecibida);
