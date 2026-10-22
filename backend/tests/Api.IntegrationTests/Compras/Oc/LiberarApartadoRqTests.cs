using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Autorizar;
using Millet.Compras.Application.Cancelar;
using Millet.Compras.Application.CerrarManual;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Domain.Ports.OrdenCompra;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.PublicAdapters;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.SharedKernel.Domain;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// 86e3tferc: surtido completo vs parcial y liberación de apartado con motivo.
public partial class OrdenesCompraEndpointsTests
{
    private sealed class EscenarioLiberar(OrdenesCompraEndpointsTests t, Guid sucursal) : IAsyncDisposable
    {
        public Guid Art;
        public readonly List<Requisicion> Rqs = [];
        private Guid _alm, _sub, _bin;
        private bool _settingsExistia, _anteriorApartar, _anteriorOc;

        public async Task InicializarAsync(decimal stock, bool apartar = true)
        {
            using var scope = t._factory.Services.CreateScope();
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<ComprasDbContext>();
            var articulo = new Millet.DatosMaestros.Domain.Articulo(Guid.NewGuid(), $"L{Guid.NewGuid():N}"[..16], "DEMO liberar apartado", "PZA");
            Art = articulo.Id;
            var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"L-{Guid.NewGuid():N}"[..18], "DEMO L", SucursalIdFija);
            var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "L", "DEMO L", TipoSubAlmacen.Insumos);
            var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "L", "DEMO L");
            (_alm, _sub, _bin) = (alm.Id, sub.Id, bin.Id);
            var settings = await db.ComprasSettings.SingleOrDefaultAsync(s => s.EmpresaId == EmpresaInicialId);
            _settingsExistia = settings is not null;
            _anteriorApartar = settings?.ApartarExistenciaAlAutorizar ?? true;
            _anteriorOc = settings?.AutoGenerarOcAlAutorizar ?? false;
            if (settings is null) { settings = ComprasSettings.CrearDefault(EmpresaInicialId); db.Add(settings); }
            settings.EstablecerApartarExistenciaAlAutorizar(apartar);
            settings.EstablecerAutoGenerarOcAlAutorizar(false);
            await db.SaveChangesAsync();
            var compartido = sp.GetRequiredService<CompartidoDbContext>();
            compartido.Add(articulo); await compartido.SaveChangesAsync();
            var almDb = sp.GetRequiredService<AlmacenDbContext>();
            almDb.AddRange(alm, sub, bin, new SaldoInventario(bin.Id, sub.Id, Art, stock, 100));
            await almDb.SaveChangesAsync();
        }

        public async Task<Requisicion> NuevaRqAsync(decimal cantidad, bool soloCompleto, bool enviar = true, Guid? sucursalId = null)
        {
            using var scope = t._factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var numero = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999);
            var rq = new Requisicion(Guid.NewGuid(), EmpresaInicialId, Millet.Compras.Domain.Folio.Parse($"MID2026-{numero}"), 2026,
                Clasificacion.MateriaPrima, sucursalId ?? sucursal, Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), Prioridad.Normal, DateTimeOffset.UtcNow, descripcion: "DEMO liberar");
            rq.AgregarLinea(Guid.NewGuid(), Art, cantidad, "PZA", Money.Mxn(100), centroCostoId: CentroCostoSeedId);
            rq.EstablecerSoloApartarSiCompleto(soloCompleto);
            if (enviar) rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
            db.Add(rq); await db.SaveChangesAsync();
            Rqs.Add(rq);
            return rq;
        }

        public async Task AutorizarAsync(Requisicion rq)
        {
            using var scope = t._factory.Services.CreateScope();
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var handler = new AutorizarRequisicionHandler(sp.GetRequiredService<ComprasDbContext>(), sp.GetRequiredService<IMediator>(), new UsuarioP2(Guid.NewGuid()), new NivelP7(),
                sp.GetRequiredService<IConsultarStockPort>(), sp.GetRequiredService<IGenerarSolicitudCompraPort>(),
                sp.GetRequiredService<IClock>(), sp.GetRequiredService<TransaccionApartadosRq>(), sp.GetRequiredService<IConversionUnidadPort>(),
                sp.GetRequiredService<IArticuloReadPort>());
            await handler.Handle(new(rq.Id, NivelAutorizacion.Nivel1), default);
        }

        public async Task<(decimal Almacen, decimal Compra, decimal Pendiente)> LeerAsync(Requisicion rq)
        {
            using var scope = t._factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var l = (await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().Requisiciones.AsNoTracking()
                .Include(r => r.Lineas).SingleAsync(r => r.Id == rq.Id)).Lineas.Single();
            var pend = await scope.ServiceProvider.GetRequiredService<AlmacenDbContext>().ApartadosRequisicion
                .Where(a => a.RequisicionId == rq.Id).SumAsync(a => a.Pendiente);
            return (l.CantidadDeAlmacen, l.CantidadDeCompra, pend);
        }

        public async Task<int> ContarApartadosAsync(Requisicion rq)
        {
            using var scope = t._factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            return await scope.ServiceProvider.GetRequiredService<AlmacenDbContext>().ApartadosRequisicion.CountAsync(a => a.RequisicionId == rq.Id);
        }

        public async ValueTask DisposeAsync()
        {
            using var scope = t._factory.Services.CreateScope();
            var sp = scope.ServiceProvider;
            using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = sp.GetRequiredService<ComprasDbContext>();
            var almDb = sp.GetRequiredService<AlmacenDbContext>();
            var ids = Rqs.Select(r => r.Id).ToArray();
            await almDb.ApartadosRequisicion.Where(a => ids.Contains(a.RequisicionId)).ExecuteDeleteAsync();
            await db.Requisiciones.Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync();
            var restaurar = await db.ComprasSettings.SingleAsync(s => s.EmpresaId == EmpresaInicialId);
            if (_settingsExistia)
            { restaurar.EstablecerApartarExistenciaAlAutorizar(_anteriorApartar); restaurar.EstablecerAutoGenerarOcAlAutorizar(_anteriorOc); }
            else db.ComprasSettings.Remove(restaurar);
            await db.SaveChangesAsync();
            await almDb.SaldosInventario.Where(s => s.UbicacionId == _bin).ExecuteDeleteAsync();
            await almDb.Ubicaciones.Where(b => b.Id == _bin).ExecuteDeleteAsync();
            await almDb.SubAlmacenes.Where(s => s.Id == _sub).ExecuteDeleteAsync();
            await almDb.Almacenes.Where(a => a.Id == _alm).ExecuteDeleteAsync();
            await sp.GetRequiredService<CompartidoDbContext>().Articulos.Where(a => a.Id == Art).ExecuteDeleteAsync();
        }
    }

    private static string UrlLiberar(Guid rq, Guid linea) => $"/api/v1/compras/requisiciones/{rq}/lineas/{linea}/liberar-apartado";
    private static string UrlApartados(Guid rq) => $"/api/v1/compras/requisiciones/{rq}/apartados";

    private async Task<(EscenarioLiberar Esc, Requisicion Rq)> ParcialAutorizadaAsync(decimal stock = 3, decimal pedido = 5)
    {
        var esc = new EscenarioLiberar(this, SucursalIdFija);
        await esc.InicializarAsync(stock);
        var rq = await esc.NuevaRqAsync(pedido, soloCompleto: false);
        await esc.AutorizarAsync(rq);
        return (esc, rq);
    }

    [Theory]
    [InlineData(false, 3, 2, 3)]
    [InlineData(true, 0, 5, 0)]
    public async Task Liberar_SurtidoCompletoVsParcial_PidiendoCincoConTres(bool completo, decimal almacen, decimal compra, decimal pendiente)
    {
        await using var esc = new EscenarioLiberar(this, SucursalIdFija);
        await esc.InicializarAsync(3);
        var rq = await esc.NuevaRqAsync(5, completo);
        await esc.AutorizarAsync(rq);
        Assert.Equal((almacen, compra, pendiente), await esc.LeerAsync(rq));
    }

    [Fact]
    public async Task Liberar_DosDeTres_ListaPendienteUnoYOtraRqReservaLoLiberado()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var client = await CreateSuperAdminClientAsync();
        var linea = rq.Lineas.Single().Id;
        var resp = await client.PostAsJsonAsync(UrlLiberar(rq.Id, linea), new { Cantidad = 2m, Motivo = "  Ya no lo necesito  ", MandarACompra = false });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var lista = await ReadJsonAsync(await client.GetAsync(UrlApartados(rq.Id)));
        var fila = lista[0];
        Assert.Equal(linea, fila.GetProperty("lineaId").GetGuid());
        Assert.Equal(1m, fila.GetProperty("pendiente").GetDecimal());
        Assert.Equal(2m, fila.GetProperty("liberada").GetDecimal());
        Assert.Equal("Ya no lo necesito", fila.GetProperty("ultimoMotivo").GetString());

        var rq2 = await esc.NuevaRqAsync(5, soloCompleto: false);
        await esc.AutorizarAsync(rq2);
        Assert.Equal(2m, (await esc.LeerAsync(rq2)).Pendiente);
    }

    [Fact]
    public async Task Liberar_SinPermiso_403_YPendienteIntacto()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var client = await CreateClientConPermisosAsync(PermisosCanonicos.ComprasRequisicionesLeer);
        var resp = await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 1m, Motivo = "x", MandarACompra = false });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(3m, (await esc.LeerAsync(rq)).Pendiente);
    }

    [Fact]
    public async Task Liberar_MasQueElPendiente_422_ApartadoLiberacionExcede()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var client = await CreateSuperAdminClientAsync();
        var resp = await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 4m, Motivo = "x", MandarACompra = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Contains("APARTADO_LIBERACION_EXCEDE", await resp.Content.ReadAsStringAsync());
        Assert.Equal(3m, (await esc.LeerAsync(rq)).Pendiente);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Liberar_MotivoVacio_Rechaza(string motivo)
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var client = await CreateSuperAdminClientAsync();
        var resp = await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 1m, Motivo = motivo, MandarACompra = false });
        Assert.True(resp.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity, resp.StatusCode.ToString());
        Assert.Equal(3m, (await esc.LeerAsync(rq)).Pendiente);
    }

    [Fact]
    public async Task Liberar_LineaDeOtraRq_404()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var otra = await esc.NuevaRqAsync(1, soloCompleto: false, enviar: false);
        var client = await CreateSuperAdminClientAsync();
        var resp = await client.PostAsJsonAsync(UrlLiberar(rq.Id, otra.Lineas.Single().Id), new { Cantidad = 1m, Motivo = "x", MandarACompra = false });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Liberar_RqEnBorrador_422()
    {
        await using var esc = new EscenarioLiberar(this, SucursalIdFija);
        await esc.InicializarAsync(3);
        var rq = await esc.NuevaRqAsync(5, soloCompleto: false, enviar: false);
        var client = await CreateSuperAdminClientAsync();
        var resp = await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 1m, Motivo = "x", MandarACompra = false });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Contains("RQ_SIN_APARTADO_LIBERABLE", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Liberar_UsuarioDeOtraSucursalSinBypass_403()
    {
        await using var esc = new EscenarioLiberar(this, SucursalIdFija);
        await esc.InicializarAsync(3);
        var admin = await CreateSuperAdminClientAsync();
        var clave = $"LIB-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var crear = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = clave, Nombre = $"Sucursal {clave}" });
        crear.EnsureSuccessStatusCode();
        var otraSucursal = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();
        var rq = await esc.NuevaRqAsync(5, soloCompleto: false, sucursalId: otraSucursal);
        // El usuario de prueba sólo está asociado a la sucursal fija.
        var client = await CreateClientConPermisosAsync(PermisosCanonicos.ComprasRequisicionesLeer, PermisosCanonicos.ComprasRequisicionesLiberarApartado);
        var resp = await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 1m, Motivo = "x", MandarACompra = false });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(UrlApartados(rq.Id))).StatusCode);
    }

    [Fact]
    public async Task Liberar_CancelarDespuesDeLiberacionParcial_LiberaElResto()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var client = await CreateSuperAdminClientAsync();
        (await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 2m, Motivo = "x", MandarACompra = false })).EnsureSuccessStatusCode();
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var libre = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var compras = sp.GetRequiredService<ComprasDbContext>();
            var cancelar = await compras.MotivosRechazo.FirstAsync(m => m.Activo && (m.AplicaA & MotivoRechazoAplicaA.Cancelacion) != 0);
            await new CancelarRequisicionHandler(compras, sp.GetRequiredService<IMediator>(), new UsuarioP2(Guid.NewGuid()), sp.GetRequiredService<IClock>(), sp.GetRequiredService<TransaccionApartadosRq>())
                .Handle(new(rq.Id, cancelar.Id, "DEMO liberar cancelar"), default);
        }
        Assert.Equal(0m, (await esc.LeerAsync(rq)).Pendiente);
    }

    [Fact]
    public async Task Liberar_MismaIdempotencyKey_ReintentoNoLiberaDosVeces()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var client = await CreateSuperAdminClientAsync();
        var key = Guid.NewGuid().ToString("D");
        for (var n = 0; n < 2; n++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, UrlLiberar(rq.Id, rq.Lineas.Single().Id))
            { Content = JsonContent.Create(new { Cantidad = 2m, Motivo = "Reintento", MandarACompra = false }) };
            request.Headers.Add("Idempotency-Key", key);
            using var resp = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        }
        Assert.Equal(1m, (await esc.LeerAsync(rq)).Pendiente);
    }

    [Theory]
    [InlineData(true, 0, 5)]
    [InlineData(false, 3, 2)]
    public async Task Liberar_ApartarAlAutorizarApagado_NoCreaApartadosPeroElCubrimientoSigueLaBandera(bool completo, decimal almacen, decimal compra)
    {
        await using var esc = new EscenarioLiberar(this, SucursalIdFija);
        await esc.InicializarAsync(3, apartar: false);
        var rq = await esc.NuevaRqAsync(5, completo);
        await esc.AutorizarAsync(rq);
        Assert.Equal((almacen, compra, 0m), await esc.LeerAsync(rq));
        Assert.Equal(0, await esc.ContarApartadosAsync(rq));
    }

    [Fact]
    public async Task Liberar_CerrarManualDespuesDeLiberacionParcial_DejaPendienteEnCeroSinError()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var client = await CreateSuperAdminClientAsync();
        (await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 2m, Motivo = "x", MandarACompra = false })).EnsureSuccessStatusCode();
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            using var libre = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var compras = sp.GetRequiredService<ComprasDbContext>();
            var cierre = await compras.MotivosRechazo.FirstAsync(m => m.Activo && (m.AplicaA & MotivoRechazoAplicaA.CierreManual) != 0);
            await new CerrarManualRequisicionHandler(compras, sp.GetRequiredService<IMediator>(), new UsuarioP2(Guid.NewGuid()), sp.GetRequiredService<IClock>(), sp.GetRequiredService<TransaccionApartadosRq>())
                .Handle(new(rq.Id, cierre.Id, "DEMO liberar cerrar"), default);
        }
        Assert.Equal(0m, (await esc.LeerAsync(rq)).Pendiente);
    }

    [Theory]
    [InlineData(true, "Nula", 2)]
    [InlineData(false, "Parcial", 1)]
    public async Task Liberar_BandejaPendientes_CompletoReportaCincoPorComprar(bool completo, string esperado, int esperadoNumerico)
    {
        await using var esc = new EscenarioLiberar(this, SucursalIdFija);
        await esc.InicializarAsync(3);
        var rq = await esc.NuevaRqAsync(5, completo);
        var client = await CreateSuperAdminClientAsync();
        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/compras/pendientes-autorizacion?departamentoId={rq.DepartamentoId}"));
        var item = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == rq.Id);
        var disp = item.GetProperty("disponibilidad");
        Assert.True(disp.ValueKind == System.Text.Json.JsonValueKind.Number ? disp.GetInt32() == esperadoNumerico : disp.GetString() == esperado, disp.ToString());
    }

    private async Task<(decimal Almacen, decimal Compra, decimal SinCompra, EstadoRequisicion Estado)> LeerLineaAsync(Requisicion rq)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var r = await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().Requisiciones.AsNoTracking()
            .Include(x => x.Lineas).SingleAsync(x => x.Id == rq.Id);
        var l = r.Lineas.Single();
        return (l.CantidadDeAlmacen, l.CantidadDeCompra, l.CantidadSinCompra, r.Estado);
    }

    [Fact]
    public async Task Liberar_MandandoACompra_RecalculaCubrimientoYQuedaComoSaldoPorComprar()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var client = await CreateSuperAdminClientAsync();
        var resp = await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 2m, Motivo = "Se usa en otra obra", MandarACompra = true });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        Assert.Equal((1m, 4m, 0m, EstadoRequisicion.EnSurtido), await LeerLineaAsync(rq));
        var detalle = await ReadJsonAsync(await client.GetAsync($"/api/v1/compras/requisiciones/{rq.Id}"));
        var linea = detalle.GetProperty("lineas")[0];
        Assert.Equal(4m, linea.GetProperty("cantSaldoPorComprar").GetDecimal());
        Assert.Equal(4m, linea.GetProperty("cantPendiente").GetDecimal());
        Assert.Equal(1m, (await esc.LeerAsync(rq)).Pendiente);
    }

    [Fact]
    public async Task Liberar_SinMandarACompra_NoCompraYDejaDePedirse()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var client = await CreateSuperAdminClientAsync();
        (await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 2m, Motivo = "Ya no lo necesita", MandarACompra = false })).EnsureSuccessStatusCode();

        Assert.Equal((1m, 2m, 2m, EstadoRequisicion.EnSurtido), await LeerLineaAsync(rq));
        var linea = (await ReadJsonAsync(await client.GetAsync($"/api/v1/compras/requisiciones/{rq.Id}"))).GetProperty("lineas")[0];
        Assert.Equal(2m, linea.GetProperty("cantSaldoPorComprar").GetDecimal());
        Assert.Equal(2m, linea.GetProperty("cantPendiente").GetDecimal());
    }

    [Fact]
    public async Task Liberar_TodoSinCompraEnRqSurtidaDeAlmacen_CierraLaRqYSueltaElApartado()
    {
        var (esc, rq) = await ParcialAutorizadaAsync(stock: 3, pedido: 3);
        await using var _ = esc;
        var client = await CreateSuperAdminClientAsync();
        var resp = await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 3m, Motivo = "Cancelaron el trabajo", MandarACompra = false });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        Assert.Equal((0m, 0m, 3m, EstadoRequisicion.Cerrada), await LeerLineaAsync(rq));
        Assert.Equal(0m, (await esc.LeerAsync(rq)).Pendiente);
    }

    [Fact]
    public async Task Liberar_SinIndicarSiVaACompra_400_YNadaCambia()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var client = await CreateSuperAdminClientAsync();
        var resp = await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 1m, Motivo = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("MANDAR_A_COMPRA_REQUERIDO", await resp.Content.ReadAsStringAsync());
        Assert.Equal((3m, 2m, 0m, EstadoRequisicion.EnSurtido), await LeerLineaAsync(rq));
    }

    [Fact]
    public async Task Liberar_MandandoACompraConRqYaEnOc_422_YNadaCambia()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var tracked = await db.Requisiciones.SingleAsync(r => r.Id == rq.Id);
            tracked.ComprometerEnOc(Guid.NewGuid());
            await db.SaveChangesAsync();
        }
        var client = await CreateSuperAdminClientAsync();
        var resp = await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 1m, Motivo = "x", MandarACompra = true });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Contains("RQ_COMPROMETIDA_EN_OC", await resp.Content.ReadAsStringAsync());
        Assert.Equal((3m, 2m, 0m, EstadoRequisicion.EnSurtido), await LeerLineaAsync(rq));
        Assert.Equal(3m, (await esc.LeerAsync(rq)).Pendiente);
    }

    [Fact]
    public async Task Liberar_QuedaEnElHistoricoConActorMotivoYCantidad()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var client = await CreateSuperAdminClientAsync();
        (await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 2m, Motivo = "Motivo histórico", MandarACompra = true })).EnsureSuccessStatusCode();

        var historico = await ReadJsonAsync(await client.GetAsync($"/api/v1/compras/requisiciones/{rq.Id}/historico"));
        var entrada = historico.EnumerateArray().Single(e =>
        {
            var tipo = e.GetProperty("tipo");
            return tipo.ValueKind == System.Text.Json.JsonValueKind.Number ? tipo.GetInt32() == 15 : tipo.GetString() == "ApartadoLiberado";
        });
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, entrada.GetProperty("actorId").ValueKind);
        var cambios = entrada.GetProperty("cambios").GetString()!;
        Assert.Contains("Motivo histórico", cambios);
        Assert.Contains("CantidadLiberadaApartado", cambios);
        Assert.Contains("CantidadDeCompra", cambios);
    }

    [Fact]
    public async Task Liberar_SiAlmacenRechazaDespuesDeRecalcular_NoQuedaNadaAMedias()
    {
        // Apartado global apagado: Compras cubre 3 de almacén pero Almacén no tiene
        // apartado; Almacén rechaza después de que Compras ya recalculó en memoria.
        await using var esc = new EscenarioLiberar(this, SucursalIdFija);
        await esc.InicializarAsync(3, apartar: false);
        var rq = await esc.NuevaRqAsync(5, soloCompleto: false);
        await esc.AutorizarAsync(rq);
        var client = await CreateSuperAdminClientAsync();

        var resp = await client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 1m, Motivo = "x", MandarACompra = true });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Contains("APARTADO_SIN_PENDIENTE", await resp.Content.ReadAsStringAsync());
        Assert.Equal((3m, 2m, 0m, EstadoRequisicion.EnSurtido), await LeerLineaAsync(rq));
    }

    [Fact]
    public async Task Liberar_DosLiberacionesSimultaneas_SoloUnaPasaYNoSeLiberaDeMas()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var a = await CreateSuperAdminClientAsync();
        var b = await CreateSuperAdminClientAsync();
        var linea = rq.Lineas.Single().Id;

        var respuestas = await Task.WhenAll(
            a.PostAsJsonAsync(UrlLiberar(rq.Id, linea), new { Cantidad = 2m, Motivo = "A", MandarACompra = false }),
            b.PostAsJsonAsync(UrlLiberar(rq.Id, linea), new { Cantidad = 2m, Motivo = "B", MandarACompra = false }));

        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(respuestas, r => r.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict);
        Assert.Equal(1m, (await esc.LeerAsync(rq)).Pendiente);
        Assert.Equal((1m, 2m, 2m, EstadoRequisicion.EnSurtido), await LeerLineaAsync(rq));
    }

    [Fact]
    public async Task Liberar_MientrasOtraRqSeAutoriza_NuncaSeApartaMasQueLaExistencia()
    {
        var (esc, rq) = await ParcialAutorizadaAsync();
        await using var _ = esc;
        var rq2 = await esc.NuevaRqAsync(5, soloCompleto: false);
        var client = await CreateSuperAdminClientAsync();

        var liberar = client.PostAsJsonAsync(UrlLiberar(rq.Id, rq.Lineas.Single().Id), new { Cantidad = 2m, Motivo = "x", MandarACompra = false });
        await Task.WhenAll(liberar, esc.AutorizarAsync(rq2));

        Assert.Equal(HttpStatusCode.NoContent, (await liberar).StatusCode);
        var p1 = (await esc.LeerAsync(rq)).Pendiente;
        var p2 = (await esc.LeerAsync(rq2)).Pendiente;
        Assert.Equal(1m, p1);
        Assert.True(p2 is 0m or 2m, $"rq2 apartó {p2}");
        Assert.True(p1 + p2 <= 3m);
    }
}
