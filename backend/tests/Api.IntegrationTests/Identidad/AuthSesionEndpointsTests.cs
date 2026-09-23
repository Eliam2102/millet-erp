using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Api.Auth;
using Millet.Api.Auth.Models;
using Xunit;

namespace Millet.Api.IntegrationTests.Identidad;

/// <summary>
/// Tests de integración para el endpoint de intercambio de sesión:
/// <c>POST /api/auth/sesion</c> (Punto de entrada de autenticación).
///
/// Valida:
/// <list type="bullet">
///   <item>Exchange exitoso con token Entra ID válido para usuario existente (dev-superadmin).</item>
///   <item>Flujo de auto-provisión cuando el Entra OID no existe en la base de datos.</item>
///   <item>Respuesta 401 Unauthorized ante tokens inválidos o expirados.</item>
///   <item>Respuesta 401 Unauthorized ante tokens vacíos o nulos.</item>
/// </list>
/// </summary>
public class AuthSesionEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Endpoint = "/api/auth/sesion";
    private const string SuperAdminOid = "dev-superadmin";

    private readonly WebApplicationFactory<Program> _factory;

    public AuthSesionEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostSesion_Con_Token_Valido_SuperAdmin_Retorna_200_Y_Jwt_Millet()
    {
        var superAdminOid = GetConfiguredSuperAdminOid();

        // 1. Arrange: Sustituir validador real de Microsoft Entra por Fake de pruebas
        var fakeValidator = new FakeEntraTokenValidator(token =>
        {
            if (token == "token-entra-superadmin-valido")
            {
                return new EntraTokenClaims(
                    Oid: superAdminOid,
                    Email: "superadmin@dev.local",
                    Name: "Super Admin Dev");
            }
            throw new UnauthorizedAccessException("Token no reconocido");
        });

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEntraTokenValidator>();
                services.AddScoped<IEntraTokenValidator>(_ => fakeValidator);
            });
        }).CreateClient();

        var request = new LoginRequest(
            EntraToken: "token-entra-superadmin-valido",
            EmpresaId: null);

        // 2. Act
        var response = await client.PostAsJsonAsync(Endpoint, request);
        var err = await response.Content.ReadAsStringAsync();

        // 3. Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, err);

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        loginResponse.Should().NotBeNull();
        loginResponse!.AccessToken.Should().NotBeNullOrWhiteSpace();
        loginResponse.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        loginResponse.Usuario.Should().NotBeNull();
        loginResponse.Usuario.Email.Should().NotBeNullOrWhiteSpace();


        // El superadmin del bootstrap tiene empresa asignada y permisos cargados
        loginResponse.Empresas.Should().NotBeEmpty();
        loginResponse.Empresas.Should().Contain(e => e.EsLaActual);
        loginResponse.Permisos.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PostSesion_Con_Usuario_Nuevo_Realiza_AutoProvision_Y_Retorna_200()
    {
        // 1. Arrange: Usuario con OID aleatorio que no existe en BD
        var nuevoOid = $"entra-user-{Guid.NewGuid():N}";
        var nuevoEmail = $"usuario-{Guid.NewGuid():N}@millet.test";
        var nuevoNombre = "Usuario Recién Incorporado";

        var fakeValidator = new FakeEntraTokenValidator(token =>
        {
            if (token == "token-usuario-nuevo")
            {
                return new EntraTokenClaims(
                    Oid: nuevoOid,
                    Email: nuevoEmail,
                    Name: nuevoNombre);
            }
            throw new UnauthorizedAccessException("Token desconocido");
        });

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEntraTokenValidator>();
                services.AddScoped<IEntraTokenValidator>(_ => fakeValidator);
            });
        }).CreateClient();

        var request = new LoginRequest(
            EntraToken: "token-usuario-nuevo",
            EmpresaId: null);

        try
        {
            // 2. Act
            var response = await client.PostAsJsonAsync(Endpoint, request);

            // 3. Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
            loginResponse.Should().NotBeNull();
            loginResponse!.AccessToken.Should().NotBeNullOrWhiteSpace();
            loginResponse.Usuario.Email.Should().Be(nuevoEmail);
            loginResponse.Usuario.Nombre.Should().Be(nuevoNombre);

            // Usuario auto-provisionado no tiene roles ni empresas aún asignados
            loginResponse.Empresas.Should().BeEmpty();
            loginResponse.Permisos.Should().BeEmpty();
        }
        finally
        {
            await CleanupUsuarioAsync(nuevoOid);
        }
    }

    [Fact]
    public async Task PostSesion_Con_Token_Invalido_Retorna_401()
    {
        // 1. Arrange: Validador configurado para fallar con UnauthorizedAccessException
        var fakeValidator = new FakeEntraTokenValidator(_ =>
            throw new UnauthorizedAccessException("Firma de token Microsoft Entra inválida."));

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEntraTokenValidator>();
                services.AddScoped<IEntraTokenValidator>(_ => fakeValidator);
            });
        }).CreateClient();

        var request = new LoginRequest(
            EntraToken: "token-falso-o-expirado",
            EmpresaId: null);

        // 2. Act
        var response = await client.PostAsJsonAsync(Endpoint, request);

        // 3. Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostSesion_Con_Token_Vacio_Retorna_401()
    {
        // 1. Arrange: Validador que rechaza token vacío
        var fakeValidator = new FakeEntraTokenValidator(token =>
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new UnauthorizedAccessException("Token de Entra vacío.");
            }
            return new EntraTokenClaims("oid", "email", "name");
        });

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEntraTokenValidator>();
                services.AddScoped<IEntraTokenValidator>(_ => fakeValidator);
            });
        }).CreateClient();

        var request = new LoginRequest(
            EntraToken: string.Empty,
            EmpresaId: null);

        // 2. Act
        var response = await client.PostAsJsonAsync(Endpoint, request);

        // 3. Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostSesion_Con_Usuario_Inactivo_Retorna_403_Forbidden()
    {
        // 1. Arrange: Crear usuario inactivo en la base de datos
        var inactiveOid = $"entra-user-inactive-{Guid.NewGuid():N}";
        var inactiveEmail = $"inactivo-{Guid.NewGuid():N}@millet.test";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Millet.Identidad.Infrastructure.IdentidadDbContext>();
            var usuario = new Millet.Identidad.Domain.Usuario(
                Guid.CreateVersion7(),
                inactiveOid,
                inactiveEmail,
                "Usuario Desactivado");
            usuario.Desactivar();

            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
        }

        var fakeValidator = new FakeEntraTokenValidator(token =>
        {
            if (token == "token-usuario-inactivo")
            {
                return new EntraTokenClaims(inactiveOid, inactiveEmail, "Usuario Desactivado");
            }
            throw new UnauthorizedAccessException("Token desconocido");
        });

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEntraTokenValidator>();
                services.AddScoped<IEntraTokenValidator>(_ => fakeValidator);
            });
        }).CreateClient();

        var request = new LoginRequest(
            EntraToken: "token-usuario-inactivo",
            EmpresaId: null);

        try
        {
            // 2. Act
            var response = await client.PostAsJsonAsync(Endpoint, request);

            // 3. Assert
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await CleanupUsuarioAsync(inactiveOid);
        }
    }

    private async Task CleanupUsuarioAsync(string entraOid)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Millet.Identidad.Infrastructure.IdentidadDbContext>();
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.EntraOid == entraOid);
        if (usuario is not null)
        {
            var preferencias = await db.UsuarioPreferencias.Where(p => p.UsuarioId == usuario.Id).ToListAsync();
            db.UsuarioPreferencias.RemoveRange(preferencias);
            db.Usuarios.Remove(usuario);
            await db.SaveChangesAsync();
        }
    }

    private string GetConfiguredSuperAdminOid()
    {
        using var scope = _factory.Services.CreateScope();
        var opts = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Millet.Identidad.Infrastructure.BootstrapSuperAdminOptions>>();
        return opts.Value.InitialAdminEntraOid ?? SuperAdminOid;
    }

    [Fact]
    public async Task PostCambiarEmpresa_Con_Usuario_Valido_Retorna_Nuevo_Token_Y_EmpresaActualizada()
    {
        var superAdminOid = GetConfiguredSuperAdminOid();

        // 1. Arrange: Obtener sesión inicial del superadmin vía POST /api/auth/sesion
        var fakeValidator = new FakeEntraTokenValidator(token =>
        {
            if (token == "token-superadmin")
            {
                return new EntraTokenClaims(superAdminOid, "superadmin@dev.local", "Super Admin Dev");
            }
            throw new UnauthorizedAccessException();
        });

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEntraTokenValidator>();
                services.AddScoped<IEntraTokenValidator>(_ => fakeValidator);
            });
        }).CreateClient();

        var loginRes = await client.PostAsJsonAsync(Endpoint, new LoginRequest("token-superadmin", null));
        loginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginRes.Content.ReadFromJsonAsync<LoginResponse>();
        var originalToken = loginBody!.AccessToken;

        // Crear una segunda empresa y asignarle el rol super-admin
        var segundaEmpresaId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var empresaContext = scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>();
            using var bypass = empresaContext.Bypass();

            var compartidoDb = scope.ServiceProvider.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
            var identidadDb = scope.ServiceProvider.GetRequiredService<Millet.Identidad.Infrastructure.IdentidadDbContext>();

            var segundaEmpresa = new Millet.Administracion.Domain.Empresa(
                segundaEmpresaId,
                "EMP2",
                $"RFC{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                "Segunda Empresa Test S.A.",
                "601",
                "Calle 1",
                "100",
                "Centro",
                "Mérida",
                "Mérida",
                "Yucatán",
                "MEX",
                nombreComercial: "Segunda Test");
            compartidoDb.Empresas.Add(segundaEmpresa);
            await compartidoDb.SaveChangesAsync();

            var rol = await identidadDb.Roles.FirstAsync(r => r.Codigo == "super-admin");
            var uer = new Millet.Identidad.Domain.UsuarioEmpresaRol(
                Guid.CreateVersion7(),
                loginBody.Usuario.Id,
                segundaEmpresaId,
                rol.Id,
                null);
            identidadDb.UsuarioEmpresaRoles.Add(uer);
            await identidadDb.SaveChangesAsync();
        }

        // 2. Act: Llamar a /api/auth/cambiar-empresa con el token del superadmin
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", originalToken);
        var cambiarRes = await client.PostAsJsonAsync("/api/auth/cambiar-empresa", new CambiarEmpresaRequest(segundaEmpresaId));

        // 3. Assert
        cambiarRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var cambiarBody = await cambiarRes.Content.ReadFromJsonAsync<LoginResponse>();
        cambiarBody.Should().NotBeNull();
        cambiarBody!.AccessToken.Should().NotBeNullOrWhiteSpace();
        cambiarBody.AccessToken.Should().NotBe(originalToken);
        cambiarBody.Empresas.First(e => e.EsLaActual).Id.Should().Be(segundaEmpresaId);
        cambiarBody.Permisos.Should().NotBeEmpty();
    }

    // ====================================================================
    // FAKE ENTRA TOKEN VALIDATOR TEST DOUBLE
    // ====================================================================

    private sealed class FakeEntraTokenValidator : IEntraTokenValidator
    {
        private readonly Func<string, EntraTokenClaims> _validateFunc;

        public FakeEntraTokenValidator(Func<string, EntraTokenClaims> validateFunc)
        {
            _validateFunc = validateFunc;
        }

        public Task<EntraTokenClaims> ValidateAsync(string accessToken, CancellationToken cancellationToken = default)
        {
            var claims = _validateFunc(accessToken);
            return Task.FromResult(claims);
        }

        public Task<ValidatedServicePrincipalToken?> TryValidateServicePrincipalAsync(
            string accessToken,
            CancellationToken cancellationToken = default)
        {
            // Para usuarios interactivos delegados en /api/auth/sesion siempre es null
            return Task.FromResult<ValidatedServicePrincipalToken?>(null);
        }
    }
}
