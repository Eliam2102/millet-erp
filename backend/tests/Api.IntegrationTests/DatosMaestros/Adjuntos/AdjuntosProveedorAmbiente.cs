using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Blob;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;

/// <summary>Reloj controlable. Arranca en la hora real; el login se hace antes de moverlo.</summary>
public sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Blob en memoria que registra subidas/eliminaciones y permite simular fallos.</summary>
public sealed class BlobGrabador : IBlobStoragePort
{
    public ConcurrentDictionary<string, byte[]> Blobs { get; } = new();
    public ConcurrentBag<string> Eliminaciones { get; } = [];
    private int _subidas;
    public int Subidas => _subidas;

    /// <summary>Si es true, escribe un blob parcial y lanza (fallo del storage a medias).</summary>
    public bool FallarAlSubir { get; set; }

    /// <summary>Se ejecuta tras subir el blob y antes de que el handler escriba en BD.</summary>
    public Func<Task>? DespuesDeSubir { get; set; }

    public async Task SubirAsync(string clave, Stream contenido, string contentType, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _subidas);
        using var ms = new MemoryStream();
        await contenido.CopyToAsync(ms, cancellationToken);
        if (FallarAlSubir)
        {
            Blobs[clave] = ms.ToArray()[..4];
            throw new IOException("Fallo simulado del storage.");
        }
        Blobs[clave] = ms.ToArray();
        if (DespuesDeSubir is not null) await DespuesDeSubir();
    }

    public Task<Stream> ObtenerStreamAsync(string clave, CancellationToken cancellationToken)
        => Blobs.TryGetValue(clave, out var b)
            ? Task.FromResult<Stream>(new MemoryStream(b))
            : throw new FileNotFoundException("Blob no encontrado.", clave);

    public Task EliminarAsync(string clave, CancellationToken cancellationToken)
    {
        Eliminaciones.Add(clave);
        Blobs.TryRemove(clave, out _);
        return Task.CompletedTask;
    }
}

/// <summary>Soporte común de las pruebas de adjuntos de proveedor (F1-ADM-11 G1.2).</summary>
public sealed class AdjuntosProveedorAmbiente
{
    public const string Base = "/api/v1/datos-maestros/proveedores";
    public static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    public static readonly Guid TipoConstancia = Guid.Parse("00000011-0001-0000-0000-000000000001");
    public static readonly Guid TipoContrato = Guid.Parse("00000011-0001-0000-0000-000000000002");
    public static readonly Guid TipoActa = Guid.Parse("00000011-0001-0000-0000-000000000003");
    public static readonly Guid TipoIdentificacion = Guid.Parse("00000011-0001-0000-0000-000000000004");
    public static readonly Guid TipoDomicilio = Guid.Parse("00000011-0001-0000-0000-000000000005");

    public static readonly byte[] Pdf = "%PDF-1.4\nContenido de prueba del expediente\n%%EOF"u8.ToArray();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public BlobGrabador Blob { get; } = new();
    public TestClock Reloj { get; } = new();
    public WebApplicationFactory<Program> Factory { get; }

