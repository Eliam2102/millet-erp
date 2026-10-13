using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>Bandeja de ligados A+W: revisar, descartar, candidatos, referencia duplicada, sincronización posterior y permisos.</summary>
public sealed class LigadosClienteEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Clasificacion = "00000012-0001-0000-0000-000000000001";
    private const string Ligados = "/api/v1/datos-maestros/clientes/ligados-aw";
    private static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    [Fact]
    public async Task Revisar_con_version_vieja_o_repetido_es_409()
    {
        using var c = await ClienteAsync(); var referencia = NuevaRef(); var ids = new List<Guid>();
        try
        {
            var (clienteId, conflicto) = await ManualYConflictoAsync(c, referencia, NuevoRfc(), ids);
            var conflictoId = conflicto.GetProperty("id").GetGuid(); var version = conflicto.GetProperty("version").GetInt32();

            var vieja = await RevisarAsync(c, conflictoId, clienteId, version + 1);
            Assert.Equal(HttpStatusCode.Conflict, vieja.StatusCode);
            Assert.Contains("LIGADO_CAMBIO", await vieja.Content.ReadAsStringAsync());

            var ok = await RevisarAsync(c, conflictoId, clienteId, version);
            Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

            var repetido = await RevisarAsync(c, conflictoId, clienteId, version);
            Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
            Assert.Contains("LIGADO_CAMBIO", await repetido.Content.ReadAsStringAsync());
        }
        finally { await LimpiarAsync(referencia, ids); }
    }

    [Fact]
    public async Task Candidato_aw_o_inexistente_es_422()
    {
        using var c = await ClienteAsync(); var referencia = NuevaRef(); var ids = new List<Guid>();
        try
        {
            var (_, conflicto) = await ManualYConflictoAsync(c, referencia, NuevoRfc(), ids);
            var conflictoId = conflicto.GetProperty("id").GetGuid(); var version = conflicto.GetProperty("version").GetInt32();

            var inexistente = await RevisarAsync(c, conflictoId, Guid.NewGuid(), version);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, inexistente.StatusCode);
            Assert.Contains("CLIENTE_NO_LIGABLE", await inexistente.Content.ReadAsStringAsync());

            var awResultado = await AplicarAwAsync(NuevaRef(), "CLIENTE AW SIN RFC", null, DateTime.UtcNow);
            Assert.Equal(AplicarClienteAwAccion.Creado, awResultado.Accion);
            var awId = awResultado.Cliente!.Id; ids.Add(awId);

            var aw = await RevisarAsync(c, conflictoId, awId, version);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, aw.StatusCode);
            Assert.Contains("CLIENTE_NO_LIGABLE", await aw.Content.ReadAsStringAsync());

            var lista = await Json(await c.GetAsync(Ligados));
            Assert.Contains(lista.EnumerateArray(), x => x.GetProperty("id").GetGuid() == conflictoId);
        }
        finally { await LimpiarAsync(referencia, ids); }
    }

    [Fact]
    public async Task Referencia_ya_ligada_a_otro_es_422()
    {
        using var c = await ClienteAsync(); var referencia = NuevaRef(); var ids = new List<Guid>();
        try
        {
            var (clienteId, conflicto) = await ManualYConflictoAsync(c, referencia, NuevoRfc(), ids);
            var conflictoId = conflicto.GetProperty("id").GetGuid(); var version = conflicto.GetProperty("version").GetInt32();

            // Segundo manual con la misma clave de A+W y otro RFC (evita la regla de RFC duplicado del alta).
            var segundo = await Json(await c.PostAsJsonAsync("/api/v1/datos-maestros/clientes", Alta(referencia + "D", NuevoRfc(), referencia)));
            ids.Add(segundo.GetProperty("id").GetGuid());

            var r = await RevisarAsync(c, conflictoId, clienteId, version);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Contains("CLIENTE_REFERENCIA_DUPLICADA", await r.Content.ReadAsStringAsync());

            var lista = await Json(await c.GetAsync(Ligados));
            Assert.Contains(lista.EnumerateArray(), x => x.GetProperty("id").GetGuid() == conflictoId);
        }
        finally { await LimpiarAsync(referencia, ids); }
    }

    [Fact]
    public async Task Tras_ligar_la_sincronizacion_respeta_lo_del_erp()
    {
        using var c = await ClienteAsync(); var referencia = NuevaRef(); var ids = new List<Guid>();
        try
        {
            var rfc = NuevoRfc();
            var (clienteId, conflicto) = await ManualYConflictoAsync(c, referencia, rfc, ids);
            var conflictoId = conflicto.GetProperty("id").GetGuid(); var version = conflicto.GetProperty("version").GetInt32();
            (await RevisarAsync(c, conflictoId, clienteId, version)).EnsureSuccessStatusCode();

            // Lectura posterior de A+W con otra razón social: el ERP no debe pisar razón social, RFC ni clasificación.
            await AplicarAwAsync(referencia, "RAZON CAMBIADA EN A+W", rfc, DateTime.UtcNow.AddMinutes(1));

            var detalle = await Json(await c.GetAsync($"/api/v1/datos-maestros/clientes/{clienteId}"));
            Assert.Equal("CLIENTE FICTICIO LIGADOS", detalle.GetProperty("razonSocial").GetString());
            Assert.Equal(rfc, detalle.GetProperty("rfc").GetString());
            Assert.Equal(Clasificacion, detalle.GetProperty("clasificacionId").GetString());
        }
        finally { await LimpiarAsync(referencia, ids); }
    }

    [Fact]
    public async Task Candidato_con_rfc_en_minusculas_aparece()
    {
        using var c = await ClienteAsync(); var referencia = NuevaRef(); var ids = new List<Guid>();
        try
        {
            var rfc = NuevoRfc();
            var (clienteId, _) = await ManualYConflictoAsync(c, referencia, rfc, ids);

            // RFC del manual en minúsculas con espacio (la columna admite 13 caracteres).
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
                await db.Clientes.Where(x => x.Id == clienteId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Rfc, " " + rfc.ToLowerInvariant()));
            }

            var lista = await Json(await c.GetAsync(Ligados));
            var ligado = lista.EnumerateArray().Single(x => x.GetProperty("referenciaExterna").GetString() == referencia);
            Assert.Contains(ligado.GetProperty("candidatos").EnumerateArray(), x => x.GetProperty("id").GetGuid() == clienteId);
        }
        finally { await LimpiarAsync(referencia, ids); }
    }

    [Fact]
    public async Task Permisos_de_ligados()
    {
        using var admin = await ClienteAsync(); var referencia = NuevaRef(); var ids = new List<Guid>();
        try
        {
            var (clienteId, conflicto) = await ManualYConflictoAsync(admin, referencia, NuevoRfc(), ids);
            var conflictoId = conflicto.GetProperty("id").GetGuid(); var version = conflicto.GetProperty("version").GetInt32();

            using var anonimo = factory.CreateClientWithIdempotency();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(Ligados)).StatusCode);

            using var sinGestion = await ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosClientesSincronizar);
            Assert.Equal(HttpStatusCode.Forbidden, (await sinGestion.GetAsync(Ligados)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await RevisarAsync(sinGestion, conflictoId, clienteId, version)).StatusCode);
            var descarte = await sinGestion.PostAsJsonAsync($"{Ligados}/{conflictoId}/descartar", new { id = conflictoId, version });
            Assert.Equal(HttpStatusCode.Forbidden, descarte.StatusCode);

            // El conflicto sigue abierto para quien sí tiene gestionar.
            Assert.Equal(conflictoId, (await ConflictoAsync(admin, referencia)).GetProperty("id").GetGuid());
        }
        finally { await LimpiarAsync(referencia, ids); }
    }

    [Fact]
    public async Task Provisionar_con_ligado_pendiente_falla_y_tras_revisar_devuelve_el_manual()
    {
        using var c = await ClienteAsync(); var referencia = NuevaRefF1A(); var ids = new List<Guid>();
        try
        {
            var rfc = NuevoRfcFia();
            var (clienteId, _) = await ManualYConflictoAsync(c, referencia, rfc, ids);

            // Mismo RFC que un manual, sin revisión: el alta por pedido no se usa hasta ligar.
            var pendiente = await Assert.ThrowsAsync<BusinessRuleException>(() => ProvisionarAsync(referencia, rfc));
            Assert.Equal("CLIENTE_LIGADO_PENDIENTE", pendiente.Code);

            // Releer tras el intento: la provisión re-aplica el snapshot y puede subir la versión del conflicto.
            var conflicto = await ConflictoAsync(c, referencia);
            var conflictoId = conflicto.GetProperty("id").GetGuid(); var version = conflicto.GetProperty("version").GetInt32();
            (await RevisarAsync(c, conflictoId, clienteId, version)).EnsureSuccessStatusCode();

            var provisionado = await ProvisionarAsync(referencia, rfc);
            Assert.Equal(clienteId, provisionado.ClienteId);
            Assert.False(provisionado.Creado);
        }
        finally { await LimpiarAsync(referencia, ids); }
    }

    [Fact]
    public async Task Revisar_con_manual_de_otro_RFC_es_422_y_el_conflicto_sigue_pendiente()
    {
        using var c = await ClienteAsync(); var referencia = NuevaRefF1A(); var ids = new List<Guid>();
        try
        {
            var (_, conflicto) = await ManualYConflictoAsync(c, referencia, NuevoRfcFia(), ids);
            var conflictoId = conflicto.GetProperty("id").GetGuid(); var version = conflicto.GetProperty("version").GetInt32();

            // Manual distinto, RFC no genérico y distinto al del conflicto.
            var otro = await Json(await c.PostAsJsonAsync("/api/v1/datos-maestros/clientes", Alta(referencia + "X", NuevoRfcFia())));
            var otroId = otro.GetProperty("id").GetGuid(); ids.Add(otroId);

            var r = await RevisarAsync(c, conflictoId, otroId, version);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            Assert.Contains("CLIENTE_RFC_NO_COINCIDE", await r.Content.ReadAsStringAsync());

            Assert.Equal(conflictoId, (await ConflictoAsync(c, referencia)).GetProperty("id").GetGuid());
        }
        finally { await LimpiarAsync(referencia, ids); }
    }

    // --- Helpers (copiados de F1AFundacionesEndpointsTests y ClientesProteccionFiscalTests) ---

    private static string NuevaRef() => "LIG-" + Guid.NewGuid().ToString("N")[..8];

    private static string NuevaRefF1A() => "F1A-" + Guid.NewGuid().ToString("N")[..8];

    private static string NuevoRfc() => "LIG261010" + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();

    private static string NuevoRfcFia() => "FIA261010" + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();

    private async Task<ProvisionarClienteDesdeAwResponse> ProvisionarAsync(string referencia, string rfc)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
            new ProvisionarClienteDesdeAwCommand(referencia, "CLIENTE AW PROVISION", rfc, null, "97000", "G03", "03", "PUE", "MXN"));
    }

    private static object Alta(string clave, string rfc, string? referenciaExterna = null) => new
    {
        clave, razonSocial = "CLIENTE FICTICIO LIGADOS", rfc, regimenFiscal = "601", codigoPostalFiscal = "97000", usoCfdiDefault = "G03",
        formaPagoDefault = "03", metodoPagoDefault = "PUE", monedaDefault = "MXN", clasificacionId = Clasificacion, referenciaExterna,
    };

    private static Task<HttpResponseMessage> RevisarAsync(HttpClient c, Guid conflictoId, Guid clienteId, int version) =>
        c.PostAsJsonAsync($"{Ligados}/{conflictoId}/revisar", new { id = conflictoId, clienteId, version });

    private async Task<(Guid ClienteId, JsonElement Conflicto)> ManualYConflictoAsync(HttpClient c, string referencia, string rfc, List<Guid> ids)
    {
        var alta = await Json(await c.PostAsJsonAsync("/api/v1/datos-maestros/clientes", Alta(referencia, rfc)));
        var id = alta.GetProperty("id").GetGuid(); ids.Add(id);
        var r = await AplicarAwAsync(referencia, "NOMBRE DE A+W", rfc, DateTime.UtcNow);
        Assert.Equal(AplicarClienteAwAccion.Conflicto, r.Accion);
        return (id, await ConflictoAsync(c, referencia));
    }

    private static async Task<JsonElement> ConflictoAsync(HttpClient c, string referencia)
    {
        var lista = await Json(await c.GetAsync(Ligados));
        return lista.EnumerateArray().Single(x => x.GetProperty("referenciaExterna").GetString() == referencia);
    }

    private async Task<AplicarClienteAwResultado> AplicarAwAsync(string referencia, string razonSocial, string? rfc, DateTime leidoEnUtc)
    {
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AplicarClienteAwService>();
        return await svc.AplicarAsync(new(referencia, razonSocial, leidoEnUtc, "1", "LIG", Rfc: rfc, MonedaDefault: "MXN"), CancellationToken.None);
    }

    private async Task LimpiarAsync(string referencia, IReadOnlyList<Guid> clientes)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        db.RemoveRange(await db.Set<ClienteLigadoAw>().Where(c => c.ReferenciaExterna == referencia).ToListAsync());
        db.RemoveRange(await db.ClientesSincronizacionAw.Where(c => clientes.Contains(c.ClienteId)).ToListAsync()); await db.SaveChangesAsync();
        db.RemoveRange(await db.Clientes.Where(c => clientes.Contains(c.Id)).ToListAsync()); await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private async Task<HttpClient> LoginAsync(string oid, string email)
    {
        var client = factory.CreateClientWithIdempotency();
        var resp = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        });
        resp.EnsureSuccessStatusCode();
        var j = await Json(resp);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", j.GetProperty("accessToken").GetString());
        return client;
    }

    private async Task<HttpClient> ClienteAsync()
    {
        var c = factory.CreateClientWithIdempotency();
        var login = await c.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "SuperAdmin", EmpresaId = EmpresaBootstrapId });
        var j = await Json(login);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", j.GetProperty("accessToken").GetString());
        return c;
    }

    private async Task<HttpClient> ClienteConPermisosAsync(params string[] codigos)
    {
        var admin = await LoginAsync("dev-superadmin", "superadmin@dev.local");
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"orig-test-{sufijo}";
        var email = $"orig-test-{sufijo}@test.local";

        var client = factory.CreateClientWithIdempotency();
        var primer = await Json(await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        }));
        var usuarioId = primer.GetProperty("usuario").GetProperty("id").GetGuid();

        var rolId = (await Json(await admin.PostAsJsonAsync("/api/v1/identidad/roles", new
        {
            Id = Guid.Empty, Codigo = $"rol-lig-{sufijo}", Nombre = $"Rol Lig {sufijo}", Descripcion = (string?)null,
        }))).GetProperty("id").GetGuid();
        var ids = codigos.Select(c => PermisosCanonicos.Todos.First(p => p.Codigo == c).Id).ToArray();
        (await admin.PutAsJsonAsync($"/api/v1/identidad/roles/{rolId}/permisos", new { PermisoIds = ids }))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/identidad/usuarios/{usuarioId}/asignaciones",
            new { EmpresaId = EmpresaBootstrapId, RolId = rolId })).EnsureSuccessStatusCode();

        return await LoginAsync(oid, email);
    }
}
