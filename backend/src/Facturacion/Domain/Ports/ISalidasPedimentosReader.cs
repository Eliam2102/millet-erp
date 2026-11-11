namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto de lectura de los pedimentos listos para las facturas retenidas en
/// <c>PendientePedimento</c> (§3.bis.5). M-216: la fuente es el expediente de
/// Comercio exterior (el Sistema de Salidas se absorbe en el ERP, D03). El
/// <c>PedimentoSalidasWorker</c> los consume y aplica con <c>AplicarPedimentoCommand</c>.
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
