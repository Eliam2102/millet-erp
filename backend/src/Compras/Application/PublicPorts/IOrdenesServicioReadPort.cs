using Millet.Compras.Domain.Oc;
namespace Millet.Compras.Application.PublicPorts;

public sealed record LineaServicioLectura(Guid Id, string Descripcion, decimal Importe);
public sealed record OrdenServicioLectura(Guid Id, Guid EmpresaId, Guid SucursalId, string Folio,
    Guid ProveedorId, string Proveedor, string Moneda, decimal Total, EstadoOrdenCompra Estado,
    Guid? CotizacionAdjuntoId, string? Contenedor, IReadOnlyList<LineaServicioLectura> Lineas)
{
    public bool TieneCotizacion => CotizacionAdjuntoId is not null;
}
public interface IOrdenesServicioReadPort
{
    Task<OrdenServicioLectura?> ObtenerAsync(Guid empresaId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<OrdenServicioLectura>> BuscarAsync(Guid empresaId, Guid sucursalId, Guid? proveedorId, string? texto, CancellationToken ct);
}
