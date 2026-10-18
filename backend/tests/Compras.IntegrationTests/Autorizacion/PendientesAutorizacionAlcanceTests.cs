using Millet.Compras.IntegrationTests.Fixtures;
using Millet.Identidad.Domain;
using static Millet.Compras.IntegrationTests.Autorizacion.AutorizacionTestSeeding;

namespace Millet.Compras.IntegrationTests.Autorizacion;

/// <summary>
/// Alcance por sucursal de <c>GET /api/v1/compras/pendientes-autorizacion</c>
/// (ADR-0051): un operativo solo ve las RQ de sus sucursales; con
/// <c>compras.requisiciones.leer-todas-sucursales</c> ve todas.
/// </summary>
public class PendientesAutorizacionAlcanceTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string EndpointPendientes = "/api/v1/compras/pendientes-autorizacion";

    private readonly StubsWebApplicationFactory _factory;

    public PendientesAutorizacionAlcanceTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Operativo_AsociadoSoloAMty_No_Ve_RqDeMid()
    {
        var admin = await CreateSuperAdminClientAsync(_factory);
        var rqMid = await CrearTransmitirAsync(admin, new[] { (ArticuloEstandar, 2m) });
        var rqMty = await CrearTransmitirAsync(
            admin, new[] { (ArticuloEstandar, 2m) }, TestComprasFixtures.SucursalMty, "MTY");

        var (operativo, usuarioId) = await CreateUsuarioConPermisosAsync(
            _factory, PermisosCanonicos.ComprasRequisicionesLeer);
        await AsociarSucursalAsync(_factory, usuarioId, TestComprasFixtures.SucursalMty);

        var ids = await IdsPendientesAsync(operativo);

        Assert.Contains(rqMty, ids);
        Assert.DoesNotContain(rqMid, ids);
    }

    [Fact]
    public async Task Bypass_LeerTodasSucursales_Ve_Ambas()
    {
        var admin = await CreateSuperAdminClientAsync(_factory);
        var rqMid = await CrearTransmitirAsync(admin, new[] { (ArticuloEstandar, 2m) });
        var rqMty = await CrearTransmitirAsync(
            admin, new[] { (ArticuloEstandar, 2m) }, TestComprasFixtures.SucursalMty, "MTY");

        // Sin asociaciones: el bypass del permiso es lo que da visibilidad.
        var (corporativo, _) = await CreateUsuarioConPermisosAsync(
            _factory,
            PermisosCanonicos.ComprasRequisicionesLeer,
            PermisosCanonicos.ComprasRequisicionesLeerTodasSucursales);

        var ids = await IdsPendientesAsync(corporativo);

        Assert.Contains(rqMid, ids);
        Assert.Contains(rqMty, ids);
    }

    /// <summary>
    /// Pagina toda la bandeja (la BD es compartida entre tests) y devuelve los ids.
    /// </summary>
    private static async Task<HashSet<Guid>> IdsPendientesAsync(HttpClient client)
    {
        var ids = new HashSet<Guid>();
        const int limit = 200;
        var offset = 0;
        while (true)
        {
            var resp = await client.GetAsync($"{EndpointPendientes}?limit={limit}&offset={offset}");
            resp.EnsureSuccessStatusCode();
            var items = (await ReadJsonAsync(resp)).GetProperty("items").EnumerateArray().ToList();
            foreach (var item in items)
            {
                ids.Add(item.GetProperty("id").GetGuid());
            }

            if (items.Count < limit)
            {
                break;
            }

            offset += limit;
        }

        return ids;
    }
}