    public AdjuntosProveedorAmbiente(
        WebApplicationFactory<Program> baseFactory, Action<IServiceCollection>? configurar = null, bool blobReal = false)
    {
        Factory = baseFactory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<IClock>(Reloj);
            if (!blobReal) s.AddSingleton<IBlobStoragePort>(Blob);
            configurar?.Invoke(s);
        }));
    }

    // --- Clientes ---

    public async Task<HttpClient> SuperAdminAsync()
    {
        var client = Factory.CreateClientWithIdempotency();
        await LoginAsync(client, "dev-superadmin", "superadmin@dev.local", "Super Admin Dev");
        return client;
    }

    /// <summary>Usuario con un rol que tiene EXACTAMENTE esos permisos.</summary>
    public async Task<(HttpClient Client, Guid UsuarioId)> ClienteConPermisosAsync(params string[] permisos)
    {
        var random = Guid.NewGuid().ToString("N")[..10];
        var oid = $"test-adj-prov-{random}";
        var usuarioId = Guid.CreateVersion7();

        using (var scope = Factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();

            var rolId = Guid.CreateVersion7();
            identidad.Roles.Add(new Rol(rolId, $"test-adj-prov-{random}", "Rol de prueba de adjuntos de proveedor"));
            foreach (var codigo in permisos)
            {
                var permisoId = await identidad.Permisos.AsNoTracking()
                    .Where(p => p.Codigo == codigo).Select(p => p.Id).SingleAsync();
                identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            }

            identidad.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Usuario Adjuntos Proveedor"));
            identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
                Guid.CreateVersion7(), usuarioId, EmpresaInicialId, rolId, asignadoPorUsuarioId: null));
            await identidad.SaveChangesAsync();
        }

        var client = Factory.CreateClientWithIdempotency();
        await LoginAsync(client, oid, $"{oid}@test.local", "Usuario Adjuntos Proveedor");
        return (client, usuarioId);
    }

    private static async Task LoginAsync(HttpClient client, string oid, string email, string nombre)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = nombre, EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var token = (await LeerAsync(response)).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    // --- Datos ---

    public async Task<Guid> SeedProveedorAsync(
        TipoPersonaProveedor tipo = TipoPersonaProveedor.Moral, EstatusCatalogo estatus = EstatusCatalogo.Activo)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var n = Guid.NewGuid().ToString("N");
        var rfc = tipo == TipoPersonaProveedor.Moral ? $"AD{n[..10]}".ToUpperInvariant() : $"AD{n[..11]}".ToUpperInvariant();
        var p = new Proveedor(Guid.CreateVersion7(), $"AP-{n[..10]}", $"Proveedor adjuntos {n[..6]}", rfc, tipo, estatus);
        db.Proveedores.Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    public async Task<int> ContarAdjuntosAsync(Guid proveedorId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        return await db.Adjuntos.IgnoreQueryFilters().CountAsync(a => a.EntidadId == proveedorId);
    }

    // --- Operaciones HTTP ---

    public static HttpRequestMessage PeticionSubida(
        Guid proveedorId, Guid tipoDocumentoId, byte[] contenido, string nombre, string contentType,
        string? vigenteHasta = null, string? idempotencyKey = null, string boundary = "AdjProvBoundary")
    {
        var form = new MultipartFormDataContent(boundary)
        {
            { new StringContent(tipoDocumentoId.ToString()), "tipoDocumentoId" },
        };
        if (vigenteHasta is not null) form.Add(new StringContent(vigenteHasta), "vigenteHasta");
        var archivo = new ByteArrayContent(contenido);
        archivo.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(archivo, "archivo", nombre);
        var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{proveedorId}/adjuntos") { Content = form };
        if (idempotencyKey is not null) req.Headers.Add("Idempotency-Key", idempotencyKey);
        return req;
    }

    public static Task<HttpResponseMessage> SubirAsync(
        HttpClient client, Guid proveedorId, Guid tipoDocumentoId, byte[]? contenido = null,
        string nombre = "documento.pdf", string contentType = "application/pdf", string? vigenteHasta = null)
        => client.SendAsync(PeticionSubida(proveedorId, tipoDocumentoId, contenido ?? Pdf, nombre, contentType, vigenteHasta));

    public static async Task<Guid> SubirOkAsync(
        HttpClient client, Guid proveedorId, Guid tipoDocumentoId, string? vigenteHasta = null)
    {
        var r = await SubirAsync(client, proveedorId, tipoDocumentoId, vigenteHasta: vigenteHasta);
        Assert.Equal(System.Net.HttpStatusCode.Created, r.StatusCode);
        return (await LeerAsync(r)).GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> LeerAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    public static async Task<string?> CodigoAsync(HttpResponseMessage response)
        => (await LeerAsync(response)).GetProperty("code").GetString();

    public static async Task<EstadoAdjunto> EstadoAsync(HttpClient client, Guid proveedorId, Guid adjuntoId)
    {
        var r = await client.GetAsync($"{Base}/{proveedorId}/adjuntos/{adjuntoId}");
        r.EnsureSuccessStatusCode();
        return (await LeerAsync(r)).GetProperty("estado").Deserialize<EstadoAdjunto>(Json);
    }
}
