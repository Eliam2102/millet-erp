using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Oc.ListarOrdenesCompra;
using Millet.Compras.Domain;
using Millet.Compras.Application.Listar;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Application.Oc.ListarPendientesAutorizacion;

public sealed class ListarPendientesAutorizacionOcHandler
    : IRequestHandler<ListarPendientesAutorizacionOcQuery, ListarOrdenesCompraResponse>
{
    private const int MaxPageSize = 200;

    private readonly ComprasDbContext _db;

    public ListarPendientesAutorizacionOcHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task<ListarOrdenesCompraResponse> Handle(
        ListarPendientesAutorizacionOcQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize switch
        {
            < 1 => 50,
            > MaxPageSize => MaxPageSize,
            _ => request.PageSize,
        };

        var query = _db.OrdenesCompra
            .Where(x => request.SucursalesPermitidas == null || (request.SucursalesPermitidas ?? Array.Empty<Guid>()).Contains(x.SucursalDestinoId))
            .AsNoTracking();
        query = PendientesAutorizacionLectura.Ordenes(query,
            request.Nivel is null or NivelAutorizacion.Nivel1,
            request.Nivel is null or NivelAutorizacion.Nivel2);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(o => o.FechaDocumento) // FIFO en la bandeja: las más viejas arriba.
            // <c>EF.Property&lt;string&gt;(o, "Folio")</c> — mismo motivo que
            // en <see cref="ListarOrdenesCompra.ListarOrdenesCompraHandler"/>:
            // EF Core 9 no traduce <c>o.Folio.Valor</c> en OrderBy (VO con
            // HasConversion). Sí traduce en <c>.Select(...)</c> proyección.
            .ThenBy(o => EF.Property<string>(o, "Folio"))
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
                // ProveedorNombre: solo lo enriquece ListarOrdenesCompra.
                (string?)null,
                o.SucursalDestinoId,
                o.CompradorTitularId,
                o.Moneda,
                o.FechaDocumento,
                o.ReferenciaProveedor))
            .ToListAsync(cancellationToken);

        return new ListarOrdenesCompraResponse(items, page, pageSize, total);
    }
}
