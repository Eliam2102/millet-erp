using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.DatosMaestros.Application.Clientes;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>
/// ADM-06 Entrega D5: guion de aceptación DEMO-100 (doc integration/05 §9, paquete v2 doc 05 §9) de punta a punta
/// por HTTP: API real + PostgreSQL, dispatcher real, origen simulado inyectado y flags con UseSetting.
/// Datos 100 % sintéticos. Ya cubiertos idénticamente en otros archivos (no se duplican):
/// 403 en los 4 endpoints / 503 / 409 barrido vivo / 202+conflicto / mensajes sin excepción -> ClientesSincronizacionEndpointsTests;
/// contadores, KZ_STATUS y Pendiente por fila -> AwClientesSincronizadorTests.
/// </summary>
[Collection("AwClientesSync")]
public class ClientesSincronizacionDemo100Tests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Base = "/api/v1/datos-maestros/clientes/sincronizacion";
    private const string Clientes = "/api/v1/datos-maestros/clientes";
    private static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static int _siguienteBase = Random.Shared.Next(1_000_000, 900_000_000);

    private readonly WebApplicationFactory<Program> _factory;

    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public ClientesSincronizacionDemo100Tests(WebApplicationFactory<Program> factory, Xunit.Abstractions.ITestOutputHelper output)
    { _factory = factory; _out = output; }

    private static int NuevaBase() => Interlocked.Add(ref _siguienteBase, 10_000);

    private static AwClienteOrigenFila Fila(int id, string zahlbed = "60 DIAS", int dias = 60, string? tlf1 = "5550100") => new(
        id, 1, $"CLIENTE DEMO-100 {id}", null, null, "CALLE DEMO 1", "CIUDAD DEMO", "06600", "PROV", "MX",
        null, null, tlf1, null, null, zahlbed, "MXN", 1000m, 500m, 30d, null, 0, null, null,
        [new AwCondicionCoincidencia(30, dias)]);

    private sealed class OrigenFake(List<AwClienteOrigenFila> filas) : IAwClientesOrigen
    {
        public List<AwClienteOrigenFila> Filas { get; } = filas;
        public Action<int>? AlLeerPagina { get; set; }
        private int _paginas;

        public Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct)
            => Task.FromResult(Filas.FirstOrDefault(f => f.Id.ToString() == referencia));

        public Task<AwClientesPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
        {
            AlLeerPagina?.Invoke(Interlocked.Increment(ref _paginas));
            var desde = cursor is null ? 0 : int.Parse(cursor);
            var pag = Filas.Where(f => f.Id > desde).OrderBy(f => f.Id).Take(tamano).ToList();
            return Task.FromResult(new AwClientesPagina(pag, pag.Count == tamano ? pag[^1].Id.ToString() : null));
        }
    }

    private WebApplicationFactory<Program> Host(OrigenFake origen) => _factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("IntegracionesAw:Clientes:LecturaHabilitada", "true");
        b.UseSetting("IntegracionesAw:Clientes:AplicacionHabilitada", "true");
        b.UseSetting("IntegracionesAw:Clientes:TamanoLote", "3");
        b.UseSetting("IntegracionesAw:Clientes:MapeoMoneda:MXN", "MXN"); // sin mapeo toda fila queda Pendiente
        b.ConfigureServices(s => s.AddSingleton<IAwClientesOrigen>(origen));
    });

    private static async Task LimpiarVivosAsync(WebApplicationFactory<Program> f)
    {
        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones
            .Where(e => e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, AwClientesEjecucionEstado.Cancelada));
    }

    /// <summary>POST /ejecuciones, espera al dispatcher y devuelve el detalle de la ejecución.</summary>
    private static async Task<JsonElement> BarridoAsync(WebApplicationFactory<Program> f, HttpClient c)
    {
        await LimpiarVivosAsync(f);
        var resp = await c.PostAsJsonAsync($"{Base}/ejecuciones", new { tipo = "Barrido" });
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        return await EsperarFinAsync(c, (await ReadJson(resp)).GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> EsperarFinAsync(HttpClient c, Guid id)
    {
        var limite = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var d = await Json(await c.GetAsync($"{Base}/ejecuciones/{id}"));
            var estado = d.GetProperty("ejecucion").GetProperty("estado").GetString();
            if (estado is not ("Pendiente" or "EnCurso")) return d;
            Assert.True(DateTime.UtcNow < limite, $"La ejecución {id} no terminó (estado {estado}).");
            await Task.Delay(500);
        }
    }

    private static int N(JsonElement e, string p) => e.GetProperty("ejecucion").GetProperty(p).GetInt32();

    private static async Task<JsonElement> ListaAsync(HttpClient c, int referencia)
    {
        var l = await Json(await c.GetAsync($"{Clientes}?referenciaExterna={referencia}"));
        Assert.Equal(1, l.GetProperty("total").GetInt32()); // un solo cliente por referencia
        return l.GetProperty("items")[0];
    }

    private static async Task<JsonElement> DetalleAsync(HttpClient c, Guid id) => await Json(await c.GetAsync($"{Clientes}/{id}"));

    private static async Task<HttpClient> OperadorAsync(WebApplicationFactory<Program> f) => await ClienteConPermisosAsync(f,
        PermisosCanonicos.DatosMaestrosClientesSincronizar, PermisosCanonicos.DatosMaestrosClientesGestionar,
        PermisosCanonicos.DatosMaestrosClientesOrigenVer);

    // Pasos 1-2 del guion: alta, repetición, completar fiscales, cambio en origen, fiscales y pedido previo intactos, relectura.
    [Fact]
    public async Task Demo100_alta_repeticion_fiscales_locales_y_cambio_de_condicion_a_CONTADO()
    {
        var b = NuevaBase();
        var origen = new OrigenFake([Fila(b + 100)]);
        await using var f = Host(origen);
        var c = await OperadorAsync(f);

        // (1) Alta por barrido: 202 -> dispatcher -> lista y detalle con un solo cliente.
        var e1 = await BarridoAsync(f, c);
        Assert.Equal("Completa", e1.GetProperty("ejecucion").GetProperty("estado").GetString());
        Assert.Equal((1, 1, 0), (N(e1, "leidos"), N(e1, "creados"), N(e1, "pendientes")));
        var item = await ListaAsync(c, b + 100);
        var clienteId = item.GetProperty("id").GetGuid();
        _out.WriteLine("EVID ClienteId=" + clienteId + " ref=" + (b + 100) + " ejec1=" + e1.GetProperty("ejecucion").GetProperty("id").GetGuid());
        Assert.Equal("Aplicado", item.GetProperty("origenAw").GetProperty("resultado").GetString());
        var d1 = await DetalleAsync(c, clienteId);
        Assert.Equal((int)OrigenMaster.Aw, d1.GetProperty("origen").GetInt32());
        Assert.Equal("60 DIAS", d1.GetProperty("origenAw").GetProperty("condicionOrigen").GetString());
        Assert.Equal(60, d1.GetProperty("origenAw").GetProperty("diasNominalesOrigen").GetInt32());
        Assert.Equal("5550100", d1.GetProperty("telefono").GetString());

        // Repetir el barrido: mismo ClienteId, SinCambios.
        var e2 = await BarridoAsync(f, c);
        Assert.Equal((1, 0, 0, 1), (N(e2, "leidos"), N(e2, "creados"), N(e2, "actualizados"), N(e2, "sinCambios")));
        Assert.Equal(clienteId, (await ListaAsync(c, b + 100)).GetProperty("id").GetGuid());

        // Dato previo que referencia el ClienteId (auto-provisión por pedido, mismo camino que Facturación).
        async Task<ProvisionarClienteDesdeAwResponse> Provisionar()
        {
            using var scope = f.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                new ProvisionarClienteDesdeAwCommand((b + 100).ToString(), "OTRO NOMBRE", null, null, null, null, null, null, null));
        }
        var pedidoPrevio = await Provisionar();
        Assert.Equal((clienteId, false), (pedidoPrevio.ClienteId, pedidoPrevio.Creado));

        // (2) Completar régimen/CP/RFC fiscal en el ERP con `gestionar` (vacíos: no requiere fiscal-editar).
        (await c.PatchAsJsonAsync($"{Clientes}/{clienteId}", new
        {
            Rfc = "XAXX010101000", RegimenFiscal = "601", CodigoPostalFiscal = "06600",
        })).EnsureSuccessStatusCode();

        // Origen: cambia TLF1 y la condición pasa a CONTADO/0.
        origen.Filas[0] = Fila(b + 100, zahlbed: "CONTADO", dias: 0, tlf1: "5559999");
        var e3 = await BarridoAsync(f, c);
        Assert.Equal((1, 0, 1, 0), (N(e3, "leidos"), N(e3, "creados"), N(e3, "actualizados"), N(e3, "sinCambios")));

        var d3 = await DetalleAsync(c, clienteId);
        Assert.Equal(clienteId, d3.GetProperty("id").GetGuid());
        Assert.Equal(("XAXX010101000", "601", "06600"), (d3.GetProperty("rfc").GetString(),
            d3.GetProperty("regimenFiscal").GetString(), d3.GetProperty("codigoPostalFiscal").GetString()));
        Assert.Equal(d1.GetProperty("razonSocial").GetString(), d3.GetProperty("razonSocial").GetString());
        Assert.Equal("5550100", d3.GetProperty("telefono").GetString()); // teléfono local ya poblado: no se pisa
        Assert.Equal("CONTADO", d3.GetProperty("origenAw").GetProperty("condicionOrigen").GetString());
        Assert.Equal(0, d3.GetProperty("origenAw").GetProperty("diasNominalesOrigen").GetInt32());
        var pedidoPosterior = await Provisionar();
        Assert.Equal(clienteId, pedidoPosterior.ClienteId);
        Assert.Equal((pedidoPrevio.RazonSocial, false), (pedidoPosterior.RazonSocial, pedidoPosterior.Creado));

        // Releer sin cambios: SinCambios y ClienteId estable.
        var e4 = await BarridoAsync(f, c);
        Assert.Equal((1, 0, 0, 1), (N(e4, "leidos"), N(e4, "creados"), N(e4, "actualizados"), N(e4, "sinCambios")));
        Assert.Equal(clienteId, (await ListaAsync(c, b + 100)).GetProperty("id").GetGuid());
    }

    // Paso 3: moneda <indf> no crea el cliente (no se asume MXN); estado desconocido y condición duplicada: Pendiente visible, sin éxito inventado.
    [Fact]
    public async Task Demo100_estado_y_condicion_duplicada_quedan_Pendiente_y_moneda_sin_equivalencia_no_crea()
    {
        var b = NuevaBase();
        var origen = new OrigenFake([
            Fila(b + 1) with { Waehrung = "<indf>" },
            Fila(b + 2) with { KzStatus = 7 },
            Fila(b + 3) with { CondicionCoincidencias = [new AwCondicionCoincidencia(30, 30), new AwCondicionCoincidencia(31, 45)] },
        ]);
        await using var f = Host(origen);
        var c = await OperadorAsync(f);

        var e = await BarridoAsync(f, c);

        Assert.Equal("Parcial", e.GetProperty("ejecucion").GetProperty("estado").GetString());
        Assert.Equal((3, 2, 2, 1), (N(e, "leidos"), N(e, "creados"), N(e, "pendientes"), N(e, "errores"))); // b+1 no se crea
        Assert.Equal(0, (await Json(await c.GetAsync($"{Clientes}?referenciaExterna={b + 1}"))).GetProperty("total").GetInt32());
        foreach (var r in new[] { b + 2, b + 3 })
        {
            var item = await ListaAsync(c, r);
            Assert.Equal("Pendiente", item.GetProperty("origenAw").GetProperty("resultado").GetString());
            var o = (await DetalleAsync(c, item.GetProperty("id").GetGuid())).GetProperty("origenAw");
            Assert.Equal("Pendiente", o.GetProperty("resultado").GetString());
            if (r == b + 2) Assert.Equal(7, o.GetProperty("estadoOrigenCrudo").GetInt32());
            if (r == b + 3) Assert.Equal(JsonValueKind.Null, o.GetProperty("diasNominalesOrigen").ValueKind); // no se elige una de las 2 filas
        }
        // Filtro por resultado: los tres son localizables como Pendiente.
        var pend = await Json(await c.GetAsync($"{Clientes}?resultadoSincronizacion=Pendiente&limit=200"));
        Assert.True(new[] { b + 2, b + 3 }.All(r => pend.GetProperty("items").EnumerateArray()
            .Any(i => i.GetProperty("referenciaExterna").GetString() == r.ToString())));
    }

    // Paso 4 (403 en POST/GET/reintentos) ya cubierto en Sin_permiso_sincronizar_da_403_en_los_cuatro_endpoints; aquí solo origen-ver.
    [Fact]
    public async Task Demo100_sin_origen_ver_los_campos_sensibles_del_detalle_vienen_null()
    {
        var b = NuevaBase();
        await using var f = Host(new OrigenFake([Fila(b + 1)]));
        var admin = await OperadorAsync(f);
        await BarridoAsync(f, admin);
        var id = (await ListaAsync(admin, b + 1)).GetProperty("id").GetGuid();
        var con = (await DetalleAsync(admin, id)).GetProperty("origenAw");
        Assert.Equal("CALLE DEMO 1", con.GetProperty("domicilioOrigenCalle").GetString());
        Assert.Equal(1000m, con.GetProperty("creditoReferenciaLimite").GetDecimal());

        var sin = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesGestionar);
        var o = (await DetalleAsync(sin, id)).GetProperty("origenAw");
        Assert.Equal("60 DIAS", o.GetProperty("condicionOrigen").GetString()); // control visible
        foreach (var campo in new[] { "candidatoFiscalUstId", "candidatoFiscalSteuernummer", "creditoReferenciaLimite",
                     "creditoReferenciaLimite1", "creditoReferenciaNet", "domicilioOrigenCalle", "domicilioOrigenCiudad",
                     "domicilioOrigenCp", "domicilioOrigenProvincia", "domicilioOrigenPais" })
            Assert.Equal(JsonValueKind.Null, o.GetProperty(campo).ValueKind);
    }

    // Paso 5: caída a mitad del lote. Se deja EnCurso con cursor y el dispatcher real lo reanuda sin duplicados.
    [Fact]
    public async Task Demo100_caida_a_mitad_del_lote_el_dispatcher_reanuda_sin_duplicados()
    {
        var b = NuevaBase();
        var origen = new OrigenFake(Enumerable.Range(1, 8).Select(i => Fila(b + i)).ToList());
        using var cts = new CancellationTokenSource();
        var armado = true;
        origen.AlLeerPagina = n => { if (armado && n == 2) { armado = false; cts.Cancel(); } };
        await using var f = Host(origen);
        var c = await OperadorAsync(f);
        await LimpiarVivosAsync(f);

        Guid id;
        using (var scope = f.Services.CreateScope())
        using (scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass())
        {
            var sync = scope.ServiceProvider.GetRequiredService<AwClientesSincronizador>();
            id = await sync.IniciarBarridoAsync("demo-100", CancellationToken.None);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sync.EjecutarAsync(id, cts.Token));
        }
        var caido = await Json(await c.GetAsync($"{Base}/ejecuciones/{id}"));
        Assert.Equal("EnCurso", caido.GetProperty("ejecucion").GetProperty("estado").GetString());
        Assert.Equal(3, N(caido, "leidos"));

        var fin = await EsperarFinAsync(c, id); // dispatcher (cada 5 s) retoma desde el cursor
        Assert.Equal("Completa", fin.GetProperty("ejecucion").GetProperty("estado").GetString());
        Assert.Equal((8, 8), (N(fin, "leidos"), N(fin, "creados")));
        for (var i = 1; i <= 8; i++) await ListaAsync(c, b + i); // exactamente 1 cliente por referencia
    }

    // Paso 6: alta simultánea por pedido y por lector para la MISMA referencia.
    [Fact]
    public async Task Demo100_alta_simultanea_por_pedido_y_lector_deja_un_solo_cliente_y_un_solo_registro()
    {
        var b = NuevaBase();
        var refs = Enumerable.Range(1, 6).Select(i => b + i).ToList();
        await using var f = Host(new OrigenFake(refs.Select(r => Fila(r)).ToList()));
        var c = await OperadorAsync(f);

        var tareas = refs.SelectMany(r => new Func<Task>[]
        {
            async () => { (await c.PostAsJsonAsync($"{Base}/reintentos", new { referencia = r.ToString() })).EnsureSuccessStatusCode(); },
            async () =>
            {
                using var scope = f.Services.CreateScope();
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                await scope.ServiceProvider.GetRequiredService<ISender>().Send(
                    new ProvisionarClienteDesdeAwCommand(r.ToString(), $"CLIENTE PEDIDO {r}", null, null, null, null, null, null, null));
            },
        }.Select(t => t())).ToList();
        await Task.WhenAll(tareas);

        using var s = f.Services.CreateScope();
        using var by = s.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = s.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var rs = refs.Select(r => r.ToString()).ToList();
        Assert.Equal(refs.Count, await db.Clientes.CountAsync(x => x.ReferenciaExterna != null && rs.Contains(x.ReferenciaExterna)));
        Assert.Equal(refs.Count, await db.ClientesSincronizacionAw.CountAsync(x => rs.Contains(x.ReferenciaExterna)));
    }

    // Paso 7: cambia BRUTTOTAGE con la misma ZAHLBED: se actualiza el registro de origen, fiscales y cliente intactos.
    [Fact]
    public async Task Demo100_cambio_de_BRUTTOTAGE_sin_cambiar_ZAHLBED_actualiza_dias_sin_tocar_fiscales()
    {
        var b = NuevaBase();
        var origen = new OrigenFake([Fila(b + 1, zahlbed: "60 DIAS", dias: 60)]);
        await using var f = Host(origen);
        var c = await OperadorAsync(f);
        await BarridoAsync(f, c);
        var id = (await ListaAsync(c, b + 1)).GetProperty("id").GetGuid();
        (await c.PatchAsJsonAsync($"{Clientes}/{id}", new { Rfc = "XAXX010101000", RegimenFiscal = "601", CodigoPostalFiscal = "06600" }))
            .EnsureSuccessStatusCode();

        origen.Filas[0] = Fila(b + 1, zahlbed: "60 DIAS", dias: 90);
        var e = await BarridoAsync(f, c);

        Assert.Equal((0, 1), (N(e, "creados"), N(e, "actualizados")));
        var d = await DetalleAsync(c, id);
        Assert.Equal(90, d.GetProperty("origenAw").GetProperty("diasNominalesOrigen").GetInt32());
        Assert.Equal("60 DIAS", d.GetProperty("origenAw").GetProperty("condicionOrigen").GetString());
        Assert.Equal(("XAXX010101000", "601", "06600"), (d.GetProperty("rfc").GetString(),
            d.GetProperty("regimenFiscal").GetString(), d.GetProperty("codigoPostalFiscal").GetString()));
    }

    // Paso 8: baja sintética. La política de bajas explícitas NO existe en el ERP y no se inventa aquí:
    // la ausencia de una fila en el origen jamás baja al cliente (fixture baja-explicita-demo solo describe la intención).
    [Fact]
    public async Task Demo100_ausencia_en_el_origen_no_baja_al_cliente_no_existe_politica_de_bajas()
    {
        var b = NuevaBase();
        var origen = new OrigenFake([Fila(b + 1), Fila(b + 2)]);
        await using var f = Host(origen);
        var c = await OperadorAsync(f);
        await BarridoAsync(f, c);
        var id = (await ListaAsync(c, b + 2)).GetProperty("id").GetGuid();

        origen.Filas.RemoveAt(1); // b+2 "desaparece" del origen
        var e = await BarridoAsync(f, c);

        Assert.Equal("Completa", e.GetProperty("ejecucion").GetProperty("estado").GetString());
        Assert.Equal(1, N(e, "leidos"));
        var d = await DetalleAsync(c, id);
        Assert.Equal((int)EstatusCatalogo.Activo, d.GetProperty("estatus").GetInt32());
        Assert.Equal("Aplicado", d.GetProperty("origenAw").GetProperty("resultado").GetString());
    }

    // --- Helpers (duplicados a propósito, como en ClientesOrigenAwLecturaTests) ---

    private static async Task<JsonElement> ReadJson(HttpResponseMessage r)
    {
        using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        return await ReadJson(r);
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> f, string oid, string email)
    {
        var client = f.CreateClientWithIdempotency();
        var resp = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        });
        var j = await Json(resp);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", j.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<HttpClient> ClienteConPermisosAsync(WebApplicationFactory<Program> f, params string[] codigos)
    {
        var admin = await LoginAsync(f, "dev-superadmin", "superadmin@dev.local");
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"sync-test-{sufijo}";
        var email = $"sync-test-{sufijo}@test.local";

        var client = f.CreateClientWithIdempotency();
        var primer = await Json(await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        }));
        var usuarioId = primer.GetProperty("usuario").GetProperty("id").GetGuid();

        var rolId = (await Json(await admin.PostAsJsonAsync("/api/v1/identidad/roles", new
        {
            Id = Guid.Empty, Codigo = $"rol-sync-{sufijo}", Nombre = $"Rol Sync {sufijo}", Descripcion = (string?)null,
        }))).GetProperty("id").GetGuid();
        var ids = codigos.Select(c => PermisosCanonicos.Todos.First(p => p.Codigo == c).Id).ToArray();
        (await admin.PutAsJsonAsync($"/api/v1/identidad/roles/{rolId}/permisos", new { PermisoIds = ids }))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/identidad/usuarios/{usuarioId}/asignaciones",
            new { EmpresaId = EmpresaBootstrapId, RolId = rolId })).EnsureSuccessStatusCode();

        return await LoginAsync(f, oid, email);
    }
}
