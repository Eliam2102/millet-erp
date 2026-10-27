using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Almacen;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Oc.Ports;
using Millet.Compras.Infrastructure.Stubs;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.Tesoreria.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// Flujo compra -> pago punta a punta POR API con dos usuarios (A solicita, B opera el resto).
/// Los listeners de Service Bus se ejecutan con sus clases reales (ProcessAsync) alimentados desde
/// el outbox de cada modulo: sin mocks de negocio.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class CompraAPagoDemoTests(WebApplicationFactory<Program> factory)
{
    private static Guid Articulo; // articulo nuevo por corrida: el stock de Almacen persiste y cubriria la RQ en re-corridas
    internal static readonly Guid Proveedor = Guid.Parse("00000005-0001-0000-0000-000000000001"); // PROV-EST, RFC SES010101AAA
    internal const string ProveedorRfc = "SES010101AAA";
    internal static readonly Guid CentroCosto = Guid.Parse("0000000c-0005-0000-0000-000000000001");
    private readonly List<string> _bitacora = [];

    [Fact]
    public async Task Rq_A_autoriza_B_oc_recepcion_factura_pasivo_pago_y_regreso_de_eventos()
    {
        await using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            // El stub de stock con ratio 1 cubre todo con almacen; ratio 0 = "no hay existencia" => se genera OC por el faltante.
            s.PostConfigure<ComprasStubsOptions>(o => o.Stubs.Stock.DefaultRatio = 0m);
        }));
        var hoy = DateOnly.FromDateTime(DateTime.Now);
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cat = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var pza = await cat.UnidadesMedida.SingleAsync(u => u.Codigo == "PZA");
            var art = new Millet.DatosMaestros.Domain.Articulo(Guid.NewGuid(), $"CP{Guid.NewGuid():N}"[..16], "DEMO compra a pago", "PZA", unidadMedidaId: pza.Id);
            cat.Add(art); await cat.SaveChangesAsync(); Articulo = art.Id;
        }
        await using var periodo = await AbrirPeriodoSiHaceFaltaAsync(app, hoy);

        // ---------- usuarios ----------
        var todos = PermisosCanonicos.Todos.Select(p => p.Codigo).ToArray();
        using var a = await ClienteConPermisosAsync(app, "compras.requisiciones.crear", "compras.requisiciones.leer", "compras.requisiciones.editar", "centros_costo.dim3.leer-todos",
            "compras.ordenes.crear", "compras.ordenes.leer", "compras.ordenes.adjuntar");
        using var b = await ClienteConPermisosAsync(app, todos);
        await AsociarSucursalAsync(app, a);
        var idA = await UserIdAsync(a); var idB = await UserIdAsync(b);
        Assert.NotEqual(idA, idB);

        // Umbral del depto: monto <= umbral => basta N1 (con 2 usuarios no hay quien firme N2; sin umbral exige N1+N2 de personas distintas).
        await Ok(await b.PutAsJsonAsync("/api/v1/compras/umbrales-aprobacion", new { DepartamentoId = TestComprasFixtures.DeptoCompras, UmbralMenorCuantia = 1000m, UmbralMonto = 100000m, Moneda = "MXN", ModoNivel2 = "SoloMonto" }), "B configura umbral depto");

        // ---------- 1. A crea RQ ----------
        var rqResp = await a.PostAsJsonAsync("/api/v1/compras/requisiciones", TestComprasFixtures.BuildCrearRqValidBody(descripcion: "DEMO compra a pago"));
        await Ok(rqResp, "A crea RQ");
        var rqId = (await J(rqResp)).GetProperty("id").GetGuid();
        await Ok(await a.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/lineas", new
        {
            ArticuloId = Articulo, Cantidad = 10m, UnidadMedida = "PZA", PrecioEstimadoMonto = 15m, PrecioEstimadoMoneda = "MXN",
            CentroCostoId = CentroCosto,
        }), "A agrega linea");
        await Ok(await a.PostAsync($"/api/v1/compras/requisiciones/{rqId}/transmitir", null), "A transmite RQ");

        // ---------- 2. B autoriza RQ (N1; sin umbral configurado => fail-open solo N1) ----------
        await Ok(await b.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/autorizaciones", new { Nivel = 1, Notas = "DEMO B autoriza" }), "B autoriza RQ N1");
        var rq = await J(await b.GetAsync($"/api/v1/compras/requisiciones/{rqId}"));
        _bitacora.Add($"RQ estado={rq.GetProperty("estado")}");

        // ---------- 3. OC del faltante ----------
        var ocResp = await a.PostAsJsonAsync("/api/v1/compras/ordenes/desde-requisicion", new
        {
            RequisicionId = rqId, SucursalCodigo = "MID", FolioAnio = (short)hoy.Year, ProveedorId = Proveedor,
            CondicionesPagoId = Guid.NewGuid(), UsoPrincipalId = Guid.NewGuid(), UsoCfdiClave = "G03", MetodoPago = "PPD", FormaPagoClave = "99", FechaDocumento = hoy,
        });
        await Ok(ocResp, "A crea OC desde RQ (faltante)");
        var ocTxt = await ocResp.Content.ReadAsStringAsync();
        Assert.True(ocTxt.Length > 0, $"OC desde RQ: HTTP {(int)ocResp.StatusCode} sin cuerpo");
        var ocId = JsonDocument.Parse(ocTxt).RootElement.GetProperty("ordenCompraId").GetGuid();
        await AdjuntarCotizacionAsync(app, ocId);
        await Ok(await a.PostAsync($"/api/v1/compras/ordenes/{ocId}/transmitir", null), "A transmite OC");
        await Ok(await b.PostAsJsonAsync($"/api/v1/compras/ordenes/{ocId}/autorizaciones", new { Nivel = 1 }), "B autoriza OC N1");
        var oc = await J(await b.GetAsync($"/api/v1/compras/ordenes/{ocId}"));
        var estadoOc = oc.GetProperty("estado").GetInt32();
        Assert.Equal((int)EstadoOrdenCompra.Autorizada, estadoOc);
        var lineaOcId = oc.GetProperty("lineas")[0].GetProperty("id").GetGuid();
        var total = oc.GetProperty("lineas")[0].GetProperty("subtotalLinea").GetDecimal();

        // ---------- 4. Recepcion exacta (tolerancia 0) en Almacen ----------
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var bin = await SembrarBinAsync(app);
        await Ok(await b.PostAsJsonAsync("/api/v1/almacen/recepciones/", new
        {
            OrdenCompraId = ocId, FechaMovimiento = hoy, CfdiUuidFiscal = uuid, Observaciones = "DEMO recepcion exacta",
            Lineas = new[] { new { ArticuloId = Articulo, LineaOcId = lineaOcId, Cantidad = 10m, UbicacionId = bin } },
        }), "B recibe 10/10 en Almacen");
        await BombearAsync(app); // OcAutorizada/OcRecepcionRegistrada -> CxP y Compras

        // ---------- 5. CFDI + factura en CxP ----------
        var subtotal = total; var iva = Math.Round(subtotal * 0.16m, 2);
        var xml = Cfdi(uuid, ProveedorRfc, subtotal, iva, hoy);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(xml)) { Headers = { ContentType = new("text/xml") } }, "xml", "f.xml" } };
        var cfdiResp = await b.PostAsync("/api/v1/cuentas-por-pagar/cfdis/cargar", form);
        await Ok(cfdiResp, "B carga CFDI");
        var cfdiId = (await J(cfdiResp)).GetProperty("id").GetGuid();
        var fecha = new DateTimeOffset(hoy.Year, hoy.Month, hoy.Day, 12, 0, 0, TimeSpan.Zero);
        var facResp = await b.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas", new
        {
            OrdenCompraId = ocId, ProveedorId = Proveedor, SucursalId = TestComprasFixtures.SucursalMid, CfdiRecibidoId = cfdiId, UuidCfdi = uuid,
            FolioProveedor = "F-DEMO", FechaDocumento = fecha, FechaContabilizacion = fecha, FechaVencimiento = hoy.AddDays(30), Moneda = "MXN",
            Subtotal = subtotal, Descuentos = 0m, ImpuestosTrasladados = iva, Retenciones = 0m, Total = subtotal + iva,
            Lineas = new[] { new { ArticuloId = (Guid?)Articulo, Descripcion = "DEMO", Cantidad = 10m, ClaveUnidad = "H87", PrecioUnitario = subtotal / 10m, Importe = subtotal, LineaOcId = (Guid?)lineaOcId } },
        });
        await Ok(facResp, "B captura factura conciliada contra OC/recepcion");
        var fac = await J(facResp);
        var facturaId = fac.GetProperty("id").GetGuid();
        _bitacora.Add($"Factura estado={fac.GetProperty("estado")} dif={fac.GetProperty("diferenciaContraOc")}");

        // ---------- 6. CxP autoriza -> pasivo en Tesoreria ----------
        var det = await J(await b.GetAsync($"/api/v1/cuentas-por-pagar/facturas/{facturaId}"));
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/cuentas-por-pagar/facturas/{facturaId}/autorizar"))
        {
            req.Headers.Add("X-Expected-Version", det.GetProperty("version").GetInt32().ToString());
            await Ok(await b.SendAsync(req), "B autoriza factura en CxP");
        }
        await BombearAsync(app);
        var pasivos = await J(await b.GetAsync($"/api/v1/tesoreria/pasivos-pendientes?proveedorId={Proveedor}"));
        Assert.Contains(pasivos.GetProperty("items").EnumerateArray(), p => p.GetProperty("facturaProveedorId").GetGuid() == facturaId);

        // ---------- 7. Pago desde Tesoreria ----------
        var cuentaResp = await b.PostAsJsonAsync("/api/v1/tesoreria/cuentas", new { banco = "Banco DEMO", numeroCuenta = DateTime.UtcNow.Ticks.ToString(), moneda = "MXN", sucursal = "MID", titular = "Millet DEMO", finalidad = "Pagos", firmantes = "DEMO" });
        await Ok(cuentaResp, "B crea cuenta bancaria");
        var cuentaId = (await J(cuentaResp)).GetProperty("id").GetGuid();
        await Ok(await b.PostAsJsonAsync("/api/v1/tesoreria/pagos", new
        {
            CuentaBancariaId = cuentaId, FechaValor = hoy, ReferenciaBancaria = "DEMO-PAGO",
            Aplicaciones = new[] { new { FacturaProveedorId = facturaId, Importe = subtotal + iva } },
        }), "B registra pago al proveedor");
        await BombearAsync(app); // PagoFacturaProveedorEvent -> CxP; pago-aplicado -> Compras

        // ---------- 8. Resultado ----------
        var final = await J(await b.GetAsync($"/api/v1/cuentas-por-pagar/facturas/{facturaId}"));
        _bitacora.Add($"Factura final: {final}");
        var ocFinal = await J(await b.GetAsync($"/api/v1/compras/ordenes/{ocId}"));
        _bitacora.Add($"OC final: estado={ocFinal.GetProperty("estado")} recepcion={ocFinal.GetProperty("subEstadoRecepcion")} facturacion={ocFinal.GetProperty("subEstadoFacturacion")} pago={ocFinal.GetProperty("subEstadoPago")}");
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "compra-a-pago-bitacora.txt"), string.Join('\n', _bitacora.Where(l => !l.Contains("no manejado"))));
        // Factura pagada en CxP (PagoFacturaProveedorEvent regreso) ...
        Assert.Equal((int)Millet.CuentasPorPagar.Domain.FacturaProveedor.EstadoPasivo.Pagada, final.GetProperty("estado").GetInt32());
        Assert.Equal(0m, final.GetProperty("saldoPendiente").GetDecimal());
        Assert.Equal(subtotal + iva, final.GetProperty("importePagado").GetDecimal());
        // ... y la OC con sub-estado Pago completo (y cerrada por Compras: recepcion+facturacion+pago completas).
        Assert.Equal((int)EstadoOrdenCompra.Cerrada, ocFinal.GetProperty("estado").GetInt32());
        Assert.Equal(ocFinal.GetProperty("subEstadoRecepcion").ToString(), ocFinal.GetProperty("subEstadoPago").ToString());
    }

    // ================= helpers =================

    internal static async Task<JsonElement> J(HttpResponseMessage r)
    {
        var t = await r.Content.ReadAsStringAsync();
        if (t.Length == 0) throw new Xunit.Sdk.XunitException($"Respuesta vacia HTTP {(int)r.StatusCode} de {r.RequestMessage?.Method} {r.RequestMessage?.RequestUri?.PathAndQuery}");
        return JsonDocument.Parse(t).RootElement.Clone();
    }

    internal static async Task Ok(HttpResponseMessage r, string paso)
    {
        if ((int)r.StatusCode is >= 200 and < 300) return;
        throw new Xunit.Sdk.XunitException($"[{paso}] HTTP {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }

    internal static async Task<Guid> UserIdAsync(HttpClient c) =>
        (await J(await c.GetAsync("/api/auth/me"))).GetProperty("userId").GetGuid();

    internal static async Task AsociarSucursalAsync(WebApplicationFactory<Program> app, HttpClient c)
    {
        var uid = await UserIdAsync(c);
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        db.UsuarioSucursales.Add(new UsuarioSucursal(Guid.CreateVersion7(), uid, TestComprasFixtures.SucursalMid, EmpresaBootstrapId));
        await db.SaveChangesAsync();
    }

    internal static async Task AdjuntarCotizacionAsync(WebApplicationFactory<Program> app, Guid ocId)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var oc = await db.OrdenesCompra.Include(o => o.Adjuntos).SingleAsync(o => o.Id == ocId);
        var tipo = await db.TiposDocumentoOc.SingleAsync(t => t.Clave == "cotizacion");
        oc.AdjuntarDocumento(Guid.NewGuid(), tipo.Id, "cotizacion-demo.pdf", "blob://DEMO/cotizacion", "application/pdf", 10, DateTimeOffset.UtcNow, Guid.NewGuid());
        await db.SaveChangesAsync();
    }

    internal static async Task<Guid> SembrarBinAsync(WebApplicationFactory<Program> app, Guid? articulo = null)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"DM{Guid.NewGuid():N}"[..16], "DEMO compra-pago", TestComprasFixtures.SucursalMid);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "DM", "DEMO", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "DM", "DEMO");
        almacen.AddRange(alm, sub, bin, new AsignacionArticuloUbicacion(Guid.NewGuid(), bin.Id, articulo ?? Articulo));
        await almacen.SaveChangesAsync();
        return bin.Id;
    }

    internal static async Task<IAsyncDisposable> AbrirPeriodoSiHaceFaltaAsync(WebApplicationFactory<Program> app, DateOnly hoy)
    {
        using var scope = app.Services.CreateScope();
        var cs = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>().Database.GetConnectionString()!;
        var fx = new PeriodoContableFixture(cs, EmpresaBootstrapId, hoy.Year);
        try { await fx.SembrarAsync(hoy.Month, cerrarAnteriores: true); return fx; }
        catch { return fx; } // ya existia el ejercicio
    }

    internal static string Cfdi(string uuid, string rfcEmisor, decimal subtotal, decimal iva, DateOnly f) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" Version="4.0" TipoDeComprobante="I" Serie="A" Folio="F-DEMO"
            Fecha="{f:yyyy-MM-dd}T10:00:00" Total="{subtotal + iva:0.00}" SubTotal="{subtotal:0.00}" Moneda="MXN" MetodoPago="PUE">
          <cfdi:Emisor Rfc="{rfcEmisor}" Nombre="Suministros Estandar SA de CV" />
          <cfdi:Receptor Rfc="MIL010101AAA" Nombre="Millet SA" />
          <cfdi:Conceptos><cfdi:Concepto ClaveProdServ="01010101" Cantidad="10" ClaveUnidad="H87" Descripcion="DEMO" ValorUnitario="{subtotal / 10:0.00}" Importe="{subtotal:0.00}" /></cfdi:Conceptos>
          <cfdi:Impuestos TotalImpuestosTrasladados="{iva:0.00}" />
          <cfdi:Complemento><tfd:TimbreFiscalDigital xmlns:tfd="http://www.sat.gob.mx/TimbreFiscalDigital" UUID="{uuid}" FechaTimbrado="{f:yyyy-MM-dd}T10:00:00" /></cfdi:Complemento>
        </cfdi:Comprobante>
        """;

    // ---- bomba de outbox: entrega cada evento a los listeners REALES de los modulos suscritos ----
    private readonly HashSet<Guid> _entregados = [];

    private async Task BombearAsync(WebApplicationFactory<Program> app)
    {
        var fuentes = new (Type Ctx, string Nombre, Type[] Workers)[]
        {
            (typeof(ComprasDbContext), "Compras", [typeof(Millet.CuentasPorPagar.Infrastructure.Workers.ComprasEventListenerWorker)]),
            (typeof(AlmacenDbContext), "Almacen", [typeof(Millet.Compras.Infrastructure.Workers.AlmacenEventListenerWorker), typeof(Millet.CuentasPorPagar.Infrastructure.Workers.AlmacenEventListenerWorker)]),
            (typeof(CuentasPorPagarDbContext), "CxP", [typeof(Millet.Compras.Infrastructure.Workers.CxpEventListenerWorker), typeof(Millet.Almacen.Infrastructure.Workers.CxpEventListenerWorker), typeof(Millet.Tesoreria.Infrastructure.Workers.CuentasPorPagarEventListenerWorker)]),
            (typeof(TesoreriaDbContext), "Tesoreria", [typeof(Millet.CuentasPorPagar.Infrastructure.Workers.TesoreriaEventListenerWorker)]),
        };
        var sb = new ServiceBusClient("Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=dGVzdA==");
        for (var ronda = 0; ronda < 6; ronda++)
        {
            var movido = false;
            foreach (var (ctx, nombre, workers) in fuentes)
            {
                List<IntegrationEventOutboxEntry> entradas;
                using (var scope = app.Services.CreateScope())
                {
                    using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                    var db = (DbContext)scope.ServiceProvider.GetRequiredService(ctx);
                    entradas = (await db.Set<IntegrationEventOutboxEntry>().AsNoTracking().ToListAsync()).OrderBy(e => e.OccurredAt).ToList();
                }
                foreach (var e in entradas.Where(e => _entregados.Add(e.Id)))
                {
                    movido = true;
                    foreach (var w in workers)
                    {
                        var log = new RecLogger();
                        var worker = Activator.CreateInstance(w, sb, app.Services.GetRequiredService<IServiceScopeFactory>(), log.As(w))!;
                        // C1.8: sin smbArgs el listener relanza las fallas (antes las tragaba);
                        // esta bomba las conserva en la bitácora como antes.
                        try
                        {
                            await (Task)w.GetMethod("ProcessAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                                .Invoke(worker, [e.EventType, e.Id, e.Payload, null, CancellationToken.None])!;
                        }
                        catch (Exception ex) { log.Errores.Add($"relanzada: {ex.Message}"); }
                        foreach (var m in log.Errores.Where(m => !m.Contains("no manejado"))) _bitacora.Add($"ERROR {nombre}:{e.EventType} -> {w.Name}: {m}");
                    }
                    _bitacora.Add($"evento {nombre}:{e.EventType}");
                }
            }
            if (!movido) break;
        }
    }

    private sealed class RecLogger : ILogger
    {
        public List<string> Errores { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel l) => true;
        public void Log<TState>(LogLevel l, EventId id, TState s, Exception? ex, Func<TState, Exception?, string> f)
        { if (l >= LogLevel.Warning) Errores.Add(f(s, ex) + (ex is null ? "" : " | " + ex.Message)); }
        public object As(Type worker) => Activator.CreateInstance(typeof(Typed<>).MakeGenericType(worker), this)!;
        private sealed class Typed<T>(RecLogger inner) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel l) => true;
            public void Log<TState>(LogLevel l, EventId id, TState s, Exception? ex, Func<TState, Exception?, string> f) => inner.Log(l, id, s, ex, f);
        }
    }
}

