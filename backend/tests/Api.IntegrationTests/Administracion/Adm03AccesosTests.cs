using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.SharedKernel.Domain.Audit;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Administracion;

public sealed class Adm03AccesosTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public Adm03AccesosTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Cambio_Denegado_Queda_En_Bitacora_Sin_Token()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, "dev-superadmin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var denied = await client.PostAsJsonAsync("/api/auth/cambiar-empresa", new
        {
            EmpresaId = Guid.NewGuid(),
        });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var url = $"/api/v1/admin/auditoria?desde={hoy.AddDays(-1):yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}&modulo=Identidad&recurso=Sesion";
        var audit = await client.GetAsync(url);
        audit.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await audit.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("operacion").GetString() == "cambiar_empresa_denegado" &&
            item.GetProperty("usuarioId").ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(item.GetProperty("usuarioNombre").GetString()) &&
            item.GetProperty("cambios").GetString()!.Contains("EMPRESA_ACCESS_DENIED"));
        Assert.DoesNotContain(token, await audit.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Auditoria_Usuario_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, $"aud-sin-perm-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await client.GetAsync(
            $"/api/v1/admin/auditoria?desde={hoy:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Asignar_Y_Revocar_Rol_Queda_Registrado_Con_Actor_Y_Entidad()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, "dev-superadmin");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var crearUsuario = await client.PostAsJsonAsync("/api/v1/identidad/usuarios", new
        {
            Id = Guid.Empty, Email = $"adm03-{suffix}@test.local",
            EntraIdObjectId = (string?)null, NombreCompleto = "Usuario QA ADM03",
            DepartamentoId = (Guid?)null,
        });
        crearUsuario.EnsureSuccessStatusCode();
        var usuarioId = (await ReadJsonAsync(crearUsuario)).GetProperty("id").GetGuid();

        var roles = await client.GetAsync("/api/v1/identidad/roles?limit=200");
        roles.EnsureSuccessStatusCode();
        var rol = (await ReadJsonAsync(roles)).GetProperty("items").EnumerateArray()
            .First(r => r.GetProperty("codigo").GetString() != "super-admin")
            .GetProperty("id").GetGuid();
        var empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
        var asignar = await client.PostAsJsonAsync(
            $"/api/v1/identidad/usuarios/{usuarioId}/asignaciones",
            new { EmpresaId = empresa, RolId = rol });
        asignar.EnsureSuccessStatusCode();
        var asignacionId = (await ReadJsonAsync(asignar)).GetProperty("id").GetGuid();
        var revocar = await client.DeleteAsync(
            $"/api/v1/identidad/usuarios/asignaciones/{asignacionId}");
        Assert.Equal(HttpStatusCode.NoContent, revocar.StatusCode);

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var audit = await client.GetAsync(
            $"/api/v1/admin/auditoria?desde={hoy.AddDays(-1):yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}&recurso=UsuarioEmpresaRol&limit=200");
        audit.EnsureSuccessStatusCode();
        var items = (await ReadJsonAsync(audit)).GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("entidadId").ValueKind == JsonValueKind.String &&
                i.GetProperty("entidadId").GetGuid() == asignacionId).ToList();
        Assert.Contains(items, i => i.GetProperty("operacion").GetString() == "crear" &&
            i.GetProperty("usuarioId").ValueKind == JsonValueKind.String &&
            i.GetProperty("empresaId").GetGuid() == empresa);
        Assert.Contains(items, i => i.GetProperty("operacion").GetString() == "borrar");
    }

    [Fact]
    public async Task La_Bitacora_Acepta_Eventos_Despues_De_Octubre_2026()
    {
        // El hosted service crea al arranque la partición actual y tres futuras.
        _ = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var future = new DateTimeOffset(DateTime.UtcNow.AddMonths(2), TimeSpan.Zero);
        var id = Guid.NewGuid();
        db.AuditLog.Add(new AuditLogEntry
        {
            Id = id,
            Timestamp = future,
            Modulo = "Identidad",
            Entidad = "Sesion",
            Operacion = "acceso",
            Cambios = "{}",
            CorrelationId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();
        try
        {
            Assert.True(await db.AuditLog.AnyAsync(e => e.Id == id && e.Timestamp == future));
        }
        finally
        {
            // La bitácora es compartida: un evento de "Sesion" con fecha futura
            // quedaría como "el más reciente" para otros tests.
            await db.AuditLog.Where(e => e.Id == id && e.Timestamp == future).ExecuteDeleteAsync();
        }
    }

    private static async Task<string> FakeLoginAsync(HttpClient client, string oid)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = $"{oid}@test.local",
            Nombre = "QA ADM-03",
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
