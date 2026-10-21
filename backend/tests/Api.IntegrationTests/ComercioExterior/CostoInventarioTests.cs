using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Seed;
using Millet.Almacen.Application.EventListeners;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.Api.IntegrationTests.ComercioExterior;
public sealed class CostoInventarioTests
{
    [Fact]
    public async Task CA4_CA5_CA6_aplicacion_transaccional_duplicado_y_version_diferencial()
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var scope = escenario.Scope(); var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var almacen = sp.GetRequiredService<AlmacenDbContext>();
        var empresa = DemoSesionSeedHostedService.EmpresaId;
        var expediente = DemoSesionSeedHostedService.Id("DEMO-CE-P1-FINANZAS");
        var recepcion = DemoSesionSeedHostedService.Id("DEMO-CE-P1-RECEPCION");
        var ubicacion = DemoSesionSeedHostedService.Id("DEMO-CE-P1-RACK");
        var linea = await almacen.LineasMovimiento.FirstAsync(l => l.MovimientoId == recepcion);
        var movimiento = await almacen.Movimientos.SingleAsync(m => m.Id == recepcion);
        var saldoAntes = await almacen.SaldosInventario.AsNoTracking().SingleAsync(s => s.UbicacionId == ubicacion && s.ArticuloId == linea.ArticuloId);
        // Dos entradas de 1,000 del mismo artículo. Una salida de 1,200 deja 400 por entrada.
        var salida = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.SalidaConsumo, empresa, new(2026, 10, 16));
        salida.AgregarLinea(new(Guid.NewGuid(), salida.Id, 1, linea.ArticuloId, 1200, "PZA", saldoAntes.CostoPromedioMxn, ubicacionId: ubicacion));
        salida.Registrar(new("M-SAL2026-900206"), DemoSesionSeedHostedService.CapturistaComprasDemoId);
        almacen.Movimientos.Add(salida); await almacen.SaveChangesAsync(); almacen.ChangeTracker.Clear();
        var inicial = await almacen.SaldosInventario.AsNoTracking().SingleAsync(s => s.UbicacionId == ubicacion && s.ArticuloId == linea.ArticuloId);
        var payload = new CostoExpedientePayload(empresa, expediente, escenario.SucursalId, 1,
            [new(linea.Id, movimiento.OcId!.Value, recepcion, linea.ArticuloId, ubicacion, 1000, 10000, 10000)]);
        var mediator = sp.GetRequiredService<IMediator>();
        await mediator.Send(new AplicarCostoExpedienteCommand(payload));
        await mediator.Send(new AplicarCostoExpedienteCommand(payload));
        var ajuste = await almacen.Set<AjusteCostoExpediente>().SingleAsync(a => a.ExpedienteId == expediente);
        Assert.Equal(4000, ajuste.InventarioMxn); Assert.Equal(6000, ajuste.CostoVentasMxn);
        var saldo = await almacen.SaldosInventario.AsNoTracking().SingleAsync(s => s.UbicacionId == ubicacion && s.ArticuloId == linea.ArticuloId);
        Assert.Equal(inicial.Cantidad, saldo.Cantidad); Assert.Equal(inicial.ValorInventarioMxn + 4000, saldo.ValorInventarioMxn);
        await mediator.Send(new AplicarCostoExpedienteCommand(payload with { NumeroVersion = 2, Partidas = [payload.Partidas[0] with { ImporteMxn = 12000, DiferenciaMxn = 2000 }] }));
        saldo = await almacen.SaldosInventario.AsNoTracking().SingleAsync(s => s.UbicacionId == ubicacion && s.ArticuloId == linea.ArticuloId);
        Assert.Equal(inicial.ValorInventarioMxn + 4800, saldo.ValorInventarioMxn);
        Assert.Equal(2, await almacen.Set<AjusteCostoExpediente>().CountAsync(a => a.ExpedienteId == expediente));
        Assert.Equal(2, await almacen.Set<IntegrationEventOutboxEntry>().CountAsync(x => x.EventType == "almacen.expediente.costo-aplicado.v1"));
    }
    [Fact]
    public async Task Distribucion_persiste_historial_y_outbox_en_la_misma_transaccion()
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var scope = escenario.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
        var e = await db.Expedientes.Include(x => x.Gastos).Include(x => x.Distribuciones).SingleAsync(x => x.Id == DemoSesionSeedHostedService.Id("DEMO-CE-P1-FINANZAS"));
        Assert.Equal(4, e.Gastos.Count); // flete, IVA y dos facturas de honorarios ligadas a la orden de servicio DEMO (P6)
        var conceptos = await db.ConceptosGasto.ToListAsync();
        var d = e.PrepararDistribucion(conceptos, [new(Guid.NewGuid(), null, "A", 200000, 2000, 1000), new(Guid.NewGuid(), null, "B", 300000, 6000, 1000)], Guid.NewGuid(), DateTimeOffset.UtcNow);
        e.ConfirmarDistribucion(d);
        await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(new Millet.ComercioExterior.Application.ExpedienteCostoDistribuidoEvent(e.EmpresaId, DateTimeOffset.UtcNow, e.Id, e.SucursalId, d.NumeroVersion, d.Partidas), CancellationToken.None);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var guardada = await db.Set<DistribucionGasto>().SingleAsync(x => x.ExpedienteId == e.Id);
        Assert.Equal(92000, guardada.Partidas.Sum(p => p.ImporteMxn)); Assert.Equal(6, guardada.Renglones.Count); // 40,000 flete + 30,000 + 22,000 honorarios; IVA no se distribuye
        Assert.True(await db.OutboxEntries.AnyAsync(x => x.EventType == "comercio_exterior.expediente.costo-distribuido.v1"));
    }
}
