using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Compras.IntegrationTests.Catalogos;

/// <summary>
/// Tests integration de CRUD completo de proveedores y artículos
/// (B.5). Cubre los 6 endpoints (POST/PATCH/DELETE × 2 entidades) con
/// happy paths + 401/403/404/422.
/// </summary>
public class CrudCatalogosEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    private static readonly Guid MonedaMxnId = Guid.Parse("00000001-0000-0000-0000-000000000001");
    private static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly StubsWebApplicationFactory _factory;

    public CrudCatalogosEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // --- POST /proveedores ---

    [Fact]
    public async Task CrearProveedor_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CrearProveedor_HappyPath_Retorna_201()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"PROV-B5-{Guid.NewGuid().ToString("N")[..8]}";

        var response = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody(clave));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal(clave, json.GetProperty("clave").GetString());
        Assert.NotEqual(Guid.Empty, json.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task CrearProveedor_Clave_Duplicada_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"PROV-DUP-{Guid.NewGuid().ToString("N")[..8]}";

        var first = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody(clave));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody(clave));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        var json = await ReadJsonAsync(second);
        Assert.Equal("PROVEEDOR_CLAVE_DUPLICADA", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CrearProveedor_Moneda_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var body = BuildProveedorBody() with { MonedaPreferidaId = Guid.CreateVersion7() };

        var response = await client.PostAsJsonAsync("/api/v1/catalogos/proveedores", body);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("MONEDA_NO_ENCONTRADA", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CrearProveedor_Sin_IdempotencyKey_Retorna_400()
    {
        var rawClient = _factory.CreateClient();
        var token = await FakeLoginAsync(rawClient, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        rawClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await rawClient.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("MISSING_IDEMPOTENCY_KEY", json.GetProperty("code").GetString());
    }

    // --- PATCH /proveedores/{id} ---

    [Fact]
    public async Task ActualizarProveedor_HappyPath_Cambia_Campos()
    {
        var client = await CreateSuperAdminClientAsync();
        var crearResp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/proveedores/{id}",
            new
            {
                RazonSocial = "Razón Social Editada SA de CV",
                CondicionesPagoDias = (short)45,
                Email = "nuevo@editado.com",
            });
        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);

        var detalle = await client.GetAsync($"/api/v1/catalogos/proveedores/{id}");
        var json = await ReadJsonAsync(detalle);
        Assert.Equal("Razón Social Editada SA de CV", json.GetProperty("razonSocial").GetString());
        Assert.Equal((short)45, json.GetProperty("condicionesPagoDias").GetInt16());
        Assert.Equal("nuevo@editado.com", json.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ActualizarProveedor_Limpiar_Email_Setea_Null()
    {
        var client = await CreateSuperAdminClientAsync();
        var body = BuildProveedorBody() with { Email = "tieneemail@test.com" };
        var crearResp = await client.PostAsJsonAsync("/api/v1/catalogos/proveedores", body);
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/proveedores/{id}",
            new { LimpiarEmail = true });
        patchResp.EnsureSuccessStatusCode();

        var detalle = await client.GetAsync($"/api/v1/catalogos/proveedores/{id}");
        var json = await ReadJsonAsync(detalle);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("email").ValueKind);
    }

    [Fact]
    public async Task ActualizarProveedor_Inexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/proveedores/{Guid.NewGuid()}",
            new { RazonSocial = "X" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- RFC único (F1-ADM-05) ---

    [Fact]
    public async Task CrearProveedor_Rfc_Duplicado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var rfc = $"TST{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        var primero = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody(rfc: rfc));
        primero.EnsureSuccessStatusCode();

        var segundo = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody(rfc: rfc));
        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        var json = await ReadJsonAsync(segundo);
        Assert.Equal("PROVEEDOR_RFC_DUPLICADO", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CrearProveedor_Rfc_Duplicado_MinusculasYEspacios_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var rfc = $"TST{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        var primero = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody(rfc: rfc));
        primero.EnsureSuccessStatusCode();

        // Un solo espacio extra: el validator de FluentValidation exige
        // Length(12,13) sobre el string crudo (antes de normalizar), así
        // que no puede exceder 13 chars — la normalización real (trim +
        // upper) ocurre después, en el dominio.
        var segundo = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores",
            BuildProveedorBody(rfc: $"{rfc.ToLowerInvariant()} "));
        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        var json = await ReadJsonAsync(segundo);
        Assert.Equal("PROVEEDOR_RFC_DUPLICADO", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CrearProveedor_RfcGenerico_MismaRazonSocial_Queda_EnRevision()
    {
        var client = await CreateSuperAdminClientAsync();
        var razonSocial = $"Duplicado Genérico {Guid.NewGuid():N}"[..40];
        var bodyOriginal = BuildProveedorBody(rfc: "XAXX010101000") with { RazonSocial = razonSocial };

        var original = await client.PostAsJsonAsync("/api/v1/catalogos/proveedores", bodyOriginal);
        original.EnsureSuccessStatusCode();
        var originalId = (await ReadJsonAsync(original)).GetProperty("id").GetGuid();

        var bodyDuplicado = BuildProveedorBody(rfc: "XAXX010101000") with { RazonSocial = razonSocial };
        var duplicado = await client.PostAsJsonAsync("/api/v1/catalogos/proveedores", bodyDuplicado);

        Assert.Equal(HttpStatusCode.Created, duplicado.StatusCode);
        var json = await ReadJsonAsync(duplicado);
        Assert.Equal((int)EstatusCatalogo.EnRevision, json.GetProperty("estatus").GetInt32());
        Assert.Equal(originalId, json.GetProperty("posibleDuplicadoDeId").GetGuid());
    }

    [Fact]
    public async Task CrearProveedor_RfcGenerico_RazonSocialDistinta_No_Se_Marca_Duplicado()
    {
        var client = await CreateSuperAdminClientAsync();
        var bodyUno = BuildProveedorBody(rfc: "XEXX010101000")
            with { RazonSocial = $"Genérico Uno {Guid.NewGuid():N}"[..40] };
        var bodyDos = BuildProveedorBody(rfc: "XEXX010101000")
            with { RazonSocial = $"Genérico Dos {Guid.NewGuid():N}"[..40] };

        (await client.PostAsJsonAsync("/api/v1/catalogos/proveedores", bodyUno))
            .EnsureSuccessStatusCode();
        var dos = await client.PostAsJsonAsync("/api/v1/catalogos/proveedores", bodyDos);

        Assert.Equal(HttpStatusCode.Created, dos.StatusCode);
        var json = await ReadJsonAsync(dos);
        // G1.1: todo alta nace «En revisión»; el RFC genérico no lo marca como posible duplicado.
        Assert.Equal((int)EstatusCatalogo.EnRevision, json.GetProperty("estatus").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("posibleDuplicadoDeId").ValueKind);
    }

    [Fact]
    public async Task ActualizarProveedor_Rfc_A_Otro_Proveedor_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var rfcOtro = $"TST{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        (await client.PostAsJsonAsync("/api/v1/catalogos/proveedores", BuildProveedorBody(rfc: rfcOtro)))
            .EnsureSuccessStatusCode();

        var crearResp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/proveedores/{id}", new { Rfc = rfcOtro });
        Assert.Equal(HttpStatusCode.Conflict, patchResp.StatusCode);
        var json = await ReadJsonAsync(patchResp);
        Assert.Equal("PROVEEDOR_RFC_DUPLICADO", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ActualizarProveedor_Rfc_Propio_Retorna_204()
    {
        var client = await CreateSuperAdminClientAsync();
        var rfcPropio = $"TST{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var crearResp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody(rfc: rfcPropio));
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/proveedores/{id}", new { Rfc = rfcPropio.ToLowerInvariant() });
        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);
    }

    // --- Datos bancarios: permiso dedicado (F1-ADM-05) ---

    [Fact]
    public async Task ActualizarProveedor_Clabe_Sin_Permiso_Bancarios_Retorna_403_Y_No_Cambia()
    {
        var admin = await CreateSuperAdminClientAsync();
        var crearResp = await admin.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        // Tiene el permiso grueso de catálogos (pasa el RequireAuthorization
        // del endpoint) pero NO el granular de bancarios.
        var clienteSinBancarios = await CreateClientConPermisosAsync(
            PermisosCanonicos.CompartidoCatalogosAdministrar);

        var patchResp = await clienteSinBancarios.PatchAsJsonAsync(
            $"/api/v1/catalogos/proveedores/{id}",
            new { Clabe = "012345678901234567" });

        Assert.Equal(HttpStatusCode.Forbidden, patchResp.StatusCode);
        var json = await ReadJsonAsync(patchResp);
        Assert.Equal("PROVEEDOR_BANCARIOS_SIN_PERMISO", json.GetProperty("code").GetString());

        var bancarios = await admin.GetAsync(
            $"/api/v1/datos-maestros/proveedores/{id}/datos-bancarios");
        bancarios.EnsureSuccessStatusCode();
        Assert.Equal(
            JsonValueKind.Null, (await ReadJsonAsync(bancarios)).GetProperty("clabe").ValueKind);
    }

    [Fact]
    public async Task ActualizarProveedor_Clabe_Con_Permiso_Bancarios_Retorna_204_Y_Persiste()
    {
        var admin = await CreateSuperAdminClientAsync();
        var crearResp = await admin.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var clienteConBancarios = await CreateClientConPermisosAsync(
            PermisosCanonicos.CompartidoCatalogosAdministrar,
            PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar,
            PermisosCanonicos.DatosMaestrosProveedoresBancariosVer);

        var patchResp = await clienteConBancarios.PatchAsJsonAsync(
            $"/api/v1/catalogos/proveedores/{id}",
            new { Banco = "BBVA", Clabe = "012345678901234567", Beneficiario = "Proveedor Test" });
        Assert.Equal(HttpStatusCode.NoContent, patchResp.StatusCode);

        var bancarios = await clienteConBancarios.GetAsync(
            $"/api/v1/datos-maestros/proveedores/{id}/datos-bancarios");
        bancarios.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(bancarios);
        Assert.Equal("BBVA", json.GetProperty("banco").GetString());
        Assert.Equal("Proveedor Test", json.GetProperty("beneficiario").GetString());
        Assert.Equal("**************4567", json.GetProperty("clabe").GetString());
        Assert.False(json.GetProperty("clabeCompleta").GetBoolean());
    }

    [Fact]
    public async Task ActualizarProveedor_Clabe_Se_Audita_Con_Actor_Fecha_Y_Clabe_Protegida()
    {
        var client = await CreateSuperAdminClientAsync();
        var crearResp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        const string clabeSensible = "998877665544332211";
        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/proveedores/{id}", new { Clabe = clabeSensible });
        patchResp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var fila = await db.AuditLog
            .Where(a => a.Entidad == "Proveedor" && a.EntidadId == id && a.Operacion == "actualizar")
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync();

        Assert.NotNull(fila);
        Assert.False(string.IsNullOrWhiteSpace(fila!.ActorNombre));
        Assert.NotEqual(default, fila.Timestamp);
        Assert.Contains("[PROTEGIDO]", fila.Cambios);
        Assert.DoesNotContain(clabeSensible, fila.Cambios);
        Assert.DoesNotContain(clabeSensible, fila.Resumen);
        Assert.DoesNotContain(clabeSensible, fila.EntidadEtiqueta);
    }

    // --- Proveedor inactivo y multi-sucursal (F1-ADM-05, evidencia) ---

    [Fact]
    public async Task ProveedorInactivo_GetDetalle_Y_GetRqPrevia_Siguen_En_200()
    {
        // "Crear RQ con proveedor inactivo → 422" ya lo cubre
        // CrearRequisicion_ConProveedorInactivo_Retorna_422
        // (Compras.IntegrationTests/Catalogos/CatalogosEndpointsTests.cs).
        // Aquí solo se evidencia la otra mitad de la regla: una RQ creada
        // mientras el proveedor estaba activo, y el detalle del proveedor,
        // se siguen pudiendo consultar después de desactivarlo.
        var client = await CreateSuperAdminClientAsync();
        var crearProv = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        var proveedorId = (await ReadJsonAsync(crearProv)).GetProperty("id").GetGuid();
        await ValidarProveedorAsync(proveedorId);

        var crearRq = await client.PostAsJsonAsync(
            "/api/v1/compras/requisiciones",
            TestComprasFixtures.BuildCrearRqValidBody(
                proveedorSugeridoId: proveedorId,
                descripcion: "F1-ADM-05 proveedor luego inactivado"));
        crearRq.EnsureSuccessStatusCode();
        var rqId = (await ReadJsonAsync(crearRq)).GetProperty("id").GetGuid();

        (await client.DeleteAsync($"/api/v1/catalogos/proveedores/{proveedorId}"))
            .EnsureSuccessStatusCode();

        var detalleProveedor = await client.GetAsync($"/api/v1/catalogos/proveedores/{proveedorId}");
        Assert.Equal(HttpStatusCode.OK, detalleProveedor.StatusCode);
        Assert.Equal(
            (int)EstatusCatalogo.Inactivo,
            (await ReadJsonAsync(detalleProveedor)).GetProperty("estatus").GetInt32());

        var detalleRq = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}");
        Assert.Equal(HttpStatusCode.OK, detalleRq.StatusCode);
    }

    [Fact]
    public async Task ProveedorActivo_En_Rqs_De_Dos_Sucursales_Distintas_Mismo_ProveedorId()
    {
        var client = await CreateSuperAdminClientAsync();
        var crearProv = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        var proveedorId = (await ReadJsonAsync(crearProv)).GetProperty("id").GetGuid();
        await ValidarProveedorAsync(proveedorId);

        var rqMid = await client.PostAsJsonAsync(
            "/api/v1/compras/requisiciones",
            TestComprasFixtures.BuildCrearRqValidBody(
                sucursalId: TestComprasFixtures.SucursalMid,
                sucursalCodigo: TestComprasFixtures.SucursalCodigoMid,
                proveedorSugeridoId: proveedorId,
                descripcion: "F1-ADM-05 mismo proveedor sucursal MID"));
        rqMid.EnsureSuccessStatusCode();
        var rqMidId = (await ReadJsonAsync(rqMid)).GetProperty("id").GetGuid();

        var rqMty = await client.PostAsJsonAsync(
            "/api/v1/compras/requisiciones",
            TestComprasFixtures.BuildCrearRqValidBody(
                sucursalId: TestComprasFixtures.SucursalMty,
                sucursalCodigo: "MTY",
                proveedorSugeridoId: proveedorId,
                descripcion: "F1-ADM-05 mismo proveedor sucursal MTY"));
        rqMty.EnsureSuccessStatusCode();
        var rqMtyId = (await ReadJsonAsync(rqMty)).GetProperty("id").GetGuid();

        // El POST no devuelve proveedorSugeridoId (solo id/folio/estado/version);
        // se confirma leyendo el detalle de cada RQ.
        var detalleMid = await client.GetAsync($"/api/v1/compras/requisiciones/{rqMidId}");
        detalleMid.EnsureSuccessStatusCode();
        var detalleMty = await client.GetAsync($"/api/v1/compras/requisiciones/{rqMtyId}");
        detalleMty.EnsureSuccessStatusCode();

        var proveedorEnMid = (await ReadJsonAsync(detalleMid)).GetProperty("proveedorSugeridoId").GetGuid();
        var proveedorEnMty = (await ReadJsonAsync(detalleMty)).GetProperty("proveedorSugeridoId").GetGuid();
        Assert.Equal(proveedorId, proveedorEnMid);
        Assert.Equal(proveedorId, proveedorEnMty);
    }

    // --- DELETE /proveedores/{id} ---

    [Fact]
    public async Task DesactivarProveedor_Cambia_Estatus_A_Inactivo()
    {
        var client = await CreateSuperAdminClientAsync();
        var crearResp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var deleteResp = await client.DeleteAsync($"/api/v1/catalogos/proveedores/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var detalle = await client.GetAsync($"/api/v1/catalogos/proveedores/{id}");
        var json = await ReadJsonAsync(detalle);
        // EstatusCatalogo.Inactivo = 1
        Assert.Equal((int)EstatusCatalogo.Inactivo, json.GetProperty("estatus").GetInt32());
    }

    [Fact]
    public async Task DesactivarProveedor_Idempotente_Segunda_Vez_Sigue_204()
    {
        var client = await CreateSuperAdminClientAsync();
        var crearResp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/proveedores", BuildProveedorBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var first = await client.DeleteAsync($"/api/v1/catalogos/proveedores/{id}");
        var second = await client.DeleteAsync($"/api/v1/catalogos/proveedores/{id}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    // --- POST /articulos ---

    [Fact]
    public async Task CrearArticulo_HappyPath_Retorna_201()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"ART-B5-{Guid.NewGuid().ToString("N")[..8]}";

        var response = await client.PostAsJsonAsync(
            "/api/v1/catalogos/articulos", BuildArticuloBody(clave));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CrearArticulo_Moneda_No_Registrada_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var body = BuildArticuloBody() with { PrecioReferenciaMonto = 10m, PrecioReferenciaMoneda = "ZZZ" };

        var response = await client.PostAsJsonAsync("/api/v1/catalogos/articulos", body);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("ARTICULO_MONEDA_NO_REGISTRADA", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CrearArticulo_Clave_Duplicada_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"ART-DUP-{Guid.NewGuid().ToString("N")[..8]}";

        await client.PostAsJsonAsync("/api/v1/catalogos/articulos", BuildArticuloBody(clave));
        var second = await client.PostAsJsonAsync("/api/v1/catalogos/articulos", BuildArticuloBody(clave));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        var json = await ReadJsonAsync(second);
        Assert.Equal("ARTICULO_CLAVE_DUPLICADA", json.GetProperty("code").GetString());
    }

    // --- PATCH /articulos/{id} ---

    [Fact]
    public async Task ActualizarArticulo_Cambio_Naturaleza_Individual_Refleja_En_Detalle()
    {
        var client = await CreateSuperAdminClientAsync();
        var crearResp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/articulos", BuildArticuloBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var patchResp = await client.PatchAsJsonAsync(
            $"/api/v1/catalogos/articulos/{id}",
            new { Naturaleza = (int)Naturaleza.Critico });
        patchResp.EnsureSuccessStatusCode();

        var detalle = await client.GetAsync($"/api/v1/catalogos/articulos/{id}");
        var json = await ReadJsonAsync(detalle);
        Assert.Equal((int)Naturaleza.Critico, json.GetProperty("naturaleza").GetInt32());

        // Paridad post-unificación del DTO: un solo record ArticuloDetalle es
        // compartido por /catalogos y /datos-maestros (fix Bug A). El detalle de
        // /catalogos debe seguir exponiendo unidadMedidaId; el artículo se creó
        // con UnidadPzaId y el PATCH solo tocó naturaleza, así que sigue siendo PZA.
        Assert.Equal(UnidadPzaId, json.GetProperty("unidadMedidaId").GetGuid());
    }

    // --- DELETE /articulos/{id} ---

    [Fact]
    public async Task DesactivarArticulo_Cambia_Estatus_A_Inactivo()
    {
        var client = await CreateSuperAdminClientAsync();
        var crearResp = await client.PostAsJsonAsync(
            "/api/v1/catalogos/articulos", BuildArticuloBody());
        var id = (await ReadJsonAsync(crearResp)).GetProperty("id").GetGuid();

        var deleteResp = await client.DeleteAsync($"/api/v1/catalogos/articulos/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode);

        var detalle = await client.GetAsync($"/api/v1/catalogos/articulos/{id}");
        var json = await ReadJsonAsync(detalle);
        Assert.Equal((int)EstatusCatalogo.Inactivo, json.GetProperty("estatus").GetInt32());
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Crea un rol nuevo con exactamente los permisos indicados, un
    /// usuario auxiliar asignado a ese rol en la empresa bootstrap, y
    /// devuelve un cliente logueado como ese usuario (F1-ADM-05: probar
    /// "tiene A pero no B" sin tocar el rol super-admin, que trae todos
    /// los permisos canónicos).
    /// </summary>
    private async Task<HttpClient> CreateClientConPermisosAsync(params string[] codigosPermiso)
    {
        var admin = await CreateSuperAdminClientAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"perm-test-{sufijo}";
        var email = $"perm-test-{sufijo}@test.local";
        var nombre = $"Usuario Permiso Test {sufijo}";

        // Primer login: auto-provisiona el usuario (Usuario.EntraOid real,
        // no "pending:...") sin empresa ni permisos todavía. Un usuario
        // creado por el CRUD de admin nace en EstadoAcceso.PendientePrimerAcceso
        // con OID "pending:{email}", y Usuario.RegistrarAcceso rechaza login
        // con OID pendiente (422 USUARIO_OID_PENDIENTE) — por eso el
        // auto-provisión de fake-login, no el POST /usuarios.
        var client = _factory.CreateClientWithIdempotency();
        var primerLogin = await FakeLoginJsonAsync(client, oid, email, nombre);
        var usuarioId = primerLogin.GetProperty("usuario").GetProperty("id").GetGuid();

        var rolResp = await admin.PostAsJsonAsync("/api/v1/identidad/roles", new
        {
            Id = Guid.Empty,
            Codigo = $"rol-test-{sufijo}",
            Nombre = $"Rol Test {sufijo}",
            Descripcion = (string?)null,
        });
        rolResp.EnsureSuccessStatusCode();
        var rolId = (await ReadJsonAsync(rolResp)).GetProperty("id").GetGuid();

        var permisoIds = codigosPermiso
            .Select(codigo => PermisosCanonicos.Todos.First(p => p.Codigo == codigo).Id)
            .ToArray();
        var putPermisos = await admin.PutAsJsonAsync(
            $"/api/v1/identidad/roles/{rolId}/permisos", new { PermisoIds = permisoIds });
        putPermisos.EnsureSuccessStatusCode();

        var asignarResp = await admin.PostAsJsonAsync(
            $"/api/v1/identidad/usuarios/{usuarioId}/asignaciones",
            new { EmpresaId = EmpresaBootstrapId, RolId = rolId });
        asignarResp.EnsureSuccessStatusCode();

        // Segundo login: ahora la empresa se auto-selecciona (única
        // asignación) y los permisos recién asignados se cargan frescos
        // (el primer login no cacheó nada porque no había empresa).
        var token = await FakeLoginAsync(client, oid, email, nombre);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> FakeLoginJsonAsync(HttpClient client, string oid, string email, string nombre)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = email,
            Nombre = nombre,
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync(response);
    }

    // RFC único por llamada (F1-ADM-05): un valor fijo compartido entre
    // tests de esta clase (IClassFixture, misma BD) choca con la regla
    // nueva de unicidad y produce 409 en tests que no la ejercitan.
    private static ProveedorBody BuildProveedorBody(string? clave = null, string? rfc = null) => new(
        Clave: clave ?? $"PROV-B5-{Guid.NewGuid().ToString("N")[..8]}",
        RazonSocial: "Test Proveedor SA de CV",
        Rfc: rfc ?? $"TST{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
        TipoPersona: TipoPersonaProveedor.Moral,
        NombreComercial: null,
        CondicionesPagoDias: (short)30,
        MonedaPreferidaId: MonedaMxnId,
        Email: null,
        Telefono: null);

    // Seed de unidades (ADR-0046): PZA = Conteo base.
    private static readonly Guid UnidadPzaId =
        Guid.Parse("00000002-0007-0000-0000-000000000001");

    private static ArticuloBody BuildArticuloBody(string? clave = null) => new(
        Clave: clave ?? $"ART-B5-{Guid.NewGuid().ToString("N")[..8]}",
        Nombre: "Artículo de prueba",
        UnidadMedidaId: UnidadPzaId,
        Naturaleza: Naturaleza.Estandar,
        DescripcionLarga: null,
        Categoria: "Test",
        PrecioReferenciaMonto: null,
        PrecioReferenciaMoneda: null);

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
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private sealed record ProveedorBody(
        string Clave,
        string RazonSocial,
        string Rfc,
        TipoPersonaProveedor TipoPersona,
        string? NombreComercial,
        short? CondicionesPagoDias,
        Guid? MonedaPreferidaId,
        string? Email,
        string? Telefono);

    private sealed record ArticuloBody(
        string Clave,
        string Nombre,
        Guid UnidadMedidaId,
        Naturaleza Naturaleza,
        string? DescripcionLarga,
        string? Categoria,
        decimal? PrecioReferenciaMonto,
        string? PrecioReferenciaMoneda);

    /// <summary>
    /// G1.1: el alta nace «En revisión» y CxP lo valida con su expediente completo. Estas pruebas no tratan del
    /// expediente, así que aplican la misma transición de dominio (<see cref="Proveedor.Validar"/>) directo en la base.
    /// </summary>
    private async Task ValidarProveedorAsync(Guid proveedorId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
        var proveedor = await db.Proveedores.IgnoreQueryFilters().FirstAsync(p => p.Id == proveedorId);
        proveedor.Validar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
    }
}
