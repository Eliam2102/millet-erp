using System.Net;
using System.Data.Common;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Api.Seed;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.ComercioExterior;
/// <summary>EntraId + JWT emitido por IJwtTokenService; PAC aislado, nunca autenticación simulada.</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class ContenedoresHttpTests
{
    private static readonly string[] ClientesDemo = ["DEMO"];
    private const string Base = "/api/v1/comercio-exterior/expedientes";
    private static readonly Guid ExpId = DemoSesionSeedHostedService.Id("DEMO-CE-EXPORTACION");
    private static Task<ExpedientesHttpTests.Escenario> Escenario(SqlCe? sql = null) => ExpedientesHttpTests.Escenario.CrearAsync(s =>
    {
        if (sql is not null)
        {
            s.ConfigureDbContext<FacturacionDbContext>((_, b) => b.AddInterceptors(sql));
            s.ConfigureDbContext<ComercioExteriorDbContext>((_, b) => b.AddInterceptors(sql));
        }
        s.RemoveAll<ICfdiTimbradoPort>(); s.AddScoped<ICfdiTimbradoPort, PacCe>();
        s.RemoveAll<IPeriodoContablePort>(); s.AddSingleton<IPeriodoContablePort>(new FakePeriodoContablePort());
        s.RemoveAll<ICatalogosSatReadPort>(); s.AddSingleton<ICatalogosSatReadPort>(new FakeCatalogosSatReadPort());
    });
    [Fact]
    public async Task Pedimento_historico_sin_aduana_bloquea_emision_hasta_recapturar()
    {
        await using var e = await Escenario(); using var c = await e.ClienteAsync(true, facturacion: true);
        using (var scope = e.Scope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>();
            var expediente = await db.Expedientes.SingleAsync(x => x.Id == ExpId);
            db.Entry(expediente).Property(x => x.AduanaPedimentoExportacion).CurrentValue = null;
            await db.SaveChangesAsync();
        }
        var r = await Detalle(c); var d = await Body(r);
        var pid = d.GetProperty("pedidos").EnumerateArray().Single(p => p.GetProperty("numeroPedido").GetString() == "DEMO-PED-EXP-P1").GetProperty("id").GetGuid();
        var bloqueo = await Emitir(e, c, pid);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bloqueo.StatusCode);
        Assert.Equal("CE_PEDIMENTO_SIN_ADUANA", (await Body(bloqueo)).GetProperty("code").GetString());
        var registro = await Enviar(c, HttpMethod.Put, $"{Base}/{ExpId}/pedimento-exportacion",
            new { numero = "DEMO-PEDIMENTO-A1", clave = "A1", fecha = "2026-10-12", aduana = "16" }, r.Headers.ETag!.Tag);
        registro.EnsureSuccessStatusCode();
        (await Emitir(e, c, pid)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Parcialidades_tres_clientes_copia_inmutable_reenvio_y_periodo()
    {
        await using var e = await Escenario(); using var c = await e.ClienteAsync(true, facturacion: true);
        var inicial = await Detalle(c);
        var registro = await Enviar(c, HttpMethod.Put, $"{Base}/{ExpId}/pedimento-exportacion",
            new { numero = "DEMO-PEDIMENTO-A1", clave = "A1", fecha = "2026-10-12", aduana = "16" }, inicial.Headers.ETag!.Tag);
        registro.EnsureSuccessStatusCode();
        Assert.Equal("16", (await Body(registro)).GetProperty("pedimento").GetProperty("aduana").GetString());
        var d = await Detalle(c); var datos = await Body(d); Assert.Equal(2, datos.GetProperty("contenedores").GetArrayLength());
        var pedidos = datos.GetProperty("pedidos").EnumerateArray().ToArray();
        var p1 = pedidos.Single(p => p.GetProperty("numeroPedido").GetString() == "DEMO-PED-EXP-P1").GetProperty("id").GetGuid();
        var p2 = pedidos.Single(p => p.GetProperty("numeroPedido").GetString() == "DEMO-PED-EXP-P2").GetProperty("id").GetGuid();
        var bloqueada = await Emitir(e, c, p2); Assert.Equal(HttpStatusCode.UnprocessableEntity, bloqueada.StatusCode);
        Assert.Equal("FAC_PEDIDO_NO_EMBARCADO", (await Body(bloqueada)).GetProperty("code").GetString());
        var antes = new Dictionary<Guid, string>();
        foreach (var p in pedidos.Where(p => p.GetProperty("id").GetGuid() != p2))
        {
            var pid = p.GetProperty("id").GetGuid();
            var comando = await Comando(e, pid); var key = Guid.NewGuid().ToString();
            var r = await Enviar(c, HttpMethod.Post, "/api/v1/facturacion/facturas/", comando, key: key); r.EnsureSuccessStatusCode();
            var f = await Body(r);
            var detalleFactura = await c.GetAsync($"/api/v1/facturacion/facturas/{f.GetProperty("id").GetGuid()}");
            detalleFactura.EnsureSuccessStatusCode();
            var copia = (await Body(detalleFactura)).GetProperty("datosEmbarqueCe");
            Assert.Equal("16", copia.GetProperty("aduana").GetString());
            Assert.Equal("Manzanillo", copia.GetProperty("aduanaNombre").GetString());
            antes[pid] = f.GetProperty("id").GetGuid().ToString();
            var repetida = await Enviar(c, HttpMethod.Post, "/api/v1/facturacion/facturas/", comando, key: key); repetida.EnsureSuccessStatusCode();
            Assert.Equal(f.GetProperty("id").GetGuid(), (await Body(repetida)).GetProperty("id").GetGuid());
        }
        Assert.Equal(3, antes.Count);
        using (var scope = e.Scope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var ids = antes.Keys.ToArray(); var fs = await db.FacturasVenta.Include(f => f.ComplementoCce).Where(f => f.PedidoFacturableId != null && ids.Contains(f.PedidoFacturableId.Value)).ToListAsync();
            Assert.Equal(3, fs.Count);
            Assert.All(fs, f => { Assert.Equal("16", f.DatosEmbarqueCe!.Aduana); Assert.Equal("Manzanillo", f.DatosEmbarqueCe.AduanaNombre); Assert.Equal("DEMO-PEDIMENTO-A1", f.DatosEmbarqueCe.NumeroPedimento); Assert.Equal("DEMO-BL-A1-1", f.DatosEmbarqueCe.Contenedores.Single().BlGuia); Assert.Equal("A1", f.ComplementoCce!.ClaveDePedimento); Assert.Equal("DDP", f.ComplementoCce.Incoterm); });
        }
        d = await Detalle(c); datos = await Body(d);
        var contenedor = datos.GetProperty("contenedores").EnumerateArray().Single(x => x.GetProperty("numero").GetString() == "DEMO-CONT-A1-1").GetProperty("id").GetGuid();
        var quitar = await Enviar(c, HttpMethod.Post, $"{Base}/{ExpId}/contenedores/{contenedor}/cancelar", new { motivo = "DEMO booking cancelado" }, d.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.Conflict, quitar.StatusCode); Assert.Equal("CE_CONTENEDOR_CON_FACTURA", (await Body(quitar)).GetProperty("code").GetString());
        var ped = await Enviar(c, HttpMethod.Put, $"{Base}/{ExpId}/pedimento-exportacion", new { numero = "DEMO-RECTIFICADO", clave = "RT", fecha = "2026-10-12", aduana = "43" }, d.Headers.ETag!.Tag); ped.EnsureSuccessStatusCode();
        using (var scope = e.Scope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            Assert.All(await scope.ServiceProvider.GetRequiredService<FacturacionDbContext>().FacturasVenta.Where(f => f.PedidoFacturableId == p1).ToListAsync(), f => { Assert.Equal("DEMO-PEDIMENTO-A1", f.DatosEmbarqueCe!.NumeroPedimento); Assert.Equal("16", f.DatosEmbarqueCe.Aduana); });
            Assert.True((await scope.ServiceProvider.GetRequiredService<ComercioExteriorDbContext>().Expedientes.SingleAsync(x => x.Id == ExpId)).RevisionPendiente);
        }
        var relacion = await c.GetAsync($"/api/v1/comercio-exterior/relacion-iva?anio=2026&mes=10&sucursalId={e.SucursalId}"); relacion.EnsureSuccessStatusCode();
        var filas = (await Body(relacion)).GetProperty("filas").EnumerateArray().Where(f => f.GetProperty("expediente").GetString() == "CE-EXP-2026-900002").ToArray();
        Assert.Equal(3, filas.Length); Assert.All(filas, f => Assert.False(string.IsNullOrWhiteSpace(f.GetProperty("uuid").GetString())));
        Assert.Empty((await Body(await c.GetAsync("/api/v1/comercio-exterior/relacion-iva?anio=2026&mes=9"))).GetProperty("filas").EnumerateArray());
    }
    [Fact]
    public async Task Asignar_cien_por_ciento_de_P1_y_embarcar_no_libera_P2()
    {
        await using var e = await Escenario(); using var c = await e.ClienteAsync(true, facturacion: true);
        var respuesta = await Detalle(c); var d = await Body(respuesta);
        var pedido = d.GetProperty("pedidos").EnumerateArray().Single(p => p.GetProperty("numeroPedido").GetString() == "DEMO-PED-EXP-P1");
        var p1 = pedido.GetProperty("id").GetGuid(); var p2 = d.GetProperty("pedidos").EnumerateArray().Single(p => p.GetProperty("numeroPedido").GetString() == "DEMO-PED-EXP-P2").GetProperty("id").GetGuid();
        var partida = d.GetProperty("partidas").EnumerateArray().Single(p => p.GetProperty("pedidoFacturableId").GetGuid() == p1).GetProperty("id").GetGuid();
        var retirar = await Enviar(c, HttpMethod.Delete, $"{Base}/{ExpId}/partidas/{partida}", etag: respuesta.Headers.ETag!.Tag); retirar.EnsureSuccessStatusCode();
        var alta = await Enviar(c, HttpMethod.Post, $"{Base}/{ExpId}/contenedores", new { numero = "DEMO-P1-HTTP", blGuia = "DEMO-BL-P1-HTTP" }, retirar.Headers.ETag!.Tag); alta.EnsureSuccessStatusCode();
        var cid = (await Body(alta)).GetProperty("contenedores").EnumerateArray().Single(x => x.GetProperty("numero").GetString() == "DEMO-P1-HTTP").GetProperty("id").GetGuid();
        var asignar = await Enviar(c, HttpMethod.Post, $"{Base}/{ExpId}/contenedores/{cid}/partidas", new { pedidoFacturableId = p1, posicion = 1, cantidad = 60 }, alta.Headers.ETag!.Tag); asignar.EnsureSuccessStatusCode();
        var exceder = await Enviar(c, HttpMethod.Post, $"{Base}/{ExpId}/contenedores/{cid}/partidas", new { pedidoFacturableId = p1, posicion = 1, cantidad = 1 }, asignar.Headers.ETag!.Tag);
        Assert.Equal("CE_CANTIDAD_EXCEDE_PEDIDO", (await Body(exceder)).GetProperty("code").GetString());
        var embarcar = await Enviar(c, HttpMethod.Post, $"{Base}/{ExpId}/contenedores/{cid}/embarcar", new { fecha = "2026-10-12" }, asignar.Headers.ETag!.Tag); embarcar.EnsureSuccessStatusCode();
        (await Emitir(e, c, p1)).EnsureSuccessStatusCode();
        var bloqueada = await Emitir(e, c, p2); Assert.Equal(HttpStatusCode.UnprocessableEntity, bloqueada.StatusCode); Assert.Equal("FAC_PEDIDO_NO_EMBARCADO", (await Body(bloqueada)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Aumento_version_bloquea_hasta_confirmar_partidas()
    {
        await using var e = await Escenario(); using var c = await e.ClienteAsync(true, facturacion: true);
        var d = await Body(await Detalle(c)); var p = d.GetProperty("pedidos").EnumerateArray().Single(p => p.GetProperty("numeroPedido").GetString() == "DEMO-PED-EXP-P1").GetProperty("id").GetGuid();
        using (var scope = e.Scope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>(); var pedido = await db.PedidosFacturables.SingleAsync(x => x.Id == p);
            pedido.RefrescarCabecera(pedido.ClienteId, pedido.ClienteNombre, pedido.CanalVentaId, pedido.ComportamientoFiscal, pedido.Moneda, null, null, "DEMO cambio en A+W", 2, "15"); await db.SaveChangesAsync();
        }
        var no = await Emitir(e, c, p); Assert.Equal(HttpStatusCode.UnprocessableEntity, no.StatusCode); Assert.Equal("CE_PARTIDA_REQUIERE_REVISION", (await Body(no)).GetProperty("code").GetString());
        var r = await Detalle(c); d = await Body(r); var partida = d.GetProperty("partidas").EnumerateArray().Single(x => x.GetProperty("pedidoFacturableId").GetGuid() == p);
        Assert.Equal(2, partida.GetProperty("estado").GetInt32());
        var confirmado = await Enviar(c, HttpMethod.Post, $"{Base}/{ExpId}/partidas/{partida.GetProperty("id").GetGuid()}/confirmar", etag: r.Headers.ETag!.Tag); confirmado.EnsureSuccessStatusCode();
        (await Emitir(e, c, p)).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Asignar_y_embarcar_exige_pedimento_y_respeta_permisos_sucursal_y_etag()
    {
        await using var e = await Escenario(); using var c = await e.ClienteAsync(true);
        var alta = await c.PostAsJsonAsync(Base, new { tipo = 2, sucursalId = e.SucursalId, referencia = "DEMO exportación CE02", moneda = "USD", responsableUsuarioId = e.Responsable, clientes = ClientesDemo }); alta.EnsureSuccessStatusCode();
        var id = (await Body(alta)).GetProperty("expediente").GetProperty("id").GetGuid();
        var crear = await Enviar(c, HttpMethod.Post, $"{Base}/{id}/contenedores", new { numero = "DEMO-C", blGuia = "DEMO-BL" }, alta.Headers.ETag!.Tag); crear.EnsureSuccessStatusCode();
        var cid = (await Body(crear)).GetProperty("contenedores")[0].GetProperty("id").GetGuid();
        var no = await Enviar(c, HttpMethod.Post, $"{Base}/{id}/contenedores/{cid}/embarcar", new { fecha = "2026-10-12" }, crear.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, no.StatusCode); Assert.Equal("CE_EMBARQUE_SIN_PEDIMENTO", (await Body(no)).GetProperty("code").GetString());
        using var ajeno = await e.ClienteAsync(true, asociar: false); using var ninguno = await e.ClienteAsync(false);
        using var facturadorAjeno = await e.ClienteAsync(true, asociar: false, facturacion: true);
        var origen = (await Body(await Detalle(c))).GetProperty("pedidos")[0].GetProperty("id").GetGuid();
        // El rol de Facturación incluye caja.leer-todas: su alcance no depende de la sucursal de CE.
        Assert.Equal(HttpStatusCode.OK, (await facturadorAjeno.GetAsync($"/api/v1/facturacion/pedidos-facturables/{origen}")).StatusCode);
        using var lectura = await e.ClienteAsync(true, soloLeer: true);
        Assert.Equal(HttpStatusCode.OK, (await lectura.GetAsync($"{Base}/{id}/contenedores")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(lectura, HttpMethod.Post, $"{Base}/{id}/contenedores", new { numero = "DEMO" }, crear.Headers.ETag!.Tag)).StatusCode);
        foreach (var usuario in new[] { ajeno, ninguno })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await usuario.GetAsync($"{Base}/{id}/contenedores")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(usuario, HttpMethod.Post, $"{Base}/{id}/contenedores", new { numero = "DEMO" }, crear.Headers.ETag!.Tag)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await usuario.GetAsync($"/api/v1/comercio-exterior/relacion-iva?anio=2026&mes=10&sucursalId={e.SucursalId}")).StatusCode);
        }
        Assert.Equal((HttpStatusCode)428, (await Enviar(c, HttpMethod.Post, $"{Base}/{id}/contenedores", new { numero = "DEMO" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Enviar(c, HttpMethod.Post, $"{Base}/{id}/contenedores", new { numero = "DEMO" }, alta.Headers.ETag!.Tag)).StatusCode);
    }
    [Fact]
    public async Task RT_exige_confirmacion_fiscal_y_no_se_puede_asignar_un_pedido_a_dos_expedientes_abiertos()
    {
        await using var e = await Escenario(); using var c = await e.ClienteAsync(true, facturacion: true);
        var rt = DemoSesionSeedHostedService.Id("DEMO-CE-EXPORTACION-RT");
        var r = await c.GetAsync($"{Base}/{rt}/contenedores"); r.EnsureSuccessStatusCode();
        var d = await Body(r); var pid = d.GetProperty("pedidos")[0].GetProperty("id").GetGuid();
        var rechazo = await Emitir(e, c, pid);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rechazo.StatusCode);
        Assert.Equal("FAC_RT_POR_CONFIRMAR", (await Body(rechazo)).GetProperty("code").GetString());
        var principal = await Body(await Detalle(c));
        var p2 = principal.GetProperty("pedidos").EnumerateArray().Single(p => p.GetProperty("numeroPedido").GetString() == "DEMO-PED-EXP-P2");
        var alta = await c.PostAsJsonAsync(Base, new { tipo = 2, sucursalId = e.SucursalId, referencia = "DEMO expediente duplicado", moneda = "USD", responsableUsuarioId = e.Responsable, clientes = ClientesDemo }); alta.EnsureSuccessStatusCode();
        var id = (await Body(alta)).GetProperty("expediente").GetProperty("id").GetGuid();
        var participante = await Enviar(c, HttpMethod.Post, $"{Base}/{id}/participantes", new { nombre = "DEMO cliente 1", tipo = 7, papel = 1, clienteId = p2.GetProperty("clienteId").GetGuid() }, alta.Headers.ETag!.Tag); participante.EnsureSuccessStatusCode();
        var cont = await Enviar(c, HttpMethod.Post, $"{Base}/{id}/contenedores", new { numero = "DEMO-DUP" }, participante.Headers.ETag!.Tag); cont.EnsureSuccessStatusCode();
        var cid = (await Body(cont)).GetProperty("contenedores")[0].GetProperty("id").GetGuid();
        var duplicado = await Enviar(c, HttpMethod.Post, $"{Base}/{id}/contenedores/{cid}/partidas", new { pedidoFacturableId = p2.GetProperty("id").GetGuid(), posicion = 1, cantidad = 40 }, cont.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode); Assert.Equal("CE_PEDIDO_CON_EXPEDIENTE", (await Body(duplicado)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Nacional_no_ligado_con_ce_registrado_no_toma_candados_ce_y_lee_lineas_una_vez()
    {
        var sql = new SqlCe();
        await using var e = await Escenario(sql); using var c = await e.ClienteAsync(true, facturacion: true, permisosCe: false);
        EmitirFacturaVentaCommand comando;
        using (var scope = e.Scope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var p = await db.PedidosFacturables.Include(p => p.Lineas).SingleAsync(p => p.NumeroPedido == "DEMO-PED-NORMAL");
            var cliente = await scope.ServiceProvider.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>().Clientes.SingleAsync(c => c.Id == p.ClienteId);
            comando = new(p.SucursalId, cliente.Rfc!, cliente.RazonSocial, cliente.RegimenFiscal!, cliente.CodigoPostalFiscal!, cliente.UsoCfdiDefault!, "MEX", "MIL010101AAA", "601", "PUE", "01", "MXN", null, 1,
                p.ComportamientoFiscal, null, null, false,
                p.Lineas.Select(l => new EmitirFacturaVentaLinea(null, l.ClaveProdServSat!, l.ProductoDescripcion, l.ClaveUnidadSat!, l.Cantidad, l.Precio, 0, "02", .16m, null, null)).ToArray(),
                PedidoFacturableId: p.Id);
        }
        sql.Comandos.Clear();
        var r = await Enviar(c, HttpMethod.Post, "/api/v1/facturacion/facturas/", comando);
        r.EnsureSuccessStatusCode(); Assert.Equal("Timbrado", (await Body(r)).GetProperty("estado").GetString());
        Assert.DoesNotContain(sql.Comandos, s => s.Contains("pg_advisory", StringComparison.Ordinal));
        Assert.Single(sql.Comandos, s => s.Contains("FROM comercio_exterior.expedientes", StringComparison.Ordinal));
        // FMT-3: la factura nacional sí lee las líneas del pedido una vez para copiar referencia, peso y área de impresión.
        Assert.Single(sql.Comandos, s => s.Contains("pedido_facturable_linea", StringComparison.Ordinal));
    }
    [Fact]
    public async Task Facturador_sin_permisos_ce_abre_detalle_de_pedido_ligado_y_emite()
    {
        await using var e = await Escenario(); using var ce = await e.ClienteAsync(true);
        var p = (await Body(await Detalle(ce))).GetProperty("pedidos").EnumerateArray().Single(p => p.GetProperty("numeroPedido").GetString() == "DEMO-PED-EXP-P1").GetProperty("id").GetGuid();
        using var c = await e.ClienteAsync(true, facturacion: true, permisosCe: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await Detalle(c)).StatusCode);
        var detalle = await c.GetAsync($"/api/v1/facturacion/pedidos-facturables/{p}"); detalle.EnsureSuccessStatusCode();
        Assert.True((await Body(detalle)).GetProperty("embarqueCe").GetProperty("ligado").GetBoolean());
        (await Emitir(e, c, p)).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Asignacion_concurrente_desde_dos_contenedores_no_excede_el_pedido()
    {
        await using var e = await Escenario(); using var c = await e.ClienteAsync(true);
        var r = await Detalle(c); var d = await Body(r);
        var p = d.GetProperty("pedidos").EnumerateArray().Single(p => p.GetProperty("numeroPedido").GetString() == "DEMO-PED-EXP-P1").GetProperty("id").GetGuid();
        var partida = d.GetProperty("partidas").EnumerateArray().Single(x => x.GetProperty("pedidoFacturableId").GetGuid() == p).GetProperty("id").GetGuid();
        var quitar = await Enviar(c, HttpMethod.Delete, $"{Base}/{ExpId}/partidas/{partida}", etag: r.Headers.ETag!.Tag); quitar.EnsureSuccessStatusCode();
        var contenedores = d.GetProperty("contenedores").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
        var respuestas = await Task.WhenAll(contenedores.Select(id => Enviar(c, HttpMethod.Post, $"{Base}/{ExpId}/contenedores/{id}/partidas",
            new { pedidoFacturableId = p, posicion = 1, cantidad = 60 }, quitar.Headers.ETag!.Tag)));
        Assert.Single(respuestas, x => x.IsSuccessStatusCode);
        Assert.All(respuestas.Where(x => !x.IsSuccessStatusCode), x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity }));
        var actual = (await Body(await Detalle(c))).GetProperty("partidas").EnumerateArray().Where(x => x.GetProperty("pedidoFacturableId").GetGuid() == p);
        Assert.Equal(60, actual.Sum(x => x.GetProperty("cantidad").GetDecimal()));
    }
    [Fact]
    public async Task Cancelar_expediente_sin_facturas_libera_pedido_y_conserva_historial_y_permite_facturar()
    {
        await using var e = await Escenario(); using var c = await e.ClienteAsync(true, facturacion: true);
        var r = await c.GetAsync($"{Base}/{ExpId}"); r.EnsureSuccessStatusCode();
        var pid = (await Body(await Detalle(c))).GetProperty("pedidos").EnumerateArray().Single(p => p.GetProperty("numeroPedido").GetString() == "DEMO-PED-EXP-P1").GetProperty("id").GetGuid();
        var cancelado = await Enviar(c, HttpMethod.Post, $"{Base}/{ExpId}/cancelar", new { motivo = "DEMO cancelación sin facturas" }, r.Headers.ETag!.Tag); cancelado.EnsureSuccessStatusCode();
        var detalle = await c.GetAsync($"/api/v1/facturacion/pedidos-facturables/{pid}"); detalle.EnsureSuccessStatusCode();
        Assert.False((await Body(detalle)).GetProperty("embarqueCe").GetProperty("ligado").GetBoolean());
        var historico = (await Body(await Detalle(c))).GetProperty("partidas").EnumerateArray().ToArray();
        Assert.NotEmpty(historico); Assert.All(historico, x => Assert.Equal(3, x.GetProperty("estado").GetInt32()));
        (await Emitir(e, c, pid)).EnsureSuccessStatusCode();
    }
    [Fact]
    public async Task Cancelar_expediente_con_factura_vigente_rechaza()
    {
        await using var e = await Escenario(); using var c = await e.ClienteAsync(true, facturacion: true);
        var pid = (await Body(await Detalle(c))).GetProperty("pedidos").EnumerateArray().Single(p => p.GetProperty("numeroPedido").GetString() == "DEMO-PED-EXP-P1").GetProperty("id").GetGuid();
        (await Emitir(e, c, pid)).EnsureSuccessStatusCode();
        var r = await c.GetAsync($"{Base}/{ExpId}"); r.EnsureSuccessStatusCode();
        var cancelado = await Enviar(c, HttpMethod.Post, $"{Base}/{ExpId}/cancelar", new { motivo = "DEMO con factura" }, r.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.Conflict, cancelado.StatusCode); Assert.Equal("CE_EXPEDIENTE_CON_FACTURAS", (await Body(cancelado)).GetProperty("code").GetString());
    }
    private sealed class SqlCe : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Comandos { get; } = new();
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Comandos.Enqueue(command.CommandText); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Comandos.Enqueue(command.CommandText); return ValueTask.FromResult(result); }
    }

    private static Task<HttpResponseMessage> Detalle(HttpClient c) => c.GetAsync($"{Base}/{ExpId}/contenedores");
    private static async Task<HttpResponseMessage> Emitir(ExpedientesHttpTests.Escenario e, HttpClient c, Guid pedido) => await Enviar(c, HttpMethod.Post, "/api/v1/facturacion/facturas/", await Comando(e, pedido));
    private static async Task<EmitirFacturaVentaCommand> Comando(ExpedientesHttpTests.Escenario e, Guid id)
    {
        using var scope = e.Scope(); using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var p = await scope.ServiceProvider.GetRequiredService<FacturacionDbContext>().PedidosFacturables.Include(p => p.Lineas).SingleAsync(p => p.Id == id);
        return new(p.SucursalId, "XEXX010101000", p.ClienteNombre, "616", "42501", "S01", "USA", "MIL010101AAA", "601", "PPD", "99", "USD", 20, 1,
            Millet.Facturacion.Domain.Facturas.ComportamientoFiscal.ExportacionConCce, null, null, false,
            p.Lineas.Select(l => new EmitirFacturaVentaLinea(null, l.ClaveProdServSat!, l.ProductoDescripcion, l.ClaveUnidadSat!, l.Cantidad, l.Precio, 0, "02", 0, null, null)).ToArray(),
            Cce: new("2", "FOB", 20, "DEMO-TAX", "USA", [new("70071999", "06", p.Lineas.Sum(l => l.Cantidad), 100, p.Total, true)], "RT", ReceptorDomicilioEstado: "DEMO", ReceptorDomicilioCodigoPostal: "00000"), PedidoFacturableId: p.Id, ClienteId: p.ClienteId);
    }
    private static async Task<JsonElement> Body(HttpResponseMessage r) { using var d = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return d.RootElement.Clone(); }
    private static Task<HttpResponseMessage> Enviar(HttpClient c, HttpMethod method, string url, object? body = null, string? etag = null, string? key = null)
    {
        var r = new HttpRequestMessage(method, url); if (body is not null) r.Content = JsonContent.Create(body);
        r.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString()); if (etag is not null) r.Headers.TryAddWithoutValidation("If-Match", etag);
        return c.SendAsync(r);
    }
    private sealed class PacCe : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) => new FakeFiscalApiClient(Guid.NewGuid().ToString()).TimbrarAsync(emision, ct);
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud solicitud, CancellationToken ct) => throw new NotSupportedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud solicitud, CancellationToken ct) => throw new NotSupportedException();
    }
}
