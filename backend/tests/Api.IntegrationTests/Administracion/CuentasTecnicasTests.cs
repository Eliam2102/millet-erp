using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;
using static Millet.Api.IntegrationTests.Administracion.ColaboradoresEndpointsTests;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// 86e3yxe9m: cuentas técnicas visibles en Empleados, ficha de la cuenta sin
/// empleado y conversión de una cuenta técnica en cuenta de persona.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class CuentasTecnicasTests
{
    private const string CuentasTecnicas = ColaboradoresEndpoint + "/cuentas-tecnicas";
    private const string CuentasSinEmpleado = ColaboradoresEndpoint + "/cuentas-sin-empleado";
    private const string Usuarios = "/api/v1/identidad/usuarios";
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public CuentasTecnicasTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Caso_del_servidor_cuatro_cuentas_sin_empleado_una_tecnica_y_super_admin()
    {
        var admin = await SuperAdminAsync();
        var sufijo = RandomSufijo();
        // Tres personas que entraron sin empleado y el administrador inicial (técnico y super-admin).
        foreach (var n in new[] { "uno", "dos", "tres" })
            await FakeLoginAsync(_factory.CreateClientWithIdempotency(), $"srv-{n}-{sufijo}", $"srv-{n}-{sufijo}@test.local", $"Servidor {n} {sufijo}");
        var tecnicaId = await SembrarTecnicaAsync($"srv-admin-{sufijo}", superAdmin: true);

        var sinEmpleado = await JsonAsync(await admin.GetAsync($"{CuentasSinEmpleado}?buscar={sufijo}"));
        Assert.Equal(3, sinEmpleado.GetProperty("total").GetInt32());
        Assert.DoesNotContain(sinEmpleado.GetProperty("items").EnumerateArray(),
            i => i.GetProperty("usuarioId").ValueKind == JsonValueKind.String && i.GetProperty("usuarioId").GetGuid() == tecnicaId);

        var tecnicas = await JsonAsync(await admin.GetAsync($"{CuentasTecnicas}?buscar={sufijo}"));
        var item = Assert.Single(tecnicas.GetProperty("items").EnumerateArray());
        Assert.Equal(tecnicaId, item.GetProperty("usuarioId").GetGuid());
        Assert.True(item.GetProperty("activo").GetBoolean());
        Assert.Contains(item.GetProperty("roles").EnumerateArray(), r => !string.IsNullOrWhiteSpace(r.GetString()));

        // La ficha de la cuenta: sin empleado, con su rol super-admin.
        var ficha = await JsonAsync(await admin.GetAsync($"{Usuarios}/{tecnicaId}"));
        Assert.True(ficha.GetProperty("usuario").GetProperty("esCuentaTecnica").GetBoolean());
        Assert.Contains(ficha.GetProperty("asignaciones").EnumerateArray(),
            a => a.GetProperty("rolCodigo").GetString() == "super-admin");
        await AssertProblemAsync(await admin.GetAsync($"{ColaboradoresEndpoint}/por-usuario/{tecnicaId}"),
            HttpStatusCode.NotFound, "EMPLEADO_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Super_admin_convierte_la_cuenta_tecnica_con_motivo_y_queda_en_la_bitacora()
    {
        var admin = await SuperAdminAsync();
        var tecnicaId = await SembrarTecnicaAsync($"conv-{RandomSufijo()}", superAdmin: false);
        var version = (await JsonAsync(await admin.GetAsync($"{Usuarios}/{tecnicaId}")))
            .GetProperty("usuario").GetProperty("version").GetInt32();
        var url = $"{Usuarios}/{tecnicaId}/convertir-en-cuenta-de-persona";

        var sinVersion = await admin.PostAsJsonAsync(url, new { motivo = "Motivo suficientemente largo" });
        Assert.Equal((HttpStatusCode)428, sinVersion.StatusCode);

        var corto = await PostConVersionAsync(admin, url, version, "corto");
        Assert.Equal(HttpStatusCode.BadRequest, corto.StatusCode);

        const string motivo = "La cuenta es de una persona del área de sistemas";
        var ok = await PostConVersionAsync(admin, url, version, motivo);
        Assert.True(ok.IsSuccessStatusCode, await ok.Content.ReadAsStringAsync());
        Assert.False((await JsonAsync(ok)).GetProperty("esCuentaTecnica").GetBoolean());

        var otraVez = await PostConVersionAsync(admin, url, version + 1, motivo);
        await AssertProblemAsync(otraVez, (HttpStatusCode)422, "USUARIO_NO_ES_CUENTA_TECNICA");

        using var scope = _factory.Services.CreateScope();
        var core = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var bitacora = await core.AuditLog.AsNoTracking()
            .Where(a => a.EntidadId == tecnicaId && a.Operacion == "usuario.convertido-en-cuenta-de-persona")
            .ToListAsync();
        var registro = Assert.Single(bitacora);
        Assert.Contains(motivo, registro.Cambios);
    }

    [Fact]
    public async Task Sin_permiso_no_ve_cuentas_tecnicas_ni_convierte()
    {
        var client = _factory.CreateClientWithIdempotency();
        var oid = $"sin-perm-tec-{RandomSufijo()}";
        var token = await FakeLoginAsync(client, oid, $"{oid}@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(CuentasTecnicas)).StatusCode);
        var convertir = await PostConVersionAsync(client, $"{Usuarios}/{Guid.NewGuid()}/convertir-en-cuenta-de-persona", 1,
            "Motivo suficientemente largo");
        Assert.Equal(HttpStatusCode.Forbidden, convertir.StatusCode);
    }

    private async Task<HttpClient> SuperAdminAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<Guid> SembrarTecnicaAsync(string oid, bool superAdmin)
    {
        using var scope = _factory.Services.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var usuario = new Usuario(Guid.CreateVersion7(), oid, $"{oid}@test.local", $"Técnica {oid}", esCuentaTecnica: true);
        identidad.Usuarios.Add(usuario);
        identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuario.Id));
        if (superAdmin)
        {
            var rol = await identidad.Roles.IgnoreQueryFilters().SingleAsync(r => r.Codigo == "super-admin");
            identidad.UsuarioEmpresaRoles.Add(usuario.AsignarRolEnEmpresa(EmpresaInicialId, rol.Id, null));
        }
        await identidad.SaveChangesAsync();
        return usuario.Id;
    }

    private static Task<HttpResponseMessage> PostConVersionAsync(HttpClient client, string url, int version, string motivo)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(new { motivo }) };
        request.Headers.Add("X-Expected-Version", version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {raw}");
        return JsonDocument.Parse(raw).RootElement.Clone();
    }
}
