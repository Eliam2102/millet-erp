using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// Tests integration de endpoints de líneas de requisición (F2-PR2).
/// Cubre POST/PATCH/PATCH-notas/DELETE bajo
/// <c>/api/v1/compras/requisiciones/{id}/lineas</c>.
///
/// Cada test crea una requisición fresca para aislar estado entre tests.
/// El SucursalId es fijo para que folio_secuencias incremente
/// secuenciales y no haya colisiones de folio.
/// </summary>
public class LineasEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    // F7-PR1: articuloId del seed test data — la validación cross-table
    // requiere un articulo activo del catálogo compartido.
    private static readonly Guid ArticuloSeedId = Guid.Parse("00000005-0002-0000-0000-000000000001");

    // Fase E PR2.1: el CC-Máquina es obligatorio en la línea de RQ. Dim3 real del
    // seed (ADCHS01). Compras no valida existencia del CC (ADR-0050), pero usar
    // un id sembrado mantiene el test realista.
    private static readonly Guid CentroCostoSeedId = Guid.Parse("0000000c-0005-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public LineasEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // --------- POST /lineas ---------

    [Fact]
    public async Task Agregar_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/lineas",
            ValidLineaRequestBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Agregar_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/lineas",
            ValidLineaRequestBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Agregar_Con_RequisicionInexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/lineas",
            ValidLineaRequestBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Agregar_Linea_Borrador_Retorna_201()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas",
            ValidLineaRequestBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
        Assert.Equal((short)1, body.GetProperty("posicion").GetInt16());
    }

    [Fact]
    public async Task Agregar_Linea_Con_Cantidad_Invalida_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);
        var body = ValidLineaRequestBody() with { Cantidad = 0m };

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas",
            body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Agregar_Sin_CentroCosto_Retorna_400()
    {
        // Fase E PR2.1: el CC-Máquina es obligatorio. Sin él, el validador
        // rechaza (ValidationException → 400) con el código correspondiente.
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas",
            ValidLineaRequestBody() with { CentroCostoId = null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Contains("LINEA_RQ_CENTRO_COSTO_REQUERIDO", json.GetRawText());
    }

    // --------- PATCH /lineas/{lineaId} ---------

    [Fact]
    public async Task Actualizar_Linea_Estructural_Retorna_204()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);
        var lineaId = await AgregarLineaAsync(client, requisicionId);

        var body = new
        {
            ArticuloId = ArticuloSeedId,
            Cantidad = 25m,
            UnidadMedida = "KG",
            PrecioEstimadoMonto = 30m,
            PrecioEstimadoMoneda = "MXN",
            CuentaContableId = (Guid?)null,
            CentroCostoId = (Guid?)CentroCostoSeedId,
            Proyecto = (string?)null,
            FechaRequerida = (DateOnly?)null,
        };

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas/{lineaId}",
            body);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Actualizar_Linea_Inexistente_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);

        var body = new
        {
            ArticuloId = ArticuloSeedId,
            Cantidad = 1m,
            UnidadMedida = "PZA",
            PrecioEstimadoMonto = 1m,
            PrecioEstimadoMoneda = "MXN",
            CuentaContableId = (Guid?)null,
            CentroCostoId = (Guid?)CentroCostoSeedId,
            Proyecto = (string?)null,
            FechaRequerida = (DateOnly?)null,
        };

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas/{Guid.NewGuid()}",
            body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("LINEA_NO_ENCONTRADA", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Actualizar_Sin_CentroCosto_Retorna_400()
    {
        // Fase E PR2.1: editar una línea sin CC-Máquina → rechazo (400).
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);
        var lineaId = await AgregarLineaAsync(client, requisicionId);

        var body = new
        {
            ArticuloId = ArticuloSeedId,
            Cantidad = 5m,
            UnidadMedida = "PZA",
            PrecioEstimadoMonto = 10m,
            PrecioEstimadoMoneda = "MXN",
            CuentaContableId = (Guid?)null,
            CentroCostoId = (Guid?)null,
            Proyecto = (string?)null,
            FechaRequerida = (DateOnly?)null,
        };

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas/{lineaId}",
            body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Contains("LINEA_RQ_CENTRO_COSTO_REQUERIDO", json.GetRawText());
    }

    // --------- PATCH /lineas/{lineaId}/notas ---------

    [Fact]
    public async Task Actualizar_Notas_En_Borrador_Retorna_204()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);
        var lineaId = await AgregarLineaAsync(client, requisicionId);

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas/{lineaId}/notas",
            new { Notas = "Pendiente de revisión" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Actualizar_Notas_Demasiado_Largas_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);
        var lineaId = await AgregarLineaAsync(client, requisicionId);

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas/{lineaId}/notas",
            new { Notas = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --------- DELETE /lineas/{lineaId} ---------

    [Fact]
    public async Task Eliminar_Linea_Borrador_Retorna_204()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);
        var lineaId = await AgregarLineaAsync(client, requisicionId);

        var response = await client.DeleteAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas/{lineaId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Eliminar_Linea_Inexistente_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);

        var response = await client.DeleteAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // --------- G1.11: Validar Centro de Costo (CeCo) al agregar / actualizar ---------

    [Fact]
    public async Task Agregar_Linea_Con_CentroCosto_Inactivo_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);
        var dim3InactivaId = await CrearDim3InactivaAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas",
            ValidLineaRequestBody() with { CentroCostoId = dim3InactivaId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("CECO_INVALIDO", json.GetProperty("code").GetString());
        Assert.Contains("centro de costo inactivo", json.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Agregar_Linea_Con_CentroCosto_FueraDeAlcance_Retorna_422()
    {
        // Usuario con permisos para crear y editar requisiciones, pero SIN centros_costo.dim3.leer-todos
        // y sin asignaciones en centros_costo.asignaciones.
        var client = await CreateClientConPermisosAsync(
            "compras.requisiciones.crear",
            "compras.requisiciones.editar");
        var requisicionId = await CrearRequisicionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas",
            ValidLineaRequestBody() with { CentroCostoId = CentroCostoSeedId });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("CECO_INVALIDO", json.GetProperty("code").GetString());
        Assert.Contains("centro de costo fuera de su alcance", json.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Agregar_Linea_Con_CentroCosto_Inexistente_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas",
            ValidLineaRequestBody() with { CentroCostoId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("CECO_INVALIDO", json.GetProperty("code").GetString());
        Assert.Contains("el centro de costo no existe", json.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Actualizar_Linea_Con_CentroCosto_Inexistente_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);
        var lineaId = await AgregarLineaAsync(client, requisicionId);

        var body = new
        {
            ArticuloId = ArticuloSeedId,
            Cantidad = 15m,
            UnidadMedida = "PZA",
            PrecioEstimadoMonto = 20m,
            PrecioEstimadoMoneda = "MXN",
            CentroCostoId = Guid.NewGuid(),
        };

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas/{lineaId}",
            body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("CECO_INVALIDO", json.GetProperty("code").GetString());
        Assert.Contains("el centro de costo no existe", json.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Actualizar_Linea_Con_CentroCosto_Inactivo_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);
        var lineaId = await AgregarLineaAsync(client, requisicionId);
        var dim3InactivaId = await CrearDim3InactivaAsync();

        var body = new
        {
            ArticuloId = ArticuloSeedId,
            Cantidad = 15m,
            UnidadMedida = "PZA",
            PrecioEstimadoMonto = 20m,
            PrecioEstimadoMoneda = "MXN",
            CentroCostoId = dim3InactivaId,
        };

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas/{lineaId}",
            body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("CECO_INVALIDO", json.GetProperty("code").GetString());
        Assert.Contains("centro de costo inactivo", json.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Actualizar_Linea_Con_CentroCosto_FueraDeAlcance_Retorna_422()
    {
        var admin = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(admin);
        var lineaId = await AgregarLineaAsync(admin, requisicionId);

        var client = await CreateClientConPermisosAsync(
            "compras.requisiciones.crear",
            "compras.requisiciones.editar");

        var body = new
        {
            ArticuloId = ArticuloSeedId,
            Cantidad = 15m,
            UnidadMedida = "PZA",
            PrecioEstimadoMonto = 20m,
            PrecioEstimadoMoneda = "MXN",
            CentroCostoId = CentroCostoSeedId,
        };

        var response = await client.PatchAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas/{lineaId}",
            body);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("CECO_INVALIDO", json.GetProperty("code").GetString());
        Assert.Contains("centro de costo fuera de su alcance", json.GetProperty("detail").GetString());
    }

    // --------- Helpers ---------

    private async Task<Guid> CrearDim3InactivaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();

        var sufijo = $"{Guid.NewGuid():N}"[..6].ToUpperInvariant();
        var grupo3 = await mediator.Send(new CrearGrupoDim3Command($"G3-{sufijo}"));
        var dim2Id = Guid.Parse("0000000c-0004-0000-0000-000000000045");
        var dim3 = await mediator.Send(new CrearDim3Command(dim2Id, $"INA{sufijo[..4]}", $"Inactiva {sufijo}", grupo3.Id));
        var version = (await db.Dim3s.AsNoTracking().FirstAsync(d => d.Id == dim3.Id)).Version;
        await mediator.Send(new CambiarEstatusDim3Command(dim3.Id, version, Activar: false));
        return dim3.Id;
    }

    private async Task<HttpClient> CreateClientConPermisosAsync(params string[] codigosPermiso)
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"test-user-{sufijo}";
        var usuarioId = Guid.CreateVersion7();
        var rolId = Guid.CreateVersion7();

        using (var scope = _factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();

            identidad.Roles.Add(new Rol(rolId, $"rol-{sufijo}", $"Rol Test {sufijo}", false, "Rol de prueba"));
            foreach (var codigo in codigosPermiso)
            {
                var permisoId = await identidad.Permisos.AsNoTracking()
                    .Where(p => p.Codigo == codigo).Select(p => p.Id).SingleAsync();
                identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            }

            identidad.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Usuario Prueba Alcance"));
            identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
                Guid.CreateVersion7(), usuarioId, EmpresaInicialId, rolId, asignadoPorUsuarioId: null));
            // Acceso territorial al documento; no concede alcance sobre centros de costo.
            identidad.UsuarioSucursales.Add(new UsuarioSucursal(
                Guid.CreateVersion7(), usuarioId, TestComprasFixtures.SucursalMid, EmpresaInicialId));
            await identidad.SaveChangesAsync();
        }

        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, oid, $"{oid}@test.local", "Usuario Prueba Alcance");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CrearRequisicionAsync(HttpClient client)
    {
        var body = TestComprasFixtures.BuildCrearRqValidBody(
            descripcion: "Test integration F2-PR2");
        var response = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", body);
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AgregarLineaAsync(HttpClient client, Guid requisicionId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas",
            ValidLineaRequestBody());
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    private static AgregarLineaRequestBody ValidLineaRequestBody() => new(
        ArticuloId: ArticuloSeedId,
        Cantidad: 10m,
        UnidadMedida: "PZA",
        PrecioEstimadoMonto: 15.50m,
        PrecioEstimadoMoneda: "MXN",
        CuentaContableId: null,
        CentroCostoId: CentroCostoSeedId,
        Proyecto: null,
        FechaRequerida: null,
        Notas: null);

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

    private sealed record AgregarLineaRequestBody(
        Guid ArticuloId,
        decimal Cantidad,
        string UnidadMedida,
        decimal PrecioEstimadoMonto,
        string PrecioEstimadoMoneda,
        Guid? CuentaContableId,
        Guid? CentroCostoId,
        string? Proyecto,
        DateOnly? FechaRequerida,
        string? Notas);
}
