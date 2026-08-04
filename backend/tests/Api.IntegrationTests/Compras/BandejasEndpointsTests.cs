using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// Tests integration de bandejas (F2-PR4):
/// <list type="bullet">
///   <item><c>GET /api/v1/compras/requisiciones</c> — bandeja general con filtros opcionales.</item>
///   <item><c>GET /api/v1/compras/pendientes-autorizacion</c> — solo <c>EnAutorizacion</c>.</item>
/// </list>
///
/// Las pruebas crean RQs en distintos estados para verificar paginación,
/// filtros y orden. Comparten DB con otros tests, por eso filtran por
/// <c>departamentoId</c> recién generado para aislar el set.
/// </summary>
public class BandejasEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    private static readonly Guid ArticuloSeedId = Guid.Parse("00000005-0002-0000-0000-000000000001");
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    // Permiso canónico compras.requisiciones.leer (ver PermisosCanonicos.Todos).
    private static readonly Guid ComprasRequisicionesLeerId = Guid.Parse("00000003-0001-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public BandejasEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // --- GET /api/v1/compras/requisiciones ---

    [Fact]
    public async Task ListarRequisiciones_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/compras/requisiciones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListarRequisiciones_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/compras/requisiciones");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListarRequisiciones_FiltradoPorDepartamento_DevuelveSoloLasDeEseDepto()
    {
        var client = await CreateSuperAdminClientAsync();
        var deptoAislado = await SeedDeptoAisladoConAsignacionAsync(TestComprasFixtures.SucursalMid);

        // 3 RQs en el depto aislado.
        for (var i = 0; i < 3; i++)
        {
            await CrearRequisicionAsync(client, deptoAislado);
        }

        var response = await client.GetAsync(
            $"/api/v1/compras/requisiciones?departamentoId={deptoAislado}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal(3, json.GetProperty("total").GetInt32());
        Assert.Equal(3, json.GetProperty("items").GetArrayLength());
        foreach (var item in json.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(deptoAislado, item.GetProperty("departamentoId").GetGuid());
        }
    }

    [Fact]
    public async Task ListarRequisiciones_FiltradoPorEstado_FiltraCorrecto()
    {
        var client = await CreateSuperAdminClientAsync();
        var deptoAislado = await SeedDeptoAisladoConAsignacionAsync(TestComprasFixtures.SucursalMid);

        // 2 borrador + 1 transmitida.
        await CrearRequisicionAsync(client, deptoAislado);
        await CrearRequisicionAsync(client, deptoAislado);
        var rqEnAuth = await CrearRequisicionAsync(client, deptoAislado);
        await AgregarLineaAsync(client, rqEnAuth);
        var transmit = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{rqEnAuth}/transmitir", content: null);
        transmit.EnsureSuccessStatusCode();

        // estado=0 (Borrador) → 2.
        var borrador = await client.GetAsync(
            $"/api/v1/compras/requisiciones?departamentoId={deptoAislado}&estado=0");
        var bj = await ReadJsonAsync(borrador);
        Assert.Equal(2, bj.GetProperty("total").GetInt32());

        // estado=1 (EnAutorizacion) → 1.
        var enAuth = await client.GetAsync(
            $"/api/v1/compras/requisiciones?departamentoId={deptoAislado}&estado=1");
        var ej = await ReadJsonAsync(enAuth);
        Assert.Equal(1, ej.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ListarRequisiciones_Paginacion_RespetaLimitYOffset()
    {
        var client = await CreateSuperAdminClientAsync();
        var deptoAislado = await SeedDeptoAisladoConAsignacionAsync(TestComprasFixtures.SucursalMid);

        for (var i = 0; i < 5; i++)
        {
            await CrearRequisicionAsync(client, deptoAislado);
        }

        var primera = await client.GetAsync(
            $"/api/v1/compras/requisiciones?departamentoId={deptoAislado}&offset=0&limit=2");
        var pj = await ReadJsonAsync(primera);
        Assert.Equal(5, pj.GetProperty("total").GetInt32());
        Assert.Equal(2, pj.GetProperty("items").GetArrayLength());
        Assert.Equal(0, pj.GetProperty("offset").GetInt32());
        Assert.Equal(2, pj.GetProperty("limit").GetInt32());

        var segunda = await client.GetAsync(
            $"/api/v1/compras/requisiciones?departamentoId={deptoAislado}&offset=4&limit=2");
        var sj = await ReadJsonAsync(segunda);
        Assert.Equal(1, sj.GetProperty("items").GetArrayLength());
    }

    // --- GET /api/v1/compras/pendientes-autorizacion ---

    [Fact]
    public async Task ListarPendientes_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/compras/pendientes-autorizacion");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListarPendientes_FiltraSoloEnAutorizacion()
    {
        var client = await CreateSuperAdminClientAsync();
        var deptoAislado = await SeedDeptoAisladoConAsignacionAsync(TestComprasFixtures.SucursalMid);

        // 1 borrador (no debe aparecer) + 2 transmitidas.
        await CrearRequisicionAsync(client, deptoAislado);
        var rq1 = await CrearYTransmitirAsync(client, deptoAislado);
        var rq2 = await CrearYTransmitirAsync(client, deptoAislado);

        var response = await client.GetAsync(
            $"/api/v1/compras/pendientes-autorizacion?departamentoId={deptoAislado}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        var ids = json.GetProperty("items")
            .EnumerateArray()
            .Select(i => i.GetProperty("id").GetGuid())
            .ToHashSet();
        Assert.Contains(rq1, ids);
        Assert.Contains(rq2, ids);
        // Todos deben estar en EnAutorizacion = 1.
        foreach (var item in json.GetProperty("items").EnumerateArray())
        {
            Assert.Equal(1, item.GetProperty("estado").GetInt32());
        }
    }

    /// <summary>
    /// ADR-0042 — guard permanente del cierre de la vulnerabilidad: un
    /// principal con <c>compras.requisiciones.leer</c> pero SIN el permiso
    /// amplio <c>identidad.usuarios.leer</c> (ni <c>compartido.catalogos.leer</c>)
    /// debe recibir <c>requisitanteNombre</c> y <c>departamentoNombre</c>
    /// resueltos en el backend. Si esto se rompe, alguien volvió a depender
    /// de la resolución client-side (que exige el permiso amplio).
    /// </summary>
    [Fact]
    public async Task Pendientes_PrincipalSinPermisoAmplio_RecibeNombresResueltos()
    {
        var admin = await CreateSuperAdminClientAsync();
        var deptoAislado = await SeedDeptoAisladoConAsignacionAsync(TestComprasFixtures.SucursalMid);
        await CrearYTransmitirAsync(admin, deptoAislado);

        // Principal que SOLO tiene compras.requisiciones.leer.
        var jefeOid = await SeedPrincipalSoloLeerRequisicionesAsync();
        var jefe = _factory.CreateClient();
        var token = await FakeLoginAsync(jefe, jefeOid, $"{jefeOid}@test.local", "Jefe Solo Lectura");
        jefe.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Sanity: el principal NO puede leer el padrón de usuarios (el permiso
        // amplio del que dependía la resolución client-side está ausente).
        var usuariosResp = await jefe.GetAsync("/api/v1/identidad/usuarios?limit=1");
        Assert.Equal(HttpStatusCode.Forbidden, usuariosResp.StatusCode);

        var response = await jefe.GetAsync(
            $"/api/v1/compras/pendientes-autorizacion?departamentoId={deptoAislado}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        foreach (var item in items)
        {
            Assert.True(
                item.TryGetProperty("requisitanteNombre", out var reqNombre)
                && !string.IsNullOrWhiteSpace(reqNombre.GetString()),
                "requisitanteNombre debe venir resuelto del backend (ADR-0042).");
            Assert.True(
                item.TryGetProperty("departamentoNombre", out var depNombre)
                && !string.IsNullOrWhiteSpace(depNombre.GetString()),
                "departamentoNombre debe venir resuelto del backend (ADR-0042).");
        }
    }

    /// <summary>
    /// ADR-0042 (alcance ampliado al histórico): un principal con
    /// <c>compras.requisiciones.leer</c> pero SIN <c>identidad.usuarios.leer</c>
    /// debe recibir <c>actorNombre</c> resuelto en el histórico de la RQ. La
    /// entrada de creación tiene como actor al super-admin (que la creó), cuyo
    /// nombre el backend resuelve por batch sin que el principal lea el padrón.
    /// </summary>
    [Fact]
    public async Task Historico_PrincipalSinPermisoAmplio_RecibeActorNombreResuelto()
    {
        var admin = await CreateSuperAdminClientAsync();
        var deptoAislado = await SeedDeptoAisladoConAsignacionAsync(TestComprasFixtures.SucursalMid);
        var rqId = await CrearRequisicionAsync(admin, deptoAislado);

        var jefeOid = await SeedPrincipalSoloLeerRequisicionesAsync();
        var jefe = _factory.CreateClient();
        var token = await FakeLoginAsync(jefe, jefeOid, $"{jefeOid}@test.local", "Jefe Solo Lectura");
        jefe.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var usuariosResp = await jefe.GetAsync("/api/v1/identidad/usuarios?limit=1");
        Assert.Equal(HttpStatusCode.Forbidden, usuariosResp.StatusCode);

        var response = await jefe.GetAsync($"/api/v1/compras/requisiciones/{rqId}/historico");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        var entries = json.EnumerateArray().ToList();
        Assert.NotEmpty(entries);
        // Las entradas con actor (no sistema) deben traer actorNombre resuelto.
        var conActor = entries
            .Where(e => e.TryGetProperty("actorId", out var a) && a.ValueKind != JsonValueKind.Null)
            .ToList();
        Assert.NotEmpty(conActor);
        foreach (var e in conActor)
        {
            Assert.True(
                e.TryGetProperty("actorNombre", out var actorNombre)
                && !string.IsNullOrWhiteSpace(actorNombre.GetString()),
                "actorNombre debe venir resuelto del backend (ADR-0042).");
        }
    }

    // --- Helpers ---

    /// <summary>
    /// Siembra un rol con SOLO <c>compras.requisiciones.leer</c> (sin
    /// <c>identidad.usuarios.leer</c> ni <c>compartido.catalogos.leer</c>),
    /// un usuario y su asignación a la empresa inicial. Devuelve el oid
    /// sintético para hacer <c>fake-login</c>. Patrón de seeding directo via
    /// DbContext (igual que <see cref="SeedDeptoAisladoConAsignacionAsync"/>).
    /// </summary>
    private async Task<string> SeedPrincipalSoloLeerRequisicionesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var random = Guid.NewGuid().ToString("N").Substring(0, 10).ToLowerInvariant();
        var rolId = Guid.CreateVersion7();
        identidad.Roles.Add(new Rol(
            rolId,
            $"test-rq-leer-{random}",
            "Test RQ Leer (sin usuarios.leer)",
            esDelSistema: false,
            "Solo compras.requisiciones.leer — guard de ADR-0042."));
        identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, ComprasRequisicionesLeerId));

        var oid = $"test-rq-leer-{random}";
        var usuario = new Usuario(Guid.CreateVersion7(), oid, $"{oid}@test.local", "Jefe Solo Lectura");
        identidad.Usuarios.Add(usuario);
        identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuario.Id));
        identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
            Guid.CreateVersion7(), usuario.Id, EmpresaInicialId, rolId, asignadoPorUsuarioId: null));

        await identidad.SaveChangesAsync();
        return oid;
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CrearRequisicionAsync(HttpClient client, Guid? departamentoId = null)
    {
        // PR-A2: si el caller no pasa depto explícito, usar el canónico
        // (DeptoCompras está Activo en MID por el seed). Los tests que
        // requieren aislamiento del baseline pasan un deptoId obtenido de
        // <see cref="SeedDeptoAisladoConAsignacionAsync"/>.
        var body = TestComprasFixtures.BuildCrearRqValidBody(
            departamentoId: departamentoId,
            descripcion: "Bandeja test F2-PR4");
        var response = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", body);
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// PR-A2: siembra un departamento aislado en
    /// <c>compartido.departamentos</c> + su asignación Activa con la
    /// sucursal indicada en <c>compartido.sucursal_departamentos</c>.
    /// Permite que los tests de filtrado por departamento mantengan
    /// aislamiento del baseline (cada test inventa su propio depto, sin
    /// colisionar con otros), respetando la validación cross-table que
    /// PR-A2 introdujo en <c>CrearRequisicionHandler</c>.
    ///
    /// <para>Patrón calcado de
    /// <c>BifurcacionEndpointsTests.QueryOcBorradorStubAsync</c> y
    /// <c>RecepcionEndpointsTests.PublishAsync</c> (scope.CreateScope →
    /// GetRequiredService → Bypass empresa context).</para>
    /// </summary>
    private async Task<Guid> SeedDeptoAisladoConAsignacionAsync(
        Guid sucursalId, string nombrePrefix = "DEPT-AISL")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var empresaContext = scope.ServiceProvider
            .GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var deptoId = Guid.CreateVersion7();
        // Random suffix: los primeros 8 chars de un Guid v7 son timestamp ms,
        // así que tests que corren en el mismo segundo colisionan en
        // UNIQUE(clave). Usar Guid.NewGuid (V4, fully random) garantiza
        // unicidad cross-test, cross-thread.
        var random = Guid.NewGuid().ToString("N").Substring(0, 10).ToUpperInvariant();
        var clave = $"{nombrePrefix}-{random}"; // p.ej. "DEPT-AISL-A1B2C3D4E5" — 20 chars (max).

        db.Departamentos.Add(new Departamento(
            id: deptoId,
            clave: clave,
            nombre: $"Depto aislado test {random}"));

        db.SucursalDepartamentos.Add(new SucursalDepartamento(
            id: Guid.CreateVersion7(),
            sucursalId: sucursalId,
            departamentoId: deptoId));

        await db.SaveChangesAsync();
        return deptoId;
    }

    private static async Task AgregarLineaAsync(HttpClient client, Guid requisicionId)
    {
        var body = new
        {
            ArticuloId = ArticuloSeedId,
            Cantidad = 10m,
            UnidadMedida = "PZA",
            PrecioEstimadoMonto = 15m,
            PrecioEstimadoMoneda = "MXN",
            CuentaContableId = (Guid?)null,
            CentroCostoId = (Guid?)Guid.Parse("0000000c-0005-0000-0000-000000000001"), // Fase E PR2.1: CC obligatorio
            Proyecto = (string?)null,
            FechaRequerida = (DateOnly?)null,
            Notas = (string?)null,
        };
        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas", body);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CrearYTransmitirAsync(HttpClient client, Guid departamentoId)
    {
        var rqId = await CrearRequisicionAsync(client, departamentoId);
        await AgregarLineaAsync(client, rqId);
        var transmit = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{rqId}/transmitir", content: null);
        transmit.EnsureSuccessStatusCode();
        return rqId;
    }

    private static async Task<string> FakeLoginAsync(HttpClient client, string oid, string email, string nombre)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = email,
            Nombre = nombre,
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.Clone();
    }
}
