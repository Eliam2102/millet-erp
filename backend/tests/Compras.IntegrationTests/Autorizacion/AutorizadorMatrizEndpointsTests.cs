using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using static Millet.Compras.IntegrationTests.Autorizacion.AutorizacionTestSeeding;

namespace Millet.Compras.IntegrationTests.Autorizacion;

/// <summary>
/// C3.1 / F-09: con matriz configurada, el N1 lo firma solo el jefe del departamento del
/// solicitante y el N2 solo el jefe de almacén de la sucursal (también en la masiva);
/// el N2 no lo firma quien firmó N1. Sin matriz basta el permiso (fail-open).
/// Siembra en <c>DeptoCompras</c>/<c>SucursalMid</c> y limpia en <c>finally</c>.
/// </summary>
public class AutorizadorMatrizEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private readonly StubsWebApplicationFactory _factory;

    public AutorizadorMatrizEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task N1_Solo_Lo_Firma_El_JefeDpto_Y_N2_Solo_El_JefeAlmacen()
    {
        var admin = await CreateSuperAdminClientAsync(_factory);
        var (jefeDpto, jefeDptoId) = await CreateUsuarioFirmanteAsync();
        var (jefeAlmacen, jefeAlmacenId) = await CreateUsuarioFirmanteAsync();
        var otro = await CreateAutorizadorAsync(_factory);
        try
        {
            await SembrarAsync(RolAprobador.JefeDpto, TestComprasFixtures.DeptoCompras, jefeDptoId);
            await SembrarAsync(RolAprobador.JefeAlmacen, TestComprasFixtures.SucursalMid, jefeAlmacenId);
            var rq = await CrearTransmitirAsync(admin, new[] { (ArticuloSinStock, 1m) });

            await AssertNoFacultadoAsync(await AutorizarAsync(otro, rq, 1));
            await AssertNoFacultadoAsync(await AutorizarAsync(admin, rq, 1)); // sin bypass de super-admin
            Assert.True((await AutorizarAsync(jefeDpto, rq, 1)).IsSuccessStatusCode);
            Assert.Equal((int)EstadoRequisicion.EnAutorizacion, await EstadoAsync(admin, rq)); // sin stock: falta N2

            await AssertNoFacultadoAsync(await AutorizarAsync(otro, rq, 2));
            var detalle = await ReadJsonAsync(await admin.GetAsync($"/api/v1/compras/requisiciones/{rq}"));
            Assert.Equal(jefeDptoId, detalle.GetProperty("autorizadorN1Id").GetGuid());
            Assert.Equal(jefeAlmacenId, detalle.GetProperty("autorizadorN2Id").GetGuid());
            Assert.True((await AutorizarAsync(jefeAlmacen, rq, 2)).IsSuccessStatusCode);
        }
        finally
        {
            await LimpiarAsync();
        }
    }

    [Fact]
    public async Task Masiva_Rechaza_Al_No_Facultado_Con_Su_Codigo()
    {
        var admin = await CreateSuperAdminClientAsync(_factory);
        var otro = await CreateAutorizadorAsync(_factory);
        try
        {
            await SembrarAsync(RolAprobador.JefeDpto, TestComprasFixtures.DeptoCompras, Guid.CreateVersion7());
            var rq = await CrearTransmitirAsync(admin, new[] { (ArticuloSinStock, 1m) });

            var resp = await otro.PostAsJsonAsync(
                "/api/v1/compras/requisiciones/autorizaciones-masivas",
                new { Ids = new[] { rq }, Nivel = 1, Notas = (string?)null });

            var item = (await ReadJsonAsync(resp)).GetProperty("resultados")[0];
            Assert.False(item.GetProperty("ok").GetBoolean());
            Assert.Equal("REQUISICION_AUTORIZADOR_NO_FACULTADO", item.GetProperty("errorCodigo").GetString());
        }
        finally
        {
            await LimpiarAsync();
        }
    }

    [Fact]
    public async Task N2_No_Lo_Firma_Quien_Firmo_N1_Aunque_Sea_El_JefeAlmacen()
    {
        var admin = await CreateSuperAdminClientAsync(_factory);
        var (jefe, jefeId) = await CreateUsuarioFirmanteAsync();
        try
        {
            await SembrarAsync(RolAprobador.JefeDpto, TestComprasFixtures.DeptoCompras, jefeId);
            await SembrarAsync(RolAprobador.JefeAlmacen, TestComprasFixtures.SucursalMid, jefeId);
            var rq = await CrearTransmitirAsync(admin, new[] { (ArticuloSinStock, 1m) });

            Assert.True((await AutorizarAsync(jefe, rq, 1)).IsSuccessStatusCode);

            var n2 = await AutorizarAsync(jefe, rq, 2);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, n2.StatusCode);
            Assert.Equal("AUTORIZACION_FIRMA_MISMA_PERSONA", (await ReadJsonAsync(n2)).GetProperty("code").GetString());
        }
        finally
        {
            await LimpiarAsync();
        }
    }

    [Fact]
    public async Task Sin_Matriz_Configurada_Basta_El_Permiso()
    {
        var admin = await CreateSuperAdminClientAsync(_factory);
        var rq = await CrearTransmitirAsync(admin, new[] { (ArticuloSinStock, 1m) });

        Assert.True((await AutorizarAsync(await CreateAutorizadorAsync(_factory), rq, 1)).IsSuccessStatusCode);
    }

    private static async Task AssertNoFacultadoAsync(HttpResponseMessage resp)
    {
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.Equal("REQUISICION_AUTORIZADOR_NO_FACULTADO", (await ReadJsonAsync(resp)).GetProperty("code").GetString());
    }

    private Task<(HttpClient Client, Guid UsuarioId)> CreateUsuarioFirmanteAsync() =>
        CreateUsuarioConPermisosAsync(
            _factory,
            PermisosCanonicos.ComprasRequisicionesLeer,
            PermisosCanonicos.ComprasRequisicionesLeerTodasSucursales,
            PermisosCanonicos.ComprasRequisicionesAutorizarNivel1,
            PermisosCanonicos.ComprasRequisicionesAutorizarNivel2,
            PermisosCanonicos.ComprasRequisicionesGestionarTodasSucursales);

    private async Task SembrarAsync(RolAprobador rol, Guid scopeId, Guid usuarioId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        db.AprobadoresDepartamento.Add(new AprobadorDepartamento(
            Guid.CreateVersion7(), EmpresaInicialId, scopeId, rol, usuarioId,
            DateTimeOffset.UtcNow, usuarioId, "test C3.1", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private async Task LimpiarAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var scopes = new[] { TestComprasFixtures.DeptoCompras, TestComprasFixtures.SucursalMid };
        db.AprobadoresDepartamento.RemoveRange(await db.AprobadoresDepartamento
            .Where(a => a.EmpresaId == EmpresaInicialId && scopes.Contains(a.DepartamentoId)).ToListAsync());
        await db.SaveChangesAsync();
    }
}
