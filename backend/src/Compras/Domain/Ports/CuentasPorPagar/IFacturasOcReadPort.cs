namespace Millet.Compras.Domain.Ports.CuentasPorPagar;

/// <summary>Factura de proveedor ligada a una OC; incluye canceladas (el consumidor filtra por <see cref="Estado"/>).</summary>
public sealed record FacturaOcLectura(
    Guid FacturaId,
    Guid OcId,
    string? Serie,
    string? Folio,
    DateOnly Fecha,
    string Moneda,
    decimal Total,
    decimal DiferenciaContraOc,
    string Estado);

/// <summary>Lectura de solo consulta de facturas de CxP por OC (reporte de cumplimiento).</summary>
public interface IFacturasOcReadPort
{
    Task<IReadOnlyList<FacturaOcLectura>> ListarPorOcAsync(
        IReadOnlyCollection<Guid> ocIds,
        CancellationToken ct);
}
