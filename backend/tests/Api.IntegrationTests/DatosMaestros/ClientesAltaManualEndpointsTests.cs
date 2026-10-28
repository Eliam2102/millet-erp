using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.Facturacion.Domain.Ports;
using Millet.Identidad.Domain;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>
/// P-17 (M2.1-a): alta, edición y filtros de clientes manuales vía API, conflictos de RFC,
/// carreras, permisos, límites del servidor y resolución desde Facturación. Cada prueba usa
/// RFC ficticios propios y limpia lo que crea en <c>finally</c>.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class ClientesAltaManualEndpointsTests(WebApplicationFactory<Program> factory)
{
    private const string Clasificacion = "00000012-0001-0000-0000-000000000001";
    private const string Base = "/api/v1/datos-maestros/clientes";
    private const string RfcGenerico = "XAXX010101000";
    private static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly int[] CodigosEsperadosSimultaneos = [201, 422];

    [Fact]
    public async Task Post_sin_clasificacion_es_400()
    {
        var c = await SuperAdminAsync();
        var body = AltaJson(ClaveNueva(), RfcNuevo());
        body.Remove("clasificacionId");

        var r = await c.PostAsJsonAsync(Base, body);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Rfc_de_cliente_aw_o_inactivo_es_duplicado()
    {
        var c = await SuperAdminAsync();
        var rfcAw = RfcNuevo();
        var rfcInactivo = RfcNuevo();
        var referencia = "AW-" + Guid.NewGuid().ToString("N")[..8];
        var ids = new List<Guid>();
        try
        {
            await AplicarAwAsync(referencia, rfcAw, DateTime.UtcNow);
            var awId = await IdPorReferenciaAsync(referencia);

            var dupAw = await c.PostAsJsonAsync(Base, Alta(ClaveNueva(), rfcAw));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, dupAw.StatusCode);
            var cuerpoAw = await Cuerpo(dupAw, "CLIENTE_RFC_DUPLICADO");
            Assert.Equal(awId, cuerpoAw.GetProperty("clientesExistentes")[0].GetProperty("id").GetGuid());

            var manualId = await CreadoAsync(await c.PostAsJsonAsync(Base, Alta(ClaveNueva(), rfcInactivo)));
            ids.Add(manualId);
            (await c.DeleteAsync($"{Base}/{manualId}")).EnsureSuccessStatusCode();

            var dupInactivo = await c.PostAsJsonAsync(Base, Alta(ClaveNueva(), rfcInactivo));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, dupInactivo.StatusCode);
            var cuerpoInactivo = await Cuerpo(dupInactivo, "CLIENTE_RFC_DUPLICADO");
            Assert.Equal(manualId, cuerpoInactivo.GetProperty("clientesExistentes")[0].GetProperty("id").GetGuid());
        }
        finally { await LimpiarAsync(ids, new[] { referencia }); }
    }

    [Fact]
    public async Task Rfc_generico_permite_varios_manuales()
    {
        var c = await SuperAdminAsync();
        var ids = new List<Guid>();
        try
        {
            // El RFC genérico exige régimen 616 y uso S01 (mismo dato que ClientesProteccionFiscalTests).
            var genericoBody = JsonSerializer.SerializeToNode(new
            {
                razonSocial = "PUBLICO EN GENERAL FICTICIO",
                rfc = RfcGenerico,
                regimenFiscal = "616",
                codigoPostalFiscal = "06600",
                usoCfdiDefault = "S01",
                formaPagoDefault = "01",
                metodoPagoDefault = "PUE",
                monedaDefault = "MXN",
                clasificacionId = Clasificacion,
            })!.AsObject();
            genericoBody["clave"] = ClaveNueva();
            ids.Add(await CreadoAsync(await c.PostAsJsonAsync(Base, genericoBody)));
            genericoBody["clave"] = ClaveNueva();
            ids.Add(await CreadoAsync(await c.PostAsJsonAsync(Base, genericoBody)));
        }
        finally { await LimpiarAsync(ids, Array.Empty<string>()); }
    }

    [Fact]
    public async Task Dos_altas_simultaneas_mismo_rfc_solo_una_gana()
    {
        var c = await SuperAdminAsync();
        var rfc = RfcNuevo();
        var ids = new List<Guid>();
        try
        {
            var tareas = Enumerable.Range(0, 2).Select(_ => c.PostAsJsonAsync(Base, Alta(ClaveNueva(), rfc))).ToArray();
            var respuestas = await Task.WhenAll(tareas);
            foreach (var r in respuestas)
            {
                if (await IdSiCreadoAsync(r) is Guid id) ids.Add(id);
            }

            var codigos = respuestas.Select(r => (int)r.StatusCode).OrderBy(x => x).ToArray();
            Assert.Equal(CodigosEsperadosSimultaneos, codigos);
            var lista = await Json(await c.GetAsync($"{Base}?rfc={rfc}"));
            Assert.Equal(1, lista.GetProperty("total").GetInt32());
        }
        finally { await LimpiarAsync(ids, Array.Empty<string>()); }
    }

    [Fact]
    public async Task Alta_manual_y_aw_simultaneas_no_duplican()
    {
        var c = await SuperAdminAsync();
        var rfc = RfcNuevo();
        var referencia = "AW-" + Guid.NewGuid().ToString("N")[..8];
        var ids = new List<Guid>();
        try
        {
            var manualTask = c.PostAsJsonAsync(Base, Alta(ClaveNueva(), rfc));
            var awTask = AplicarAwAsync(referencia, rfc, DateTime.UtcNow);
            await Task.WhenAll(manualTask, awTask);
            if (await IdSiCreadoAsync(await manualTask) is Guid manualId) ids.Add(manualId);

            Assert.Equal(1, await ContarClientesPorRfcAsync(rfc));
        }
        finally { await LimpiarAsync(ids, new[] { referencia }); }
    }

    [Fact]
    public async Task Alta_manual_y_aw_simultaneas_por_ust_id_nunca_comparten_clave()
    {
        await CarreraAltaManualVsAwAsync(modoReal: true);
    }

    [Fact]
    public async Task Alta_manual_y_aw_simultaneas_por_rfc_demo_nunca_comparten_clave()
    {
        await CarreraAltaManualVsAwAsync(modoReal: false);
    }

    [Fact]
    public async Task Patch_con_rfc_de_otro_es_422_con_existentes()
    {
        var c = await SuperAdminAsync();
        var rfc1 = RfcNuevo();
        var rfc2 = RfcNuevo();
        var ids = new List<Guid>();
        try
        {
            var primero = await CreadoAsync(await c.PostAsJsonAsync(Base, Alta(ClaveNueva(), rfc1)));
            ids.Add(primero);
            var segundo = await CreadoAsync(await c.PostAsJsonAsync(Base, Alta(ClaveNueva(), rfc2)));
            ids.Add(segundo);

            var r = await c.PatchAsJsonAsync($"{Base}/{segundo}", new { rfc = rfc1, clasificacionId = Clasificacion });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
            var cuerpo = await Cuerpo(r, "CLIENTE_RFC_DUPLICADO");
            Assert.Equal(primero, cuerpo.GetProperty("clientesExistentes")[0].GetProperty("id").GetGuid());
        }
        finally { await LimpiarAsync(ids, Array.Empty<string>()); }
    }

    [Fact]
    public async Task Sin_gestionar_es_403_en_clientes()
    {
        var admin = await SuperAdminAsync();
        var ids = new List<Guid>();
        try
        {
            var id = await CreadoAsync(await admin.PostAsJsonAsync(Base, Alta(ClaveNueva(), RfcNuevo())));
            ids.Add(id);
            var sinGestionar = await ClienteConPermisosAsync(PermisosCanonicos.DatosMaestrosClientesOrigenVer);

            Assert.Equal(HttpStatusCode.Forbidden, (await sinGestionar.GetAsync(Base)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await sinGestionar.GetAsync($"{Base}/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await sinGestionar.PostAsJsonAsync(Base, Alta(ClaveNueva(), RfcNuevo()))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await sinGestionar.PatchAsJsonAsync($"{Base}/{id}", new { telefono = "9990000000" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await sinGestionar.DeleteAsync($"{Base}/{id}")).StatusCode);
        }
        finally { await LimpiarAsync(ids, Array.Empty<string>()); }
    }

    public static TheoryData<string, string> LimitesFuera() => new()
    {
        { "clave", new string('K', 21) },
        { "telefono", new string('1', 51) },
        { "monedaDefault", "PE" },
        { "paisResidencia", "ME" },
        { "numRegIdTrib", new string('9', 41) },
        // M-50: un carácter por encima del máximo de CrearClienteValidator (F1A- como prefijo de datos de prueba).
        { "razonSocial", "F1A-" + new string('R', 251) },                       // max 254
        { "email", "F1A-" + new string('a', 239) + "@ejemplo.com" },           // max 254
        { "codigoPostalFiscal", "970000" },                                     // Length(5)
        { "domicilioExtranjeroCalle", "F1A-" + new string('C', 197) },         // max 200
        { "domicilioExtranjeroEstado", "F1A-" + new string('E', 97) },         // max 100
        { "domicilioExtranjeroCodigoPostal", new string('9', 13) },            // max 12
    };

    [Theory]
    [MemberData(nameof(LimitesFuera))]
    public async Task Limites_del_servidor(string campo, string valor)
    {
        var c = await SuperAdminAsync();
        var ids = new List<Guid>();
        try
        {
            var body = AltaJson(ClaveNueva(), RfcNuevo());
            body[campo] = valor;

            var r = await c.PostAsJsonAsync(Base, body);

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }
        finally { await LimpiarAsync(ids, Array.Empty<string>()); }
    }

    [Fact]
    public async Task Clasificacion_se_conserva_si_aw_cambia_nombre_via_api()
    {
        var c = await SuperAdminAsync();
        var rfc = RfcNuevo();
        var referencia = "AW-" + Guid.NewGuid().ToString("N")[..8];
        var ids = new List<Guid>();
        try
        {
            var t0 = DateTime.UtcNow;
            await AplicarAwAsync(referencia, rfc, t0);
            var id = await IdPorReferenciaAsync(referencia);
            (await c.PatchAsJsonAsync($"{Base}/{id}", new { clasificacionId = Clasificacion })).EnsureSuccessStatusCode();

            await AplicarAwAsync(referencia, rfc, t0.AddMinutes(5), "NOMBRE NUEVO DESDE AW");

            var detalle = await Json(await c.GetAsync($"{Base}/{id}"));
            Assert.Equal(Clasificacion, detalle.GetProperty("clasificacionId").GetString());
        }
        finally { await LimpiarAsync(ids, new[] { referencia }); }
    }

    [Fact]
    public async Task Filtros_sin_clasificar_combinados()
    {
        var c = await SuperAdminAsync();
        var rfcAw = RfcNuevo();
        var rfcManual = RfcNuevo();
        var referencia = "AW-" + Guid.NewGuid().ToString("N")[..8];
        var ids = new List<Guid>();
        try
        {
            await AplicarAwAsync(referencia, rfcAw, DateTime.UtcNow);
            var awId = await IdPorReferenciaAsync(referencia);
            ids.Add(await CreadoAsync(await c.PostAsJsonAsync(Base, Alta(ClaveNueva(), rfcManual))));

            var lista = await Json(await c.GetAsync($"{Base}?sinClasificar=true&origen=1&rfc={rfcAw}"));
            var items = lista.GetProperty("items");
            Assert.Equal(lista.GetProperty("total").GetInt32(), items.GetArrayLength());
            Assert.Equal(1, items.GetArrayLength());
            Assert.Equal(awId, items[0].GetProperty("id").GetGuid());

            var manualClasificado = await Json(await c.GetAsync($"{Base}?sinClasificar=true&rfc={rfcManual}"));
            Assert.Equal(0, manualClasificado.GetProperty("total").GetInt32());

            var otroOrigen = await Json(await c.GetAsync($"{Base}?sinClasificar=true&origen=0&rfc={rfcAw}"));
            Assert.Equal(0, otroOrigen.GetProperty("total").GetInt32());
        }
        finally { await LimpiarAsync(ids, new[] { referencia }); }
    }

    [Fact]
    public async Task Cliente_manual_resoluble_por_facturacion()
    {
        var c = await SuperAdminAsync();
        var rfc = RfcNuevo();
        var ids = new List<Guid>();
        try
        {
            var id = await CreadoAsync(await c.PostAsJsonAsync(Base, Alta(ClaveNueva(), rfc)));
            ids.Add(id);

            using var scope = factory.Services.CreateScope();
            var puerto = scope.ServiceProvider.GetRequiredService<IClientesReadPort>();
            var lectura = await puerto.ObtenerAsync(id, CancellationToken.None);

            Assert.NotNull(lectura);
            Assert.Equal(id, lectura!.ClienteId);
            Assert.Equal(rfc, lectura.Rfc);
        }
        finally { await LimpiarAsync(ids, Array.Empty<string>()); }
    }

    // --- Helpers (copiados de F1AFundacionesEndpointsTests y ClientesProteccionFiscalTests) ---

    private static object Alta(string clave, string rfc) => new
    {
        clave,
        razonSocial = "CLIENTE FICTICIO ALTA",
        rfc,
        regimenFiscal = "601",
        codigoPostalFiscal = "97000",
        usoCfdiDefault = "G03",
        formaPagoDefault = "03",
        metodoPagoDefault = "PUE",
        monedaDefault = "MXN",
        clasificacionId = Clasificacion,
    };

    private static JsonObject AltaJson(string clave, string rfc) => JsonSerializer.SerializeToNode(Alta(clave, rfc))!.AsObject();

    private static string ClaveNueva() => "ALT-" + Guid.NewGuid().ToString("N")[..8];

    // RFC ficticio con formato SAT de persona moral, único por llamada.
    private static string RfcNuevo() => "FIA261010" + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage r, string codigoEsperado)
    {
        var raw = await r.Content.ReadAsStringAsync();
        Assert.Contains(codigoEsperado, raw);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private static async Task<Guid> CreadoAsync(HttpResponseMessage r)
    {
        Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await Json(r)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid?> IdSiCreadoAsync(HttpResponseMessage r)
    {
        if (r.StatusCode != HttpStatusCode.Created) return null;
        return (await Json(r)).GetProperty("id").GetGuid();
    }

    private async Task<HttpClient> SuperAdminAsync() => await LoginAsync("dev-superadmin", "superadmin@dev.local");

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

    private async Task<HttpClient> ClienteConPermisosAsync(params string[] codigos)
    {
        var admin = await SuperAdminAsync();
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"alta-test-{sufijo}";
        var email = $"alta-test-{sufijo}@test.local";

        var client = factory.CreateClientWithIdempotency();
        var primer = await Json(await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        }));
        var usuarioId = primer.GetProperty("usuario").GetProperty("id").GetGuid();

        var rolId = (await Json(await admin.PostAsJsonAsync("/api/v1/identidad/roles", new
        {
            Id = Guid.Empty, Codigo = $"rol-alta-{sufijo}", Nombre = $"Rol Alta {sufijo}", Descripcion = (string?)null,
        }))).GetProperty("id").GetGuid();
        var ids = codigos.Select(c => PermisosCanonicos.Todos.First(p => p.Codigo == c).Id).ToArray();
        (await admin.PutAsJsonAsync($"/api/v1/identidad/roles/{rolId}/permisos", new { PermisoIds = ids }))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/identidad/usuarios/{usuarioId}/asignaciones",
            new { EmpresaId = EmpresaBootstrapId, RolId = rolId })).EnsureSuccessStatusCode();

        return await LoginAsync(oid, email);
    }

    private async Task<AplicarClienteAwResultado> AplicarAwAsync(string referencia, string? rfc, DateTime leidoEnUtc, string razonSocial = "FICTICIO", string? ustId = null)
    {
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<AplicarClienteAwService>();
        return await svc.AplicarAsync(
            new(referencia, razonSocial, leidoEnUtc, "1", "F1A", Rfc: rfc, MonedaDefault: "MXN", CandidatoFiscalUstId: ustId),
            CancellationToken.None);
    }

    // Repite la carrera para que colisione de verdad. Modo real: A+W trae Rfc null y la clave en CandidatoFiscalUstId con guiones.
    // Modo demo: A+W copia el RFC en el campo fiscal.
    private async Task CarreraAltaManualVsAwAsync(bool modoReal)
    {
        const int iteraciones = 12;
        var c = await SuperAdminAsync();
        for (var i = 0; i < iteraciones; i++)
        {
            var rfc = RfcNuevo();
            var referencia = "AW-" + Guid.NewGuid().ToString("N")[..8];
            var ids = new List<Guid>();
            try
            {
                var manualTask = c.PostAsJsonAsync(Base, Alta(ClaveNueva(), rfc));
                var awTask = modoReal
                    ? AplicarAwAsync(referencia, null, DateTime.UtcNow, ustId: $"{rfc[..3]}-{rfc[3..9]}-{rfc[9..]}")
                    : AplicarAwAsync(referencia, rfc, DateTime.UtcNow);
                await Task.WhenAll(manualTask, awTask);
                var manual = await manualTask;
                var aw = await awTask;

                var cuerpo = await manual.Content.ReadAsStringAsync();
                Assert.True(manual.StatusCode is HttpStatusCode.Created or HttpStatusCode.UnprocessableEntity,
                    $"Iteración {i}: POST manual devolvió {(int)manual.StatusCode}: {cuerpo}");
                var manualId = await IdSiCreadoAsync(manual);
                if (manualId is Guid id) ids.Add(id);
                var (clientesAw, clientesRfc, ligados) = await EstadoPorReferenciaAsync(referencia, rfc);

                if (manualId is not null)
                {
                    Assert.True(aw.Accion == AplicarClienteAwAccion.Conflicto,
                        $"Iteración {i}: alta manual 201 pero la aplicación A+W dio {aw.Accion}.");
                    Assert.True(clientesAw == 0 && clientesRfc == 1 && ligados == 1,
                        $"Iteración {i}: tras 201 se esperaba 0 clientes A+W, 1 cliente con RFC y 1 ligado pendiente; hubo {clientesAw}, {clientesRfc} y {ligados}.");
                }
                else
                {
                    await Cuerpo(manual, "CLIENTE_RFC_DUPLICADO");
                    Assert.True(aw.Accion == AplicarClienteAwAccion.Creado,
                        $"Iteración {i}: alta manual 422 pero la aplicación A+W dio {aw.Accion}.");
                    // En modo demo el cliente A+W copia el RFC en su campo fiscal; en modo real queda sin RFC.
                    var rfcEnAw = modoReal ? 0 : 1;
                    Assert.True(clientesAw == 1 && clientesRfc == rfcEnAw && ligados == 0,
                        $"Iteración {i}: tras 422 se esperaba 1 cliente A+W, {rfcEnAw} cliente con RFC y 0 ligados; hubo {clientesAw}, {clientesRfc} y {ligados}.");
                }
            }
            finally { await LimpiarAsync(ids, new[] { referencia }); }
        }
    }

    private async Task<(int ClientesAw, int ClientesRfc, int LigadosPendientes)> EstadoPorReferenciaAsync(string referencia, string rfc)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        return (
            await db.Clientes.CountAsync(c => c.ReferenciaExterna == referencia),
            await db.Clientes.CountAsync(c => c.Rfc == rfc),
            await db.Set<ClienteLigadoAw>().CountAsync(l => l.ReferenciaExterna == referencia && l.Rfc == rfc && l.RevisadoEn == null));
    }

    private async Task<Guid> IdPorReferenciaAsync(string referencia)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        return await db.Clientes.Where(c => c.ReferenciaExterna == referencia).Select(c => c.Id).SingleAsync();
    }

    private async Task<int> ContarClientesPorRfcAsync(string rfc)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        return await db.Clientes.CountAsync(c => c.Rfc == rfc);
    }

    private async Task LimpiarAsync(IReadOnlyList<Guid> ids, IReadOnlyList<string> referencias)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        db.RemoveRange(await db.Set<ClienteLigadoAw>().Where(c => referencias.Contains(c.ReferenciaExterna!)).ToListAsync());
        await db.SaveChangesAsync();
        var clientes = await db.Clientes
            .Where(c => ids.Contains(c.Id) || (c.ReferenciaExterna != null && referencias.Contains(c.ReferenciaExterna)))
            .Select(c => c.Id)
            .ToListAsync();
        db.RemoveRange(await db.ClientesSincronizacionAw.Where(s => clientes.Contains(s.ClienteId)).ToListAsync());
        await db.SaveChangesAsync();
        db.RemoveRange(await db.Clientes.Where(c => clientes.Contains(c.Id)).ToListAsync());
        await db.SaveChangesAsync();
    }
}
