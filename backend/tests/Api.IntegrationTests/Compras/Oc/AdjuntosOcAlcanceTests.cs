using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras.Oc;

/// <summary>
/// Pruebas de integración para el alcance por sucursal (ADR-0051) de adjuntos de órdenes de compra (F1-ADM-11).
/// Comprueba que la autorización se derive del documento padre (OrdenCompra.SucursalDestinoId).
/// </summary>
public class AdjuntosOcAlcanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/compras/ordenes";
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid ProveedorActivoId = Guid.Parse("00000005-0001-0000-0000-000000000001");
    private static readonly Guid TipoDocumentoCotizacionId = Guid.Parse("00000003-0003-0010-0000-000000000001");

    private static readonly byte[] ContenidoPdfOriginal = "%PDF-1.4 Millet ERP Test Adjunto"u8.ToArray();

    private readonly WebApplicationFactory<Program> _factory;

    public AdjuntosOcAlcanceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Operativo_Asociado_A_Sucursal_A_Sube_Lista_Ve_Y_Descarga_Adjunto_De_OC_De_A()
    {
        var (sucursalA, _) = await CrearSucursalesAsync();
        var (clienteA, _) = await CreateOperativoAsync(
            sucursalA,
            PermisosCanonicos.ComprasOrdenesLeer,
            PermisosCanonicos.ComprasOrdenesAdjuntar);

        var admin = await CreateSuperAdminClientAsync();
        var ocAId = await CrearOrdenCompraAsync(admin, sucursalA, "SCA");

        // Subir adjunto como operativo de A
        var adjuntoId = await SubirAdjuntoAsync(
            clienteA, ocAId, ContenidoPdfOriginal, "application/pdf", "cotizacion_a.pdf");

        // Listar / Detalle de la OC como operativo de A
        var detalleResp = await clienteA.GetAsync($"{EndpointBase}/{ocAId}");
        Assert.Equal(HttpStatusCode.OK, detalleResp.StatusCode);
        var detalleJson = await ReadJsonAsync(detalleResp);
        var adjuntos = detalleJson.GetProperty("adjuntos").EnumerateArray().ToList();
        Assert.Contains(adjuntos, a => a.GetProperty("id").GetGuid() == adjuntoId);

        // Ver / Descargar contenido como operativo de A
        var contenidoResp = await clienteA.GetAsync($"{EndpointBase}/{ocAId}/adjuntos/{adjuntoId}/contenido");
        Assert.Equal(HttpStatusCode.OK, contenidoResp.StatusCode);
        Assert.Equal("application/pdf", contenidoResp.Content.Headers.ContentType?.MediaType);
        var bytes = await contenidoResp.Content.ReadAsByteArrayAsync();
        Assert.Equal(ContenidoPdfOriginal, bytes);
    }

    [Fact]
    public async Task Operativo_No_Asociado_A_Sucursal_B_Recibe_403_En_Detalle_Post_Contenido_Y_Delete()
    {
        var (sucursalA, sucursalB) = await CrearSucursalesAsync();
        var (clienteA, _) = await CreateOperativoAsync(
            sucursalA,
            PermisosCanonicos.ComprasOrdenesLeer,
            PermisosCanonicos.ComprasOrdenesAdjuntar,
            PermisosCanonicos.ComprasOrdenesCrear);

        var admin = await CreateSuperAdminClientAsync();
        var ocBId = await CrearOrdenCompraAsync(admin, sucursalB, "SCB");
        var adjuntoBId = await SubirAdjuntoAsync(
            admin, ocBId, ContenidoPdfOriginal, "application/pdf", "cotizacion_b.pdf");

        var conteoAdjuntosAntes = await ContarAdjuntosOcAsync(ocBId);

        // (1) GET detalle sobre OC de B
        var respDetalle = await clienteA.GetAsync($"{EndpointBase}/{ocBId}");
        await AssertForbiddenSucursalAsync(respDetalle);

        // (2) POST adjuntos sobre OC de B
        using var form = new MultipartFormDataContent
        {
            { new StringContent(TipoDocumentoCotizacionId.ToString()), "tipoDocumentoId" },
            { new ByteArrayContent(ContenidoPdfOriginal), "archivo", "intruso.pdf" }
        };
        var respPost = await clienteA.PostAsync($"{EndpointBase}/{ocBId}/adjuntos", form);
        await AssertForbiddenSucursalAsync(respPost);

        // (3) GET contenido sobre adjunto de OC de B
        var respContenido = await clienteA.GetAsync($"{EndpointBase}/{ocBId}/adjuntos/{adjuntoBId}/contenido");
        await AssertForbiddenSucursalAsync(respContenido);

        // (4) DELETE adjuntos sobre OC de B
        var respDelete = await clienteA.DeleteAsync($"{EndpointBase}/{ocBId}/adjuntos/{adjuntoBId}");
        await AssertForbiddenSucursalAsync(respDelete);

        // 0 filas nuevas en BD
        var conteoAdjuntosDespues = await ContarAdjuntosOcAsync(ocBId);
        Assert.Equal(conteoAdjuntosAntes, conteoAdjuntosDespues);
    }

    [Fact]
    public async Task Corporativo_Con_Permiso_Bypass_Opera_OC_De_Cualquier_Sucursal_Sin_Asociacion()
    {
        var (_, sucursalB) = await CrearSucursalesAsync();
        // Usuario sin asociación a sucursales, pero con permiso de bypass compras.ordenes.leer-todas-sucursales
        var (clienteCorp, _) = await CreateUsuarioConPermisosAsync(
            PermisosCanonicos.ComprasOrdenesLeer,
            PermisosCanonicos.ComprasOrdenesAdjuntar,
            PermisosCanonicos.ComprasOrdenesLeerTodasSucursales);

        var admin = await CreateSuperAdminClientAsync();
        var ocBId = await CrearOrdenCompraAsync(admin, sucursalB, "SCB");

        // Subida exitosa
        var adjuntoId = await SubirAdjuntoAsync(
            clienteCorp, ocBId, ContenidoPdfOriginal, "application/pdf", "corp.pdf");

        // Detalle exitoso
        var detalleResp = await clienteCorp.GetAsync($"{EndpointBase}/{ocBId}");
        Assert.Equal(HttpStatusCode.OK, detalleResp.StatusCode);

        // Contenido exitoso
        var contenidoResp = await clienteCorp.GetAsync($"{EndpointBase}/{ocBId}/adjuntos/{adjuntoId}/contenido");
        Assert.Equal(HttpStatusCode.OK, contenidoResp.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_Tiene_Acceso_Total()
    {
        var (_, sucursalB) = await CrearSucursalesAsync();
        var admin = await CreateSuperAdminClientAsync();
        var ocBId = await CrearOrdenCompraAsync(admin, sucursalB, "SCB");

        var adjuntoId = await SubirAdjuntoAsync(
            admin, ocBId, ContenidoPdfOriginal, "application/pdf", "admin.pdf");

        var respDetalle = await admin.GetAsync($"{EndpointBase}/{ocBId}");
        Assert.Equal(HttpStatusCode.OK, respDetalle.StatusCode);

        var respContenido = await admin.GetAsync($"{EndpointBase}/{ocBId}/adjuntos/{adjuntoId}/contenido");
        Assert.Equal(HttpStatusCode.OK, respContenido.StatusCode);
    }

    [Fact]
    public async Task Adjunto_De_OC_B_Consultado_Con_Oc_A_Retorna_404_OC_ADJUNTO_NO_ENCONTRADO()
    {
        var (sucursalA, sucursalB) = await CrearSucursalesAsync();
        var (clienteA, _) = await CreateOperativoAsync(
            sucursalA,
            PermisosCanonicos.ComprasOrdenesLeer,
            PermisosCanonicos.ComprasOrdenesAdjuntar);

        var admin = await CreateSuperAdminClientAsync();
        var ocAId = await CrearOrdenCompraAsync(admin, sucursalA, "SCA");
        var ocBId = await CrearOrdenCompraAsync(admin, sucursalB, "SCB");
        var adjuntoBId = await SubirAdjuntoAsync(
            admin, ocBId, ContenidoPdfOriginal, "application/pdf", "b.pdf");

        // El usuario tiene acceso a ocAId, pero adjuntoBId pertenece a ocBId
        var response = await clienteA.GetAsync($"{EndpointBase}/{ocAId}/adjuntos/{adjuntoBId}/contenido");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var json = await ReadJsonAsync(response);
        Assert.Equal("OC_ADJUNTO_NO_ENCONTRADO", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task OcId_Inexistente_Retorna_404_ORDEN_COMPRA_NO_ENCONTRADA()
    {
        var (clienteCorp, _) = await CreateUsuarioConPermisosAsync(
            PermisosCanonicos.ComprasOrdenesLeer,
            PermisosCanonicos.ComprasOrdenesLeerTodasSucursales);

        var idInexistente = Guid.NewGuid();

        var respDetalle = await clienteCorp.GetAsync($"{EndpointBase}/{idInexistente}");
        Assert.Equal(HttpStatusCode.NotFound, respDetalle.StatusCode);
        var jsonDetalle = await ReadJsonAsync(respDetalle);
        Assert.Equal("ORDEN_COMPRA_NO_ENCONTRADA", jsonDetalle.GetProperty("code").GetString());

        var respContenido = await clienteCorp.GetAsync(
            $"{EndpointBase}/{idInexistente}/adjuntos/{Guid.NewGuid()}/contenido");
        Assert.Equal(HttpStatusCode.NotFound, respContenido.StatusCode);
        var jsonContenido = await ReadJsonAsync(respContenido);
        Assert.Equal("ORDEN_COMPRA_NO_ENCONTRADA", jsonContenido.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Sin_Permiso_De_Rol_Retorna_403()
    {
        var (sucursalA, _) = await CrearSucursalesAsync();
        var admin = await CreateSuperAdminClientAsync();
        var ocAId = await CrearOrdenCompraAsync(admin, sucursalA, "SCA");

        // Usuario con solo sucursal pero sin permisos de Compras
        var (clienteSinPermisos, _) = await CreateOperativoAsync(sucursalA);

        var respGet = await clienteSinPermisos.GetAsync($"{EndpointBase}/{ocAId}");
        Assert.Equal(HttpStatusCode.Forbidden, respGet.StatusCode);

        using var form = new MultipartFormDataContent
        {
            { new StringContent(TipoDocumentoCotizacionId.ToString()), "tipoDocumentoId" },
            { new ByteArrayContent(ContenidoPdfOriginal), "archivo", "doc.pdf" }
        };
        var respPost = await clienteSinPermisos.PostAsync($"{EndpointBase}/{ocAId}/adjuntos", form);
        Assert.Equal(HttpStatusCode.Forbidden, respPost.StatusCode);
    }

    [Fact]
    public async Task Adjunto_Persiste_Al_Reabrir_La_Orden()
    {
        var (sucursalA, _) = await CrearSucursalesAsync();
        var (clienteA, _) = await CreateOperativoAsync(
            sucursalA,
            PermisosCanonicos.ComprasOrdenesLeer,
            PermisosCanonicos.ComprasOrdenesAdjuntar);

        var admin = await CreateSuperAdminClientAsync();
        var ocAId = await CrearOrdenCompraAsync(admin, sucursalA, "SCA");

        // 1. Subir adjunto
        var adjuntoId = await SubirAdjuntoAsync(
            clienteA, ocAId, ContenidoPdfOriginal, "application/pdf", "reabrir_test.pdf");

        // 2. Nuevo cliente HTTP autenticado (simula reabrir sesión / nueva petición limpia)
        var (nuevoCliente, _) = await CreateOperativoAsync(
            sucursalA,
            PermisosCanonicos.ComprasOrdenesLeer);

        var detalleResp = await nuevoCliente.GetAsync($"{EndpointBase}/{ocAId}");
        Assert.Equal(HttpStatusCode.OK, detalleResp.StatusCode);
        var detalleJson = await ReadJsonAsync(detalleResp);
        var adjuntos = detalleJson.GetProperty("adjuntos").EnumerateArray().ToList();
        Assert.Contains(adjuntos, a => a.GetProperty("id").GetGuid() == adjuntoId);

        var contenidoResp = await nuevoCliente.GetAsync($"{EndpointBase}/{ocAId}/adjuntos/{adjuntoId}/contenido");
        Assert.Equal(HttpStatusCode.OK, contenidoResp.StatusCode);
        var bytes = await contenidoResp.Content.ReadAsByteArrayAsync();
        Assert.Equal(ContenidoPdfOriginal, bytes);
    }

    // --- Helpers ---

    private async Task<(HttpClient Client, Guid UsuarioId)> CreateOperativoAsync(
        Guid sucursalId, params string[] permisoCodigos)
    {
        var (client, usuarioId) = await CreateUsuarioConPermisosAsync(permisoCodigos);
        using var scope = _factory.Services.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        identidad.UsuarioSucursales.Add(new UsuarioSucursal(
            Guid.CreateVersion7(), usuarioId, sucursalId, EmpresaInicialId));
        await identidad.SaveChangesAsync();
        return (client, usuarioId);
    }

    private async Task<(HttpClient Client, Guid UsuarioId)> CreateUsuarioConPermisosAsync(
        params string[] permisoCodigos)
    {
        var random = Guid.NewGuid().ToString("N")[..10];
        var oid = $"test-oc-scope-{random}";
        var usuarioId = Guid.CreateVersion7();

        using (var scope = _factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();

            var rolId = Guid.CreateVersion7();
            identidad.Roles.Add(new Rol(rolId, $"test-oc-scope-{random}", "Rol de prueba de alcance OC"));
            foreach (var codigo in permisoCodigos)
            {
                var permisoId = await identidad.Permisos.AsNoTracking()
                    .Where(p => p.Codigo == codigo).Select(p => p.Id).SingleAsync();
                identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            }

            identidad.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Usuario Alcance OC"));
            identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
                Guid.CreateVersion7(), usuarioId, EmpresaInicialId, rolId, asignadoPorUsuarioId: null));
            await identidad.SaveChangesAsync();
        }

        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, oid, $"{oid}@test.local", "Usuario Alcance OC");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, usuarioId);
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(Guid SucursalA, Guid SucursalB)> CrearSucursalesAsync()
    {
        var admin = await CreateSuperAdminClientAsync();
        return (await CrearSucursalAsync(admin, "SCA"), await CrearSucursalAsync(admin, "SCB"));
    }

    private static async Task<Guid> CrearSucursalAsync(HttpClient client, string prefix)
    {
        var clave = $"{prefix}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var response = await client.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Sucursal {clave}",
        });
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CrearOrdenCompraAsync(HttpClient client, Guid sucursalId, string sucursalCodigo)
    {
        var body = new CrearOrdenCompraVaciaBody(
            SucursalDestinoId: sucursalId,
            SucursalCodigo: sucursalCodigo,
            FolioAnio: 2026,
            ProveedorId: ProveedorActivoId,
            CondicionesPagoId: Guid.CreateVersion7(),
            UsoPrincipalId: Guid.CreateVersion7(),
            FechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            Moneda: "MXN",
            TipoCambio: null,
            SinRequisicionPrevia: false,
            EsImportacion: false,
            CotizacionExcepcionada: false,
            Observaciones: "OC test alcance",
            MotivoSinRequisicion: null,
            FechaEntregaEsperada: null,
            EncargadoComprasId: null,
            OcOrigenId: null);

        var response = await client.PostAsJsonAsync(EndpointBase, body);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Error al crear OC ({response.StatusCode}): {err}");
        }
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> SubirAdjuntoAsync(
        HttpClient client,
        Guid ocId,
        byte[] contenido,
        string contentType,
        string nombreArchivo)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(TipoDocumentoCotizacionId.ToString()), "tipoDocumentoId" },
        };
        var fileContent = new ByteArrayContent(contenido);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "archivo", nombreArchivo);

        var response = await client.PostAsync($"{EndpointBase}/{ocId}/adjuntos", form);
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        return json.GetProperty("adjuntoId").GetGuid();
    }

    private async Task<int> ContarAdjuntosOcAsync(Guid ocId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        return await db.AdjuntosOc.AsNoTracking().CountAsync(a => a.OrdenCompraId == ocId);
    }

    private static async Task AssertForbiddenSucursalAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("SUCURSAL_NO_ASOCIADA", json.GetProperty("code").GetString());
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
        return (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private sealed record CrearOrdenCompraVaciaBody(
        Guid SucursalDestinoId,
        string SucursalCodigo,
        short FolioAnio,
        Guid ProveedorId,
        Guid CondicionesPagoId,
        Guid UsoPrincipalId,
        DateOnly FechaDocumento,
        string Moneda,
        decimal? TipoCambio,
        bool SinRequisicionPrevia,
        bool EsImportacion,
        bool CotizacionExcepcionada,
        string? Observaciones,
        string? MotivoSinRequisicion,
        DateOnly? FechaEntregaEsperada,
        Guid? EncargadoComprasId,
        Guid? OcOrigenId);
}
