using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Compras.Application.Integration.AlmCompra;
using Millet.Compras.Application.Oc.ActualizarNumeroPedimento;
using Millet.Compras.Application.Oc.Autorizar;
using Millet.Compras.Domain;
using Millet.Compras.Domain.AlmCompra;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Infrastructure;
using Millet.Integraciones.Aw.Infrastructure.AlmCompra;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;

namespace Millet.Api.IntegrationTests.Compras.Oc;

/// <summary>
/// 86e3jvu83 (C3.2): la OC que surte una RQ de la app de Almacenes deja COMPRA_RECIBIDA al autorizarse, COMPRA_MODIFICADA al
/// editar el pedimento y COMPRA_CANCELADA al cancelarse, y la bandeja se vacía contra el transporte simulado (por omisión).
/// Sin cleanup, igual que el resto de la clase: folios y claves A+W son únicos por corrida.
/// </summary>
public partial class OrdenesCompraEndpointsTests
{
    private enum ModoOc { AppRq, MpSinRqApp, NoMp }

    private sealed record OcApp(Guid OcId, string FolioOc, string? FolioRq, Guid LineaOcId, Guid RqId, Guid ProveedorId);

    /// <summary>Como el sembrado de la app, pero con clave de artículo A+W numérica (el contrato la manda como entero).</summary>
    private async Task<SembradoApp> SembrarProveedorYArticuloMpAsync()
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var claveAw = Random.Shared.Next(1_000_000, 2_000_000_000);
        var prov = new Proveedor(Guid.CreateVersion7(), $"MP-{n[..10]}", $"Proveedor MP {n[..6]}", $"MPX{n[..9]}",
            Millet.Catalogos.Domain.TipoPersonaProveedor.Moral, Millet.Catalogos.Domain.EstatusCatalogo.Activo);
        prov.AsignarClaveAw(claveAw);
        var refAw = Random.Shared.Next(100_000, 999_999).ToString() + Random.Shared.Next(100, 999);
        var prod = new ProductoAw(Guid.CreateVersion7(), refAw, "Vidrio de prueba MP", "M2");
        var art = new Articulo(Guid.CreateVersion7(), $"MP-{n[10..22]}", "Vidrio de prueba MP", "PZA");
        art.AsignarProductoAw(prod.Id);
        db.AddRange(prov, prod, art);
        await db.SaveChangesAsync();
        return new(prov.Id, claveAw, refAw);
    }

    private async Task<OcApp> SembrarOcAppAutorizadaAsync(HttpClient client, ModoOc modo = ModoOc.AppRq, bool proveedorSinClaveAw = false)
    {
        var s = await SembrarProveedorYArticuloMpAsync();
        var response = await client.PostAsJsonAsync(EndpointBase, ValidBody());
        response.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = sp.GetRequiredService<ComprasDbContext>();
        var compartido = sp.GetRequiredService<CompartidoDbContext>();
        var articulo = await (from a in compartido.Articulos
                              join p in compartido.ProductosAw on a.ProductoAwId equals p.Id
                              where p.ReferenciaExterna == s.ArticuloAw select a).SingleAsync();
        // Criterio de MP: artículo ligado a A+W. El «no MP» es el mismo artículo sin esa liga.
        if (modo == ModoOc.NoMp) articulo.LimpiarProductoAw();
        if (proveedorSinClaveAw) (await compartido.Proveedores.SingleAsync(p => p.Id == s.ProveedorId)).AsignarClaveAw(null);
        await compartido.SaveChangesAsync();
        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == id);
        db.Entry(oc).Property(o => o.CompradorTitularId).CurrentValue = Guid.NewGuid();
        db.Entry(oc).Property(o => o.ProveedorId).CurrentValue = s.ProveedorId;
        var folioRq = $"RQ-{Guid.NewGuid():N}"[..20];
        var numero = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999);
        var folio = Millet.Compras.Domain.Folio.Parse($"MID2026-{numero:D6}");
        Requisicion rq;
        LineaRequisicion lr;
        if (modo == ModoOc.AppRq)
        {
            rq = Requisicion.CrearDesdeAlmacenesApp(Guid.NewGuid(), oc.EmpresaId, folio, 2026, oc.SucursalDestinoId, Guid.NewGuid(), null,
                Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, folioRq, s.ProveedorId,
                [new LineaAppRequisicion(articulo.Id, 13, "PZA", Money.Mxn(100), 13, 3302, 6100)]);
            lr = rq.Lineas.Single();
        }
        else
        {
            // RQ manual de MP (no viene de la app).
            rq = new Requisicion(Guid.NewGuid(), oc.EmpresaId, folio, 2026, Clasificacion.MateriaPrima, oc.SucursalDestinoId,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Prioridad.Normal, DateTimeOffset.UtcNow, null);
            lr = rq.AgregarLinea(Guid.NewGuid(), articulo.Id, 13, "PZA", Money.Mxn(100), centroCostoId: CentroCostoSeedId);
            rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
            rq.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow, RequiereNivel.SoloN1);
        }
        rq.RegistrarCubrimiento([new CubrimientoLinea(lr.Id, 0, 13)], DateTimeOffset.UtcNow);
        var lo = oc.AgregarLineaDesdeRequisicion(Guid.NewGuid(), articulo.Id, 13, "PZA", 100, rq.DepartamentoId, rq.Id, lr.Id,
            centroCostoId: CentroCostoSeedId);
        rq.ComprometerEnOc(id);
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.Requisiciones.Add(rq);
        await db.SaveChangesAsync();
        // N2 por el handler real: publica OrdenCompraAutorizadaEvent antes de guardar (el encolador va en la misma transacción).
        await new AutorizarOrdenCompraHandler(db, compartido, new UsuarioP2(Guid.NewGuid()), sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<IPublisher>()).Handle(new(id, NivelAutorizacion.Nivel2, null), default);
        return new(id, oc.Folio.Valor, modo == ModoOc.AppRq ? folioRq : null, lo.Id, rq.Id, s.ProveedorId);
    }

    private async Task<Guid> MotivoCancelacionIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().MotivosRechazo
            .Where(m => m.Activo && (m.AplicaA & MotivoRechazoAplicaA.Cancelacion) != 0 && !m.PermiteTextoLibre)
            .Select(m => m.Id).FirstAsync();
    }

    private async Task<HttpResponseMessage> CancelarOcAsync(HttpClient client, Guid ocId) =>
        await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/cancelar", new { MotivoCancelacionId = await MotivoCancelacionIdAsync() });

    private async Task<(Millet.Compras.Domain.Oc.EstadoOrdenCompra Estado, Guid? RqComprometidaEn)> EstadoOcAsync(Guid ocId, Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        return ((await db.OrdenesCompra.AsNoTracking().SingleAsync(o => o.Id == ocId)).Estado,
            (await db.Requisiciones.AsNoTracking().SingleAsync(r => r.Id == rqId)).ComprometidaEnOcId);
    }

    private async Task<List<AlmCompraEnvio>> EnviosAsync(Guid ocId)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().AlmCompraEnvios.AsNoTracking()
            .Where(e => e.OrdenCompraId == ocId).OrderBy(e => e.Secuencia).ToListAsync();
    }

    private async Task<AlmCompraTick> ProcesarBandejaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AlmCompraProcesador>().ProcesarAsync(default);
    }

    [Fact]
    public async Task AlmCompra_al_autorizar_la_OC_de_la_app_sale_COMPRA_RECIBIDA_con_el_contrato_v6_y_el_simulado_la_procesa_una_vez()
    {
        var client = await CreateSuperAdminClientAsync();
        var oc = await SembrarOcAppAutorizadaAsync(client);

        var r = Assert.Single(await EnviosAsync(oc.OcId));
        Assert.Equal(("COMPRA_RECIBIDA", "PENDIENTE", oc.FolioOc, $"ERP-{oc.FolioOc}-R1"), (r.Tipo, r.Estado, r.Referencia, r.MensajeId));
        using (var doc = JsonDocument.Parse(r.Sobre))
        {
            var datos = doc.RootElement.GetProperty("datos");
            Assert.Equal(oc.FolioOc, datos.GetProperty("folio").GetString());
            Assert.Equal("MXN", datos.GetProperty("moneda").GetString());
            Assert.False(datos.TryGetProperty("tipoCambio", out _));
            var l = Assert.Single(datos.GetProperty("lineas").EnumerateArray());
            Assert.Equal((oc.FolioRq, 1, 3302, 6100, 13), (l.GetProperty("requisicion").GetString(), l.GetProperty("posicion").GetInt32(),
                l.GetProperty("ancho").GetInt32(), l.GetProperty("alto").GetInt32(), l.GetProperty("piezas").GetInt32()));
            Assert.Equal(100m, l.GetProperty("costoPieza").GetDecimal());
            Assert.False(l.TryGetProperty("costoMedioM2", out _)); // fórmula pendiente: no se manda
            Assert.False(l.TryGetProperty("articuloAlmacenAw", out _));
        }

        await ProcesarBandejaAsync();
        await ProcesarBandejaAsync(); // segundo ciclo: nada nuevo
        var procesada = Assert.Single(await EnviosAsync(oc.OcId));
        Assert.Equal("PROCESADO", procesada.Estado);
        Assert.NotNull(procesada.CompraIdAlmacenes);
        var sim = _factory.Services.GetRequiredService<SimuladoAlmCompraTransporte>();
        Assert.Single(sim.Escrituras, x => x == r.MensajeId);

        // El mismo evento de autorización llegando otra vez no duplica ni crea una MODIFICADA sin cambios.
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await scope.ServiceProvider.GetRequiredService<AlmCompraEncolador>().OcAutorizadaAsync(oc.OcId, default);
            await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().SaveChangesAsync();
        }
        Assert.Single(await EnviosAsync(oc.OcId));
    }

    [Fact]
    public async Task AlmCompra_el_pedimento_viaja_en_COMPRA_MODIFICADA_completa_solo_si_cambia()
    {
        var client = await CreateSuperAdminClientAsync();
        var oc = await SembrarOcAppAutorizadaAsync(client);
        await ProcesarBandejaAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new ActualizarNumeroPedimentoCommand(oc.OcId, "26 47 3456 6001234"));
            await mediator.Send(new ActualizarNumeroPedimentoCommand(oc.OcId, "26 47 3456 6001234")); // sin cambio
        }
        var filas = await EnviosAsync(oc.OcId);
        Assert.Equal(["COMPRA_RECIBIDA", "COMPRA_MODIFICADA"], filas.Select(f => f.Tipo));
        var m = filas[1];
        Assert.Equal($"ERP-{oc.FolioOc}-M1", m.MensajeId);
        using (var doc = JsonDocument.Parse(m.Sobre))
        {
            var datos = doc.RootElement.GetProperty("datos");
            Assert.Equal(oc.FolioOc, datos.GetProperty("folio").GetString()); // completa, no en diferencias
            Assert.Equal("26 47 3456 6001234", datos.GetProperty("lineas")[0].GetProperty("pedimento").GetString());
        }
        await ProcesarBandejaAsync();
        Assert.Equal("PROCESADO", (await EnviosAsync(oc.OcId))[1].Estado);
    }

    [Fact]
    public async Task AlmCompra_MP_sin_RQ_de_la_app_tambien_se_manda_sin_requisicion_y_una_OC_que_no_es_MP_no_genera_nada()
    {
        var client = await CreateSuperAdminClientAsync();
        var mp = await SembrarOcAppAutorizadaAsync(client, ModoOc.MpSinRqApp);
        var noMp = await SembrarOcAppAutorizadaAsync(client, ModoOc.NoMp);

        var r = Assert.Single(await EnviosAsync(mp.OcId));
        using (var doc = JsonDocument.Parse(r.Sobre))
        {
            var l = Assert.Single(doc.RootElement.GetProperty("datos").GetProperty("lineas").EnumerateArray());
            Assert.False(l.TryGetProperty("requisicion", out _));
            Assert.False(l.TryGetProperty("posicion", out _));
            Assert.False(l.TryGetProperty("ancho", out _)); // material por cantidad: sin medida
            Assert.Equal((13, 100m), (l.GetProperty("piezas").GetInt32(), l.GetProperty("costoPieza").GetDecimal()));
        }
        Assert.Empty(await EnviosAsync(noMp.OcId));

        // La OC que no es MP se cancela como siempre: de inmediato, sin esperar a Almacenes.
        var cancelar = await CancelarOcAsync(client, noMp.OcId);
        Assert.Equal(HttpStatusCode.NoContent, cancelar.StatusCode);
        var (estado, rqEn) = await EstadoOcAsync(noMp.OcId, noMp.RqId);
        Assert.Equal((Millet.Compras.Domain.Oc.EstadoOrdenCompra.Cancelada, (Guid?)null), (estado, rqEn));
        Assert.Empty(await EnviosAsync(noMp.OcId));
    }

    [Fact]
    public async Task AlmCompra_cancelar_una_OC_ya_mandada_espera_el_acuse_y_con_PROCESADO_cancela_y_libera_la_RQ()
    {
        var client = await CreateSuperAdminClientAsync();
        var oc = await SembrarOcAppAutorizadaAsync(client);
        await ProcesarBandejaAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await CancelarOcAsync(client, oc.OcId)).StatusCode);

        // En espera: ni cancelada ni RQ liberada; COMPRA_CANCELADA en la bandeja.
        var (estado, rqEn) = await EstadoOcAsync(oc.OcId, oc.RqId);
        Assert.Equal((Millet.Compras.Domain.Oc.EstadoOrdenCompra.CancelacionSolicitada, (Guid?)oc.OcId), (estado, rqEn));
        var c = (await EnviosAsync(oc.OcId))[1];
        Assert.Equal(("COMPRA_CANCELADA", "PENDIENTE", $"ERP-{oc.FolioOc}-C1"), (c.Tipo, c.Estado, c.MensajeId));
        var detalle = await DetalleOcAsync(client, oc.OcId);
        Assert.True(detalle.GetProperty("solicitudesCancelacion")[0].GetProperty("esperaAlmacenes").GetBoolean());

        // Nadie la resuelve a mano mientras espera.
        var manual = await client.PostAsJsonAsync($"{EndpointBase}/{oc.OcId}/resolver-cancelacion", new { Confirmar = true, Motivo = "x" });
        Assert.Contains("OC_CANCELACION_ESPERA_ALMACENES", await manual.Content.ReadAsStringAsync());

        await ProcesarBandejaAsync();
        Assert.Equal("PROCESADO", (await EnviosAsync(oc.OcId))[1].Estado);
        (estado, rqEn) = await EstadoOcAsync(oc.OcId, oc.RqId);
        Assert.Equal((Millet.Compras.Domain.Oc.EstadoOrdenCompra.Cancelada, (Guid?)null), (estado, rqEn));
    }

    [Fact]
    public async Task AlmCompra_si_Almacenes_rechaza_la_cancelacion_la_OC_vuelve_a_Autorizada_con_aviso_y_la_referencia_sigue()
    {
        var client = await CreateSuperAdminClientAsync();
        var oc = await SembrarOcAppAutorizadaAsync(client);
        await ProcesarBandejaAsync();
        _factory.Services.GetRequiredService<SimuladoAlmCompraTransporte>().MarcarEntradaCerrada(oc.FolioOc);

        Assert.Equal(HttpStatusCode.NoContent, (await CancelarOcAsync(client, oc.OcId)).StatusCode);
        await ProcesarBandejaAsync();

        var c = (await EnviosAsync(oc.OcId))[1];
        Assert.Equal("RECHAZADO", c.Estado);
        Assert.Contains("entrada cerrada", c.UltimoError);
        var (estado, rqEn) = await EstadoOcAsync(oc.OcId, oc.RqId);
        Assert.Equal((Millet.Compras.Domain.Oc.EstadoOrdenCompra.Autorizada, (Guid?)oc.OcId), (estado, rqEn));
        var solicitud = (await DetalleOcAsync(client, oc.OcId)).GetProperty("solicitudesCancelacion")[0];
        Assert.Contains("lo sobrante sale como salida manual", solicitud.GetProperty("respuestaAlmacenes").GetString());
        Assert.False(solicitud.GetProperty("esperaAlmacenes").GetBoolean());

        // El rechazo no detiene la referencia: un cambio de pedimento posterior sale en orden.
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new ActualizarNumeroPedimentoCommand(oc.OcId, "26 47 3456 6001234"));
        }
        await ProcesarBandejaAsync();
        var m = (await EnviosAsync(oc.OcId)).Last();
        // Salió (no quedó PENDIENTE detrás de la cancelación rechazada); el simulador la rechaza porque la entrada sigue cerrada.
        Assert.Equal(("COMPRA_MODIFICADA", "ERROR"), (m.Tipo, m.Estado));
    }

    [Fact]
    public async Task AlmCompra_error_de_armado_queda_visible_y_el_endpoint_lo_reintenta_cuando_se_corrige()
    {
        var client = await CreateSuperAdminClientAsync();
        var oc = await SembrarOcAppAutorizadaAsync(client, proveedorSinClaveAw: true);

        var local = Assert.Single(await EnviosAsync(oc.OcId));
        Assert.Equal(("ERROR", ""), (local.Estado, local.Sobre));
        Assert.Contains("ALM_COMPRA_PROVEEDOR_SIN_CLAVE_AW", local.UltimoError);

        var lista = await client.GetAsync($"/api/v1/compras/integraciones/alm-compra/envios?ordenCompraId={oc.OcId}");
        lista.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(lista);
        Assert.Contains(json.EnumerateArray(), e => e.GetProperty("estado").GetString() == "ERROR");
        Assert.DoesNotContain("sobre", await lista.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            (await compartido.Proveedores.SingleAsync(p => p.Id == oc.ProveedorId)).AsignarClaveAw(Random.Shared.Next(1_000_000, 2_000_000_000));
            await compartido.SaveChangesAsync();
        }
        var reintento = await client.PostAsync($"/api/v1/compras/integraciones/alm-compra/envios/{local.Id}/reintentar", null);
        Assert.Equal(HttpStatusCode.NoContent, reintento.StatusCode);
        await ProcesarBandejaAsync();
        Assert.Equal("PROCESADO", Assert.Single(await EnviosAsync(oc.OcId)).Estado);
    }
}
