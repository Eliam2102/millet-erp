using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.EventListeners;
// Contrato espejo; Almacén no referencia el módulo Comercio Exterior.
public sealed record CostoExpedientePayload(Guid EmpresaId, Guid ExpedienteId, Guid SucursalId, int NumeroVersion, IReadOnlyList<CostoPartidaPayload> Partidas);
public sealed record CostoPartidaPayload(Guid PartidaId, Guid OcId, Guid RecepcionId, Guid ArticuloId, Guid UbicacionId,
    decimal CantidadRecibida, decimal ImporteMxn, decimal DiferenciaMxn);
public sealed record AplicarCostoExpedienteCommand(CostoExpedientePayload Payload) : IRequest;
public sealed record CostoPartidaAplicado(Guid PartidaId, Guid OcId, Guid RecepcionId, Guid ArticuloId, Guid UbicacionId,
    decimal DiferenciaMxn, decimal InventarioMxn, decimal CostoVentasMxn);
/// <summary>Contabilidad consume este resultado confirmado, nunca la estimación de la vista previa.</summary>
public sealed record ExpedienteCostoAplicadoEvent(Guid Empresa, DateTimeOffset Fecha, Guid ExpedienteId, int NumeroVersion,
    IReadOnlyList<CostoPartidaAplicado> Partidas) : IntegrationEvent("almacen.expediente.costo-aplicado.v1", Empresa, Fecha);
public sealed class ExpedienteCostoDistribuidoHandler(AlmacenDbContext db, IIntegrationEventPublisher eventos, IClock clock)
    : IRequestHandler<AplicarCostoExpedienteCommand>
{
    public const string EventType = "comercio_exterior.expediente.costo-distribuido.v1";
    public async Task Handle(AplicarCostoExpedienteCommand request, CancellationToken cancellationToken)
    {
        var p = request.Payload;
        if (p.EmpresaId == Guid.Empty || p.ExpedienteId == Guid.Empty || p.NumeroVersion < 1 || p.Partidas is null ||
            p.Partidas.Any(x => x.PartidaId == Guid.Empty || x.CantidadRecibida <= 0) || p.Partidas.Select(x => x.PartidaId).Distinct().Count() != p.Partidas.Count)
            throw new BusinessRuleException("CE_EVENTO_INVALIDO", "La distribución no contiene referencias válidas.");
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({p.ExpedienteId.ToString()}, 6))", cancellationToken);
        var llave = EventType + "/" + p.NumeroVersion;
        if (await db.EventosProcesados.AnyAsync(x => x.EventoId == p.ExpedienteId && x.EventoTipo == llave, cancellationToken)) return;
        var previa = EventType + "/" + (p.NumeroVersion - 1);
        if (p.NumeroVersion > 1 && !await db.EventosProcesados.AnyAsync(x => x.EventoId == p.ExpedienteId && x.EventoTipo == previa, cancellationToken))
            throw new BusinessRuleException("CE_VERSION_COSTO_PENDIENTE", "Falta aplicar la versión anterior.");
        List<CostoPartidaAplicado> aplicadas = [];
        foreach (var l in p.Partidas.OrderBy(x => x.UbicacionId).ThenBy(x => x.ArticuloId).ThenBy(x => x.PartidaId))
        {
            var ajustes = db.Set<AjusteCostoExpediente>().Where(x => x.EmpresaId == p.EmpresaId && x.ExpedienteId == p.ExpedienteId && x.PartidaId == l.PartidaId);
            if (await ajustes.AnyAsync(x => x.NumeroVersion == p.NumeroVersion, cancellationToken)) continue;
            var anterior = await ajustes.OrderByDescending(x => x.NumeroVersion).FirstOrDefaultAsync(cancellationToken);
            if (l.ImporteMxn - (anterior?.AcumuladoMxn ?? 0) != l.DiferenciaMxn)
                throw new BusinessRuleException("CE_VERSION_COSTO_PENDIENTE", "Falta aplicar una versión anterior o el acumulado no cuadra.");
            var partida = await (from m in db.Movimientos join linea in db.LineasMovimiento on m.Id equals linea.MovimientoId
                join u in db.Ubicaciones on linea.UbicacionId equals u.Id join sub in db.SubAlmacenes on u.SubAlmacenId equals sub.Id
                join a in db.Almacenes on sub.AlmacenId equals a.Id
                where m.EmpresaId == p.EmpresaId && a.SucursalId == p.SucursalId && m.Id == l.RecepcionId && m.OcId == l.OcId &&
                    m.Tipo == TipoMovimiento.EntradaCompra && m.Estado == EstadoMovimiento.Registrado && linea.Id == l.PartidaId &&
                    linea.ArticuloId == l.ArticuloId && linea.UbicacionId == l.UbicacionId && linea.Cantidad == l.CantidadRecibida
                select new { linea.ProductoAwVarianteId }).FirstOrDefaultAsync(cancellationToken);
            if (partida is null) throw new BusinessRuleException("CE_RECEPCION_INVALIDA", "La partida no corresponde a una recepción de la empresa y sucursal.");
            // El mismo lock de fila que toma el trigger de movimientos físicos impide perder salidas concurrentes.
            var saldo = await db.SaldosInventario.FromSqlInterpolated($"SELECT * FROM almacen.saldos_inventario WHERE ubicacion_id = {l.UbicacionId} AND articulo_id = {l.ArticuloId} AND producto_aw_variante_id IS NOT DISTINCT FROM {partida.ProductoAwVarianteId} FOR UPDATE").SingleAsync(cancellationToken);
            await db.Entry(saldo).ReloadAsync(cancellationToken);
            var remanente = Math.Min(saldo.Cantidad, await RecepcionesCostoReadAdapter.RemanenteAsync(db, p.EmpresaId, l.PartidaId, l.UbicacionId, l.ArticuloId, partida.ProductoAwVarianteId, cancellationToken));
            var reparto = SaldoInventario.SepararCosto(l.DiferenciaMxn, l.CantidadRecibida, remanente);
            saldo.AjustarValor(reparto.Inventario, clock.UtcNow);
            db.Set<AjusteCostoExpediente>().Add(new(p.EmpresaId, p.ExpedienteId, p.NumeroVersion, l.PartidaId, l.UbicacionId, l.ArticuloId,
                l.ImporteMxn, l.DiferenciaMxn, reparto.Inventario, clock.UtcNow));
            aplicadas.Add(new(l.PartidaId, l.OcId, l.RecepcionId, l.ArticuloId, l.UbicacionId, l.DiferenciaMxn, reparto.Inventario, reparto.CostoVentas));
            // Varias partidas pueden compartir saldo; el siguiente cálculo parte del valor recién persistido.
            await db.SaveChangesAsync(cancellationToken);
        }
        db.EventosProcesados.Add(new EventoProcesado(p.ExpedienteId, llave));
        if (aplicadas.Count > 0)
        {
            await eventos.PublishAsync(new ExpedienteCostoAplicadoEvent(p.EmpresaId, clock.UtcNow, p.ExpedienteId, p.NumeroVersion, aplicadas), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
