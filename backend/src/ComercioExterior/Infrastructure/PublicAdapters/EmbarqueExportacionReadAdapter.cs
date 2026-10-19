using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Application.PublicPorts;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;

namespace Millet.ComercioExterior.Infrastructure.PublicAdapters;
public sealed class EmbarqueExportacionReadAdapter(ComercioExteriorDbContext db, IPedidosEmbarcablesReadPort pedidos, ICatalogosCeReadPort catalogos) : IEmbarqueExportacionReadPort
{
    public async Task<EmbarqueExportacionLectura> ObtenerAsync(Guid pedidoFacturableId, CancellationToken ct)
    {
        var expedientes = await db.Expedientes.AsNoTracking().Include(e => e.Participantes).Include(e => e.Documentos)
            .Include(e => e.Contenedores).Include(e => e.Partidas).AsSplitQuery()
            .Where(e => e.Estado == EstadoExpediente.Abierto && e.Partidas.Any(p => p.PedidoFacturableId == pedidoFacturableId && p.Estado != EstadoPartida.Liberada)).ToListAsync(ct);
        // El vínculo permanece incluso con contenedor cancelado: falta reasignar, nunca evade la compuerta.
        var e = expedientes.OrderByDescending(x => x.Estado == EstadoExpediente.Abierto).ThenByDescending(x => x.CreatedAt).FirstOrDefault();
        if (e is null) return new(false);
        var pedido = (await pedidos.ObtenerAsync([pedidoFacturableId], ct)).SingleOrDefault();
        var bloqueo = pedido is null ? (Codigo: (string?)"FAC_PEDIDO_NO_EMBARCADO", Motivo: (string?)"El pedido ya no existe.") :
            e.BloqueoFacturacion(pedido, await catalogos.AdjuntosVigentesAsync(e.Id, ct));
        var ids = e.Partidas.Where(p => p.PedidoFacturableId == pedidoFacturableId).Select(p => p.ContenedorId).ToHashSet();
        var contenedores = e.Contenedores.Where(c => ids.Contains(c.Id) && c.Estado == EstadoContenedor.Activo && c.FechaEmbarqueReal != null)
            .OrderBy(c => c.Numero).ThenBy(c => c.Id).Select(c => new ContenedorExportacionLectura(c.Id, c.Numero!, c.BlGuia!, c.FechaEmbarqueReal!.Value)).ToArray();
        var incoterms = await catalogos.IncotermsAsync([e.IncotermId], ct);
        var incoterm = incoterms.GetValueOrDefault(e.IncotermId);
        if (string.IsNullOrWhiteSpace(incoterm))
            bloqueo = ("CE_INCOTERM_REQUERIDO", "Seleccione un Incoterm vigente en el expediente.");
        return new(true, e.Id, e.SucursalId, bloqueo.Codigo is null, bloqueo.Codigo, bloqueo.Motivo, e.Folio,
            e.NumeroPedimentoExportacion, e.ClavePedimento, e.FechaPedimento, incoterm, contenedores);
    }
}
