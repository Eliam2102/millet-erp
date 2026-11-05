namespace Millet.Facturacion.Domain.Ports;
public sealed record VentaActivoLectura(Guid EmpresaId, string ActivoRef, decimal Precio, DateOnly Fecha, bool Cancelada);
public interface IVentaActivoReadPort
{
    Task<VentaActivoLectura?> ObtenerAsync(Guid factura, CancellationToken ct);
}
public interface IDisposicionVentaReadPort
{
    Task<bool> TieneAbiertaAsync(string activoRef, CancellationToken ct);
    Task<IReadOnlyList<AutorizacionVentaHistorial>> HistorialAsync(string activoRef, CancellationToken ct);
}

public sealed record AutorizacionVentaHistorial(Guid Id, int Version, string Estado, string? MotivoRechazo, Guid AutorizadoPor, DateTimeOffset FechaAutorizacion, DateTimeOffset? RechazadoEn);
