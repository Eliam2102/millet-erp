using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Consumo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Infrastructure.ConsumoComponentes;
using Millet.Integraciones.Aw.Infrastructure.PiezasPt;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// A4.8: componentes descontados por composición. El origen A+W es un lector simulado (nunca SER-DATA); corre el
/// worker real contra la BD de prueba. Cada prueba siembra sus propios artículos A+W (clave única) y etiquetas.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class ConsumoComponentesTests(WebApplicationFactory<Program> factory)
{
    private const string Bandeja = "/api/v1/almacen/mensajes-app/revision";
    private const string Normas = "/api/v1/almacen/normas-consumo";
    private const decimal Ancho = 875.51m, Alto = 1041.65m;

    private sealed class LectorSimulado(List<FilaConsumoOrigen> filas) : IConsumoComponentesOrigenReader
    {
        public Task<string> OrigenActualAsync(CancellationToken ct) => Task.FromResult("Demo");
        public Task<IReadOnlyList<FilaConsumoOrigen>> LeerAsync(DateTime desde, (DateTime Tiempo, string Etiqueta)? desdeEscaneo, int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FilaConsumoOrigen>>(filas);
    }

    private static string Etiqueta() => Random.Shared.NextInt64(100_000_000, 999_999_999).ToString();

    private static FilaConsumoOrigen Ensamblado(string etiqueta, string? gas, string? separador, string sucursal = "MID") =>
        new(etiqueta, DateTime.UtcNow, "A48-" + etiqueta, 1, 999970, gas is not null, gas, Ancho, Alto, separador,
            separador is null ? 0 : 1, Ancho, Alto, sucursal);

    private async Task<Guid> EmpresaServicioAsync()
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IUsuarioServicioReadPort>().ObtenerReordenAsync(CancellationToken.None))!.EmpresaId;
    }

    /// <summary>Corre un ciclo del worker con el lector simulado.</summary>
    private async Task CicloAsync(params FilaConsumoOrigen[] filas)
    {
        var empresa = await EmpresaServicioAsync();
        await using var f = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddScoped<IConsumoComponentesOrigenReader>(_ => new LectorSimulado([.. filas]))));
        var worker = new ConsumoComponentesWorker(f.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PiezasPtOptions { ConsumoComponentesHabilitado = true, EmpresaId = empresa }),
            NullLogger<ConsumoComponentesWorker>.Instance);
        await worker.CorrerUnaVezAsync(CancellationToken.None);
    }

    /// <summary>Artículo componente ligado a un artículo A+W nuevo, con <paramref name="stock"/> en MID (a 100 MXN).</summary>
    private async Task<(Guid ArticuloId, string Ref)> SembrarAsync(string unidad, decimal stock)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var refAw = $"A48{n[..12]}";
        var prod = new ProductoAw(Guid.CreateVersion7(), refAw, "Componente A4.8", unidad);
        var art = new Articulo(Guid.CreateVersion7(), $"A48-{n[12..24]}", "Componente A4.8", unidad);
        art.AsignarProductoAw(prod.Id);
        compartido.AddRange(prod, art);
        await compartido.SaveChangesAsync();
        var mid = await compartido.Sucursales.AsNoTracking().Where(s => s.Clave == "MID").Select(s => s.Id).SingleAsync();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"C-{n[..16]}", "DEMO MP A4.8", mid);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "MP", "DEMO MP", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "MP", "DEMO MP");
        db.AddRange(alm, sub, bin, new SaldoInventario(bin.Id, sub.Id, art.Id, stock, 100m, null));
        await db.SaveChangesAsync();
        return (art.Id, refAw);
    }

    private async Task NormaAsync(Guid articuloId, decimal consumo, decimal merma = 0, decimal? factor = null)
    {
        var empresa = await EmpresaServicioAsync();
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        db.NormasConsumoComponente.Add(new NormaConsumoComponente(articuloId, consumo, merma, factor) { EmpresaId = empresa });
        await db.SaveChangesAsync();
    }

    private async Task<decimal> DisponibleAsync(Guid articuloId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<AlmacenDbContext>().SaldosInventario.AsNoTracking()
            .Where(s => s.ArticuloId == articuloId).SumAsync(s => s.CantidadDisponible);
    }

    private async Task<List<(TipoMovimiento Tipo, string Motivo, decimal Cantidad)>> MovsAsync(Guid articuloId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        return (await (from l in db.LineasMovimiento.AsNoTracking()
                       join m in db.Movimientos.AsNoTracking() on l.MovimientoId equals m.Id
                       where l.ArticuloId == articuloId
                       select new { m.Tipo, m.Motivo, l.Cantidad }).ToListAsync())
            .Select(x => (x.Tipo, x.Motivo, x.Cantidad)).ToList();
    }

    private async Task<ConsumoComponenteAw?> ConsumoAsync(string etiqueta, string componente)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<AlmacenDbContext>().ConsumosComponenteAw.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Etiqueta == etiqueta && c.ComponenteRef == componente);
    }

    [Fact]
    public async Task CA4_5_insulado_con_argon_y_separador_descuenta_ambos_por_su_norma_una_sola_vez()
    {
        var argon = await SembrarAsync("M³", 10m);
        var separador = await SembrarAsync("M LIN.", 100m);
        await NormaAsync(argon.ArticuloId, 1m, factor: 0.0127m);   // valores de prueba, no normas de Millet
        await NormaAsync(separador.ArticuloId, 1m, merma: 5m);
        var etiqueta = Etiqueta();
        var fila = Ensamblado(etiqueta, argon.Ref, separador.Ref);

        await CicloAsync(fila);

        var m3 = Math.Round(Ancho * Alto / 1_000_000m * 0.0127m, 4);
        var metros = Math.Round(2m * (Ancho + Alto) / 1000m * 1.05m, 4);
        Assert.Equal(10m - m3, await DisponibleAsync(argon.ArticuloId));
        Assert.Equal(100m - metros, await DisponibleAsync(separador.ArticuloId));
        var mov = Assert.Single(await MovsAsync(argon.ArticuloId));
        Assert.Equal(TipoMovimiento.SalidaConsumo, mov.Tipo);
        Assert.Contains(etiqueta, mov.Motivo);
        var c = await ConsumoAsync(etiqueta, argon.Ref);
        Assert.Equal(ConsumoComponenteAw.Recibido, c!.Estado);
        Assert.StartsWith("M-SAL", c.Folio);

        // D2: el mismo escaneo leído otra vez (traslape o reproceso) no vuelve a descontar.
        await CicloAsync(fila);
        Assert.Equal(10m - m3, await DisponibleAsync(argon.ArticuloId));
        Assert.Single(await MovsAsync(separador.ArticuloId));
    }

    [Fact]
    public async Task Laminado_descuenta_el_PVB_por_area_y_capas()
    {
        var pvb = await SembrarAsync("M2", 50m);
        await NormaAsync(pvb.ArticuloId, 1m);
        var etiqueta = Etiqueta();
        await CicloAsync(new FilaConsumoOrigen(etiqueta, DateTime.UtcNow, "A48-" + etiqueta, 1, 999980, false, null,
            600m, 900m, pvb.Ref, 2, 600m, 900m, "MID"));
        Assert.Equal(50m - 2 * 0.54m, await DisponibleAsync(pvb.ArticuloId));
    }

    [Fact]
    public async Task Sin_norma_queda_en_revision_sin_descontar_y_al_capturarla_se_descuenta_en_el_siguiente_ciclo()
    {
        var separador = await SembrarAsync("M LIN.", 100m);
        var etiqueta = Etiqueta();
        await CicloAsync(Ensamblado(etiqueta, null, separador.Ref));

        Assert.Equal(100m, await DisponibleAsync(separador.ArticuloId));
        Assert.Empty(await MovsAsync(separador.ArticuloId));
        var lector = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenSalidasLeerTodas);
        var editor = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenAlmacenesAdministrar);
        try
        {
            var bandeja = await ContabTestKit.Json(await lector.GetAsync(Bandeja));
            var fila = Assert.Single(bandeja.EnumerateArray(), x => x.GetProperty("mensajeId").GetString()!.Contains(etiqueta));
            Assert.Equal("CONSUMO_COMPONENTE", fila.GetProperty("tipo").GetString());
            Assert.Equal("EN_REVISION", fila.GetProperty("estado").GetString());
            Assert.Contains("Sin norma", fila.GetProperty("error").GetString());

            using var put = await editor.PutAsJsonAsync($"{Normas}/{separador.ArticuloId}",
                new { consumoPorUnidad = 1m, porcentajeMerma = 0m, factorM3PorM2 = (decimal?)null });
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);

            await CicloAsync(); // el reintento de la bandeja lo descuenta aunque A+W ya no lo vuelva a mandar
            Assert.Equal(100m - Math.Round(2m * (Ancho + Alto) / 1000m, 4), await DisponibleAsync(separador.ArticuloId));
            var despues = await ContabTestKit.Json(await lector.GetAsync(Bandeja));
            Assert.DoesNotContain(despues.EnumerateArray(), x => x.GetProperty("mensajeId").GetString()!.Contains(etiqueta));
        }
        finally
        {
            await ContabTestKit.LimpiarClienteAsync(lector);
            await ContabTestKit.LimpiarClienteAsync(editor);
        }
    }

    [Fact]
    public async Task Argon_sin_factor_sucursal_sin_mapeo_y_sin_existencia_quedan_en_revision_sin_mover_saldo()
    {
        var argon = await SembrarAsync("M³", 10m);
        await NormaAsync(argon.ArticuloId, 1m);
        var corto = await SembrarAsync("M LIN.", 0.5m);
        await NormaAsync(corto.ArticuloId, 1m);
        var sinFactor = Etiqueta(); var sinSucursal = Etiqueta(); var sinExistencia = Etiqueta();

        await CicloAsync(Ensamblado(sinFactor, argon.Ref, null), Ensamblado(sinSucursal, null, corto.Ref, sucursal: "NO-EXISTE-A48"),
            Ensamblado(sinExistencia, null, corto.Ref));

        Assert.Contains("factor", (await ConsumoAsync(sinFactor, argon.Ref))!.Error);
        Assert.Contains("No existe la sucursal", (await ConsumoAsync(sinSucursal, corto.Ref))!.Error);
        var c = await ConsumoAsync(sinExistencia, corto.Ref);
        Assert.Equal(ConsumoComponenteAw.EnRevision, c!.Estado);
        Assert.Contains("Sin existencia", c.Error);
        Assert.Equal(10m, await DisponibleAsync(argon.ArticuloId));
        Assert.Equal(0.5m, await DisponibleAsync(corto.ArticuloId));
    }

    [Fact]
    public async Task A4_8_a_pieza_sin_componentes_queda_como_excepcion_y_solo_se_descuenta_el_vidrio()
    {
        var etiqueta = Etiqueta();
        await CicloAsync(Ensamblado(etiqueta, null, null));
        var c = await ConsumoAsync(etiqueta, "");
        Assert.Equal(ConsumoComponenteAw.Excepcion, c!.Estado);
        Assert.Contains("solo se descuenta el vidrio", c.Error);
    }

    [Fact]
    public async Task Normas_GET_y_PUT_con_permisos_404_sin_articulo_y_422_con_valores_invalidos()
    {
        var pvb = await SembrarAsync("M2", 1m);
        var editor = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenAlmacenesAdministrar, PermisosCanonicos.AlmacenSalidasLeerTodas);
        var lector = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenSalidasLeerTodas);
        try
        {
            object Body(decimal consumo, decimal merma) => new { consumoPorUnidad = consumo, porcentajeMerma = merma, factorM3PorM2 = (decimal?)null };
            using (var r = await lector.PutAsJsonAsync($"{Normas}/{pvb.ArticuloId}", Body(1m, 3m)))
                Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            using (var r = await editor.PutAsJsonAsync($"{Normas}/{Guid.NewGuid()}", Body(1m, 3m)))
                Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            using (var r = await editor.PutAsJsonAsync($"{Normas}/{pvb.ArticuloId}", Body(0m, 3m)))
                Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            using (var r = await editor.PutAsJsonAsync($"{Normas}/{pvb.ArticuloId}", Body(1m, 3m)))
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            using (var r = await editor.PutAsJsonAsync($"{Normas}/{pvb.ArticuloId}", Body(1.1m, 4m)))
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);

            var lista = await ContabTestKit.Json(await lector.GetAsync(Normas));
            var n = Assert.Single(lista.EnumerateArray(), x => x.GetProperty("articuloId").GetGuid() == pvb.ArticuloId);
            Assert.Equal(1.1m, n.GetProperty("consumoPorUnidad").GetDecimal());
            Assert.Equal(4m, n.GetProperty("porcentajeMerma").GetDecimal());
        }
        finally
        {
            await ContabTestKit.LimpiarClienteAsync(editor);
            await ContabTestKit.LimpiarClienteAsync(lector);
        }
    }
}
