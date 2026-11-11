using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Api.Seed;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Contabilidad.Infrastructure.Workers;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Api.IntegrationTests.ComercioExterior;

/// <summary>
/// M-327 · reversa de un anticipo al agente aduanal ya comprobado, de punta a punta por HTTP: anticipo de $50,000 comprobado con una
/// factura del agente por $42,000; Comercio exterior solicita con motivo y el Contador General autoriza (Millet, 10-nov-2026). El
/// anticipo vuelve a «pendiente de comprobar», el saldo a favor del agente queda en $50,000, la factura vuelve a quedar por pagar y
/// Contabilidad genera la póliza de reversa. Lo corre CI (PostgreSQL aislado).
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class ReversaComprobacionAnticipoHttpTests
{
    private const string Base = "/api/v1/comercio-exterior/expedientes";

    private static async Task<HttpResponseMessage> Post(HttpClient client, Guid id, string ruta, object body, string etag)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{id}/{ruta}") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
        return await client.SendAsync(request);
    }

    private static async Task<(JsonElement Anticipo, JsonElement Finanzas, string Etag)> Leer(HttpClient client, Guid id, Guid anticipo)
    {
        using var r = await client.GetAsync($"{Base}/{id}/finanzas"); r.EnsureSuccessStatusCode();
        var f = await r.Content.ReadFromJsonAsync<JsonElement>();
        return (f.GetProperty("anticipos").EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == anticipo), f, r.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task CA1_a_CA4_comercio_solicita_contador_autoriza_saldo_a_favor_50000_poliza_e_historial()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var comercio = await e.ClienteAsync(true);
        using var contador = await e.ClienteAsync(true, permisosCe: false, gestionarTodas: true,
            extra: [PermisosCanonicos.ComercioExteriorAnticiposRevertirComprobacionAutorizar, PermisosCanonicos.ComercioExteriorExpedientesLeer]);
        // La Dirección de Administración y Finanzas firma pagos de CE, pero ya no autoriza esta reversa.
        using var direccion = await e.ClienteAsync(true, permisosCe: false, gestionarTodas: true, autorizarSaldoCe: true,
            extra: [PermisosCanonicos.ComercioExteriorExpedientesLeer]);
        using var creado = await comercio.PostAsJsonAsync(Base, e.Importacion()); creado.EnsureSuccessStatusCode();
        var id = (await creado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expediente").GetProperty("id").GetGuid();

        // Anticipo de $50,000 pagado y comprobado con la factura del agente por $42,000 (ya saldada con el anticipo).
        var empresa = DemoSesionSeedHostedService.EmpresaId;
        Guid anticipoId, facturaId, agente;
        var hoy = FechaContable.HoyLocal(e.Ahora);
        using (var scope = e.Scope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            agente = await sp.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>().Proveedores
                .Where(p => p.Clave == "DEMO-PROV-ACT").Select(p => p.Id).SingleAsync();
            var cxp = sp.GetRequiredService<CuentasPorPagarDbContext>();
            var factura = FacturaProveedor.CapturarSinOc(empresa, null, Guid.NewGuid().ToString(), agente, e.SucursalId,
                "AG-" + Guid.NewGuid().ToString()[..8], "A", e.Ahora, new DateTimeOffset(hoy.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), hoy.AddDays(30),
                "MXN", 1, 42000, 0, 0, 0, 42000, "Honorarios y maniobras del agente aduanal", e.Ahora);
            factura.Autorizar(Guid.NewGuid(), e.Ahora); cxp.FacturasProveedor.Add(factura); await cxp.SaveChangesAsync();
            facturaId = factura.Id;
            var ce = sp.GetRequiredService<ComercioExteriorDbContext>();
            var exp = await ce.Expedientes.Include(x => x.Anticipos).Include(x => x.Gastos).SingleAsync(x => x.Id == id);
            var a = exp.RegistrarAnticipo(Guid.NewGuid(), agente, "26 24 3984 6001234", new(50000, 0, 0), hoy.AddDays(-5), null);
            a.Solicitar(Guid.NewGuid(), 50000); a.RegistrarPago(Guid.NewGuid(), 50000, "SPEI-M327");
            var concepto = new ConceptoGasto(empresa, "M327-" + Guid.NewGuid().ToString()[..8], "Honorarios del agente"); ce.ConceptosGasto.Add(concepto);
            var gastoId = Guid.NewGuid();
            exp.GuardarGasto(gastoId, new(concepto.Id, agente, factura.FolioProveedor!, "MXN", 42000, hoy, FacturaProveedorId: factura.Id), concepto, 1, "MXN");
            var gasto = exp.Gastos.Single(g => g.Id == gastoId);
            a.Comprobar(new(0, 0, 0), Guid.NewGuid(), Guid.NewGuid(), hoy, Guid.NewGuid()); a.AplicarGastos([gasto]);
            exp.MarcarCambioFinanciero(); await ce.SaveChangesAsync();
            anticipoId = a.Id;
            await ActivatorUtilities.CreateInstance<AnticipoGastosAplicadosHandler>(sp)
                .Handle(new(new(empresa, a.Id, agente, exp.Folio, a.RevisionComprobacion, [new(gasto.Id, factura.Id, 42000)])), default);
            Assert.Equal(0, factura.SaldoPendiente);
        }
        var (antes, _, etag) = await Leer(comercio, id, anticipoId);
        Assert.Equal(42000, antes.GetProperty("aplicado").GetDecimal()); Assert.Equal(8000, antes.GetProperty("saldo").GetDecimal());

        // CA2: sin motivo, o sin solicitud que autorizar, se rechaza y no cambia ningún saldo.
        using (var sinMotivo = await Post(comercio, id, $"anticipos/{anticipoId}/revertir-comprobacion", new { motivo = "  " }, etag))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, sinMotivo.StatusCode);
            Assert.Contains("CE_REVERSA_MOTIVO_REQUERIDO", await sinMotivo.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        using (var sinSolicitud = await Post(contador, id, $"anticipos/{anticipoId}/reversa/autorizar", new { }, etag))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, sinSolicitud.StatusCode);
            Assert.Contains("CE_REVERSA_SIN_SOLICITUD", await sinSolicitud.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        var (intacto, _, _) = await Leer(comercio, id, anticipoId);
        Assert.Equal(42000, intacto.GetProperty("aplicado").GetDecimal()); Assert.Equal(0, intacto.GetProperty("saldoAFavorAgenteMxn").GetDecimal());

        // Comercio exterior solicita con motivo: queda por autorizar sin tocar saldos.
        const string motivo = "Facturas del agente aplicadas al pedimento equivocado";
        using (var solicitud = await Post(comercio, id, $"anticipos/{anticipoId}/revertir-comprobacion", new { motivo }, etag))
        {
            solicitud.EnsureSuccessStatusCode(); etag = solicitud.Headers.ETag!.Tag;
        }
        var (pendiente, _, _) = await Leer(comercio, id, anticipoId);
        Assert.Equal("Reversa de comprobación por autorizar", pendiente.GetProperty("estado").GetString());
        Assert.Equal(42000, pendiente.GetProperty("aplicado").GetDecimal());

        // Otro rol (Dirección) no puede autorizar; quien solicitó tampoco, aunque tenga el permiso.
        using (var otroRol = await Post(direccion, id, $"anticipos/{anticipoId}/reversa/autorizar", new { }, etag))
            Assert.Equal(HttpStatusCode.Forbidden, otroRol.StatusCode);
        using (var mismo = await Post(comercio, id, $"anticipos/{anticipoId}/reversa/autorizar", new { }, etag))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, mismo.StatusCode);
            Assert.Contains("CE_REVERSA_MISMA_PERSONA", await mismo.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        // CA1: el Contador General autoriza.
        using (var autorizada = await Post(contador, id, $"anticipos/{anticipoId}/reversa/autorizar", new { }, etag))
        {
            autorizada.EnsureSuccessStatusCode(); etag = autorizada.Headers.ETag!.Tag;
        }
        var (revertido, finanzas, _) = await Leer(contador, id, anticipoId);
        Assert.Equal("Pendiente de comprobar", revertido.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, revertido.GetProperty("aplicado").ValueKind);
        Assert.Equal(50000, revertido.GetProperty("saldoAFavorAgenteMxn").GetDecimal());
        // CA4: historial con usuario, fecha, motivo y firma.
        var r = revertido.GetProperty("reversasComprobacion").EnumerateArray().Single();
        Assert.True(r.GetProperty("autorizada").GetBoolean());
        Assert.Equal(motivo, r.GetProperty("motivo").GetString());
        var solicito = r.GetProperty("solicitadoPor").GetGuid(); var firmo = r.GetProperty("firmadoPor").GetGuid();
        Assert.NotEqual(solicito, firmo);
        Assert.True(r.GetProperty("solicitadoEn").GetDateTimeOffset() <= r.GetProperty("firmadoEn").GetDateTimeOffset());
        Assert.True(finanzas.GetProperty("nombres").TryGetProperty(firmo.ToString(), out _));
        Assert.True(finanzas.GetProperty("nombres").TryGetProperty(solicito.ToString(), out _));

        // Una reversa se aplica una sola vez.
        using (var repetida = await Post(contador, id, $"anticipos/{anticipoId}/reversa/autorizar", new { }, etag))
            repetida.EnsureSuccessStatusCode();
        Assert.Single((await Leer(contador, id, anticipoId)).Anticipo.GetProperty("reversasComprobacion").EnumerateArray());

        // Cuentas por pagar: la factura vuelve a quedar por pagar y el agente tiene $50,000 a favor (entrega repetida no duplica).
        using (var scope = e.Scope())
        {
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var ce = sp.GetRequiredService<ComercioExteriorDbContext>();
            var eventosCe = (await ce.OutboxEntries.Where(x => x.EventType == "comercio_exterior.anticipo-agente.gastos-aplicados.v1"
                    || x.EventType == AnticipoComprobacionRevertidaHandler.EventType).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToListAsync())
                .Where(x => x.Payload.Contains(anticipoId.ToString(), StringComparison.Ordinal)).ToList();
            Assert.Single(eventosCe, x => x.EventType == AnticipoComprobacionRevertidaHandler.EventType);
            await using var serviceBus = new Azure.Messaging.ServiceBus.ServiceBusClient("Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=dGVzdA==");
            using var worker = ActivatorUtilities.CreateInstance<Millet.CuentasPorPagar.Infrastructure.Workers.ComercioExteriorEventListenerWorker>(sp, serviceBus);
            ILocalIntegrationEventConsumer consumidor = worker;
            foreach (var ev in eventosCe.Concat(eventosCe)) await consumidor.ConsumirLocalAsync(ev.EventType, ev.Id, ev.Payload, default);

            var cxp = sp.GetRequiredService<CuentasPorPagarDbContext>();
            var factura = await cxp.FacturasProveedor.AsNoTracking().SingleAsync(f => f.Id == facturaId);
            Assert.Equal(42000, factura.SaldoPendiente);
            var saldo = Assert.Single(await cxp.AnticiposProveedor.AsNoTracking()
                .Where(x => x.ReferenciaOrigen != null && x.ReferenciaOrigen.StartsWith(anticipoId + ":reversa:")).ToListAsync());
            Assert.Equal(50000, saldo.SaldoAmortizable); Assert.Equal(agente, saldo.ProveedorId);

            // CA3: el saldo a favor compensa la factura del agente y nunca queda negativo.
            var aplicar = ActivatorUtilities.CreateInstance<Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarAnticipo.AplicarAnticipoAFacturaHandler>(sp);
            await Assert.ThrowsAsync<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>(() =>
                aplicar.Handle(new(factura.Id, factura.Version, saldo.Id, saldo.Version, 50000.01m), default));
            await aplicar.Handle(new(factura.Id, factura.Version, saldo.Id, saldo.Version, 42000), default);
            cxp.ChangeTracker.Clear();
            Assert.Equal(8000, (await cxp.AnticiposProveedor.SingleAsync(x => x.Id == saldo.Id)).SaldoAmortizable);
            Assert.Equal(0, (await cxp.FacturasProveedor.SingleAsync(f => f.Id == facturaId)).SaldoPendiente);

            // Póliza de reversa: cargo al saldo del agente, abono a Anticipo al agente aduanal y reposición de Proveedores.
            var contab = sp.GetRequiredService<ContabilidadDbContext>();
            foreach (var (rol, control) in new[] { ("ANTICIPO_A_PROVEEDORES", 0), ("ANTICIPO_AGENTE_ADUANAL", 0), ("PROVEEDORES", 2) })
                await contab.Database.ExecuteSqlRawAsync("""
                    UPDATE contabilidad.roles_cuenta SET cuenta_id = (SELECT c.id FROM contabilidad.cuentas_contables c
                        WHERE c.empresa_id = {0} AND c.tipo = 1 AND c.cuenta_control = {2} AND c.deleted_at IS NULL ORDER BY c.codigo LIMIT 1)
                    WHERE empresa_id = {0} AND codigo = {1} AND cuenta_id IS NULL
                    """, empresa, rol, control);
            var polizaEvento = (await cxp.OutboxEntries.Where(x => x.EventType == "cuentas_por_pagar.anticipo-agente.comprobacion-revertida.v1").ToListAsync())
                .Single(x => x.Payload.Contains(anticipoId.ToString(), StringComparison.Ordinal));
            ILocalIntegrationEventConsumer contabilidad = new CuentasPorPagarContabilidadListenerWorker(null,
                sp.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NullLogger<CuentasPorPagarContabilidadListenerWorker>.Instance);
            await contabilidad.ConsumirLocalAsync(polizaEvento.EventType, polizaEvento.Id, polizaEvento.Payload, default);
            var solicitud = r.GetProperty("solicitudId").GetGuid();
            var rechazo = await contab.RechazosContables.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.EventoId == polizaEvento.Id);
            var poliza = await contab.Polizas.IgnoreQueryFilters().AsNoTracking().Include(p => p.Partidas)
                .SingleOrDefaultAsync(p => p.OrigenDocumentoId == solicitud);
            Assert.True(poliza is not null, $"Sin póliza de reversa: {rechazo?.Causa} {rechazo?.Mensaje} {rechazo?.Detalle}");
            Assert.Equal(92000, poliza!.Partidas.Sum(p => p.Cargo)); Assert.Equal(92000, poliza.Partidas.Sum(p => p.Abono));
        }
    }
}
