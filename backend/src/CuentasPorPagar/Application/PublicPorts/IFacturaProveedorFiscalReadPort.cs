namespace Millet.CuentasPorPagar.Application.PublicPorts;

/// <summary>K10.7 · datos fiscales de facturas de proveedor para otros módulos: método de pago del CFDI y REP recibido.</summary>
public sealed record FacturaProveedorFiscal(Guid Id, string? Uuid, string? MetodoPago, bool ReppRecibido);

public interface IFacturaProveedorFiscalReadPort
{
    Task<IReadOnlyList<FacturaProveedorFiscal>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
