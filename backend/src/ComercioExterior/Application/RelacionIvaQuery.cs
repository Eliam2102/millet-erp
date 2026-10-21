using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compras.Application.PublicPorts;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Application;
public sealed record RelacionIvaQuery(int Anio, int Mes, Guid? SucursalId = null) : IRequest<RelacionIvaReporte>;
public sealed record RelacionIvaFila(string Periodo, string Expediente, string Tipo, string? Oc, string? Pedimento,
    string? Clave, string? Bl, string? Contenedor, string? Cliente, string? Pedido, string? Factura, string? Uuid,
    DateOnly? FechaEmbarque, string? EntradaAlmacen);
public sealed record ColumnaRelacionIva(string Clave, string Etiqueta, string Tipo = "texto");
public sealed record RelacionIvaReporte(string Titulo, DateTimeOffset GeneradoEn, IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ColumnaRelacionIva> Columnas, IReadOnlyList<RelacionIvaFila> Filas);
public sealed class RelacionIvaHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio,
    IPedidosEmbarcablesReadPort pedidos, IOcImportacionReadPort compras, IClock clock) : IRequestHandler<RelacionIvaQuery, RelacionIvaReporte>
{
    public async Task<RelacionIvaReporte> Handle(RelacionIvaQuery request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorRelacionIvaLeer, cancellationToken);
        if (request.Anio is < 2000 or > 9998 || request.Mes is < 1 or > 12)
            throw new BusinessRuleException("CE_PERIODO_INVALIDO", "Seleccione año y mes válidos.");
        var desde = new DateOnly(request.Anio, request.Mes, 1); var hasta = desde.AddMonths(1);
        var sucursales = await servicio.SucursalesAsync(request.SucursalId, cancellationToken);
        // Ambos contextos aplican el filtro SQL de empresa antes de materializar.
        var ocsPeriodo = await compras.ObtenerIdsPeriodoAsync(desde, hasta, sucursales, cancellationToken);
        var q = db.Expedientes.AsNoTracking();
        if (sucursales is not null) q = q.Where(e => sucursales.Contains(e.SucursalId));
        var expedientes = await q.Where(e => (e.Tipo == TipoExpediente.Importacion && e.OcId != null && ocsPeriodo.Contains(e.OcId.Value)) ||
                e.Contenedores.Any(c => c.FechaEmbarqueReal >= desde && c.FechaEmbarqueReal < hasta))
            .Include(e => e.Contenedores).Include(e => e.Partidas).AsSplitQuery().OrderBy(e => e.Folio).ToListAsync(cancellationToken);
        var origen = (await pedidos.ObtenerAsync(expedientes.SelectMany(e => e.Partidas).Select(p => p.PedidoFacturableId).Distinct().ToArray(), cancellationToken)).ToDictionary(p => p.Id);
        var ocs = (await compras.ObtenerVariasAsync(expedientes.Where(e => e.OcId != null)
            .Select(e => e.OcId!.Value).Distinct().ToArray(), cancellationToken)).ToDictionary(o => o.Id);
        var periodo = desde.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        List<RelacionIvaFila> filas = [];
        foreach (var e in expedientes)
        {
            if (e.Tipo == TipoExpediente.Importacion && e.OcId is Guid oid)
            {
                if (!ocs.TryGetValue(oid, out var oc)) continue;
                // PLATFORM-TODO(<CeRelacionEntradas>): Almacén aún no expone un puerto de entradas por OC.
                filas.Add(new(periodo, e.Folio, "Importación", oc.Folio, e.PedimentoVigente(oc.InformacionImportacion?.NumeroPedimento),
                    null, oc.InformacionImportacion?.NumeroBl, oc.InformacionImportacion?.NumeroContenedor, null, null, null, null, null, null));
                continue;
            }
            foreach (var c in e.Contenedores.Where(c => c.Estado == EstadoContenedor.Activo && c.FechaEmbarqueReal >= desde && c.FechaEmbarqueReal < hasta))
                foreach (var id in e.Partidas.Where(p => p.ContenedorId == c.Id && p.Estado != EstadoPartida.Liberada).Select(p => p.PedidoFacturableId).Distinct())
                {
                    origen.TryGetValue(id, out var p);
                    filas.Add(new(periodo, e.Folio, "Exportación", null, e.PedimentoVigente(), e.ClavePedimento,
                        c.BlGuia, c.Numero, p?.ClienteNombre, p?.NumeroPedido, p?.FacturaFolio, p?.FacturaUuid, c.FechaEmbarqueReal, null));
                }
        }
        return new("Relación para IVA", clock.UtcNow, new Dictionary<string, string?> { ["Periodo"] = periodo,
            ["Sucursal"] = request.SucursalId?.ToString() },
            [new("periodo", "Periodo"), new("expediente", "Expediente"), new("tipo", "Operación"), new("oc", "OC"),
                new("pedimento", "Pedimento"), new("clave", "Clave"), new("bl", "BL o guía"), new("contenedor", "Contenedor"),
                new("cliente", "Cliente"), new("pedido", "Pedido"), new("factura", "Factura"), new("uuid", "UUID"),
                new("fechaEmbarque", "Fecha de embarque", "fecha"), new("entradaAlmacen", "Entrada de almacén")], filas);
    }
}
