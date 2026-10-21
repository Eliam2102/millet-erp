namespace Millet.Facturacion.Domain.Ports;
public sealed record VentaActivoLectura(Guid EmpresaId, string ActivoRef, decimal Precio, DateOnly Fecha, bool Cancelada);
public interface IVentaActivoReadPort
{
    Task<VentaActivoLectura?> ObtenerAsync(Guid factura, CancellationToken ct);
}
