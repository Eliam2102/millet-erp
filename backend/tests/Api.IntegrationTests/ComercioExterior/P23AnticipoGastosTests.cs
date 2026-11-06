using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Seed;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Infrastructure.Persistence;
namespace Millet.Api.IntegrationTests.ComercioExterior;
[Collection(ApiCompartida.Nombre)]
public sealed class P23AnticipoGastosTests
{
    [Fact]
    public async Task Anticipo_50000_gasto_47000_salda_factura_y_la_retira_de_tesoreria_con_reversa_idempotente()
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var scope = escenario.Scope(); var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var empresa = DemoSesionSeedHostedService.EmpresaId;
        var maestros = sp.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
        var agente = await maestros.Proveedores.Where(p => p.Clave == "DEMO-PROV-ACT").Select(p => p.Id).SingleAsync();
        var ahora = sp.GetRequiredService<IClock>().UtcNow;
        var hoy = FechaContable.HoyLocal(ahora);
        var cxp = sp.GetRequiredService<CuentasPorPagarDbContext>();
        var factura = FacturaProveedor.CapturarSinOc(empresa, null, Guid.NewGuid().ToString(), agente, escenario.SucursalId,
            "P23-FACTURA", "A", ahora, new DateTimeOffset(hoy.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), hoy.AddDays(30),
            "MXN", 1, 47000, 0, 0, 0, 47000, "Gasto del agente para prueba P23", ahora);
        factura.Autorizar(Guid.NewGuid(), ahora); cxp.FacturasProveedor.Add(factura); await cxp.SaveChangesAsync();
        var ce = sp.GetRequiredService<ComercioExteriorDbContext>();
        var e = new Expediente(Guid.NewGuid(), empresa, "CE-P23-" + Guid.NewGuid().ToString()[..8], TipoExpediente.Importacion,
            escenario.SucursalId, "P23", Guid.NewGuid(), escenario.Incoterm, "MXN", escenario.Responsable, []);
        var a = e.RegistrarAnticipo(Guid.NewGuid(), agente, "P23-PED", new(50000,0,0), hoy, null);
        a.Solicitar(Guid.NewGuid(), 50000); a.RegistrarPago(Guid.NewGuid(), 50000, "TRF-P23");
        var concepto = new ConceptoGasto(empresa, "P23-" + Guid.NewGuid().ToString()[..8], "Honorarios"); ce.ConceptosGasto.Add(concepto);
        var gastoId = Guid.NewGuid(); e.GuardarGasto(gastoId, new(concepto.Id, agente, "P23-FACTURA", "MXN", 47000, hoy, FacturaProveedorId: factura.Id), concepto, 1, "MXN");
        var gasto = e.Gastos.Single(g => g.Id == gastoId);
        a.Comprobar(new(0,0,0), Guid.NewGuid(), Guid.NewGuid(), hoy, Guid.NewGuid()); a.AplicarGastos([gasto]);
        ce.Expedientes.Add(e); await ce.SaveChangesAsync();
        var handler = ActivatorUtilities.CreateInstance<AnticipoGastosAplicadosHandler>(sp);
        var payload = new GastosAnticipoCePayload(empresa, a.Id, agente, e.Folio, 1, [new(gasto.Id, factura.Id, 47000)]);
        await handler.Handle(new(payload), default); await handler.Handle(new(payload), default);
        Assert.Equal(0, factura.SaldoPendiente); Assert.Equal(47000, factura.AnticipoAplicadoTotal);
        Assert.Single(await cxp.AnticiposProveedor.Where(x => x.ReferenciaOrigen == $"{a.Id}:gasto:{gasto.Id}").ToListAsync());
        await using var serviceBus = new Azure.Messaging.ServiceBus.ServiceBusClient("Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=dGVzdA==");
        using var worker = ActivatorUtilities.CreateInstance<Millet.Tesoreria.Infrastructure.Workers.CuentasPorPagarEventListenerWorker>(sp, serviceBus);
        var consumidor = (ILocalIntegrationEventConsumer)worker;
        async Task Proyectar()
        {
            foreach (var ev in await cxp.OutboxEntries.Where(x => x.EventType == "cuentas_por_pagar.pasivo.autorizado-para-pago.v1").OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToListAsync())
            {
                using var json = JsonDocument.Parse(ev.Payload);
                if (json.RootElement.GetProperty("FacturaProveedorId").GetGuid() == factura.Id)
                    await consumidor.ConsumirLocalAsync(ev.EventType, ev.Id, ev.Payload, default);
            }
        }
        await Proyectar();
        var tes = sp.GetRequiredService<TesoreriaDbContext>();
        var pasivo = await tes.PasivosPendientesPago.SingleAsync(x => x.FacturaProveedorId == factura.Id);
        Assert.Equal(0, pasivo.SaldoPendiente);
        await handler.Handle(new(payload with { Revision = 2, Gastos = [] }), default);
        Assert.Equal(47000, factura.SaldoPendiente);
        Assert.Equal(0, (await cxp.AnticiposProveedor.SingleAsync(x => x.ReferenciaOrigen == $"{a.Id}:gasto:{gasto.Id}")).SaldoAmortizable);
        await handler.Handle(new(payload), default); Assert.Equal(47000, factura.SaldoPendiente);
        await Proyectar(); await tes.Entry(pasivo).ReloadAsync(); Assert.Equal(47000, pasivo.SaldoPendiente);
        factura.RegistrarPago(47000, ahora, "Pago anterior a la comprobación"); await cxp.SaveChangesAsync();
        await handler.Handle(new(payload with { Revision = 3 }), default);
        Assert.Contains("no aplicada", (await cxp.ComprobacionesAnticipoCe.SingleAsync(x => x.Id == a.Id)).Error!, StringComparison.Ordinal);
        Assert.Equal(0, factura.AnticipoAplicadoTotal);
    }
    [Fact]
    public async Task Matriz_vacia_permite_embarcado_y_mantiene_evidencia_de_despacho()
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var scope = escenario.Scope(); var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = sp.GetRequiredService<ComercioExteriorDbContext>();
        var empresa = DemoSesionSeedHostedService.EmpresaId;
        await db.RequisitosHitos.Where(r => r.EmpresaId == empresa).ExecuteDeleteAsync();
        var requisitos = await sp.GetRequiredService<Millet.ComercioExterior.Application.ICatalogosCeReadPort>().RequisitosAsync(TipoExpediente.Importacion, default);
        Assert.NotEmpty(requisitos); Assert.All(requisitos, r => Assert.Null(r.HitoClave));
        var hoy = FechaContable.HoyLocal(sp.GetRequiredService<IClock>().UtcNow);
        var e = new Expediente(Guid.NewGuid(), empresa, "CE-P23-" + Guid.NewGuid().ToString()[..8], TipoExpediente.Importacion,
            escenario.SucursalId, "Matriz vacía", Guid.NewGuid(), escenario.Incoterm, "MXN", escenario.Responsable, []);
        e.InicializarHitos(hoy);
        e.AgregarParticipante(Guid.NewGuid(), "Agente de prueba", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
        e.AgregarParticipante(Guid.NewGuid(), "Naviera de prueba", TipoParticipante.Naviera, null, PapelParticipante.Captura, null, null);
        var contenedor = Guid.NewGuid();
        e.GuardarContenedor(contenedor, new("P23-CONT", null, null, "P23-BL", null, null, null, null, ModalidadTransporte.Maritimo));
        e.AsignarPartidaImportacion(contenedor, Guid.NewGuid(), 1, 1, 1, "PZA", escenario.Responsable); e.RegistrarEmbarque(contenedor, hoy, hoy);
        var ordenDespacho = e.Hitos.Single(h => h.Clave == "despacho-aduanal").Orden;
        foreach (var h in e.Hitos.Where(h => h.Orden > 1 && h.Orden < ordenDespacho).OrderBy(h => h.Orden))
            e.RegistrarHito(h.Clave, hoy, escenario.Responsable, null, null, requisitos, [], hoy);
        var error = Assert.Throws<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>(() =>
            { e.RegistrarHito("despacho-aduanal", hoy, escenario.Responsable, null, null, requisitos, [], hoy); });
        Assert.Equal("CE_EVIDENCIA_HITO_REQUERIDA", error.Code);
        var evidencia = Guid.NewGuid();
        e.RegistrarHito("despacho-aduanal", hoy, escenario.Responsable, evidencia, null, requisitos, [evidencia], hoy);
        e.RegistrarAduanaEntrada(Millet.SharedKernel.Domain.CatalogoAduanas.Aduanas.Keys.First());
        e.RegistrarHito("embarcado", hoy, escenario.Responsable, null, null, requisitos, [], hoy);
        db.Expedientes.Add(e); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var guardado = await db.Expedientes.Include(x => x.Hitos).SingleAsync(x => x.Id == e.Id);
        Assert.Equal(hoy, guardado.Hitos.Single(h => h.Clave == "embarcado").FechaReal); Assert.Equal(e.AduanaEntrada, guardado.AduanaEntrada);
    }

    [Fact]
    public async Task Cancelacion_desde_ce_no_acepta_anticipo_de_otro_expediente()
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var client = await escenario.ClienteAsync(true);
        const string ruta = "/api/v1/comercio-exterior/expedientes";
        using var creado = await client.PostAsJsonAsync(ruta, escenario.Importacion()); creado.EnsureSuccessStatusCode();
        var id = (await creado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expediente").GetProperty("id").GetGuid();
        using var scope = escenario.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var otro = new Expediente(Guid.NewGuid(), DemoSesionSeedHostedService.EmpresaId, "CE-P23-" + Guid.NewGuid().ToString()[..8], TipoExpediente.Importacion,
            escenario.SucursalId, "Otro expediente", Guid.NewGuid(), escenario.Incoterm, "MXN", escenario.Responsable, []);
        var anticipo = otro.RegistrarAnticipo(Guid.NewGuid(), Guid.NewGuid(), "P23", new(100,0,0), new(2026,11,5), null);
        var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>(); db.Expedientes.Add(otro); await db.SaveChangesAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ruta}/{id}/anticipos/{anticipo.Id}/cancelar")
            { Content = JsonContent.Create(new { motivo = "No pertenece a este expediente" }) };
        request.Headers.Add("If-Match", creado.Headers.ETag!.Tag); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("CE_ANTICIPO_NO_ENCONTRADO", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await db.Entry(anticipo).ReloadAsync(); Assert.Null(anticipo.MotivoCancelacion);
    }

}
