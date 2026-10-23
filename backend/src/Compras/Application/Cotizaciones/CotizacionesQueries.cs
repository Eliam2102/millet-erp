using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Oc;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Cotizaciones;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Inicio;

namespace Millet.Compras.Application.Cotizaciones;

public sealed record PartidaComparativoResponse(
    Guid LineaRequisicionId, short Posicion, Guid ArticuloId, string? ArticuloClave, string? ArticuloNombre,
    decimal CantidadDeCompra, decimal SaldoPorComprar, string Unidad);

public sealed record TipoCambioComparativoResponse(string Moneda, DateOnly Fecha, decimal? ValorEnMxn);

public sealed record LineaComparativoResponse(
    Guid LineaRequisicionId, string UnidadCotizada, decimal FactorAUnidadRq, decimal PrecioUnitario, int? DiasEntrega,
    decimal PrecioPorUnidadRq, decimal? PrecioMxnPorUnidadRq, decimal? ImporteMxn, bool EsMenorPrecio);

public sealed record CotizacionComparativoResponse(
    Guid Id, Guid ProveedorId, string? ProveedorClave, string? ProveedorRazonSocial, string? ProveedorRfc,
    bool ProveedorActivo, string? FolioProveedor, string Moneda, DateOnly FechaCotizacion, DateOnly VigenteHasta,
    bool Vigente, string? CondicionesCredito, Guid? ArchivoAdjuntoId, string? ArchivoNombre, bool Elegida,
    string? MotivoEleccion, Guid? ElegidaPor, DateTimeOffset? ElegidaEn, Guid? OrdenCompraId, Guid RegistradaPor,
    DateTimeOffset RegistradaEn, int Version, IReadOnlyList<LineaComparativoResponse> Lineas, Guid? CondicionesPagoId, DateOnly? FechaEntrega);

public sealed record ComparativoCotizacionesResponse(
    Guid RequisicionId, string RequisicionFolio, Guid SucursalId, EstadoRequisicion EstadoRequisicion,
    Guid? ComprometidaEnOcId, bool PuedeRegistrar, bool ComparativoCerrado, DateOnly Hoy,
    IReadOnlyList<PartidaComparativoResponse> Partidas, IReadOnlyList<TipoCambioComparativoResponse> TiposCambio,
    IReadOnlyList<CotizacionComparativoResponse> Cotizaciones);

public sealed record ComparativoCotizacionesQuery(Guid RequisicionId) : IRequest<ComparativoCotizacionesResponse>;

