using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;

namespace Millet.Api.IntegrationTests.Compras.Oc;

/// <summary>
/// Pruebas de integración para la subida atómica de adjuntos de OC (F1-ADM-11, D5).
/// Verifica que las validaciones de acceso y archivo ocurran antes de escribir el blob físico,
/// y que cualquier fallo en la persistencia compense eliminando el blob huérfano.
/// </summary>
public class AdjuntosOcSubidaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/compras/ordenes";
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid ProveedorActivoId = Guid.Parse("00000005-0001-0000-0000-000000000001");
    private static readonly Guid TipoDocumentoCotizacionId = Guid.Parse("00000003-0003-0010-0000-000000000001");

    private static readonly byte[] CabeceraPdfValida = "%PDF-1.4 Contenido de prueba subida atomica"u8.ToArray();
    private static readonly byte[] CabeceraExeMz = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];

    private readonly WebApplicationFactory<Program> _factory;

    public AdjuntosOcSubidaTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Doble de prueba para IAlmacenarBlobPort que registra todas las invocaciones a SubirAsync y EliminarAsync.
    /// </summary>
    public sealed class GrabadorBlobPort : IAlmacenarBlobPort
    {
        public List<(Guid BlobId, string Url, string ContentType, string NombreArchivo)> Subidas { get; } = [];
        public List<string> Eliminaciones { get; } = [];

        public Task<string> SubirAsync(
            Guid blobId,
            Stream contenido,
            string contentType,
            string nombreArchivoOriginal,
            CancellationToken cancellationToken)
        {
            var url = $"memory://blobs/{blobId:D}_{nombreArchivoOriginal}";
            Subidas.Add((blobId, url, contentType, nombreArchivoOriginal));
            return Task.FromResult(url);
        }

        public Task<Stream> ObtenerStreamAsync(string blobUrl, CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new MemoryStream(CabeceraPdfValida));
        }

        public Task EliminarAsync(string blobUrl, CancellationToken cancellationToken)
        {
            Eliminaciones.Add(blobUrl);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Formato_O_Mime_No_Permitido_Retorna_422_Sin_Subir_Blob_Y_Cero_Filas()
    {
        var grabador = new GrabadorBlobPort();
        await using var customFactory = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IAlmacenarBlobPort>(grabador);
            });
        });

        var sucursalId = await CrearSucursalAsync(customFactory);
        var admin = await CreateSuperAdminClientAsync(customFactory);
        var ocId = await CrearOrdenCompraAsync(admin, sucursalId, "SCA");

        // Intentar subir un .exe con MIME no permitido
        using var form = new MultipartFormDataContent
        {
            { new StringContent(TipoDocumentoCotizacionId.ToString()), "tipoDocumentoId" },
        };
        var fileContent = new ByteArrayContent(CabeceraExeMz);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "archivo", "malicioso.exe");

        var response = await admin.PostAsync($"{EndpointBase}/{ocId}/adjuntos", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var json = await ReadJsonAsync(response);
        Assert.Equal("ADJUNTO_FORMATO_NO_PERMITIDO", json.GetProperty("code").GetString());

        // Afirmar que el doble no recibió llamadas a Subir
        Assert.Empty(grabador.Subidas);

        // Afirmar 0 filas en BD
        Assert.Equal(0, await ContarAdjuntosOcAsync(customFactory, ocId));
    }

    [Fact]
    public async Task Archivo_Mayor_A_MaxBytes_Retorna_422_Sin_Subir_Blob_Y_Cero_Filas()
    {
        var grabador = new GrabadorBlobPort();
        await using var customFactory = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IAlmacenarBlobPort>(grabador);
                services.Configure<AdjuntosPoliticaOptions>(opts =>
                {
                    // Límite artificialmente bajo para probar rechazo sin enviar 20 MB reales
                    opts.MaxBytes = 20;
                });
            });
        });

        var sucursalId = await CrearSucursalAsync(customFactory);
        var admin = await CreateSuperAdminClientAsync(customFactory);
        var ocId = await CrearOrdenCompraAsync(admin, sucursalId, "SCA");

        // CabeceraPdfValida tiene > 20 bytes
        using var form = new MultipartFormDataContent
        {
            { new StringContent(TipoDocumentoCotizacionId.ToString()), "tipoDocumentoId" },
        };
        var fileContent = new ByteArrayContent(CabeceraPdfValida);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "archivo", "grande.pdf");

        var response = await admin.PostAsync($"{EndpointBase}/{ocId}/adjuntos", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var json = await ReadJsonAsync(response);
        Assert.Equal("ADJUNTO_TAMANO_EXCEDIDO", json.GetProperty("code").GetString());

        // Doble sin llamadas a Subir
        Assert.Empty(grabador.Subidas);
        Assert.Equal(0, await ContarAdjuntosOcAsync(customFactory, ocId));
    }

    [Fact]
    public async Task TipoDocumento_Inexistente_Compensa_Eliminando_Blob_Y_Deja_Cero_Filas()
    {
        var grabador = new GrabadorBlobPort();
        await using var customFactory = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IAlmacenarBlobPort>(grabador);
            });
        });

        var sucursalId = await CrearSucursalAsync(customFactory);
        var admin = await CreateSuperAdminClientAsync(customFactory);
        var ocId = await CrearOrdenCompraAsync(admin, sucursalId, "SCA");

        var tipoInexistenteId = Guid.NewGuid();

        using var form = new MultipartFormDataContent
        {
            { new StringContent(tipoInexistenteId.ToString()), "tipoDocumentoId" },
        };
        var fileContent = new ByteArrayContent(CabeceraPdfValida);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "archivo", "prueba.pdf");

        var response = await admin.PostAsync($"{EndpointBase}/{ocId}/adjuntos", form);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var json = await ReadJsonAsync(response);
        Assert.Equal("TIPO_DOCUMENTO_NO_ENCONTRADO", json.GetProperty("code").GetString());

        // El blob se subió pero inmediatamente se compensó con EliminarAsync
        Assert.Single(grabador.Subidas);
        var urlSubida = grabador.Subidas[0].Url;
        Assert.Contains(urlSubida, grabador.Eliminaciones);

        // 0 filas en BD
        Assert.Equal(0, await ContarAdjuntosOcAsync(customFactory, ocId));
    }

    [Fact]
    public async Task Oc_En_Estado_Terminal_Compensa_Eliminando_Blob()
    {
        var grabador = new GrabadorBlobPort();
        await using var customFactory = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IAlmacenarBlobPort>(grabador);
            });
        });

        var sucursalId = await CrearSucursalAsync(customFactory);
        var admin = await CreateSuperAdminClientAsync(customFactory);
        var ocId = await CrearOrdenCompraAsync(admin, sucursalId, "SCA");

        // Poner la OC en estado Cancelada (terminal)
        using (var scope = customFactory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == ocId);
            db.Entry(oc).Property<EstadoOrdenCompra>("Estado").CurrentValue = EstadoOrdenCompra.Cancelada;
            await db.SaveChangesAsync();
        }

        using var form = new MultipartFormDataContent
        {
            { new StringContent(TipoDocumentoCotizacionId.ToString()), "tipoDocumentoId" },
        };
        var fileContent = new ByteArrayContent(CabeceraPdfValida);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(fileContent, "archivo", "terminal.pdf");

        var response = await admin.PostAsync($"{EndpointBase}/{ocId}/adjuntos", form);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var json = await ReadJsonAsync(response);
        Assert.Equal("OC_ADJUNTO_NO_EN_TERMINAL", json.GetProperty("code").GetString());

        // Se subió el blob y se compensó eliminándolo
        Assert.Single(grabador.Subidas);
        Assert.Contains(grabador.Subidas[0].Url, grabador.Eliminaciones);

        Assert.Equal(0, await ContarAdjuntosOcAsync(customFactory, ocId));
    }

    [Fact]
    public async Task Reintento_Tras_Fallo_Crea_Exactamente_Una_Fila_Y_Un_Blob_Vivo()
    {
        var grabador = new GrabadorBlobPort();
        await using var customFactory = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IAlmacenarBlobPort>(grabador);
            });
        });

        var sucursalId = await CrearSucursalAsync(customFactory);
        var admin = await CreateSuperAdminClientAsync(customFactory);
        var ocId = await CrearOrdenCompraAsync(admin, sucursalId, "SCA");

        // 1. Intento fallido con tipoDocumento inexistente
        var fileContent1 = new ByteArrayContent(CabeceraPdfValida);
        fileContent1.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var formFallido = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString()), "tipoDocumentoId" },
            { fileContent1, "archivo", "intento1.pdf" }
        };
        var respFallida = await admin.PostAsync($"{EndpointBase}/{ocId}/adjuntos", formFallido);
        Assert.Equal(HttpStatusCode.NotFound, respFallida.StatusCode);

        // 2. Reintento limpio con tipo válido
        var fileContent2 = new ByteArrayContent(CabeceraPdfValida);
        fileContent2.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var formExitoso = new MultipartFormDataContent
        {
            { new StringContent(TipoDocumentoCotizacionId.ToString()), "tipoDocumentoId" },
            { fileContent2, "archivo", "intento2.pdf" }
        };
        var respExitosa = await admin.PostAsync($"{EndpointBase}/{ocId}/adjuntos", formExitoso);
        Assert.Equal(HttpStatusCode.Created, respExitosa.StatusCode);

        // Afirmar: 2 subidas en total, pero la primera fue eliminada
        Assert.Equal(2, grabador.Subidas.Count);
        Assert.Single(grabador.Eliminaciones);
        Assert.Equal(grabador.Subidas[0].Url, grabador.Eliminaciones[0]);

        // Exactamente 1 fila en BD
        Assert.Equal(1, await ContarAdjuntosOcAsync(customFactory, ocId));
    }

    [Fact]
    public async Task Mismo_IdempotencyKey_Reenviado_No_Duplica()
    {
        var grabador = new GrabadorBlobPort();
        await using var customFactory = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureServices(services =>
            {
                services.AddSingleton<IAlmacenarBlobPort>(grabador);
            });
        });

        var sucursalId = await CrearSucursalAsync(customFactory);
        var admin = await CreateSuperAdminClientAsync(customFactory);
        var ocId = await CrearOrdenCompraAsync(admin, sucursalId, "SCA");

        var idempotencyKey = Guid.NewGuid().ToString("D");
        const string boundary = "IdempotentBoundary12345";

        // Petición 1
        var fileContent1 = new ByteArrayContent(CabeceraPdfValida);
        fileContent1.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form1 = new MultipartFormDataContent(boundary)
        {
            { new StringContent(TipoDocumentoCotizacionId.ToString()), "tipoDocumentoId" },
            { fileContent1, "archivo", "idempotente.pdf" }
        };
        var req1 = new HttpRequestMessage(HttpMethod.Post, $"{EndpointBase}/{ocId}/adjuntos")
        {
            Content = form1,
        };
        req1.Headers.Add("Idempotency-Key", idempotencyKey);

        var resp1 = await admin.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
        var json1 = await ReadJsonAsync(resp1);
        var adjuntoId1 = json1.GetProperty("adjuntoId").GetGuid();

        // Petición 2 con misma Idempotency-Key y mismo boundary/contenido
        var fileContent2 = new ByteArrayContent(CabeceraPdfValida);
        fileContent2.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        var form2 = new MultipartFormDataContent(boundary)
        {
            { new StringContent(TipoDocumentoCotizacionId.ToString()), "tipoDocumentoId" },
            { fileContent2, "archivo", "idempotente.pdf" }
        };
        var req2 = new HttpRequestMessage(HttpMethod.Post, $"{EndpointBase}/{ocId}/adjuntos")
        {
            Content = form2,
        };
        req2.Headers.Add("Idempotency-Key", idempotencyKey);

        var resp2 = await admin.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);
        var json2 = await ReadJsonAsync(resp2);
        var adjuntoId2 = json2.GetProperty("adjuntoId").GetGuid();

        // Mismo id devuelto por el middleware de idempotencia
        Assert.Equal(adjuntoId1, adjuntoId2);

        // No se duplicó en BD: exactamente 1 fila
        Assert.Equal(1, await ContarAdjuntosOcAsync(customFactory, ocId));
    }

    // --- Helpers ---

    private static async Task<HttpClient> CreateSuperAdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CrearSucursalAsync(WebApplicationFactory<Program> factory)
    {
        var admin = await CreateSuperAdminClientAsync(factory);
        var clave = $"SCA-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var response = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new
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
            Observaciones: "OC test subida atomica",
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

    private static async Task<int> ContarAdjuntosOcAsync(WebApplicationFactory<Program> factory, Guid ocId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        return await db.AdjuntosOc.IgnoreQueryFilters().CountAsync(a => a.OrdenCompraId == ocId);
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
