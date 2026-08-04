namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto de lectura de los emparejamientos Hoja de Salida ↔ pedimento del
/// Sistema de Salidas (§3.bis.5, §9 diseño). El <c>PedimentoSalidasWorker</c> los
/// consume para aplicar el pedimento a las facturas retenidas en
/// <c>PendientePedimento</c>. Dueño real: <c>Integraciones.Origenes</c> (vía
/// Hybrid Connection); en dev es stub vacío
/// (<c>PLATFORM-TODO(&lt;SalidasPedimentos&gt;)</c>).
/// </summary>
public interface ISalidasPedimentosReader
{
    /// <summary>Lee hasta <paramref name="max"/> pedimentos listos para aplicar a sus facturas.</summary>
    Task<IReadOnlyList<PedimentoSalida>> LeerPendientesAsync(int max, CancellationToken cancellationToken);
}

/// <summary>Pedimento emparejado a una factura de exportación retenida.</summary>
public sealed record PedimentoSalida(
    Guid FacturaVentaId,
    string Pedimento,
    DateOnly? FechaDocAduanero,
    string? IdentificacionMercancia);
