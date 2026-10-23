using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>
/// U1.8b (F1-FAC-14): endpoints del reporte de diferencias fiscales A+W vs ERP contra PostgreSQL real, con un
/// origen A+W falso. Cubre permisos (leer/revisar), Idempotency-Key, los criterios a/b por HTTP y la corrida
/// guardada. Cada prueba usa referencias propias y filtra sus filas: la BD es compartida por la suite.
/// </summary>
[Collection("AwClientesSync")]
public class DiferenciasFiscalesAwEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Base = "/api/v1/datos-maestros/diferencias-fiscales-aw";
    private static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static int _siguiente = Random.Shared.Next(1_000_000, 900_000_000);

    private sealed class OrigenFake : IAwClientesOrigen
    {
        public Dictionary<int, AwClienteOrigenFila> Filas { get; } = [];

        public Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct) =>
            Task.FromResult(int.TryParse(referencia, out var id) ? Filas.GetValueOrDefault(id) : null);

        public Task<AwClientesPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
        {
            var desde = cursor is null ? 0 : int.Parse(cursor);
            var pag = Filas.Values.Where(f => f.Id > desde).OrderBy(f => f.Id).Take(tamano).ToList();
            return Task.FromResult(new AwClientesPagina(pag, pag.Count == tamano ? pag[^1].Id.ToString() : null));
        }
    }

    private static AwClienteOrigenFila Fila(int id, string regimen) => new(
        id, 1, "CLIENTE DEMO U18B", null, null, null, null, "97000", null, "MEX", "DEMO010101AB1", null, null, null, null,
        "CONTADO", "PESOSMX", null, null, null, 1, 0, null, null, [], Bonitaet: "MORAL DEMO", RegimenFiscalOrigen: regimen);

    private WebApplicationFactory<Program> Host(OrigenFake origen) => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("IntegracionesAw:Clientes:LecturaHabilitada", "true");
        b.ConfigureServices(s => s.AddSingleton<IAwClientesOrigen>(origen));
    });

    private static async Task<Guid> CrearClienteAsync(WebApplicationFactory<Program> f, int referencia, string regimen)
    {
        using var scope = f.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var c = new Cliente(Guid.CreateVersion7(), $"U18B-{referencia}", "CLIENTE DEMO U18B",
            OrigenMaster.Aw, referenciaExterna: referencia.ToString(), rfc: "DEMO010101AB1", regimenFiscal: regimen, codigoPostalFiscal: "97000");
        db.Clientes.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    private static IEnumerable<JsonElement> FilasDe(JsonElement reporte, int referencia) =>
        reporte.GetProperty("filas").EnumerateArray().Where(f => f.GetProperty("referencia").GetString() == referencia.ToString());

    [Fact]
    public async Task Sin_permiso_leer_da_403_y_con_solo_leer_no_puede_revisar_ni_guardar_corrida()
    {
        await using var f = Host(new OrigenFake());
        var sinPermiso = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesGestionar);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync(Base)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync($"{Base}/corridas/ultima")).StatusCode);

        var lector = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosDiferenciasFiscalesAwLeer);
        Assert.Equal(HttpStatusCode.OK, (await lector.GetAsync($"{Base}?tipo=Producto")).StatusCode);
        var body = new { tipo = "Cliente", entidadId = Guid.NewGuid(), campo = "RegimenFiscal", huella = new string('a', 64) };
        Assert.Equal(HttpStatusCode.Forbidden, (await lector.PostAsJsonAsync($"{Base}/revisiones", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lector.PostAsync($"{Base}/corridas", null)).StatusCode);
    }

    [Fact]
    public async Task Revisar_sin_Idempotency_Key_da_400_y_filtro_invalido_da_400()
    {
        await using var f = Host(new OrigenFake());
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosDiferenciasFiscalesAwLeer,
            PermisosCanonicos.DatosMaestrosDiferenciasFiscalesAwRevisar);
        var sinKey = f.CreateDefaultClient();
        sinKey.DefaultRequestHeaders.Authorization = c.DefaultRequestHeaders.Authorization;
        var body = new { tipo = "Cliente", entidadId = Guid.NewGuid(), campo = "RegimenFiscal", huella = new string('a', 64) };

        Assert.Equal(HttpStatusCode.BadRequest, (await sinKey.PostAsJsonAsync($"{Base}/revisiones", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"{Base}?tipo=Proveedor")).StatusCode);
    }

    [Fact]
    public async Task CA_a_y_b_por_HTTP_y_la_corrida_guardada_respeta_lo_revisado()
    {
        var referencia = Interlocked.Increment(ref _siguiente);
        var origen = new OrigenFake();
        origen.Filas[referencia] = Fila(referencia, "601");
        await using var f = Host(origen);
        var clienteId = await CrearClienteAsync(f, referencia, "612");
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosDiferenciasFiscalesAwLeer,
            PermisosCanonicos.DatosMaestrosDiferenciasFiscalesAwRevisar);

        // a) aparece con ambos valores.
        var reporte = await Json(await c.GetAsync($"{Base}?tipo=Cliente&campo=RegimenFiscal"));
        var fila = Assert.Single(FilasDe(reporte, referencia));
        Assert.Equal("601", fila.GetProperty("valorOrigen").GetString());
        Assert.Equal("612", fila.GetProperty("valorErp").GetString());
        Assert.Equal("Diferente", fila.GetProperty("estadoEtiqueta").GetString());
        Assert.Equal(clienteId, fila.GetProperty("entidadId").GetGuid());

        // Corrida manual: la diferencia queda guardada como pendiente.
        var corrida = await Json(await c.PostAsync($"{Base}/corridas", null));
        Assert.True(corrida.GetProperty("exitosa").GetBoolean());
        Assert.Single(FilasDe((await Json(await c.GetAsync($"{Base}/corridas/ultima"))).GetProperty("reporte"), referencia));

        // b) marcar revisado: desaparece del vivo y de la última corrida (salvo con incluirRevisadas).
        var revision = await c.PostAsJsonAsync($"{Base}/revisiones", new
        {
            tipo = "Cliente", entidadId = clienteId, campo = "RegimenFiscal",
            huella = fila.GetProperty("huella").GetString(), nota = "Validado con Fiscal",
        });
        Assert.Equal(HttpStatusCode.OK, revision.StatusCode);
        Assert.Empty(FilasDe(await Json(await c.GetAsync($"{Base}?tipo=Cliente")), referencia));
        Assert.Empty(FilasDe((await Json(await c.GetAsync($"{Base}/corridas/ultima"))).GetProperty("reporte"), referencia));
        var conRevisadas = await Json(await c.GetAsync($"{Base}?tipo=Cliente&incluirRevisadas=true"));
        Assert.True(Assert.Single(FilasDe(conRevisadas, referencia)).GetProperty("revisada").GetBoolean());

        // Repetir la revisión con la misma huella ya no aplica a algo distinto; cambiar A+W la hace reaparecer.
        origen.Filas[referencia] = Fila(referencia, "603");
        var vieja = await c.PostAsJsonAsync($"{Base}/revisiones", new
        {
            tipo = "Cliente", entidadId = clienteId, campo = "RegimenFiscal", huella = fila.GetProperty("huella").GetString(),
        });
        Assert.Equal(HttpStatusCode.Conflict, vieja.StatusCode);
        var reaparece = Assert.Single(FilasDe(await Json(await c.GetAsync($"{Base}?tipo=Cliente")), referencia));
        Assert.Equal("603", reaparece.GetProperty("valorOrigen").GetString());
    }

    // --- Helpers (duplicados a propósito, como en ClientesSincronizacionEndpointsTests) ---

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> f, string oid, string email)
    {
        var client = f.CreateClientWithIdempotency();
        var j = await Json(await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", j.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<HttpClient> ClienteConPermisosAsync(WebApplicationFactory<Program> f, params string[] codigos)
    {
        var admin = await LoginAsync(f, "dev-superadmin", "superadmin@dev.local");
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"u18b-test-{sufijo}";
        var email = $"u18b-test-{sufijo}@test.local";

        var client = f.CreateClientWithIdempotency();
        var primer = await Json(await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        }));
        var usuarioId = primer.GetProperty("usuario").GetProperty("id").GetGuid();

        var rolId = (await Json(await admin.PostAsJsonAsync("/api/v1/identidad/roles", new
        {
            Id = Guid.Empty, Codigo = $"rol-u18b-{sufijo}", Nombre = $"Rol U18B {sufijo}", Descripcion = (string?)null,
        }))).GetProperty("id").GetGuid();
        var ids = codigos.Select(c => PermisosCanonicos.Todos.First(p => p.Codigo == c).Id).ToArray();
        (await admin.PutAsJsonAsync($"/api/v1/identidad/roles/{rolId}/permisos", new { PermisoIds = ids }))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/identidad/usuarios/{usuarioId}/asignaciones",
            new { EmpresaId = EmpresaBootstrapId, RolId = rolId })).EnsureSuccessStatusCode();

        return await LoginAsync(f, oid, email);
    }
}
