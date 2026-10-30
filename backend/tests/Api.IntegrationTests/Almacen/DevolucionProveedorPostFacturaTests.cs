using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Compras;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Stubs;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using static Millet.Api.IntegrationTests.Compras.CompraAPagoDemoTests;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Triada Compras - Almacén - CxP: devolución a proveedor de una línea de OC ya totalmente facturada
/// (pedido 20, recibido 20, facturado 20, devuelve 2). La salida física no debe toparse con
/// DEVOLUCION_EXCEDE_NO_FACTURADO porque lo facturado (20) sigue siendo &lt;= MAX(pedido, recibido - devuelto).
/// Todo por API; los eventos del outbox se entregan en proceso a los listeners reales (misma bomba que
/// <see cref="CompraAPagoDemoTests"/>, acotada a los eventos de esta OC y esta devolución).
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class DevolucionProveedorPostFacturaTests(WebApplicationFactory<Program> factory)
{
    private readonly HashSet<Guid> _entregados = [];

    private sealed record Escenario(HttpClient B, Guid Articulo, Guid OcId, Guid LineaOcId, Guid Bin, Guid RecepcionId,
        Guid FacturaId, string Uuid, decimal Subtotal, decimal CostoUnitario) : IDisposable
    {
        public void Dispose() => B.Dispose();
    }

    private WebApplicationFactory<Program> NuevaApp() => factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        s.PostConfigure<ComprasStubsOptions>(o => o.Stubs.Stock.DefaultRatio = 0m)));

    /// <summary>OC autorizada de 20 piezas, recibida 20/20 y facturada 20/20 (el periodo contable lo abre quien llama).</summary>
    private async Task<Escenario> SembrarOcFacturadaAsync(WebApplicationFactory<Program> app, DateOnly hoy)
    {
        Guid articulo;
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cat = scope.ServiceProvider.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
            var pza = await cat.UnidadesMedida.SingleAsync(u => u.Codigo == "PZA");
            var art = new Millet.DatosMaestros.Domain.Articulo(Guid.NewGuid(), $"DP{Guid.NewGuid():N}"[..16], "DEVOL post factura", "PZA", unidadMedidaId: pza.Id);
            cat.Add(art); await cat.SaveChangesAsync(); articulo = art.Id;
        }

        using var a = await ClienteConPermisosAsync(app, "compras.requisiciones.crear", "compras.requisiciones.leer", "compras.requisiciones.editar", "centros_costo.dim3.leer-todos",
            "compras.ordenes.crear", "compras.ordenes.leer", "compras.ordenes.adjuntar");
        var b = await ClienteConPermisosAsync(app, PermisosCanonicos.Todos.Select(p => p.Codigo).ToArray());
        await AsociarSucursalAsync(app, a);

        // ---------- OC autorizada de 20 piezas ----------
        await Ok(await b.PutAsJsonAsync("/api/v1/compras/umbrales-aprobacion", new { DepartamentoId = TestComprasFixtures.DeptoCompras, UmbralMenorCuantia = 1000m, UmbralMonto = 100000m, Moneda = "MXN", ModoNivel2 = "SoloMonto" }), "umbral");
        var rqResp = await a.PostAsJsonAsync("/api/v1/compras/requisiciones", TestComprasFixtures.BuildCrearRqValidBody(descripcion: "DEVOL post factura"));
        await Ok(rqResp, "crea RQ");
        var rqId = (await J(rqResp)).GetProperty("id").GetGuid();
        await Ok(await a.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/lineas", new
        {
            ArticuloId = articulo, Cantidad = 20m, UnidadMedida = "PZA", PrecioEstimadoMonto = 15m, PrecioEstimadoMoneda = "MXN", CentroCostoId = CentroCosto,
        }), "agrega linea");
        await Ok(await a.PostAsync($"/api/v1/compras/requisiciones/{rqId}/transmitir", null), "transmite RQ");
        await Ok(await b.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/autorizaciones", new { Nivel = 1 }), "autoriza RQ");
        var ocResp = await a.PostAsJsonAsync("/api/v1/compras/ordenes/desde-requisicion", new
        {
            RequisicionId = rqId, SucursalCodigo = "MID", FolioAnio = (short)hoy.Year, ProveedorId = Proveedor,
            CondicionesPagoId = Guid.NewGuid(), UsoPrincipalId = Guid.NewGuid(), UsoCfdiClave = "G03", MetodoPago = "PPD", FormaPagoClave = "99", FechaDocumento = hoy,
        });
        await Ok(ocResp, "crea OC");
        var ocId = (await J(ocResp)).GetProperty("ordenCompraId").GetGuid();
        await AdjuntarCotizacionAsync(app, ocId);
        await Ok(await a.PostAsync($"/api/v1/compras/ordenes/{ocId}/transmitir", null), "transmite OC");
        await Ok(await b.PostAsJsonAsync($"/api/v1/compras/ordenes/{ocId}/autorizaciones", new { Nivel = 1 }), "autoriza OC");
        var linea = (await J(await b.GetAsync($"/api/v1/compras/ordenes/{ocId}"))).GetProperty("lineas")[0];
        var lineaOcId = linea.GetProperty("id").GetGuid();
        var subtotal = linea.GetProperty("subtotalLinea").GetDecimal();
        var costoUnitario = subtotal / 20m;

        // ---------- recepción 20/20 ----------
        var uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var bin = await SembrarBinAsync(app, articulo);
        var recResp = await b.PostAsJsonAsync("/api/v1/almacen/recepciones/", new
        {
            OrdenCompraId = ocId, FechaMovimiento = hoy, CfdiUuidFiscal = uuid, Observaciones = "DEVOL post factura",
            Lineas = new[] { new { ArticuloId = articulo, LineaOcId = lineaOcId, Cantidad = 20m, UbicacionId = bin } },
        });
        await Ok(recResp, "recibe 20/20");
        var recepcionId = (await J(recResp)).GetProperty("recepcionId").GetGuid();
        await BombearAsync(app, ocId.ToString(), lineaOcId.ToString());

        // ---------- factura 20/20 capturada contra la recepción ----------
        // El XML de CompraAPagoDemoTests trae 10 piezas fijas: se ajusta a las 20 de esta prueba.
        var iva = Math.Round(subtotal * 0.16m, 2);
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(Cfdi(uuid, ProveedorRfc, subtotal, iva, hoy).Replace("Cantidad=\"10\"", "Cantidad=\"20\"").Replace($"ValorUnitario=\"{subtotal / 10:0.00}\"", $"ValorUnitario=\"{costoUnitario:0.00}\""))) { Headers = { ContentType = new("text/xml") } }, "xml", "f.xml" } };
        var cfdiResp = await b.PostAsync("/api/v1/cuentas-por-pagar/cfdis/cargar", form);
        await Ok(cfdiResp, "carga CFDI");
        var fecha = new DateTimeOffset(hoy.Year, hoy.Month, hoy.Day, 12, 0, 0, TimeSpan.Zero);
        var facResp = await b.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas", new
        {
            OrdenCompraId = ocId, ProveedorId = Proveedor, SucursalId = TestComprasFixtures.SucursalMid, CfdiRecibidoId = (await J(cfdiResp)).GetProperty("id").GetGuid(), UuidCfdi = uuid,
            FolioProveedor = "F-DEVOL", FechaDocumento = fecha, FechaContabilizacion = fecha, FechaVencimiento = hoy.AddDays(30), Moneda = "MXN",
            Subtotal = subtotal, Descuentos = 0m, ImpuestosTrasladados = iva, Retenciones = 0m, Total = subtotal + iva,
            Lineas = new[] { new { ArticuloId = (Guid?)articulo, Descripcion = "DEVOL", Cantidad = 20m, ClaveUnidad = "H87", PrecioUnitario = costoUnitario, Importe = subtotal, LineaOcId = (Guid?)lineaOcId } },
        });
        await Ok(facResp, "captura factura 20/20");
        var facJson = (await J(facResp)).ToString();
        await BombearAsync(app, ocId.ToString(), lineaOcId.ToString());
        var antes = await LeerOcAsync(app, ocId);
        Assert.Equal(20m, antes.Lineas.Single().CantidadRecibida);
        Assert.True(antes.Lineas.Single().CantidadFacturada == 20m, "factura: " + facJson);
        Assert.Equal(SubEstadoRecepcion.Completa, antes.SubEstadoRecepcion);
        var facturaId = await LeerFacturaAsync(app, uuid, f => f.Id);
        return new Escenario(b, articulo, ocId, lineaOcId, bin, recepcionId, facturaId, uuid, subtotal, costoUnitario);
    }

    [Fact]
    public async Task Devolver_lo_facturado_sale_reabre_la_OC_aplica_la_nota_de_cargo_y_la_NC_03_la_sustituye()
    {
        await using var app = NuevaApp();
        var hoy = DateOnly.FromDateTime(DateTime.Now);
        await using var periodo = await AbrirPeriodoSiHaceFaltaAsync(app, hoy);
        using var esc = await SembrarOcFacturadaAsync(app, hoy);
        var (b, articulo, ocId, lineaOcId, bin, recepcionId, facturaId, uuid, _, costoUnitario) = esc;

        // ---------- devolución de 2: iniciar, evidencia, solicitar, autorizar ----------
        Guid lineaRecepcionId, subAlmacenId;
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            lineaRecepcionId = await db.LineasMovimiento.AsNoTracking().Where(l => l.MovimientoId == recepcionId).Select(l => l.Id).SingleAsync();
            subAlmacenId = await db.Ubicaciones.AsNoTracking().Where(u => u.Id == bin).Select(u => u.SubAlmacenId).SingleAsync();
        }
        var devResp = await b.PostAsJsonAsync("/api/v1/almacen/devoluciones-proveedor", new
        {
            ProveedorId = Proveedor, Motivo = "Piezas defectuosas", RecepcionOrigenId = recepcionId,
            Lineas = new[] { new { ArticuloId = articulo, Cantidad = 2m, UnidadMedida = "PZA", CostoUnitarioMxn = 0m, LineaRecepcionOrigenId = lineaRecepcionId } },
        });
        await Ok(devResp, "inicia devolución");
        var devId = (await J(devResp)).GetProperty("devolucionId").GetGuid();
        var baseDev = $"/api/v1/almacen/devoluciones-proveedor/{devId}";
        await Ok(await b.PostAsJsonAsync($"{baseDev}/evidencias", new { TipoEvidencia = "FOTO", NombreArchivo = "dano.jpg", BlobRef = "blob://dano.jpg", Comentario = (string?)null }), "evidencia");
        await Ok(await b.PostAsync($"{baseDev}/solicitar-autorizacion", null), "solicita autorización");
        await Ok(await b.PostAsync($"{baseDev}/autorizar", null), "autoriza devolución");

        Guid lineaDevId;
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            lineaDevId = await scope.ServiceProvider.GetRequiredService<AlmacenDbContext>()
                .Set<Millet.Almacen.Domain.DevolucionesProveedor.LineaDevolucionProveedor>().AsNoTracking()
                .Where(l => l.DevolucionId == devId).Select(l => l.Id).SingleAsync();
        }

        // ---------- 1. la salida física NO choca con DEVOLUCION_EXCEDE_NO_FACTURADO ----------
        var salida = await b.PostAsJsonAsync($"{baseDev}/registrar-salida", new
        {
            SubAlmacenId = subAlmacenId, FechaMovimiento = hoy, Bins = new[] { new { LineaDevolucionId = lineaDevId, UbicacionId = bin } },
        });
        await Ok(salida, "registra salida física (no debe ser DEVOLUCION_EXCEDE_NO_FACTURADO)");

        // Entrega en proceso: Almacén -> Compras (OcDevolucionRegistradaEvent) y Almacén -> CxP (nota de cargo).
        await BombearAsync(app, ocId.ToString(), lineaOcId.ToString(), devId.ToString());

        // ---------- 2. Compras: recibido baja a 18, facturado se queda en 20, recepción reabierta ----------
        var oc = await LeerOcAsync(app, ocId);
        Assert.Equal(18m, oc.Lineas.Single().CantidadRecibida);
        Assert.Equal(20m, oc.Lineas.Single().CantidadFacturada);
        Assert.Equal(SubEstadoRecepcion.Parcial, oc.SubEstadoRecepcion);

        // ---------- 3. CxP: la nota de cargo nace aplicada (sin IVA) y retiene el IVA esperado (Uziel 30-oct) ----------
        var nota = await LeerNotaCargoAsync(app, devId);
        Assert.Equal(Millet.CuentasPorPagar.Domain.NotaCargo.EstadoNotaCargo.Aplicada, nota.Estado);
        Assert.Equal(2m * costoUnitario, nota.Monto);
        var (totalFactura, saldo, elegible) = await LeerPagableAsync(b, facturaId);
        Assert.Equal(totalFactura - nota.Monto, saldo);
        // G1.4: lo pagable descuenta la devolución una sola vez y retiene su IVA: 18/20 del total.
        Assert.Equal(Math.Round(totalFactura * 18m / 20m, 2), elegible);

        // ---------- 5. NC fiscal 03 (Subtotal = monto de la nota, IVA 16%): la captura formaliza sola (IntentarAsync) ----------
        var ncUuid = Guid.NewGuid().ToString().ToUpperInvariant();
        var ncIva = Math.Round(nota.Monto * 0.16m, 2);
        var ncResp = await b.PostAsJsonAsync("/api/v1/cuentas-por-pagar/notas-credito", new
        {
            UuidCfdi = ncUuid, ProveedorId = Proveedor, FolioProveedor = "NC-DEVOL", FechaCfdi = DateTimeOffset.UtcNow, Moneda = "MXN",
            Subtotal = nota.Monto, ImpuestosTrasladados = ncIva, Retenciones = 0m, Total = nota.Monto + ncIva,
            Tipo = 2, TipoRelacionCfdi = 3, UuidRelacionCfdi = uuid, // TipoNotaCredito.Devolucion, relación 03
        });
        await Ok(ncResp, "captura NC 03");
        Assert.Equal(Millet.CuentasPorPagar.Domain.NotaCargo.EstadoNotaCargo.Formalizada, (await LeerNotaCargoAsync(app, devId)).Estado);
        // La NC sustituye a la nota de cargo (no se suman): saldo = total − NC y se libera la retención.
        var (_, saldoNc, elegibleNc) = await LeerPagableAsync(b, facturaId);
        Assert.Equal(totalFactura - (nota.Monto + ncIva), saldoNc);
        Assert.Equal(saldoNc, elegibleNc);

        // CxP -> Almacén (concilia la devolución exacta) -> Compras (acredita la línea).
        await BombearAsync(app, ocId.ToString(), lineaOcId.ToString(), devId.ToString());

        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var dev = await scope.ServiceProvider.GetRequiredService<AlmacenDbContext>().DevolucionesProveedor.AsNoTracking().SingleAsync(d => d.Id == devId);
            Assert.Equal(Millet.Almacen.Domain.DevolucionesProveedor.EstadoDevolucionProveedor.ConciliadaConNcFiscal, dev.Estado);
        }
        var final = await LeerOcAsync(app, ocId);
        Assert.Equal(2m, final.Lineas.Single().CantidadAcreditada);
        Assert.Equal(SubEstadoRecepcion.Completa, final.SubEstadoRecepcion);
        Assert.Equal(SubEstadoFacturacion.Completa, final.SubEstadoFacturacion);
        // Fuera de alcance: pago y cierre de la OC (requieren Tesorería); la prueba unitaria de Compras cubre el cierre con objetivo = pedido - acreditado.
    }

    [Fact]
    public async Task Cancelar_la_factura_baja_lo_facturado_en_Compras_al_acumulado_vigente()
    {
        await using var app = NuevaApp();
        var hoy = DateOnly.FromDateTime(DateTime.Now);
        await using var periodo = await AbrirPeriodoSiHaceFaltaAsync(app, hoy);
        using var esc = await SembrarOcFacturadaAsync(app, hoy);

        var version = await LeerFacturaAsync(app, esc.Uuid, f => f.Version);
        var resp = await PostConVersionAsync(esc.B, $"/api/v1/cuentas-por-pagar/facturas/{esc.FacturaId}/cancelar", version, new { Motivo = 3, Texto = (string?)null }); // ErrorCaptura
        await Ok(resp, "cancela factura");
        await BombearAsync(app, esc.OcId.ToString(), esc.LineaOcId.ToString());

        // Es la única factura de la línea: el acumulado vigente que manda CxP es 0. Con dos facturas quedaría la restante.
        var oc = await LeerOcAsync(app, esc.OcId);
        Assert.Equal(0m, oc.Lineas.Single().CantidadFacturada);
        Assert.Equal(20m, oc.Lineas.Single().CantidadRecibida);
        Assert.Equal(SubEstadoFacturacion.SinFactura, oc.SubEstadoFacturacion);
    }

    private static Task<HttpResponseMessage> PostConVersionAsync(HttpClient c, string url, int version, object? body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = body is null ? null : JsonContent.Create(body) };
        req.Headers.Add("X-Expected-Version", version.ToString());
        return c.SendAsync(req);
    }

    private static async Task<Millet.CuentasPorPagar.Domain.NotaCargo.NotaCargo> LeerNotaCargoAsync(WebApplicationFactory<Program> app, Guid devId)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().NotasCargo.AsNoTracking()
            .SingleAsync(n => n.DevolucionAProveedorId == devId);
    }

    private static async Task<T> LeerFacturaAsync<T>(WebApplicationFactory<Program> app, string uuid, System.Linq.Expressions.Expression<Func<Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor, T>> campo)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().FacturasProveedor.AsNoTracking()
            .Where(f => f.UuidCfdi == uuid).Select(campo).SingleAsync();
    }

    private static async Task<OrdenCompra> LeerOcAsync(WebApplicationFactory<Program> app, Guid ocId)
    {
        using var scope = app.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().OrdenesCompra
            .AsNoTracking().Include(o => o.Lineas).FirstAsync(o => o.Id == ocId);
    }

    /// <summary>
    /// Igual que la bomba de <see cref="CompraAPagoDemoTests"/> pero solo entrega las entradas del outbox cuyo
    /// payload menciona alguno de <paramref name="claves"/> (la base del gate guarda eventos de otras pruebas).
    /// Una falla de listener se relanza: si un salto de la triada se rompe, la prueba lo dice.
    /// </summary>
    private static async Task<(decimal Total, decimal Saldo, decimal Elegible)> LeerPagableAsync(HttpClient b, Guid facturaId)
    {
        var r = await b.GetAsync($"/api/v1/cuentas-por-pagar/facturas/{facturaId}");
        await Ok(r, "lee factura");
        var j = await J(r);
        return (j.GetProperty("total").GetDecimal(), j.GetProperty("saldoPendiente").GetDecimal(), j.GetProperty("elegible").GetDecimal());
    }

    private async Task BombearAsync(WebApplicationFactory<Program> app, params string[] claves)
    {
        var fuentes = new (Type Ctx, Type[] Workers)[]
        {
            (typeof(ComprasDbContext), [typeof(Millet.CuentasPorPagar.Infrastructure.Workers.ComprasEventListenerWorker)]),
            (typeof(AlmacenDbContext), [typeof(Millet.Compras.Infrastructure.Workers.AlmacenEventListenerWorker), typeof(Millet.CuentasPorPagar.Infrastructure.Workers.AlmacenEventListenerWorker)]),
            (typeof(CuentasPorPagarDbContext), [typeof(Millet.Compras.Infrastructure.Workers.CxpEventListenerWorker), typeof(Millet.Almacen.Infrastructure.Workers.CxpEventListenerWorker)]),
        };
        var sb = new ServiceBusClient("Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=dGVzdA==");
        for (var ronda = 0; ronda < 6; ronda++)
        {
            var movido = false;
            foreach (var (ctx, workers) in fuentes)
            {
                List<IntegrationEventOutboxEntry> entradas;
                using (var scope = app.Services.CreateScope())
                {
                    using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                    var db = (DbContext)scope.ServiceProvider.GetRequiredService(ctx);
                    entradas = (await db.Set<IntegrationEventOutboxEntry>().AsNoTracking().ToListAsync()).OrderBy(e => e.OccurredAt).ToList();
                }
                foreach (var e in entradas.Where(e => claves.Any(k => e.Payload.Contains(k, StringComparison.OrdinalIgnoreCase)) && _entregados.Add(e.Id)))
                {
                    movido = true;
                    foreach (var w in workers)
                    {
                        var log = Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(w))!;
                        var worker = Activator.CreateInstance(w, sb, app.Services.GetRequiredService<IServiceScopeFactory>(), log)!;
                        await (Task)w.GetMethod("ProcessAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                            .Invoke(worker, [e.EventType, e.Id, e.Payload, null, CancellationToken.None])!;
                    }
                }
            }
            if (!movido) break;
        }
    }
}
