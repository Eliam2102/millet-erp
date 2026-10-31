using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Contabilidad.Application.CuentaPuente;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// K10.5 contra el API y Postgres reales: las pólizas se registran con el puerto del motor (IPolizaContablePort) con la
/// <see cref="ReferenciaPuente"/> en la partida de la puente, y el observador de C1.4 (<see cref="ObservadorCuentaPuente"/>) escribe los
/// movimientos en la misma transacción. Criterios a, b, c, d y f de la ficha; e (cancelación de recepción) se simula con la anulación por
/// documento, porque el evento de cancelación de recepción aún no existe (86e3tabdp). El folio y proveedor de cada OC los da un doble
/// del puerto de Compras. Montos de la puente sin IVA. Año lejano propio.
/// </summary>
public class CuentaPuenteHttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Reporte = Base + "/reportes/cuenta-puente";

    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    private sealed record Escenario(
        string Suf, WebApplicationFactory<Program> F, HttpClient Admin, Guid Sucursal, Guid Puente, Guid Inventario, Guid Iva, Guid Proveedores,
        Guid Tipo, string Clave, int Anio, OrdenesDoble Ordenes);

    /// <summary>Doble del puerto de Compras: las OC del escenario no existen en Compras.</summary>
    private sealed class OrdenesDoble : IOrdenCompraPuentePort
    {
        private readonly ConcurrentDictionary<Guid, OrdenCompraPuente> _ocs = new();

        public void Agregar(Guid id, string folio, Guid proveedor) => _ocs[id] = new(id, folio, proveedor);

        public Task<IReadOnlyDictionary<Guid, OrdenCompraPuente>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, OrdenCompraPuente>>(
                ids.Where(_ocs.ContainsKey).ToDictionary(id => id, id => _ocs[id]));
    }

    private async Task<Escenario> CrearEscenarioAsync()
    {
        var suf = Sufijo();
        var anio = Random.Shared.Next(2100, 2990);
        var ordenes = new OrdenesDoble();
        var f = factory.ConEmpresa(EmpresaBootstrapId, ("Contabilidad:CuentaPuente:CodigoCuenta", Codigo(suf, "2.1")))
            .WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IOrdenCompraPuentePort>();
                s.AddSingleton<IOrdenCompraPuentePort>(ordenes);
            }));
        var admin = await LoginAsync(f);
        await AsegurarPeriodosAbiertosAsync(admin, new DateOnly(anio, 8, 1));
        var sucursal = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = $"PU{suf}", Nombre = $"FIX Sucursal {suf}" });
        sucursal.EnsureSuccessStatusCode();
        var activo = await CrearCuenta(admin, Codigo(suf, "1"), "FIX activo");
        var inventario = await CrearCuenta(admin, Codigo(suf, "1.1"), "FIX inventario", Id(activo));
        var iva = await CrearCuenta(admin, Codigo(suf, "1.2"), "FIX IVA acreditable pendiente", Id(activo));
        var pasivo = await CrearCuenta(admin, Codigo(suf, "2"), "FIX pasivo", naturaleza: "Acreedora");
        var puente = await CrearCuenta(admin, Codigo(suf, "2.1"), "FIX DEMO-PUENTE recepción–factura", Id(pasivo), naturaleza: "Acreedora");
        var proveedores = await CrearCuenta(admin, Codigo(suf, "2.2"), "FIX proveedores", Id(pasivo), naturaleza: "Acreedora");
        var clave = $"FIX-{suf}";
        var tipo = await admin.PostAsJsonAsync($"{Base}/tipos-documento", new { clave, nombre = $"FIX Puente {suf}", esPrueba = true, tipoSat = "Diario" });
        Assert.Equal(HttpStatusCode.Created, tipo.StatusCode);
        return new(suf, f, admin, Id(await Json(sucursal)), Id(puente), Id(inventario), Id(iva), Id(proveedores), Id(await Json(tipo)), clave, anio, ordenes);
    }

    private async Task LimpiarAsync(Escenario? e)
    {
        if (e is null) return;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.movimientos_puente WHERE poliza_id IN (SELECT id FROM contabilidad.polizas WHERE tipo_operacion_id = {0})", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE consumidor = 'POLIZA' AND referencia IN (SELECT id::text FROM contabilidad.polizas WHERE tipo_operacion_id = {0})", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.polizas SET anulada_por_poliza_id = NULL, poliza_anulada_id = NULL WHERE tipo_operacion_id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE tipo_operacion_id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.consecutivos_poliza WHERE tipo_operacion_id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.tipos_documento_contable WHERE id = {0}", e.Tipo);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables_uso WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", $"FIX-{e.Suf}-%");
        await Limpiar(factory.Services, e.Suf);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.periodos_contables WHERE anio = {0}", e.Anio);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.ejercicios_contables WHERE anio = {0}", e.Anio);
        e.F.Dispose();
    }

    /// <summary>
    /// Registra la póliza automática del documento; la partida de la cuenta puente lleva la referencia de OC (las demás no) y el
    /// observador escribe el movimiento de la puente en la misma transacción.
    /// </summary>
    private static async Task<PolizaRegistrada> PolizaAsync(Escenario e, DateOnly fecha, string tipoDocumento, Guid documentoId,
        ReferenciaPuente? referencia, params (Guid Cuenta, decimal Cargo, decimal Abono)[] partidas)
    {
        Sobre.Value = EmpresaBootstrapId;
        using var scope = e.F.Services.CreateScope();
        var puerto = scope.ServiceProvider.GetRequiredService<IPolizaContablePort>();
        return await puerto.RegistrarAutomaticaAsync(new SolicitudPolizaAutomatica(
            e.Clave, fecha, $"FIX {tipoDocumento} {e.Suf}", new DocumentoOrigenPoliza("compras", tipoDocumento, documentoId, null, null),
            $"fix.{tipoDocumento}:{documentoId}",
            [.. partidas.Select(p => new PartidaRequest(p.Cuenta, null, p.Cargo, p.Abono, e.Sucursal, Puente: p.Cuenta == e.Puente ? referencia : null))]),
            default);
    }

    private static Task<PolizaRegistrada> RecepcionAsync(Escenario e, DateOnly fecha, Guid oc, string folioOc, decimal importe, Guid proveedor)
    {
        var recepcion = Guid.NewGuid();
        e.Ordenes.Agregar(oc, folioOc, proveedor);
        return PolizaAsync(e, fecha, "recepcion", recepcion, new ReferenciaPuente(oc, null, TipoDocumentoPuente.Recepcion, recepcion),
            (e.Inventario, importe, 0m), (e.Puente, 0m, importe));
    }

    private static async Task<Guid> FacturaAsync(Escenario e, DateOnly fecha, Guid oc, string folioOc, decimal importe, Guid proveedor)
    {
        var factura = Guid.NewGuid();
        var iva = decimal.Round(importe * 0.16m, 2);
        e.Ordenes.Agregar(oc, folioOc, proveedor);
        await PolizaAsync(e, fecha, "factura", factura, new ReferenciaPuente(oc, null, TipoDocumentoPuente.Factura, factura),
            (e.Puente, importe, 0m), (e.Iva, iva, 0m), (e.Proveedores, 0m, importe + iva));
        return factura;
    }

    private static async Task<JsonElement> ReporteAsync(Escenario e, DateOnly corte, bool soloConSaldo = true, Guid? proveedor = null)
    {
        var url = $"{Reporte}?corte={corte:yyyy-MM-dd}&soloConSaldo={soloConSaldo.ToString().ToLowerInvariant()}"
            + (proveedor is { } p ? $"&proveedorId={p}" : string.Empty);
        var r = await e.Admin.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await Json(r);
    }

    private static JsonElement? Fila(JsonElement reporte, Guid oc) =>
        reporte.GetProperty("filas").EnumerateArray().Cast<JsonElement?>().FirstOrDefault(f => f!.Value.GetProperty("ordenCompraId").GetGuid() == oc);

    [Fact]
    public async Task K105_concilia_la_puente_por_OC_con_antiguedad_y_cuadra_contra_la_balanza()
    {
        Escenario? e = null;
        try
        {
            e = await CrearEscenarioAsync();
            var y = e.Anio;
            var corte = new DateOnly(y, 10, 31);
            Guid oc1 = Guid.NewGuid(), oc2 = Guid.NewGuid(), oc3 = Guid.NewGuid(), oc4 = Guid.NewGuid(), oc5 = Guid.NewGuid();
            Guid prov1 = Guid.NewGuid(), prov2 = Guid.NewGuid();

            // a) Recepción packing list 10,000 (1-oct) + factura 10,000 + IVA (6-oct): la OC queda en cero.
            await RecepcionAsync(e, new(y, 10, 1), oc1, "OC-DEMO-K105-01", 10_000m, prov1);
            await FacturaAsync(e, new(y, 10, 6), oc1, "OC-DEMO-K105-01", 10_000m, prov1);

            // b) Factura de reserva 5,000 + IVA sin recepción (10-oct).
            await FacturaAsync(e, new(y, 10, 10), oc2, "OC-DEMO-K105-02", 5_000m, prov2);

            // c) Recepción sin factura y sin movimiento desde hace 61 días (31-ago).
            await RecepcionAsync(e, new(y, 8, 31), oc3, "OC-DEMO-K105-03", 1_000m, prov1);

            // d) Recepción 10,000 + factura 10,000 + devolución 2,000 + nota de cargo 2,000 (sin OC: se resuelve por la factura).
            await RecepcionAsync(e, new(y, 10, 1), oc4, "OC-DEMO-K105-04", 10_000m, prov1);
            var factura4 = await FacturaAsync(e, new(y, 10, 6), oc4, "OC-DEMO-K105-04", 10_000m, prov1);
            var devolucion = Guid.NewGuid();
            await PolizaAsync(e, new(y, 10, 12), "devolucion", devolucion,
                new ReferenciaPuente(oc4, null, TipoDocumentoPuente.Devolucion, devolucion),
                (e.Puente, 2_000m, 0m), (e.Inventario, 0m, 2_000m));
            var nota = Guid.NewGuid();
            await PolizaAsync(e, new(y, 10, 14), "nota-cargo", nota,
                new ReferenciaPuente(Guid.Empty, null, TipoDocumentoPuente.NotaCargo, nota, factura4),
                (e.Proveedores, 2_320m, 0m), (e.Puente, 0m, 2_000m), (e.Iva, 0m, 320m));

            // e) (simulado) Recepción 700 anulada por cancelación del documento: la reversa regresa la OC a cero.
            var recepcion5 = await RecepcionAsync(e, new(y, 10, 15), oc5, "OC-DEMO-K105-05", 700m, prov1);
            Sobre.Value = EmpresaBootstrapId;
            using (var scope = e.F.Services.CreateScope())
            {
                var puerto = scope.ServiceProvider.GetRequiredService<IPolizaContablePort>();
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                var doc = await db.Polizas.AsNoTracking().Where(p => p.Id == recepcion5.Id).Select(p => p.OrigenDocumentoId!.Value).SingleAsync();
                var anulacion = Assert.Single(await puerto.AnularPorDocumentoOrigenAsync("compras", "recepcion", doc, "FIX recepción cancelada en Almacén", default));
                // Repetir la anulación es idempotente: el puerto no vuelve a avisar y la reversa no se duplica.
                Assert.True(Assert.Single(await puerto.AnularPorDocumentoOrigenAsync("compras", "recepcion", doc, "FIX repetida", default)).Idempotente);
                var reversa = Assert.Single(await db.MovimientosPuente.AsNoTracking().Where(m => m.OrdenCompraId == oc5 && m.EsReversa).ToListAsync());
                Assert.Equal(anulacion.Id, reversa.PolizaId);
            }

            // Al corte 31-oct.
            var reporte = await ReporteAsync(e, corte);
            Assert.Null(Fila(reporte, oc1));
            Assert.Null(Fila(reporte, oc4));
            Assert.Null(Fila(reporte, oc5));
            var f2 = Fila(reporte, oc2)!.Value;
            Assert.Equal(5_000m, f2.GetProperty("saldo").GetDecimal());
            Assert.Equal("FacturadoSinRecibir", f2.GetProperty("naturaleza").GetString());
            Assert.Equal("Facturado sin recibir", f2.GetProperty("naturalezaEtiqueta").GetString());
            Assert.Equal(21, f2.GetProperty("diasAntiguedad").GetInt32());
            Assert.Equal("OC-DEMO-K105-02", f2.GetProperty("ordenCompraFolio").GetString());
            var f3 = Fila(reporte, oc3)!.Value;
            Assert.Equal(-1_000m, f3.GetProperty("saldo").GetDecimal());
            Assert.Equal("RecibidoSinFacturar", f3.GetProperty("naturaleza").GetString());
            Assert.Equal(61, f3.GetProperty("diasAntiguedad").GetInt32());
            Assert.Equal("MasDe60", f3.GetProperty("tramo").GetString());
            Assert.Equal("Más de 60", f3.GetProperty("tramoEtiqueta").GetString());

            // f) El total del reporte es el saldo de la puente en balanza.
            var totales = reporte.GetProperty("totales");
            Assert.Equal(4_000m, totales.GetProperty("totalReporte").GetDecimal());
            Assert.Equal(4_000m, totales.GetProperty("saldoBalanza").GetDecimal());
            Assert.Equal(0m, totales.GetProperty("partidasSinOc").GetDecimal());
            Assert.True(totales.GetProperty("cuadra").GetBoolean());
            Assert.Equal(2, totales.GetProperty("ordenesConSaldo").GetInt32());
            Assert.Equal(-1_000m, totales.GetProperty("tramoMasDe60").GetDecimal());
            Assert.Equal(5, (await ReporteAsync(e, corte, soloConSaldo: false)).GetProperty("filas").GetArrayLength());

            // d) El inventario bajó 2,000 con la devolución: 10,000 + 1,000 + 10,000 − 2,000 (la recepción anulada no cuenta).
            Sobre.Value = EmpresaBootstrapId;
            using (var scope = e.F.Services.CreateScope())
            {
                var saldos = await scope.ServiceProvider.GetRequiredService<ISaldosContablesReadPort>().ObtenerAsync(corte, [e.Inventario], null, default);
                Assert.Equal(19_000m, saldos[e.Inventario]);
            }

            // Detalle de la OC 4: la nota de cargo tomó la OC de su factura y el acumulado termina en cero.
            var detalle = await Json(await e.Admin.GetAsync($"{Reporte}/{oc4}/movimientos?corte={corte:yyyy-MM-dd}"));
            var movs = detalle.GetProperty("movimientos").EnumerateArray().ToList();
            Assert.Equal(4, movs.Count);
            Assert.Equal("NotaCargo", movs[^1].GetProperty("tipoDocumento").GetString());
            Assert.Equal(factura4, movs[^1].GetProperty("facturaOrigenId").GetGuid());
            Assert.Equal(0m, movs[^1].GetProperty("saldoAcumulado").GetDecimal());
            Assert.Equal("Saldada", detalle.GetProperty("saldo").GetProperty("naturaleza").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await e.Admin.GetAsync($"{Reporte}/{Guid.NewGuid()}/movimientos?corte={corte:yyyy-MM-dd}")).StatusCode);

            // Filtro de proveedor: solo sus OC y sin conciliar contra balanza.
            var deProv2 = await ReporteAsync(e, corte, proveedor: prov2);
            Assert.Equal(oc2, Assert.Single(deProv2.GetProperty("filas").EnumerateArray()).GetProperty("ordenCompraId").GetGuid());
            Assert.Equal(JsonValueKind.Null, deProv2.GetProperty("totales").GetProperty("saldoBalanza").ValueKind);

            // Una póliza de ajuste en la puente sin referencia de OC aparece como «partidas sin OC» y el reporte ya no cuadra.
            await PolizaAsync(e, new(y, 10, 20), "ajuste", Guid.NewGuid(), null, (e.Puente, 50m, 0m), (e.Inventario, 0m, 50m));
            var conAjuste = (await ReporteAsync(e, corte)).GetProperty("totales");
            Assert.Equal(4_050m, conAjuste.GetProperty("saldoBalanza").GetDecimal());
            Assert.Equal(50m, conAjuste.GetProperty("partidasSinOc").GetDecimal());
            Assert.False(conAjuste.GetProperty("cuadra").GetBoolean());

            // b) Al recibir los 5,000 (3-nov), la OC 2 queda en cero.
            await RecepcionAsync(e, new(y, 11, 3), oc2, "OC-DEMO-K105-02", 5_000m, prov2);
            Assert.Null(Fila(await ReporteAsync(e, new DateOnly(y, 11, 30)), oc2));
        }
        finally { await LimpiarAsync(e); }
    }
}
