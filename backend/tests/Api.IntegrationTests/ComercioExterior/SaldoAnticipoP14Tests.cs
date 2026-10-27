using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.SharedKernel.Application.Integration;
using Millet.CuentasPorPagar.Infrastructure.Workers;
using Millet.Api.Seed;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Application.AnticipoProveedor.Queries;
using Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarAnticipo;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.ComercioExterior;

/// <summary>Usa la plantilla CE aislada; Claude ejecuta esta integración con PostgreSQL.</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class SaldoAnticipoP14Tests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    [Fact]
    public async Task Resolver_publica_saldo_CxP_lo_lista_sin_duplicar_y_aplica_a_factura()
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var client = await escenario.ClienteAsync(true);
        const string ruta = "/api/v1/comercio-exterior/expedientes";
        using var creado = await client.PostAsJsonAsync(ruta, escenario.Importacion());
        creado.EnsureSuccessStatusCode();
        var json = await creado.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("expediente").GetProperty("id").GetGuid();
        var tipo = await escenario.TipoAsync("IMP_PEDIMENTO");
        var evidencia = await escenario.AdjuntoAsync(id, tipo);
        Guid anticipoId, agente;
        using (var preparacion = escenario.Scope())
        {
            var sp = preparacion.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<ComercioExteriorDbContext>();
            var e = await db.Expedientes.Include(e => e.Anticipos).SingleAsync(e => e.Id == id);
            var maestros = sp.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
            agente = await maestros.Proveedores.Where(p => p.Clave == "DEMO-PROV-ACT").Select(p => p.Id).SingleAsync();
            var a = e.RegistrarAnticipo(Guid.NewGuid(), agente, "P14-TEST", new(150000, 0, 0), new(2026, 10, 1), "P14");
            a.Comprobar(new(142500, 0, 0), evidencia, evidencia, new(2026, 10, 2));
            anticipoId = a.Id;
            await db.SaveChangesAsync();
        }
        using var actual = await client.GetAsync($"{ruta}/{id}/finanzas");
        actual.EnsureSuccessStatusCode();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ruta}/{id}/anticipos/{anticipoId}/resolver")
        {
            Content = JsonContent.Create(new { tipo = "saldo-al-agente", importe = 7500, fecha = "2026-10-03", referencia = "Cuenta agente P14", evidenciaId = evidencia })
        };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(actual.Headers.ETag!.Tag));
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var resuelto = await client.SendAsync(request);
        resuelto.EnsureSuccessStatusCode();
        var finanzas = await resuelto.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, finanzas.GetProperty("anticipos")[0].GetProperty("saldo").GetDecimal());
        using var scope = escenario.Scope();
        var servicios = scope.ServiceProvider;
        using var empresaBypass = servicios.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var ce = servicios.GetRequiredService<ComercioExteriorDbContext>();
        var evento = Assert.Single(await ce.OutboxEntries.Where(e => e.EventType == AnticipoAgenteSaldoTransferidoHandler.EventType && e.Payload.Contains(anticipoId.ToString())).ToListAsync());
        var payload = JsonSerializer.Deserialize<SaldoAnticipoAgentePayload>(evento.Payload, JsonOptions)!;
        Assert.Equal(DemoSesionSeedHostedService.EmpresaId, payload.EmpresaId);
        Assert.Equal(7500, payload.ImporteMxn);
        using var worker = new ComercioExteriorEventListenerWorker(null!, servicios.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ComercioExteriorEventListenerWorker>.Instance);
        ILocalIntegrationEventConsumer consumidor = worker;
        await consumidor.ConsumirLocalAsync(evento.EventType, evento.Id, evento.Payload, default);
        await consumidor.ConsumirLocalAsync(evento.EventType, evento.Id, evento.Payload, default);
        var cxp = servicios.GetRequiredService<CuentasPorPagarDbContext>();
        var anticipo = Assert.Single(await cxp.AnticiposProveedor.Where(a => a.ReferenciaOrigen == $"{anticipoId}:0").ToListAsync());
        var lista = await ActivatorUtilities.CreateInstance<ListarAnticiposHandler>(servicios).Handle(new(ProveedorId: agente), default);
        Assert.Contains(lista.Items, a => a.Id == anticipo.Id && a.SaldoAmortizable == 7500 && a.UuidCfdi == null);
        var ahora = servicios.GetRequiredService<IClock>().UtcNow;
        var factura = FacturaProveedor.CapturarConOc(empresaId: payload.EmpresaId, cfdiRecibidoId: null, uuidCfdi: null,
            proveedorId: agente, sucursalId: escenario.SucursalId, folioProveedor: "P14-FACTURA", serieProveedor: "A",
            fechaDocumento: ahora, fechaContabilizacion: ahora, fechaVencimiento: DateOnly.FromDateTime(ahora.UtcDateTime.AddDays(30)),
            moneda: "MXN", tipoCambio: null, subtotal: 10000, descuentos: 0, impuestosTrasladados: 0, retenciones: 0,
            total: 10000, ordenCompraId: Guid.NewGuid(), encargadoComprasSnapshot: null, tolerancia: Tolerancia.MontoAbsoluto(0.99m),
            diferenciaContraOc: 0, redondeoAplicado: 0, ahora: ahora);
        cxp.FacturasProveedor.Add(factura); await cxp.SaveChangesAsync();
        var aplicado = await ActivatorUtilities.CreateInstance<AplicarAnticipoAFacturaHandler>(servicios).Handle(
            new(factura.Id, factura.Version, anticipo.Id, anticipo.Version, 5000), default);
        Assert.Equal(2500, aplicado.SaldoAmortizableAnticipo);
        Assert.Equal(5000, aplicado.AnticipoAplicadoTotal);
    }
}
