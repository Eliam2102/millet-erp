using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Oc.ListarOrdenesCompra;
using Millet.Compras.Domain;
using Millet.Compras.Application.Listar;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Oc.ListarPendientesAutorizacion;

public sealed class ListarPendientesAutorizacionOcHandler
    : IRequestHandler<ListarPendientesAutorizacionOcQuery, ListarOrdenesCompraResponse>
{
    private const int MaxPageSize = 200;

    private readonly ComprasDbContext _db;
    private readonly ICurrentUserContext? _user;
    private readonly ICurrentUserPermissions? _permisos;

    public ListarPendientesAutorizacionOcHandler(ComprasDbContext db, ICurrentUserContext? user = null, ICurrentUserPermissions? permisos = null)
    {
        _db = db;
        _user = user;
        _permisos = permisos;
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

        if (request.SoloAccionables)
        {
            var usuarioId = _user?.UserId ?? throw new UnauthorizedAccessException("Sin usuario autenticado.");
            var n1 = _permisos is not null && await _permisos.TieneAsync("compras.ordenes.autorizar-nivel1", cancellationToken);
            var n2 = _permisos is not null && await _permisos.TieneAsync("compras.ordenes.autorizar-nivel2", cancellationToken);
            // Los avisos son tareas que puede firmar esta persona, no sus propias OCs.
            query = query.Where(o => o.CompradorTitularId != usuarioId &&
                ((n1 && o.Estado == EstadoOrdenCompra.EnAutorizacionJefeCompras) ||
                 (n2 && o.Estado == EstadoOrdenCompra.EnAutorizacionDireccion &&
                    !o.Autorizaciones.Any(a => a.Ciclo == o.CicloAutorizacion && a.Nivel == NivelAutorizacion.Nivel1 && a.UsuarioId == usuarioId))));
        }

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
