using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras.Oc;

/// <summary>
/// M-446: avisos de requisición de la app (contrato v8, Jorge 9-nov). Cancelar una entrada son dos mensajes escritos juntos:
/// REQUISICION_CANCELADA (cancela la RQ en Compras, sin mover existencia) y SALIDA_MANUAL con <c>origen.entrada</c> (baja lo
/// recibido). Cada uno aplicado una sola vez: el reenvío es DUPLICADO y el saldo baja una vez. REQUISICION_REDUCIDA con cierre con
/// faltante quita lo que ya no se compra. Sucursal MID (sin llaves de almacén: comportamiento previo de la salida).
/// </summary>
public partial class OrdenesCompraEndpointsTests
{
    private async Task<JsonElement> MensajeAppAsync(string mensajeId, string tipo, string referencia, object datos)
    {
        await using var f = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Integraciones:AlmacenesApp:ApiKey"] = LlaveApp })));
        var r = new HttpRequestMessage(HttpMethod.Post, RutaMensajesApp)
        {
            Content = JsonContent.Create(new { mensajeId, tipo, version = 1, fecha = "2026-11-09T17:29:00-06:00", origen = "AlmacenRQ_MP", referencia, datos }),
        };
        r.Headers.Add("X-Mensaje-Id", mensajeId);
        r.Headers.Add("X-Millet-Almacenes-Key", LlaveApp);
        var resp = await f.CreateClient().SendAsync(r);
        var cuerpo = await resp.Content.ReadAsStringAsync();
        Assert.True(cuerpo.Contains("\"estado\""), $"HTTP {(int)resp.StatusCode}: {cuerpo}");
        return JsonDocument.Parse(cuerpo).RootElement.Clone();
    }

