using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// A4.6: conciliación diaria del ERP contra el físico que reporta A+W, por /api/v1/almacen/conciliacion-diaria. Cada prueba
/// siembra su artículo y su almacén en MID con 10 piezas a 100 MXN. El saldo lo baja el trigger de PG: aquí sí se ve.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class ConciliacionDiariaTests(WebApplicationFactory<Program> factory)
{
    private const string Ruta = "/api/v1/almacen/conciliacion-diaria";

    private async Task<(Guid ArticuloId, Guid AlmacenId)> SembrarAsync()
    {
        using var admin = await ContabTestKit.LoginAsync(factory);
        await ContabTestKit.AsegurarPeriodosAbiertosAsync(admin, FechaContable.HoyLocal(DateTimeOffset.UtcNow));
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var art = new Articulo(Guid.CreateVersion7(), $"CON-{n[..12]}", "Separador de aluminio 12 mm", "PZA");
        compartido.Add(art);
        await compartido.SaveChangesAsync();
        var mid = await compartido.Sucursales.AsNoTracking().Where(s => s.Clave == "MID").Select(s => s.Id).SingleAsync();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"C-{n[..16]}", "Conciliación", mid);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "C", "C", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "C", "C");
        db.AddRange(alm, sub, bin, new SaldoInventario(bin.Id, sub.Id, art.Id, 10m, 100m, null));
        await db.SaveChangesAsync();
        return (art.Id, alm.Id);
    }

    private async Task<decimal> SaldoAsync(Guid articuloId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<AlmacenDbContext>().SaldosInventario.AsNoTracking()
            .Where(s => s.ArticuloId == articuloId).SumAsync(s => s.Cantidad);
    }

    private async Task<int> MovimientosSnpAsync(Guid articuloId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        return await (from l in db.LineasMovimiento.AsNoTracking()
                      join m in db.Movimientos.AsNoTracking() on l.MovimientoId equals m.Id
                      where l.ArticuloId == articuloId && m.Tipo == TipoMovimiento.SalidaNoPlanificada
                      select l.Id).CountAsync();
    }

    private static object Cuerpo(Guid almacenId, Guid articuloId, decimal fisico) =>
        new { almacenId, fuente = "AW", lineas = new[] { new { articuloId, fisico } } };

    [Fact]
    public async Task CA4_6_una_pieza_de_diferencia_genera_salida_no_planificada_que_se_aclara_sin_descontar_otra_vez()
    {
        var s = await SembrarAsync();
        var gestor = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenInventariosCrear,
            PermisosCanonicos.AlmacenInventariosLeer, PermisosCanonicos.AlmacenInventariosGestionarTodasSucursales,
            PermisosCanonicos.AlmacenInventariosLeerTodasSucursales);
        try
        {
            using var r = await gestor.PostAsJsonAsync(Ruta, Cuerpo(s.AlmacenId, s.ArticuloId, 9m));
            Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
            var corrida = await ContabTestKit.Json(r);
            Assert.Equal(1, corrida.GetProperty("diferencias").GetInt32());
            var d = corrida.GetProperty("detalle")[0];
            Assert.Equal("FALTANTE", d.GetProperty("sentido").GetString());
            Assert.Equal("PENDIENTE", d.GetProperty("estado").GetString());
            Assert.Equal(-1m, d.GetProperty("diferencia").GetDecimal());
            Assert.StartsWith("M-SNP", d.GetProperty("folioMovimiento").GetString());
            Assert.Equal(9m, await SaldoAsync(s.ArticuloId));
            Assert.Equal(1, await MovimientosSnpAsync(s.ArticuloId));

            // Bandeja de la mesa de corte.
            var bandeja = await ContabTestKit.Json(await gestor.GetAsync($"{Ruta}/diferencias?estado=PENDIENTE"));
            Assert.Contains(bandeja.EnumerateArray(), x => x.GetProperty("id").GetGuid() == d.GetProperty("id").GetGuid());

            // Repetir la corrida con el mismo físico: el saldo ya cuadra, no hay segunda salida.
            using var otra = await gestor.PostAsJsonAsync(Ruta, Cuerpo(s.AlmacenId, s.ArticuloId, 9m));
            Assert.Equal(0, (await ContabTestKit.Json(otra)).GetProperty("diferencias").GetInt32());
            Assert.Equal(1, await MovimientosSnpAsync(s.ArticuloId));

            // Aclarar: queda el motivo, el saldo y los movimientos no cambian; aclarar dos veces se rechaza.
            var id = d.GetProperty("id").GetGuid();
            using (var vacio = await gestor.PostAsJsonAsync($"{Ruta}/diferencias/{id}/aclarar", new { motivo = " " }))
                Assert.Equal(HttpStatusCode.UnprocessableEntity, vacio.StatusCode);
            using (var ok = await gestor.PostAsJsonAsync($"{Ruta}/diferencias/{id}/aclarar", new { motivo = "Hoja quebrada en la mesa de corte sin reporte" }))
            {
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
                Assert.Equal("ACLARADA", (await ContabTestKit.Json(ok)).GetProperty("estado").GetString());
            }
            using (var dos = await gestor.PostAsJsonAsync($"{Ruta}/diferencias/{id}/aclarar", new { motivo = "otra vez" }))
                Assert.Equal(HttpStatusCode.UnprocessableEntity, dos.StatusCode);
            Assert.Equal(9m, await SaldoAsync(s.ArticuloId));
            Assert.Equal(1, await MovimientosSnpAsync(s.ArticuloId));

            var historico = await ContabTestKit.Json(await gestor.GetAsync($"{Ruta}/diferencias"));
            Assert.Contains(historico.EnumerateArray(), x => x.GetProperty("id").GetGuid() == id && x.GetProperty("estado").GetString() == "ACLARADA");
        }
        finally { await ContabTestKit.LimpiarClienteAsync(gestor); }
    }

    [Fact]
    public async Task A4_6_a_dia_sin_diferencias_deja_la_corrida_sin_salidas()
    {
        var s = await SembrarAsync();
        var gestor = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenInventariosCrear,
            PermisosCanonicos.AlmacenInventariosLeer, PermisosCanonicos.AlmacenInventariosGestionarTodasSucursales,
            PermisosCanonicos.AlmacenInventariosLeerTodasSucursales);
        try
        {
            using var r = await gestor.PostAsJsonAsync(Ruta, Cuerpo(s.AlmacenId, s.ArticuloId, 10m));
            Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
            var corrida = await ContabTestKit.Json(r);
            Assert.Equal(0, corrida.GetProperty("diferencias").GetInt32());
            Assert.Equal(1, corrida.GetProperty("lineasComparadas").GetInt32());
            Assert.Equal(0, await MovimientosSnpAsync(s.ArticuloId));
            Assert.Equal(10m, await SaldoAsync(s.ArticuloId));

            var corridas = await ContabTestKit.Json(await gestor.GetAsync($"{Ruta}/corridas?almacenId={s.AlmacenId}"));
            Assert.Single(corridas.EnumerateArray());
        }
        finally { await ContabTestKit.LimpiarClienteAsync(gestor); }
    }

    [Fact]
    public async Task Sin_permiso_de_inventarios_no_se_concilia()
    {
        var s = await SembrarAsync();
        var lector = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenInventariosLeer);
        try
        {
            using var r = await lector.PostAsJsonAsync(Ruta, Cuerpo(s.AlmacenId, s.ArticuloId, 9m));
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            Assert.Equal(10m, await SaldoAsync(s.ArticuloId));
        }
        finally { await ContabTestKit.LimpiarClienteAsync(lector); }
    }
}
