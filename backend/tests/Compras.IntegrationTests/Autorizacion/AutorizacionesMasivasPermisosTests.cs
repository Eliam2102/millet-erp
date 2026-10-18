using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Millet.Compras.Domain;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.Identidad.Domain;
using static Millet.Compras.IntegrationTests.Autorizacion.AutorizacionTestSeeding;

namespace Millet.Compras.IntegrationTests.Autorizacion;

/// <summary>
/// Tests de integración de <c>POST /api/v1/compras/requisiciones/autorizaciones-masivas</c>
/// contra Postgres real: permiso del nivel (403), validación del lote (400),
/// <c>Idempotency-Key</c> obligatoria y alcance por sucursal (ADR-0051) por id.
/// </summary>
public class AutorizacionesMasivasPermisosTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string EndpointMasiva = "/api/v1/compras/requisiciones/autorizaciones-masivas";

    private readonly StubsWebApplicationFactory _factory;

    public AutorizacionesMasivasPermisosTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Masiva_SinPermisoDelNivel_Retorna403_Y_No_Firma()
    {
        var admin = await CreateSuperAdminClientAsync(_factory);
        var rqId = await CrearTransmitirAsync(admin, new[] { (ArticuloEstandar, 2m) });

        // Solo lectura: no tiene autorizar-nivel1.
        var (soloLectura, _) = await CreateUsuarioConPermisosAsync(
            _factory, PermisosCanonicos.ComprasRequisicionesLeer);
        var nivel1 = await soloLectura.PostAsJsonAsync(
            EndpointMasiva, new { Ids = new[] { rqId }, Nivel = 1, Notas = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, nivel1.StatusCode);
        Assert.Equal("AUTORIZAR_NIVEL_DENEGADO", (await ReadJsonAsync(nivel1)).GetProperty("code").GetString());

        // Tiene autorizar-nivel1 pero pide nivel 2: también 403.
        var (soloNivel1, _) = await CreateUsuarioConPermisosAsync(
            _factory,
            PermisosCanonicos.ComprasRequisicionesLeer,
            PermisosCanonicos.ComprasRequisicionesAutorizarNivel1);
        var nivel2 = await soloNivel1.PostAsJsonAsync(
            EndpointMasiva, new { Ids = new[] { rqId }, Nivel = 2, Notas = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, nivel2.StatusCode);
        Assert.Equal("AUTORIZAR_NIVEL_DENEGADO", (await ReadJsonAsync(nivel2)).GetProperty("code").GetString());

        Assert.Equal((int)EstadoRequisicion.EnAutorizacion, await EstadoAsync(admin, rqId));
    }

    [Fact]
    public async Task Masiva_ListaVacia_Retorna400()
    {
        var admin = await CreateSuperAdminClientAsync(_factory);

        var resp = await admin.PostAsJsonAsync(
            EndpointMasiva, new { Ids = Array.Empty<Guid>(), Nivel = 1, Notas = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Masiva_MasDe50Ids_Retorna400()
    {
        var admin = await CreateSuperAdminClientAsync(_factory);
        var ids = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray();

        var resp = await admin.PostAsJsonAsync(
            EndpointMasiva, new { Ids = ids, Nivel = 1, Notas = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Masiva_SinIdempotencyKey_Retorna400()
    {
        var admin = await CreateSuperAdminClientAsync(_factory);
        var rqId = await CrearTransmitirAsync(admin, new[] { (ArticuloEstandar, 2m) });
        var sinKey = await CreateClientSinIdempotencyAsync(
            _factory, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");

        var resp = await sinKey.PostAsJsonAsync(
            EndpointMasiva, new { Ids = new[] { rqId }, Nivel = 1, Notas = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("MISSING_IDEMPOTENCY_KEY", (await ReadJsonAsync(resp)).GetProperty("code").GetString());
        Assert.Equal((int)EstadoRequisicion.EnAutorizacion, await EstadoAsync(admin, rqId));
    }

    [Fact]
    public async Task Masiva_RqDeSucursalNoAsociada_ItemErrorSinFirmar_Y_La_Otra_Se_Procesa()
    {
        var admin = await CreateSuperAdminClientAsync(_factory);
        var rqMid = await CrearTransmitirAsync(admin, new[] { (ArticuloEstandar, 2m) });
        var rqMty = await CrearTransmitirAsync(
            admin, new[] { (ArticuloEstandar, 2m) }, TestComprasFixtures.SucursalMty, "MTY");

        // Operativo asociado solo a MTY: no puede firmar en MID (ADR-0051).
        var (operativo, usuarioId) = await CreateUsuarioConPermisosAsync(
            _factory,
            PermisosCanonicos.ComprasRequisicionesLeer,
            PermisosCanonicos.ComprasRequisicionesAutorizarNivel1);
        await AsociarSucursalAsync(_factory, usuarioId, TestComprasFixtures.SucursalMty);

        var resp = await operativo.PostAsJsonAsync(
            EndpointMasiva, new { Ids = new[] { rqMid, rqMty }, Nivel = 1, Notas = (string?)null });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var resultados = (await ReadJsonAsync(resp)).GetProperty("resultados").EnumerateArray().ToList();

        var noAsociada = Resultado(resultados, rqMid);
        Assert.False(noAsociada.GetProperty("ok").GetBoolean());
        Assert.Equal("SUCURSAL_NO_ASOCIADA", noAsociada.GetProperty("errorCodigo").GetString());

        Assert.True(Resultado(resultados, rqMty).GetProperty("ok").GetBoolean());

        // La RQ de MID no quedó firmada.
        Assert.Equal((int)EstadoRequisicion.EnAutorizacion, await EstadoAsync(admin, rqMid));
    }

    private static JsonElement Resultado(IEnumerable<JsonElement> resultados, Guid id) =>
        resultados.Single(r => r.GetProperty("id").GetGuid() == id);
}