    /// <summary>13 piezas del artículo de la app en un almacén nuevo de MID (lo recibido por la entrada que se va a cancelar).</summary>
    private async Task<Guid> SembrarExistenciaMidAsync(string articuloAw, decimal piezas)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var cat = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var articulo = await (from a in cat.Articulos.AsNoTracking()
                              join p in cat.ProductosAw.AsNoTracking() on a.ProductoAwId equals p.Id
                              where p.ReferenciaExterna == articuloAw
                              select a.Id).SingleAsync();
        var mid = await cat.Sucursales.AsNoTracking().Where(s => s.Clave == "MID").Select(s => s.Id).SingleAsync();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var n = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"C-{n}", "DEMO cancelación entrada", mid);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "C", "DEMO C", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "C", "DEMO C");
        db.AddRange(alm, sub, bin, new SaldoInventario(bin.Id, sub.Id, articulo, piezas, 18.5m));
        await db.SaveChangesAsync();
        return articulo;
    }

    private async Task<(decimal Disponible, int Lineas)> ExistenciaAsync(Guid articulo)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        return (await db.SaldosInventario.AsNoTracking().Where(s => s.ArticuloId == articulo).SumAsync(s => s.CantidadDisponible),
            await db.LineasMovimiento.AsNoTracking().CountAsync(l => l.ArticuloId == articulo));
    }

    [Fact]
    public async Task M446_REQUISICION_CANCELADA_y_su_SALIDA_MANUAL_cancelan_la_RQ_y_bajan_lo_recibido_una_sola_vez()
    {
        var s = await SembrarProveedorYArticuloAppAsync();
        var folioExt = $"RQ-{Guid.NewGuid():N}"[..20];
        var envio = await MensajeAppAsync(Guid.NewGuid().ToString(), "REQUISICION_ENVIADA", folioExt, new
        {
            folio = folioExt, sucursal = "MID", proveedor = new { claveAw = s.ClaveAw, nombre = "3M", moneda = (string?)null },
            fechaRequerida = "2026-11-12", comentario = (string?)null,
            lineas = new[] { new { posicion = 1, articuloAw = s.ArticuloAw, articulo = "JALADERA", articuloAlmacenAw = 4032, piezas = 13 } },
        });
        Assert.Equal("RECIBIDO", envio.GetProperty("estado").GetString());
        var folioRq = envio.GetProperty("folio").GetString();
        var articulo = await SembrarExistenciaMidAsync(s.ArticuloAw, 13m);

        // 1) REQUISICION_CANCELADA: la RQ queda Cancelada; la existencia no se toca.
        var idCancelada = Guid.NewGuid().ToString();
        var cancelada = new { folio = folioExt, sucursal = "MID", folioErp = folioRq, entradaCancelada = "EN-M446", compra = "OC-M446", motivo = "Se capturó dos veces" };
        var a1 = await MensajeAppAsync(idCancelada, "REQUISICION_CANCELADA", folioExt, cancelada);
        Assert.Equal(("RECIBIDO", folioRq), (a1.GetProperty("estado").GetString(), a1.GetProperty("folio").GetString()));
        var rq = await RqPorFolioExternoAsync(folioExt);
        Assert.Equal(EstadoRequisicion.Cancelada, rq!.Estado);
        Assert.Contains("entrada EN-M446 cancelada", rq.MotivoTerminacionTexto);
        Assert.Equal((13m, 0), await ExistenciaAsync(articulo));

        // 2) SALIDA_MANUAL con origen CANCELACION_ENTRADA: baja lo recibido.
        var idSalida = Guid.NewGuid().ToString();
        var salida = new
        {
            movimiento = "MV-M446", sucursal = "MID", motivo = "Cancelación de entrada", motivoClave = "CANCELACION_ENTRADA",
            origen = new { tipo = "CANCELACION_ENTRADA", entrada = "EN-M446", compra = "OC-M446", encargoAw = (int?)null, motivo = "Se capturó dos veces" },
            moneda = "PESOSMX", piezas = 13, documentoAw = (string?)null,
            almacenes = new[]
            {
                new
                {
                    almacenGeneral = new { id = 1, nombre = "General", tipo = "GENERAL", linea = (string?)null }, piezas = 13,
                    grupos = new[] { new { articuloAw = s.ArticuloAw, ancho = (decimal?)null, alto = (decimal?)null, lote = (string?)null, piezas = 13, m2 = (decimal?)null, costoPieza = 18.5m } },
                },
            },
        };
        var a2 = await MensajeAppAsync(idSalida, "SALIDA_MANUAL", "MV-M446", salida);
        Assert.Equal("RECIBIDO", a2.GetProperty("estado").GetString());
        Assert.Equal((0m, 1), await ExistenciaAsync(articulo));

        // 3) Reenvíos: DUPLICADO con el mismo folio; ni la RQ ni el saldo cambian otra vez.
        var r1 = await MensajeAppAsync(idCancelada, "REQUISICION_CANCELADA", folioExt, cancelada);
        var r2 = await MensajeAppAsync(idSalida, "SALIDA_MANUAL", "MV-M446", salida);
        Assert.Equal(("DUPLICADO", folioRq), (r1.GetProperty("estado").GetString(), r1.GetProperty("folio").GetString()));
        Assert.Equal(("DUPLICADO", a2.GetProperty("folio").GetString()), (r2.GetProperty("estado").GetString(), r2.GetProperty("folio").GetString()));
        Assert.Equal((0m, 1), await ExistenciaAsync(articulo));
    }

    [Fact]
    public async Task M446_REQUISICION_REDUCIDA_con_cierre_con_faltante_quita_lo_que_ya_no_se_compra()
    {
        var s = await SembrarProveedorYArticuloAppAsync();
        var folioExt = $"RQ-{Guid.NewGuid():N}"[..20];
        await EnviarRqAppAsync(ClienteMensajesApp(), Guid.NewGuid().ToString(), folioExt, s.ClaveAw, s.ArticuloAw); // 13 piezas

        var a = await MensajeAppAsync(Guid.NewGuid().ToString(), "REQUISICION_REDUCIDA", folioExt, new
        {
            folio = folioExt, sucursal = "MID", folioErp = (string?)null, cierreConFaltante = true, motivo = "Ya no llega",
            lineas = new[] { new { posicion = 1, piezasQuitadas = 4 } },
        });

        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        var l = Assert.Single((await RqPorFolioExternoAsync(folioExt))!.Lineas);
        Assert.Equal((9m, 4m, 9m), (l.CantidadDeCompra, l.CantidadSinCompra, l.CantidadVigente));
    }

    [Fact]
    public async Task M446_REQUISICION_CANCELADA_de_una_RQ_que_nunca_llego_al_ERP_se_acepta_sin_folio()
    {
        var folioExt = $"RQ-{Guid.NewGuid():N}"[..20];
        var a = await MensajeAppAsync(Guid.NewGuid().ToString(), "REQUISICION_CANCELADA", folioExt,
            new { folio = folioExt, sucursal = "MID", folioErp = (string?)null, motivo = "El ERP la rechazó" });

        Assert.Equal("RECIBIDO", a.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, a.GetProperty("folio").ValueKind);
    }
}
