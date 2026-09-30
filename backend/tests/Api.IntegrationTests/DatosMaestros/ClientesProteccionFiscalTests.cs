using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>ADM-06 Entrega D3: protección fiscal en PATCH de clientes Origen=Aw.</summary>
public class ClientesProteccionFiscalTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private readonly WebApplicationFactory<Program> _factory;

    public ClientesProteccionFiscalTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static string NuevaRef() => "L" + Guid.NewGuid().ToString("N")[..8];

    private async Task<Guid> SembrarAwAsync(string referencia)
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AplicarClienteAwService>();
        var snap = new AplicarClienteAwSnapshot(
            referencia, "CLIENTE DEMO LECTURA", DateTime.UtcNow, "1", "0-borrador",
            Rfc: "XAXX010101000", CodigoPostalFiscal: "06600",
            MandantOrigen: 1, NombreComercialOrigen: "DEMO NAME1",
            DomicilioCalle: "CALLE DEMO 1", DomicilioCiudad: "CIUDAD DEMO", DomicilioCp: "06600",
            DomicilioProvincia: "PROV", DomicilioPais: "MX",
            CondicionCodigoOrigen: "Z030", CondicionNumeroOrigen: 30, DiasNominalesOrigen: 30,
            MonedaCodigoOrigen: "MXN", MonedaNormalizada: "MXN", MonedaDefault: "MXN",
            CreditoReferenciaLimite: 1000m, CreditoReferenciaLimite1: 500m, CreditoReferenciaNet: 30d);
        var res = await svc.AplicarAsync(snap, CancellationToken.None);
        return res.Cliente!.Id;
    }

    private static string Url(Guid id) => $"/api/v1/datos-maestros/clientes/{id}";

    [Fact]
    public async Task Aw_cambiar_razon_social_solo_con_gestionar_es_403_y_no_cambia()
    {
        var id = await SembrarAwAsync(NuevaRef());
        var op = await ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosClientesGestionar);

        var r = await op.PatchAsJsonAsync(Url(id), new { RazonSocial = "OTRA RAZON" });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("CLIENTE_FISCAL_AW_SIN_PERMISO", await r.Content.ReadAsStringAsync());
        Assert.Equal("CLIENTE DEMO LECTURA", (await Json(await op.GetAsync(Url(id)))).GetProperty("razonSocial").GetString());
    }

    [Fact]
    public async Task Aw_limpiar_rfc_solo_con_gestionar_es_403()
    {
        var id = await SembrarAwAsync(NuevaRef());
        var op = await ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosClientesGestionar);

        var r = await op.PatchAsJsonAsync(Url(id), new { LimpiarRfc = true });

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("XAXX010101000", (await Json(await op.GetAsync(Url(id)))).GetProperty("rfc").GetString());
    }

    [Fact]
    public async Task Aw_completar_regimen_vacio_y_mismo_valor_con_gestionar_es_ok()
    {
        var id = await SembrarAwAsync(NuevaRef());
        var op = await ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosClientesGestionar);

        var r = await op.PatchAsJsonAsync(Url(id), new
        {
            RegimenFiscal = "601", RazonSocial = "CLIENTE DEMO LECTURA", Rfc = "XAXX010101000", CodigoPostalFiscal = "06600",
        });

        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        Assert.Equal("601", (await Json(await op.GetAsync(Url(id)))).GetProperty("regimenFiscal").GetString());
    }

    [Fact]
    public async Task Aw_con_fiscal_editar_cambia_razon_social()
    {
        var id = await SembrarAwAsync(NuevaRef());
        var op = await ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosClientesGestionar, PermisosCanonicos.DatosMaestrosClientesFiscalEditar);

        var r = await op.PatchAsJsonAsync(Url(id), new { RazonSocial = "RAZON CORREGIDA" });

        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        Assert.Equal("RAZON CORREGIDA", (await Json(await op.GetAsync(Url(id)))).GetProperty("razonSocial").GetString());
    }

    [Fact]
    public async Task Manual_cambia_razon_social_solo_con_gestionar()
    {
        var op = await ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosClientesGestionar);
        var crear = await op.PostAsJsonAsync("/api/v1/datos-maestros/clientes", new
        {
            Clave = "MAN-" + Guid.NewGuid().ToString("N")[..8], RazonSocial = "CLIENTE MANUAL FISCAL",
            Rfc = "XAXX010101000", RegimenFiscal = "616", CodigoPostalFiscal = "06600", MonedaDefault = "MXN",
        });
        var id = (await Json(crear)).GetProperty("id").GetGuid();

        var r = await op.PatchAsJsonAsync(Url(id), new { RazonSocial = "MANUAL EDITADO", LimpiarRfc = true });

        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
    }

    // --- Helpers (duplicados a propósito, como en DatosMaestrosEndpointsTests) ---

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private async Task<HttpClient> LoginAsync(string oid, string email)
    {
        var client = _factory.CreateClientWithIdempotency();
        var resp = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        });
        resp.EnsureSuccessStatusCode();
        var j = await Json(resp);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", j.GetProperty("accessToken").GetString());
        return client;
    }

    private async Task<HttpClient> ClienteConPermisosAsync(params string[] codigos)
    {
        var admin = await LoginAsync("dev-superadmin", "superadmin@dev.local");
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"orig-test-{sufijo}";
        var email = $"orig-test-{sufijo}@test.local";

        var client = _factory.CreateClientWithIdempotency();
        var primer = await Json(await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        }));
        var usuarioId = primer.GetProperty("usuario").GetProperty("id").GetGuid();

        var rolId = (await Json(await admin.PostAsJsonAsync("/api/v1/identidad/roles", new
        {
            Id = Guid.Empty, Codigo = $"rol-orig-{sufijo}", Nombre = $"Rol Orig {sufijo}", Descripcion = (string?)null,
        }))).GetProperty("id").GetGuid();
        var ids = codigos.Select(c => PermisosCanonicos.Todos.First(p => p.Codigo == c).Id).ToArray();
        (await admin.PutAsJsonAsync($"/api/v1/identidad/roles/{rolId}/permisos", new { PermisoIds = ids }))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/identidad/usuarios/{usuarioId}/asignaciones",
            new { EmpresaId = EmpresaBootstrapId, RolId = rolId })).EnsureSuccessStatusCode();

        return await LoginAsync(oid, email);
    }
}
