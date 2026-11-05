using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Almacen.Application.Taller;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Infrastructure.PiezasPt;
using Millet.Integraciones.Aw.Infrastructure.Taller;
using Millet.Integraciones.Aw.Infrastructure.Taller.Workers;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// PT5.10: pedido interno Serie 9000 baja las hojas de su medida una sola vez (el origen A+W es un lector simulado,
/// nunca SER-DATA) y el cierre de día del almacenista calcula el factor de optimización. Cada prueba usa su propio
/// artículo, folio y fecha.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class TallerSerie9000Tests(WebApplicationFactory<Program> factory)
{
    private const string Cierres = "/api/v1/almacen/taller/cierres";
    private const decimal Area = 8.9304m; // 2440 x 3660 mm

    private sealed class LectorSimulado(List<PedidoInternoLineaAw> filas) : IPedidosInternosOrigenReader
    {
        public Task<string> OrigenActualAsync(CancellationToken ct) => Task.FromResult("Demo");
        public Task<IReadOnlyList<PedidoInternoLineaAw>> LeerAsync(DateOnly desde, int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PedidoInternoLineaAw>>(filas);
    }

    private async Task CicloAsync(params PedidoInternoLineaAw[] filas)
    {
        Guid empresa;
        using (var scope = factory.Services.CreateScope())
            empresa = (await scope.ServiceProvider.GetRequiredService<IUsuarioServicioReadPort>().ObtenerReordenAsync(CancellationToken.None))!.EmpresaId;
        await using var f = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddScoped<IPedidosInternosOrigenReader>(_ => new LectorSimulado([.. filas]))));
        var worker = new Serie9000Worker(f.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new PiezasPtOptions { Serie9000Habilitada = true, EmpresaId = empresa }),
            NullLogger<Serie9000Worker>.Instance);
        await worker.CorrerUnaVezAsync(CancellationToken.None);
    }

    /// <summary>Artículo A+W con la medida 2440x3660 y <paramref name="hojas"/> hojas (en m²) en MID; devuelve su clave y la sucursal.</summary>
    private async Task<(string Ref, Guid ArticuloId, Guid SucursalId)> SembrarAsync(decimal hojas)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var refAw = $"T9K{n[..12]}";
        var prod = new ProductoAw(Guid.CreateVersion7(), refAw, "Vidrio float claro 6 mm taller", "M2");
        prod.AplicarVariantes([new ProductoAwVarianteDato($"V-{n[..8]}", 3660m, 2440m, null, null)]);
        var art = new Articulo(Guid.CreateVersion7(), $"T9K-{n[12..24]}", "Vidrio float claro 6 mm taller", "M2");
        art.AsignarProductoAw(prod.Id);
        compartido.AddRange(prod, art);
        await compartido.SaveChangesAsync();
        var mid = await compartido.Sucursales.AsNoTracking().Where(s => s.Clave == "MID").Select(s => s.Id).SingleAsync();
        var varianteId = await compartido.ProductosAwVariantes.AsNoTracking().Where(v => v.ProductoAwId == prod.Id).Select(v => v.Id).SingleAsync();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"T-{n[..16]}", "DEMO taller", mid);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "T", "DEMO T", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "T", "DEMO T");
        db.AddRange(alm, sub, bin, new SaldoInventario(bin.Id, sub.Id, art.Id, hojas * Area, 95m, varianteId));
        await db.SaveChangesAsync();
        return (refAw, art.Id, mid);
    }

    private async Task<decimal> DisponibleAsync(Guid articuloId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<AlmacenDbContext>().SaldosInventario.AsNoTracking()
            .Where(s => s.ArticuloId == articuloId).SumAsync(s => s.CantidadDisponible);
    }

    private static string Folio() => Random.Shared.NextInt64(90_000_000, 99_999_999).ToString();

    // Fecha propia por corrida (siempre pasada: no se cierra un día futuro): el cierre es único por taller y día y la base de prueba se comparte.
    private static DateOnly FechaUnica() => new DateOnly(2025, 1, 1).AddDays(Random.Shared.Next(0, 600));

    [Fact]
    public async Task PT5_10_a_pedido_9000_baja_3_hojas_de_esa_medida_y_si_llega_dos_veces_baja_una_sola_vez()
    {
        var (refAw, articuloId, _) = await SembrarAsync(10);
        var fecha = FechaUnica();
        var pedido = new PedidoInternoLineaAw(Folio(), 1, refAw, 3, 2440m, 3660m, "MID", fecha);

        await CicloAsync(pedido);
        Assert.Equal(10 * Area - 3 * Area, await DisponibleAsync(articuloId));

        await CicloAsync(pedido); // el mismo pedido otra vez
        Assert.Equal(7 * Area, await DisponibleAsync(articuloId));
    }

    [Fact]
    public async Task CA5_11_cierre_de_dia_calcula_el_factor_con_su_formula_y_un_dia_cerrado_solo_lo_corrige_quien_administra()
    {
        var (refAw, _, sucursalId) = await SembrarAsync(10);
        var fecha = FechaUnica();
        await CicloAsync(new PedidoInternoLineaAw(Folio(), 1, refAw, 3, 2440m, 3660m, "MID", fecha),
            new PedidoInternoLineaAw(Folio(), 1, refAw, 2, 2440m, 3660m, "MID", fecha));

        var capturista = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenSalidasLeerTodas,
            PermisosCanonicos.AlmacenSalidasCapturar, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales);
        var admin = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenSalidasLeerTodas,
            PermisosCanonicos.AlmacenAlmacenesAdministrar, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales);
        try
        {
            var pre = await ContabTestKit.Json(await capturista.GetAsync($"{Cierres}/prellenado?sucursalId={sucursalId}&fecha={fecha:yyyy-MM-dd}"));
            Assert.Equal(5m, pre.GetProperty("hojasDescargadas").GetDecimal());
            Assert.Equal(5 * Area, pre.GetProperty("m2Descargados").GetDecimal());
            Assert.Equal(2, pre.GetProperty("pedidosSerie9000").GetInt32());

            var cuerpo = new { sucursalId, fecha, hojasTomadas = 5m, retazosTomados = 2m, m2Cortados = 50m, pedidosServidos = 4, cortadores = 2, notas = (string?)null };
            using var creado = await capturista.PostAsJsonAsync(Cierres, cuerpo);
            Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
            var cierre = await ContabTestKit.Json(creado);
            Assert.Equal(Math.Round(5 * Area / 50m, 4), cierre.GetProperty("factorOptimizacion").GetDecimal());
            Assert.Contains("m² descargados", cierre.GetProperty("formula").GetString());

            using (var otra = await capturista.PostAsJsonAsync(Cierres, cuerpo)) // día ya cerrado
                Assert.Equal(HttpStatusCode.UnprocessableEntity, otra.StatusCode);

            var correccion = new { hojasTomadas = 5m, retazosTomados = 3m, m2Cortados = 40m, pedidosServidos = 4, cortadores = 2, notas = "Se contó un retazo más" };
            var id = cierre.GetProperty("id").GetGuid();
            using (var sinPermiso = await capturista.PutAsJsonAsync($"{Cierres}/{id}", correccion))
                Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);
            using var corregido = await admin.PutAsJsonAsync($"{Cierres}/{id}", correccion);
            Assert.Equal(HttpStatusCode.OK, corregido.StatusCode);
            Assert.Equal(Math.Round(5 * Area / 40m, 4), (await ContabTestKit.Json(corregido)).GetProperty("factorOptimizacion").GetDecimal());
        }
        finally
        {
            await ContabTestKit.LimpiarClienteAsync(capturista);
            await ContabTestKit.LimpiarClienteAsync(admin);
        }
    }
}
