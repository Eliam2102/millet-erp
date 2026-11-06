using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Domain;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Application.PublicPorts;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;

namespace Millet.ComercioExterior.Infrastructure.PublicAdapters;
public sealed class EmbarqueExportacionReadAdapter(ComercioExteriorDbContext db, IPedidosEmbarcablesReadPort pedidos, ICatalogosCeReadPort catalogos) : IEmbarqueExportacionReadPort
{
    public Task<EmbarqueExportacionLectura> ObtenerAsync(Guid pedidoFacturableId, CancellationToken ct) => ObtenerAsync(pedidoFacturableId, null, ct);

    /// <summary>FAC-08: con contenedor, la compuerta y los contenedores del CCE se limitan a ese contenedor.</summary>
    public async Task<EmbarqueExportacionLectura> ObtenerAsync(Guid pedidoFacturableId, Guid? contenedorId, CancellationToken ct)
    {
        var expedientes = await db.Expedientes.AsNoTracking().Include(e => e.AutorizacionesSalida).Include(e => e.Participantes).Include(e => e.Documentos)
            .Include(e => e.Contenedores).Include(e => e.Partidas).AsSplitQuery()
            .Where(e => e.Estado != EstadoExpediente.Cancelado && e.Partidas.Any(p => p.PedidoFacturableId == pedidoFacturableId && p.Estado != EstadoPartida.Liberada)).ToListAsync(ct);
        // El vínculo permanece incluso con contenedor cancelado: falta reasignar, nunca evade la compuerta.
        var e = expedientes.OrderByDescending(x => x.Estado == EstadoExpediente.Abierto).ThenByDescending(x => x.CreatedAt).FirstOrDefault();
        if (e is null) return new(false);
        var pedido = (await pedidos.ObtenerAsync([pedidoFacturableId], ct)).SingleOrDefault();
        var bloqueo = pedido is null ? (Codigo: (string?)"FAC_PEDIDO_NO_EMBARCADO", Motivo: (string?)"El pedido ya no existe.") :
            e.BloqueoFacturacion(pedido, await catalogos.AdjuntosVigentesAsync(e.Id, ct), contenedorId);
        var ids = e.Partidas.Where(p => p.PedidoFacturableId == pedidoFacturableId).Select(p => p.ContenedorId).ToHashSet();
        var contenedores = e.Contenedores.Where(c => ids.Contains(c.Id) && (contenedorId == null || c.Id == contenedorId) && c.Estado == EstadoContenedor.Activo && c.FechaEmbarqueReal != null)
            .OrderBy(c => c.Numero).ThenBy(c => c.Id).Select(c => new ContenedorExportacionLectura(c.Id, c.Numero!, c.BlGuia!, c.FechaEmbarqueReal!.Value)).ToArray();
        var incoterms = await catalogos.IncotermsAsync([e.IncotermId], ct);
        var incoterm = incoterms.GetValueOrDefault(e.IncotermId);
        if (string.IsNullOrWhiteSpace(incoterm))
            bloqueo = ("CE_INCOTERM_REQUERIDO", "Seleccione un Incoterm vigente en el expediente.");
        return new(true, e.Id, e.SucursalId, bloqueo.Codigo is null, bloqueo.Codigo, bloqueo.Motivo, e.Folio,
            e.NumeroPedimentoExportacion, e.ClavePedimento, e.FechaPedimento, incoterm, contenedores, e.AduanaPedimentoExportacion,
            e.AduanaPedimentoExportacion is { } aduana ? CatalogoAduanas.Aduanas.GetValueOrDefault(aduana) : null, e.VersionEmbarque, e.Partidas.Where(p => p.PedidoFacturableId == pedidoFacturableId && p.Estado != EstadoPartida.Liberada && p.AduanalesRevisadoPorId != null).GroupBy(p => p.Posicion).OrderBy(g => g.Key).Select(g => new PartidaAduanalLectura(g.Key, g.First().FraccionArancelaria!, g.First().UnidadAduana!)).ToArray());
    }
}
