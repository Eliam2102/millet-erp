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
    [Theory]
    [InlineData(100, 0, 0, 1000, 0)]
    [InlineData(100, 30, 0, 0, 1000)]
    [InlineData(60, 0, 400, 600, 0)]
    [InlineData(60, 30, 0, 0, 1000)]
    public async Task P8_P10_traslado_conserva_existencia_y_salida_lleva_todo_a_gasto(
        int liberadas, int consumidas, decimal costoRevision, decimal costoRack, decimal gastoImportacion)
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var scope = escenario.Scope(); var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = sp.GetRequiredService<AlmacenDbContext>();
        var empresa = DemoSesionSeedHostedService.EmpresaId;
        var recepcionDemo = DemoSesionSeedHostedService.Id("DEMO-CE-P1-RECEPCION");
        var demo = await db.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Id == recepcionDemo);
        var articulo = demo.Lineas.First().ArticuloId;
        var rackDemo = await db.Ubicaciones.SingleAsync(u => u.Id == DemoSesionSeedHostedService.Id("DEMO-CE-P1-RACK"));
        var subDemo = await db.SubAlmacenes.SingleAsync(s => s.Id == rackDemo.SubAlmacenId);
        var revision = new Millet.Almacen.Domain.Catalogo.SubAlmacen(Guid.NewGuid(), subDemo.AlmacenId, "P8-REV", "Revisión P8", Millet.Almacen.Domain.Catalogo.TipoSubAlmacen.MaterialEnRevision);
        var origen = new Millet.Almacen.Domain.Catalogo.Ubicacion(Guid.NewGuid(), revision.Id, "P8-REV", "Revisión P8");
        var rack = new Millet.Almacen.Domain.Catalogo.Ubicacion(Guid.NewGuid(), subDemo.Id, "P8-R01", "Rack P8");
        db.SubAlmacenes.Add(revision); db.Ubicaciones.AddRange(origen, rack);
        await db.SaveChangesAsync();
        var secuencia = 910000;
        MovimientoInventario Agregar(TipoMovimiento tipo, Guid ubicacion, decimal cantidad)
        {
            var m = new MovimientoInventario(Guid.CreateVersion7(), tipo, empresa, new(2026, 10, 22));
            m.AgregarLinea(new(Guid.NewGuid(), m.Id, 1, articulo, cantidad, "PZA", 10, ubicacionId: ubicacion));
            if (tipo == TipoMovimiento.EntradaCompra) m.VincularRecepcionVarianteB(demo.OcId!.Value, null, "P8.pdf");
            m.Registrar(FolioMovimiento.Construir(tipo, 2026, ++secuencia), DemoSesionSeedHostedService.CapturistaComprasDemoId);
            db.Movimientos.Add(m); return m;
        }
        var recibida = Agregar(TipoMovimiento.EntradaCompra, origen.Id, 100);
        await db.SaveChangesAsync();
        var salida = Agregar(TipoMovimiento.SalidaPorLiberacionRevision, origen.Id, liberadas);
        Agregar(TipoMovimiento.ReincorporacionTrasRevision, rack.Id, liberadas).VincularSalidaLiberacion(salida.Id);
        await db.SaveChangesAsync();
        if (consumidas > 0) { Agregar(TipoMovimiento.SalidaConsumo, rack.Id, consumidas); await db.SaveChangesAsync(); }
        var iniciales = await db.SaldosInventario.AsNoTracking().Where(s => s.UbicacionId == origen.Id || s.UbicacionId == rack.Id).ToDictionaryAsync(s => s.UbicacionId);
        var expediente = Guid.NewGuid();
        var partida = recibida.Lineas.Single().Id;
        var lectura = (await sp.GetRequiredService<Millet.Almacen.Application.PublicPorts.IRecepcionesCostoReadPort>()
            .ListarAsync(empresa, escenario.SucursalId, demo.OcId!.Value, CancellationToken.None)).Single(x => x.PartidaId == partida);
        Assert.Equal(100 - consumidas, lectura.Remanente); // Incluye lo trasladado, aun con origen vacío.
        var payload = new CostoExpedientePayload(empresa, expediente, escenario.SucursalId, 1,
            [new(partida, demo.OcId!.Value, recibida.Id, articulo, origen.Id, 100, 1000, 1000)]);
        var mediator = sp.GetRequiredService<IMediator>();
        await mediator.Send(new AplicarCostoExpedienteCommand(payload));
        await mediator.Send(new AplicarCostoExpedienteCommand(payload));
        var ajustes = await db.Set<AjusteCostoExpediente>().Where(a => a.ExpedienteId == expediente).ToListAsync();
        Assert.Equal(2, ajustes.Count);
        Assert.Equal(costoRevision, ajustes.Single(a => a.UbicacionId == origen.Id).InventarioMxn);
        Assert.Equal(costoRack, ajustes.Single(a => a.UbicacionId == rack.Id).InventarioMxn);
        Assert.Equal(gastoImportacion, ajustes.Sum(a => a.CostoVentasMxn));
        Assert.Equal(gastoImportacion, ajustes.Single(a => a.UbicacionId == origen.Id).CostoVentasMxn);
        await mediator.Send(new AplicarCostoExpedienteCommand(payload with { NumeroVersion = 2,
            Partidas = [payload.Partidas[0] with { ImporteMxn = 1200, DiferenciaMxn = 200 }] }));
        db.ChangeTracker.Clear();
        var posteriores = await db.SaldosInventario.AsNoTracking().Where(s => s.UbicacionId == origen.Id || s.UbicacionId == rack.Id).ToListAsync();
        foreach (var saldo in posteriores)
        {
            Assert.Equal(iniciales[saldo.UbicacionId].Cantidad, saldo.Cantidad);
            Assert.Equal(iniciales[saldo.UbicacionId].ValorInventarioMxn + (saldo.UbicacionId == origen.Id ? costoRevision : costoRack) * 1.2m, saldo.ValorInventarioMxn);
        }
        ajustes = await db.Set<AjusteCostoExpediente>().Where(a => a.ExpedienteId == expediente && a.NumeroVersion == 2).ToListAsync();
        Assert.Equal(1200, ajustes.Sum(a => a.AcumuladoMxn));
        Assert.Equal(200, ajustes.Sum(a => a.DiferenciaMxn));
        Assert.Equal(gastoImportacion * .2m, ajustes.Sum(a => a.CostoVentasMxn));
        var eventos = await db.Set<IntegrationEventOutboxEntry>().Where(x => x.EventType == "almacen.expediente.costo-aplicado.v1").ToListAsync();
        Assert.Equal(2, eventos.Count);
        var aplicado = System.Text.Json.JsonSerializer.Deserialize<ExpedienteCostoAplicadoEvent>(eventos[0].Payload,
            System.Text.Json.JsonSerializerOptions.Web)!;
        Assert.Single(aplicado.Partidas);
        Assert.Equal(2, aplicado.Partidas[0].Ubicaciones.Count);
        Assert.Equal(aplicado.Partidas[0].InventarioMxn, aplicado.Partidas[0].Ubicaciones.Sum(u => u.InventarioMxn));
        Assert.Equal(aplicado.Partidas[0].DiferenciaMxn, aplicado.Partidas[0].InventarioMxn + aplicado.Partidas[0].GastoImportacionMxn);
    }

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
        Assert.Equal(0, ajuste.InventarioMxn); Assert.Equal(10000, ajuste.CostoVentasMxn);
        var saldo = await almacen.SaldosInventario.AsNoTracking().SingleAsync(s => s.UbicacionId == ubicacion && s.ArticuloId == linea.ArticuloId);
        Assert.Equal(inicial.CostoPromedioMxn, saldo.CostoPromedioMxn);
        Assert.Equal(inicial.Cantidad, saldo.Cantidad); Assert.Equal(inicial.ValorInventarioMxn, saldo.ValorInventarioMxn);
        await mediator.Send(new AplicarCostoExpedienteCommand(payload with { NumeroVersion = 2, Partidas = [payload.Partidas[0] with { ImporteMxn = 12000, DiferenciaMxn = 2000 }] }));
        saldo = await almacen.SaldosInventario.AsNoTracking().SingleAsync(s => s.UbicacionId == ubicacion && s.ArticuloId == linea.ArticuloId);
        Assert.Equal(inicial.ValorInventarioMxn, saldo.ValorInventarioMxn);
        var ajusteDos = await almacen.Set<AjusteCostoExpediente>().SingleAsync(a => a.ExpedienteId == expediente && a.NumeroVersion == 2);
        Assert.Equal(0, ajusteDos.InventarioMxn); Assert.Equal(2000, ajusteDos.CostoVentasMxn);
        Assert.Equal(inicial.CostoPromedioMxn, saldo.CostoPromedioMxn);
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
