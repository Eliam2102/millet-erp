using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Listar;
using Millet.Compras.Application.Oc.ListarOrdenesCompra;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Oc.ListarPendientesAutorizacion;

public sealed class ListarPendientesAutorizacionOcHandler
    : IRequestHandler<ListarPendientesAutorizacionOcQuery, ListarOrdenesCompraResponse>
{
    private const int MaxPageSize = 200;

    private readonly ComprasDbContext _db;
    private readonly IProveedorReadPort _proveedores;
    private readonly EvaluadorNivelOc _evaluadorNivel;
    private readonly IClock _clock;
    private readonly ICurrentUserContext? _user;
    private readonly ICurrentUserPermissions? _permisos;

    public ListarPendientesAutorizacionOcHandler(
        ComprasDbContext db,
        IProveedorReadPort proveedores,
        EvaluadorNivelOc evaluadorNivel,
        IClock clock,
        ICurrentUserContext? user = null,
        ICurrentUserPermissions? permisos = null)
    {
        _db = db;
        _proveedores = proveedores;
        _evaluadorNivel = evaluadorNivel;
        _clock = clock;
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
                // ProveedorNombre: se completa en EnriquecerAsync (read-port batch).
                (string?)null,
                o.SucursalDestinoId,
                o.CompradorTitularId,
                o.Moneda,
                o.FechaDocumento,
                o.ReferenciaProveedor))
            .ToListAsync(cancellationToken);

        if (items.Count > 0)
        {
            items = await EnriquecerAsync(items, cancellationToken);
        }

        return new ListarOrdenesCompraResponse(items, page, pageSize, total);
    }

    /// <summary>
    /// Completa los campos de la bandeja con consultas por lote (una por tabla, sin N+1):
    /// proveedor (read-port), líneas/totales, matriz de nivel, recurrencia (60 días) y
    /// proveedor conocido (con OC Autorizada/Cerrada previa).
    /// </summary>
    private async Task<List<OrdenCompraResumen>> EnriquecerAsync(
        List<OrdenCompraResumen> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(i => i.Id).ToArray();
        var proveedorIds = items.Select(i => i.ProveedorId).Distinct().ToArray();

        var ocs = await _db.OrdenesCompra
            .AsNoTracking()
            .Include(o => o.Lineas)
            .Where(o => ids.Contains(o.Id))
            .ToListAsync(cancellationToken);
        var porId = ocs.ToDictionary(o => o.Id);

        var proveedores = await _proveedores.ObtenerPorIdsAsync(proveedorIds, cancellationToken);
        var decisiones = await _evaluadorNivel.EvaluarAsync(ocs, cancellationToken);

        var desde = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime).AddDays(-PendientesAutorizacionClasificacion.VentanaRecurrenciaDias);
        var conteoRecurrencia = await _db.OrdenesCompra
            .AsNoTracking()
            .Where(o => proveedorIds.Contains(o.ProveedorId)
                        && o.FechaDocumento >= desde
                        && o.Estado != EstadoOrdenCompra.Cancelada)
            .GroupBy(o => new { o.ProveedorId, o.CompradorTitularId })
            .Select(g => new { g.Key.ProveedorId, g.Key.CompradorTitularId, Conteo = g.Count() })
            .ToListAsync(cancellationToken);

        var proveedoresConocidos = (await _db.OrdenesCompra
            .AsNoTracking()
            .Where(o => proveedorIds.Contains(o.ProveedorId)
                        && (o.Estado == EstadoOrdenCompra.Autorizada || o.Estado == EstadoOrdenCompra.Cerrada))
            .Select(o => o.ProveedorId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        return items.Select(i =>
        {
            if (!porId.TryGetValue(i.Id, out var oc) || !decisiones.TryGetValue(oc.Id, out var decision))
            {
                return i;
            }

            var recurrente = conteoRecurrencia.Any(c =>
                c.ProveedorId == i.ProveedorId
                && c.CompradorTitularId == i.CompradorTitularId
                && PendientesAutorizacionClasificacion.EsRecurrente(c.Conteo));

            return i with
            {
                ProveedorNombre = proveedores.TryGetValue(i.ProveedorId, out var p) ? p.RazonSocial : null,
                TotalAPagar = oc.CalcularTotales().TotalAPagar,
                CantidadTotal = oc.Lineas.Sum(l => l.Cantidad),
                NumeroLineas = oc.Lineas.Count,
                EsMenorCuantia = decision.EsMenorCuantia,
                EsRecurrente = recurrente,
                ProveedorConocido = proveedoresConocidos.Contains(i.ProveedorId),
                // Persistido al enviar a autorización (matriz de nivel); no se recalcula aquí.
                RequiereNivel2 = oc.RequiereNivel2,
            };
        }).ToList();
    }
}
