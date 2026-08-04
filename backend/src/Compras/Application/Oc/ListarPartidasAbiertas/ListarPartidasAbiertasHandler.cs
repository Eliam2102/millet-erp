using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Oc.ListarPartidasAbiertas;

/// <summary>
/// Handler de <see cref="ListarPartidasAbiertasQuery"/> (F7-PR1).
/// Filtra OCs en estados no terminales con al menos una dimensión
/// abierta. La detección de "abierta" es el filtro raíz; los demás
/// filtros refinan dentro.
/// </summary>
public sealed class ListarPartidasAbiertasHandler
    : IRequestHandler<ListarPartidasAbiertasQuery, ListarPartidasAbiertasResponse>
{
    private const int MaxPageSize = 500;

    private static readonly EstadoOrdenCompra[] EstadosTerminales =
    [
        EstadoOrdenCompra.Cerrada,
        EstadoOrdenCompra.Cancelada,
        EstadoOrdenCompra.Rechazada,
    ];

    private readonly ComprasDbContext _db;
    private readonly IClock _clock;

    public ListarPartidasAbiertasHandler(ComprasDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ListarPartidasAbiertasResponse> Handle(
        ListarPartidasAbiertasQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize switch
        {
            < 1 => 50,
            > MaxPageSize => MaxPageSize,
            _ => request.PageSize,
        };

        // ADR-0040 (D2): el "hoy" de una fecha de negocio se calcula en hora
        // local de México, no UTC. "Atrasada" = fecha_entrega < hoy (por día).
        var hoy = _clock.HoyLocal();

        var query = _db.OrdenesCompra
            .AsNoTracking()
            .Where(o => !EstadosTerminales.Contains(o.Estado))
            .Where(o =>
                o.SubEstadoRecepcion != SubEstadoRecepcion.Completa
                || o.SubEstadoFacturacion != SubEstadoFacturacion.Completa
                || o.SubEstadoPago != SubEstadoPago.Pagada);

        if (request.Estado is { } estado)
        {
            query = query.Where(o => o.Estado == estado);
        }
        if (request.SubEstadoRecepcion is { } sr)
        {
            query = query.Where(o => o.SubEstadoRecepcion == sr);
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
        if (!string.IsNullOrWhiteSpace(request.NumeroContenedor))
        {
            var c2 = request.NumeroContenedor.Trim();
            query = query.Where(o => o.InfoImportNumeroContenedor != null
                && o.InfoImportNumeroContenedor.Contains(c2));
        }
        if (!string.IsNullOrWhiteSpace(request.CodigoRuta))
        {
            var r = request.CodigoRuta.Trim();
            query = query.Where(o => o.InfoImportCodigoRuta != null
                && o.InfoImportCodigoRuta.Contains(r));
        }
        if (!string.IsNullOrWhiteSpace(request.SemanaEmbarque))
        {
            var s = request.SemanaEmbarque.Trim();
            query = query.Where(o => o.InfoImportSemanaEmbarque != null
                && o.InfoImportSemanaEmbarque.Contains(s));
        }
        if (request.DiasAtrasadosMinimos is int minDias && minDias > 0)
        {
            // Días atrasados = (hoy - fecha_entrega_esperada) en días de
            // calendario. Solo aplica si fecha_entrega_esperada está seteada.
            var umbral = hoy.AddDays(-minDias);
            query = query.Where(o => o.FechaEntregaEsperada != null
                && o.FechaEntregaEsperada < umbral);
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderBy(o => o.FechaEntregaEsperada ?? o.FechaDocumento)
            // <c>EF.Property&lt;string&gt;(o, "Folio")</c> — mismo motivo que
            // en <see cref="ListarOrdenesCompra.ListarOrdenesCompraHandler"/>:
            // EF Core 9 no traduce <c>o.Folio.Valor</c> en OrderBy (VO con
            // HasConversion).
            .ThenBy(o => EF.Property<string>(o, "Folio"))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                FolioValor = o.Folio.Valor,
                o.FolioAnio,
                o.Estado,
                o.SubEstadoRecepcion,
                o.SubEstadoFacturacion,
                o.SubEstadoPago,
                o.ProveedorId,
                o.CompradorTitularId,
                o.Moneda,
                o.FechaDocumento,
                o.FechaEntregaEsperada,
                o.ReferenciaProveedor,
                o.InfoImportNumeroContenedor,
                o.InfoImportCodigoRuta,
                o.InfoImportSemanaEmbarque,
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new PartidaAbiertaResumen(
            r.Id,
            r.FolioValor,
            r.FolioAnio,
            r.Estado,
            r.SubEstadoRecepcion,
            r.SubEstadoFacturacion,
            r.SubEstadoPago,
            r.ProveedorId,
            r.CompradorTitularId,
            r.Moneda,
            r.FechaDocumento,
            r.FechaEntregaEsperada,
            CalcularDiasAtrasados(r.FechaEntregaEsperada, hoy),
            r.ReferenciaProveedor,
            r.InfoImportNumeroContenedor,
            r.InfoImportCodigoRuta,
            r.InfoImportSemanaEmbarque)).ToList();

        return new ListarPartidasAbiertasResponse(items, page, pageSize, total);
    }

    private static int CalcularDiasAtrasados(DateOnly? fechaEntrega, DateOnly hoy)
    {
        if (fechaEntrega is not DateOnly f) return 0;
        var dias = hoy.DayNumber - f.DayNumber;
        return dias <= 0 ? 0 : dias;
    }
}
