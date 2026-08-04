using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Application.Oc.ListarOrdenesCompra;

public sealed class ListarOrdenesCompraHandler
    : IRequestHandler<ListarOrdenesCompraQuery, ListarOrdenesCompraResponse>
{
    private const int MaxPageSize = 200;

    private readonly ComprasDbContext _db;
    private readonly IProveedorReadPort _proveedores;

    public ListarOrdenesCompraHandler(ComprasDbContext db, IProveedorReadPort proveedores)
    {
        _db = db;
        _proveedores = proveedores;
    }

    public async Task<ListarOrdenesCompraResponse> Handle(
        ListarOrdenesCompraQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize switch
        {
            < 1 => 50,
            > MaxPageSize => MaxPageSize,
            _ => request.PageSize,
        };

        var query = _db.OrdenesCompra.AsNoTracking();

        if (request.Estado is { } estado)
        {
            query = query.Where(o => o.Estado == estado);
        }
        if (request.SubEstadoRecepcion is { } sr)
        {
            query = query.Where(o => o.SubEstadoRecepcion == sr);
        }
        if (request.SoloConPendienteRecepcion == true)
        {
            // "Con pendiente de recepción" = no recibida al 100%. La columna
            // es non-null (default SinRecepcion) y Completa ⟺ todas las líneas
            // recibidas, así que != Completa cubre SinRecepcion + Parcial.
            query = query.Where(o => o.SubEstadoRecepcion != Domain.Oc.SubEstadoRecepcion.Completa);
        }
        if (request.SubEstadoFacturacion is { } sf)
        {
            query = query.Where(o => o.SubEstadoFacturacion == sf);
        }
        if (request.SubEstadoPago is { } sp)
        {
            query = query.Where(o => o.SubEstadoPago == sp);
        }
        if (request.ProveedorId is Guid p)
        {
            query = query.Where(o => o.ProveedorId == p);
        }
        if (request.CompradorTitularId is Guid c)
        {
            query = query.Where(o => o.CompradorTitularId == c);
        }
        if (request.FechaDocumentoDesde is DateOnly desde)
        {
            query = query.Where(o => o.FechaDocumento >= desde);
        }
        if (request.FechaDocumentoHasta is DateOnly hasta)
        {
            query = query.Where(o => o.FechaDocumento <= hasta);
        }
        if (!string.IsNullOrWhiteSpace(request.ReferenciaProveedor))
        {
            var ref_ = request.ReferenciaProveedor.Trim().ToUpperInvariant();
            query = query.Where(o => o.ReferenciaProveedor != null
                && o.ReferenciaProveedor.Contains(ref_));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(o => o.FechaDocumento)
            // <c>EF.Property&lt;string&gt;(o, "Folio")</c> en lugar de
            // <c>o.Folio.Valor</c>: <see cref="Folio"/> es value object
            // mapeado vía <c>HasConversion</c> y EF Core 9 NO traduce
            // accesos a <c>.Valor</c> dentro de LINQ — produce
            // <c>InvalidOperationException</c> "could not be translated"
            // al compilar el query. <c>EF.Property</c> resuelve directo
            // contra la columna <c>folio</c>. Cuidado: si Folio se
            // promueve a OwnsOne/Complex Type en el futuro, este shape
            // también deja de funcionar — habría que indexar por
            // <c>"Folio_Valor"</c> o promover Folio a IComparable y
            // ordenar por el VO directamente.
            .ThenByDescending(o => EF.Property<string>(o, "Folio"))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrdenCompraResumen(
                o.Id,
                o.Folio.Valor,
                o.FolioAnio,
                o.Estado,
                o.SubEstadoRecepcion,
                o.SubEstadoFacturacion,
                o.SubEstadoPago,
                o.ProveedorId,
                // ProveedorNombre se resuelve post-página vía read-port batch.
                (string?)null,
                o.SucursalDestinoId,
                o.CompradorTitularId,
                o.Moneda,
                o.FechaDocumento,
                o.ReferenciaProveedor))
            .ToListAsync(cancellationToken);

        // Enriquecer la etiqueta del proveedor (id → razón social) vía read-port
        // batch sobre los proveedores DISTINTOS de la página — mismo molde que
        // ObtenerOrdenCompraPorIdHandler (ADR-0042). El proveedor vive en
        // compartido.proveedores; lo resuelve el servidor, no el cliente (que
        // solo tendría un catálogo capado). Fallback null si no resuelve.
        var proveedorIds = items.Select(i => i.ProveedorId).Distinct().ToArray();
        if (proveedorIds.Length > 0)
        {
            var proveedores = await _proveedores.ObtenerPorIdsAsync(proveedorIds, cancellationToken);
            items = items
                .Select(i => proveedores.TryGetValue(i.ProveedorId, out var p)
                    ? i with { ProveedorNombre = p.RazonSocial }
                    : i)
                .ToList();
        }

        return new ListarOrdenesCompraResponse(items, page, pageSize, total);
    }
}
