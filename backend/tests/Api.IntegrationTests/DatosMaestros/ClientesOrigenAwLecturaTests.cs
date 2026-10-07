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

/// <summary>
/// ADM-06 Entrega D1: lectura de origen A+W en lista y detalle de clientes,
/// con y sin `origen-ver`, cliente manual sin origenAw y filtros nuevos.
/// </summary>
public class ClientesOrigenAwLecturaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private readonly WebApplicationFactory<Program> _factory;

    public ClientesOrigenAwLecturaTests(WebApplicationFactory<Program> factory) => _factory = factory;

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

    [Fact]
    public async Task Detalle_con_origen_ver_incluye_sensibles_y_version()
    {
        var id = await SembrarAwAsync(NuevaRef());
        var admin = await ClienteConPermisosAsync(
            PermisosCanonicos.DatosMaestrosClientesGestionar, PermisosCanonicos.DatosMaestrosClientesOrigenVer);

        var d = await Json(await admin.GetAsync($"/api/v1/datos-maestros/clientes/{id}"));

        Assert.True(d.GetProperty("version").GetInt32() >= 1);
        var o = d.GetProperty("origenAw");
        Assert.Equal("Aplicado", o.GetProperty("resultado").GetString());
        Assert.Equal("Z030", o.GetProperty("condicionOrigen").GetString());
        Assert.Equal("CALLE DEMO 1", o.GetProperty("domicilioOrigenCalle").GetString());
        Assert.Equal(1000m, o.GetProperty("creditoReferenciaLimite").GetDecimal());
    }

    [Fact]
    public async Task Detalle_sin_origen_ver_oculta_sensibles_pero_muestra_control()
    {
        var id = await SembrarAwAsync(NuevaRef());
        var op = await ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosClientesGestionar);

        var o = (await Json(await op.GetAsync($"/api/v1/datos-maestros/clientes/{id}"))).GetProperty("origenAw");

        Assert.Equal("Aplicado", o.GetProperty("resultado").GetString());
        Assert.Equal("Z030", o.GetProperty("condicionOrigen").GetString());
        foreach (var campo in new[] { "candidatoFiscalUstId", "creditoReferenciaLimite", "creditoReferenciaNet",
                     "domicilioOrigenCalle", "domicilioOrigenCp" })
            Assert.Equal(JsonValueKind.Null, o.GetProperty(campo).ValueKind);
    }

    [Fact]
    public async Task Cliente_manual_no_trae_origenAw_en_detalle_ni_lista()
    {
        var admin = await ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosClientesGestionar);
        var clave = "MAN-" + Guid.NewGuid().ToString("N")[..8];
        var crear = await admin.PostAsJsonAsync("/api/v1/datos-maestros/clientes", new
        {
            Clave = clave, RazonSocial = "CLIENTE MANUAL LECTURA", Rfc = "XAXX010101000",
            RegimenFiscal = "616", CodigoPostalFiscal = "06600", MonedaDefault = "MXN",
        });
        crear.EnsureSuccessStatusCode();
        var id = (await Json(crear)).GetProperty("id").GetGuid();

        var d = await Json(await admin.GetAsync($"/api/v1/datos-maestros/clientes/{id}"));
        Assert.Equal(JsonValueKind.Null, d.GetProperty("origenAw").ValueKind);

        var lista = await Json(await admin.GetAsync($"/api/v1/datos-maestros/clientes?razonSocial=CLIENTE MANUAL LECTURA"));
        var item = lista.GetProperty("items").EnumerateArray().First(i => i.GetProperty("id").GetGuid() == id);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("origenAw").ValueKind);
    }

    [Fact]
    public async Task Lista_filtra_por_referenciaExterna_y_resultadoSincronizacion_y_trae_origenAw()
    {
        var r = NuevaRef();
        var id = await SembrarAwAsync(r);
        var admin = await ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosClientesGestionar);

        var porRef = await Json(await admin.GetAsync($"/api/v1/datos-maestros/clientes?referenciaExterna={r}"));
        Assert.Equal(1, porRef.GetProperty("total").GetInt32());
        var item = porRef.GetProperty("items")[0];
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.Equal("Aplicado", item.GetProperty("origenAw").GetProperty("resultado").GetString());

        var conflicto = await Json(await admin.GetAsync(
            $"/api/v1/datos-maestros/clientes?referenciaExterna={r}&resultadoSincronizacion=Conflicto"));
        Assert.Equal(0, conflicto.GetProperty("total").GetInt32());
        var aplicado = await Json(await admin.GetAsync(
            $"/api/v1/datos-maestros/clientes?referenciaExterna={r}&resultadoSincronizacion=Aplicado"));
        Assert.Equal(1, aplicado.GetProperty("total").GetInt32());
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
