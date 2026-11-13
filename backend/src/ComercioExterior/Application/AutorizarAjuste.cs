using MediatR;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Application.PublicPorts;
using Millet.Compras.Application.PublicPorts;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Application;
public sealed record AutorizarAjusteCommand(Guid ExpedienteId, Guid IncidenciaId, int Version) : IRequest<ExpedienteDetalle>;
public sealed class AutorizarAjusteHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, IPedidosEmbarcablesReadPort pedidos, IOcImportacionReadPort compras, IClock clock) : IRequestHandler<AutorizarAjusteCommand, ExpedienteDetalle>
{
    public async Task<ExpedienteDetalle> Handle(AutorizarAjusteCommand request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorExpedientesEditar, cancellationToken);
        var e = await servicio.CargarAsync(request.ExpedienteId, true, cancellationToken);
        if (e.Version != request.Version) throw new ConcurrencyException("Expediente", e.Id);
        var i = e.Incidencias.SingleOrDefault(i => i.Id == request.IncidenciaId) ?? throw new EntityNotFoundException("CE_INCIDENCIA_NO_ENCONTRADA", "No existe la incidencia.");
        var p = e.Partidas.SingleOrDefault(p => p.Id == i.PartidaId);
        var ids = p is not null ? new[] { p.PedidoFacturableId } : e.OrdenesCompraIds;
        ExpedienteDetalle? resultado = null;
        await PedidoEmbarqueLock.ExecuteAsync(db, ids, async token =>
        {
            bool facturada; decimal limite;
            if (p is not null)
            {
                var pedido = (await pedidos.ObtenerAsync(ids, token)).Single();
                facturada = pedido.FacturaVigente; limite = pedido.Lineas.Single(l => l.Posicion == p.Posicion).Cantidad;
            }
            else
            {
                var imp = e.PartidasImportacion.SingleOrDefault(p => p.Id == i.PartidaId) ?? throw new BusinessRuleException("CE_PARTIDA_DATOS_INVALIDOS", "La partida del ajuste ya no existe.");
                var linea = (await compras.ObtenerVariasAsync(e.OrdenesCompraIds, token)).SelectMany(o => o.Partidas ?? []).SingleOrDefault(l => l.Id == imp.OcLineaId) ?? throw new BusinessRuleException("CE_PARTIDA_DATOS_INVALIDOS", "La partida de la OC ya no existe.");
                var otras = await servicio.CantidadesImportacionOtrosAsync(e, token);
                facturada = linea.CantidadFacturada > 0; limite = linea.Cantidad - otras.GetValueOrDefault(linea.Id);
            }
            e.AutorizarAjuste(i.Id, servicio.Actor, clock.UtcNow, facturada, limite);
            await db.SaveChangesAsync(token); resultado = await servicio.DetalleAsync(e, token);
        }, cancellationToken);
        return resultado!;
    }
}
