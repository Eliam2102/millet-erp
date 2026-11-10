using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Stubs;

using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using static Millet.Api.IntegrationTests.Compras.CompraAPagoDemoTests;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// M-159: ENTRADA_ALMACEN de la app contra una OC autorizada del ERP es la recepción de esa OC: entra al almacén general al
/// precio de la línea de la OC, deja la fila <c>almacen.oc_recepcion.registrada.v1</c> en el outbox y, entregada a Compras,
/// sube lo recibido de la línea. La misma entrada con otro mensajeId no se recibe dos veces. El <c>grupos[].lote</c> es llave del
/// saldo: dos lotes en una línea quedan en dos filas de saldo.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class AlmacenesAppEntradaOcTests(WebApplicationFactory<Program> factory)
{
    private const string Ruta = "/api/v1/integraciones/almacenes/mensajes";
    private const string Llave = "llave-test-almacenes";

    [Fact]
    public async Task M159_ENTRADA_ALMACEN_contra_OC_autorizada_recibe_la_OC_al_precio_de_la_linea_y_publica_la_recepcion()
    {
        await using var app = factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Integraciones:AlmacenesApp:ApiKey"] = Llave }));
            b.ConfigureTestServices(s => s.PostConfigure<ComprasStubsOptions>(o => o.Stubs.Stock.DefaultRatio = 0m));
        });
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        Guid articulo;
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cat = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var pza = await cat.UnidadesMedida.SingleAsync(u => u.Codigo == "PZA");
            var art = new Millet.DatosMaestros.Domain.Articulo(Guid.NewGuid(), $"EO{n}"[..16], "M-159 entrada app contra OC", "PZA", unidadMedidaId: pza.Id);
            cat.Add(art);
            await Millet.Api.IntegrationTests.DatosMaestros.Adjuntos.AdjuntosProveedorAmbiente.AgregarOpinionCumplimientoAsync(cat, Proveedor);
            await cat.SaveChangesAsync();
            articulo = art.Id;

            // Almacén general (ÚNICA) en MID, destino de la OC.
            var mid = await cat.Sucursales.AsNoTracking().Where(x => x.Clave == "MID").Select(x => x.Id).SingleAsync();
            var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"E-{n[..16]}", "DEMO entrada app OC", mid);
            var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "E", "DEMO E", TipoSubAlmacen.MaterialesDirectos);
            db.AddRange(alm, sub, new Ubicacion(Guid.NewGuid(), sub.Id, "ÚNICA", "Almacén general", esDefault: true));
            await db.SaveChangesAsync();
        }
        await using var periodo = await AbrirPeriodoSiHaceFaltaAsync(app, hoy);

        // RQ → OC autorizada de 10 piezas.
        using var a = await ClienteConPermisosAsync(app, "compras.requisiciones.crear", "compras.requisiciones.leer", "compras.requisiciones.editar",
            "centros_costo.dim3.leer-todos", "compras.ordenes.crear", "compras.ordenes.leer", "compras.ordenes.adjuntar");
        using var b = await ClienteConPermisosAsync(app, PermisosCanonicos.Todos.Select(p => p.Codigo).ToArray());
        await AsociarSucursalAsync(app, a);
        await Ok(await b.PutAsJsonAsync("/api/v1/compras/umbrales-aprobacion", new { DepartamentoId = TestComprasFixtures.DeptoCompras, UmbralMenorCuantia = 1000m, UmbralMonto = 100000m, Moneda = "MXN", ModoNivel2 = "SoloMonto" }), "umbral depto");
        var rqResp = await a.PostAsJsonAsync("/api/v1/compras/requisiciones", TestComprasFixtures.BuildCrearRqValidBody(descripcion: "M-159 entrada app"));
        await Ok(rqResp, "A crea RQ");
        var rqId = (await J(rqResp)).GetProperty("id").GetGuid();
        await Ok(await a.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/lineas", new
        {
            ArticuloId = articulo, Cantidad = 10m, UnidadMedida = "PZA", PrecioEstimadoMonto = 15m, PrecioEstimadoMoneda = "MXN", CentroCostoId = CentroCosto,
        }), "A agrega línea");
        await Ok(await a.PostAsync($"/api/v1/compras/requisiciones/{rqId}/transmitir", null), "A transmite RQ");
        await Ok(await b.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/autorizaciones", new { Nivel = 1, Notas = "M-159" }), "B autoriza RQ");
        var ocResp = await a.PostAsJsonAsync("/api/v1/compras/ordenes/desde-requisicion", new
        {
            RequisicionId = rqId, SucursalCodigo = "MID", FolioAnio = (short)hoy.Year, ProveedorId = Proveedor,
            CondicionesPagoId = Guid.NewGuid(), UsoPrincipalId = Guid.NewGuid(), UsoCfdiClave = "G03", MetodoPago = "PPD", FormaPagoClave = "99", FechaDocumento = hoy,
        });
        await Ok(ocResp, "A crea OC desde RQ");
        var ocId = JsonDocument.Parse(await ocResp.Content.ReadAsStringAsync()).RootElement.GetProperty("ordenCompraId").GetGuid();
        await AdjuntarCotizacionAsync(app, ocId);
        await Ok(await a.PostAsync($"/api/v1/compras/ordenes/{ocId}/transmitir", null), "A transmite OC");
        await Ok(await b.PostAsJsonAsync($"/api/v1/compras/ordenes/{ocId}/autorizaciones", new { Nivel = 1 }), "B autoriza OC");
        var oc = await J(await b.GetAsync($"/api/v1/compras/ordenes/{ocId}"));
        Assert.Equal((int)EstadoOrdenCompra.Autorizada, oc.GetProperty("estado").GetInt32());
        var folioOc = oc.GetProperty("folio").GetString()!;
        var lineaOcId = oc.GetProperty("lineas")[0].GetProperty("id").GetGuid();
        var precio = oc.GetProperty("lineas")[0].GetProperty("subtotalLinea").GetDecimal() / 10m;

        // Ya autorizada, el artículo queda ligado a un artículo A+W (herraje sin medida) para que la app lo nombre.
        var refAw = $"ENT{n[..12]}";
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cat = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var prod = new Millet.DatosMaestros.Domain.ProductoAw(Guid.CreateVersion7(), refAw, "Herraje entrada app", "PZA");
            cat.Add(prod);
            (await cat.Articulos.SingleAsync(x => x.Id == articulo)).AsignarProductoAw(prod.Id);
            await cat.SaveChangesAsync();
        }

        // La app da entrada a 4 de las 10 piezas, en dos lotes (3 + 1).
        var desde = DateTimeOffset.UtcNow.AddSeconds(-5);
        var cliente = app.CreateClient();
        var a1 = await Acuse(await cliente.SendAsync(Req(Guid.NewGuid().ToString(), Datos($"EN-{n[..8]}", folioOc, refAw, 4, dosLotes: true))));
        Assert.Equal("RECIBIDO", a1.GetProperty("estado").GetString());
        var folio = a1.GetProperty("folio").GetString();
        Assert.StartsWith("M-ENT", folio);

        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            // Promedio = precio de la línea de la OC, en el almacén general (sin rack).
            var saldos = await (from x in db.SaldosInventario.AsNoTracking()
                                join u in db.Ubicaciones.AsNoTracking() on x.UbicacionId equals u.Id
                                where x.ArticuloId == articulo
                                orderby x.Lote
                                select new { x.Lote, x.Cantidad, x.CostoPromedioMxn, u.EsDefault }).ToListAsync();
            Assert.Equal(
                [("L-VPS-2611-01", 3m, precio, true), ("L-VPS-2611-02", 1m, precio, true)],
                saldos.Select(x => (x.Lote, x.Cantidad, x.CostoPromedioMxn, x.EsDefault)).ToList());
            var mov = await db.Movimientos.AsNoTracking().Include(m => m.Lineas).SingleAsync(m => m.Folio == folio);
            Assert.Equal(ocId, mov.OcId);
            Assert.All(mov.Lineas, l => Assert.Equal(lineaOcId, l.LineaOcId));
            Assert.Equal(2, mov.Lineas.Count);
        }

        // Fila del outbox de Almacén, entregada al listener real de Compras: sub-estado Recepción y recibido de la línea.
        IntegrationEventOutboxEntry fila;
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            fila = (await db.Set<IntegrationEventOutboxEntry>().AsNoTracking()
                    .Where(e => e.EventType == "almacen.oc_recepcion.registrada.v1" && e.OccurredAt >= desde).ToListAsync())
                .Single(e => e.Payload.Contains(ocId.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        var sb = new ServiceBusClient("Endpoint=sb://fake.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=dGVzdA==");
        var worker = new Millet.Compras.Infrastructure.Workers.AlmacenEventListenerWorker(sb, app.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<Millet.Compras.Infrastructure.Workers.AlmacenEventListenerWorker>.Instance);
        await (Task)typeof(Millet.Compras.Infrastructure.Workers.AlmacenEventListenerWorker)
            .GetMethod("ProcessAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(worker, [fila.EventType, fila.Id, fila.Payload, null, CancellationToken.None])!;
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var compras = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var ocDb = await compras.OrdenesCompra.AsNoTracking().Include(o => o.Lineas).SingleAsync(o => o.Id == ocId);
            Assert.Equal(4m, ocDb.Lineas.Single(l => l.Id == lineaOcId).CantidadRecibida);
            Assert.Equal(SubEstadoRecepcion.Parcial, ocDb.SubEstadoRecepcion);
        }

        // La misma entrada con otro mensajeId: DUPLICADO con el mismo folio, sin segunda recepción.
        var a2 = await Acuse(await cliente.SendAsync(Req(Guid.NewGuid().ToString(), Datos($"EN-{n[..8]}", folioOc, refAw, 4))));
        Assert.Equal(("DUPLICADO", folio), (a2.GetProperty("estado").GetString(), a2.GetProperty("folio").GetString()));
        using (var scope = app.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            Assert.Equal(4m, await db.SaldosInventario.AsNoTracking().Where(x => x.ArticuloId == articulo).SumAsync(x => x.Cantidad));
        }

        // Más de lo pendiente (quedan 6): ERROR sin mover saldo.
        var a3 = await Acuse(await cliente.SendAsync(Req(Guid.NewGuid().ToString(), Datos($"EN2-{n[..8]}", folioOc, refAw, 7))));
        Assert.Equal("ERROR", a3.GetProperty("estado").GetString());
        Assert.Contains("Excede lo pendiente", a3.GetProperty("error").GetString());
    }

    private static object Datos(string entrada, string compra, string articuloAw, int piezas, bool dosLotes = false) => new
    {
        entrada, compra, sucursal = "MID", piezas,
        lineas = new[]
        {
            new
            {
                requisicion = "RQ-1", posicion = 1, articuloAw, piezas, reclamadas = 0,
                grupos = dosLotes
                    ? new[] { new { articuloAw, lote = "L-VPS-2611-01", piezas = piezas - 1 }, new { articuloAw, lote = "L-VPS-2611-02", piezas = 1 } }
                    : new[] { new { articuloAw, lote = "L-VPS-2611-01", piezas } },
            },
        },
    };

    private static HttpRequestMessage Req(string id, object datos)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, Ruta)
        {
            Content = JsonContent.Create(new
            {
                mensajeId = id, tipo = "ENTRADA_ALMACEN", version = 1, fecha = "2026-11-10T10:00:00-06:00",
                origen = "AlmacenRQ_MP", referencia = "EN", datos,
            }),
        };
        r.Headers.Add("X-Mensaje-Id", id);
        r.Headers.Add("X-Millet-Almacenes-Key", Llave);
        return r;
    }

    private static async Task<JsonElement> Acuse(HttpResponseMessage r)
    {
        var cuerpo = await r.Content.ReadAsStringAsync();
        Assert.True(cuerpo.Contains("\"estado\""), $"HTTP {(int)r.StatusCode}: {cuerpo}");
        return JsonDocument.Parse(cuerpo).RootElement.Clone();
    }
}