public sealed class ComparativoCotizacionesHandler(
    ComprasDbContext db, CompartidoDbContext compartido, IArticuloReadPort articulos, IClock clock)
    : IRequestHandler<ComparativoCotizacionesQuery, ComparativoCotizacionesResponse>
{
    public async Task<ComparativoCotizacionesResponse> Handle(ComparativoCotizacionesQuery request, CancellationToken cancellationToken)
    {
        var rq = await db.Requisiciones.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException("REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{request.RequisicionId}'.");
        var hoy = FechasInicio.Hoy(clock);

        // Proyección (no se materializa el VO Money de la línea).
        var lineasRq = await db.LineaRequisiciones.AsNoTracking()
            .Where(l => l.RequisicionId == rq.Id && l.CantidadDeCompra > 0).OrderBy(l => l.Posicion)
            .Select(l => new { l.Id, l.Posicion, l.ArticuloId, l.CantidadDeCompra, l.UnidadMedida }).ToListAsync(cancellationToken);
        var saldos = await SaldoCompraRq.ObtenerAsync(db, lineasRq.Select(l => l.Id), cancellationToken);
        var arts = await articulos.ObtenerPorIdsAsync(lineasRq.Select(l => l.ArticuloId).Distinct().ToArray(), cancellationToken);
        var partidas = lineasRq.Select(l =>
        {
            arts.TryGetValue(l.ArticuloId, out var a);
            return new PartidaComparativoResponse(l.Id, l.Posicion, l.ArticuloId, a?.Clave, a?.Nombre,
                l.CantidadDeCompra, saldos.GetValueOrDefault(l.Id), l.UnidadMedida);
        }).ToList();
        var porLinea = lineasRq.ToDictionary(l => l.Id);

        var cotizaciones = await db.Cotizaciones.AsNoTracking().Include(c => c.Lineas)
            .Where(c => c.RequisicionId == request.RequisicionId).OrderBy(c => c.RegistradaEn).ToListAsync(cancellationToken);

        var provIds = cotizaciones.Select(c => c.ProveedorId).Distinct().ToArray();
        var proveedores = await compartido.Proveedores.AsNoTracking().Where(p => provIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Clave, p.RazonSocial, p.Rfc, Activo = p.Estatus == EstatusCatalogo.Activo })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var cotIds = cotizaciones.Select(c => c.Id).ToArray();
        var archivos = (await compartido.Adjuntos.AsNoTracking()
            .Where(a => a.TipoEntidad == "cotizacion_compra" && cotIds.Contains(a.EntidadId) && a.BajaEn == null)
            .Select(a => new { a.EntidadId, a.Id, a.NombreArchivo, a.SubidoEn }).ToListAsync(cancellationToken))
            .GroupBy(a => a.EntidadId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.SubidoEn).First());

        var tipos = new List<TipoCambioComparativoResponse>();
        foreach (var moneda in cotizaciones.Select(c => c.Moneda).Where(m => m != "MXN").Distinct().Order())
        {
            var valor = await (from tc in compartido.TiposCambio.AsNoTracking()
                               join m in compartido.Monedas.AsNoTracking() on tc.MonedaId equals m.Id
                               where m.Codigo == moneda && tc.Fecha == hoy
                               select (decimal?)tc.ValorEnMxn).FirstOrDefaultAsync(cancellationToken);
            tipos.Add(new TipoCambioComparativoResponse(moneda, hoy, valor));
        }

        // Precio MXN por partida y cotización; el menor se calcula solo entre elegibles (vigente, con PDF y proveedor activo) con precio MXN.
        var precios = cotizaciones.ToDictionary(c => c.Id, c =>
        {
            var tc = tipos.FirstOrDefault(t => t.Moneda == c.Moneda)?.ValorEnMxn;
            return c.Lineas.ToDictionary(l => l.LineaRequisicionId,
                l => NormalizadorCotizacion.PrecioMxn(l.PrecioPorUnidadRequisicion, c.Moneda, tc));
        });
        bool Elegible(Cotizacion c) => c.EsVigente(hoy) && archivos.ContainsKey(c.Id)
            && proveedores.TryGetValue(c.ProveedorId, out var pv) && pv.Activo;
        var minimos = lineasRq.ToDictionary(l => l.Id, l => cotizaciones
            .Where(c => Elegible(c) && precios[c.Id].GetValueOrDefault(l.Id) is not null)
            .Select(c => (decimal?)precios[c.Id][l.Id]!.Value).Min());

        var respuesta = cotizaciones.Select(c =>
        {
            proveedores.TryGetValue(c.ProveedorId, out var p);
            archivos.TryGetValue(c.Id, out var arch);
            var lineas = c.Lineas.Where(l => porLinea.ContainsKey(l.LineaRequisicionId))
                .OrderBy(l => porLinea[l.LineaRequisicionId].Posicion).Select(l =>
                {
                    var mxn = precios[c.Id][l.LineaRequisicionId];
                    var menor = Elegible(c) && mxn is not null && mxn.Value == minimos[l.LineaRequisicionId];
                    return new LineaComparativoResponse(l.LineaRequisicionId, l.UnidadCotizada, l.FactorAUnidadRq,
                        l.PrecioUnitario, l.DiasEntrega, l.PrecioPorUnidadRequisicion, mxn,
                        mxn is decimal m ? Math.Round(m * porLinea[l.LineaRequisicionId].CantidadDeCompra, 2, MidpointRounding.ToEven) : null,
                        menor);
                }).ToList();
            return new CotizacionComparativoResponse(c.Id, c.ProveedorId, p?.Clave, p?.RazonSocial, p?.Rfc,
                p?.Activo ?? false, c.FolioProveedor, c.Moneda, c.FechaCotizacion, c.VigenteHasta, c.EsVigente(hoy),
                c.CondicionesCredito, arch?.Id, arch?.NombreArchivo, c.Elegida, c.MotivoEleccion, c.ElegidaPor,
                c.ElegidaEn, c.OrdenCompraId, c.RegistradaPor, c.RegistradaEn, c.Version, lineas, c.CondicionesPagoId, c.FechaEntrega);
        }).ToList();

        // Cerrado = hay cotización elegida con OC vigente (al cancelar la OC la elección se libera).
        var cerrado = cotizaciones.Any(c => c.Elegida);
        return new ComparativoCotizacionesResponse(rq.Id, rq.Folio.Valor, rq.SucursalId, rq.Estado, rq.ComprometidaEnOcId,
            !cerrado && rq.Estado == EstadoRequisicion.EnSurtido && rq.ComprometidaEnOcId is null && partidas.Any(x => x.SaldoPorComprar > 0),
            cerrado, hoy, partidas, tipos, respuesta);
    }
}
