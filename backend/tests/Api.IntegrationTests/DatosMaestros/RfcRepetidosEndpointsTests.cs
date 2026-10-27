using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>86e3qcgyn: grupos de clientes de A+W con RFC repetido y su unificación (Fusionar / Agrupar).</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class RfcRepetidosEndpointsTests(WebApplicationFactory<Program> factory)
{
    private const string Base = "/api/v1/datos-maestros/clientes/rfc-repetidos";
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static string NuevaRef() => "U" + Guid.NewGuid().ToString("N")[..8];
    private static string NuevoRfc() => "ZZU" + Random.Shared.Next(0, 1_000_000).ToString("D6") + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();

    // El RFC llega como UST_ID candidato (modo real de A+W): el cliente nace sin RFC propio.
    private static AplicarClienteAwSnapshot Snap(string referencia, string ust) => new(
        referencia, "CLIENTE DEMO UNIFICAR " + referencia, DateTime.UtcNow, "1", "0-borrador",
        CandidatoFiscalUstId: ust, MonedaDefault: "MXN", MonedaNormalizada: "MXN", MonedaCodigoOrigen: "MXN");

    private async Task<Guid> CrearAsync(string referencia, string ust, MapeoEstatusClientesAwOptions? mapeo = null, int? bloqueo = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var r = await new AplicarClienteAwService(db, mapeo is null ? null : Options.Create(mapeo))
            .AplicarAsync(Snap(referencia, ust) with { BloqueoOrigenCrudo = bloqueo }, CancellationToken.None);
        return r.Cliente!.Id;
    }

    private async Task<(Guid A, Guid B, string RefA, string RefB, string Rfc)> DosAsync()
    {
        var rfc = NuevoRfc(); string refA = NuevaRef(), refB = NuevaRef();
        return (await CrearAsync(refA, rfc), await CrearAsync(refB, rfc), refA, refB, rfc);
    }

    private static async Task<JsonElement?> BuscarGrupoAsync(HttpClient http, string rfc, bool soloPendientes = true)
    {
        for (var offset = 0; ; offset += 100)
        {
            var r = await http.GetAsync($"{Base}?limit=100&offset={offset}&soloPendientes={soloPendientes.ToString().ToLowerInvariant()}");
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            var items = doc.RootElement.GetProperty("items").EnumerateArray().Select(i => i.Clone()).ToList();
            if (items.FirstOrDefault(i => i.GetProperty("rfc").GetString() == rfc) is { ValueKind: JsonValueKind.Object } g) return g;
            if (offset + 100 >= doc.RootElement.GetProperty("total").GetInt32()) return null;
        }
    }

    private static Task<HttpResponseMessage> Unificar(HttpClient http, string? rfc, Guid principal, string motivo = "mismo cliente con dos claves") =>
        http.PostAsJsonAsync($"{Base}/unificar", new { rfc, principalClienteId = principal, motivo });

    private async Task<(string Estatus, Guid? UnificadoEn)> EstadoAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var c = await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Clientes.AsNoTracking().SingleAsync(x => x.Id == id);
        return (c.Estatus.ToString(), c.UnificadoEnClienteId);
    }

    private static JsonElement Cliente(JsonElement grupo, Guid id) =>
        grupo.GetProperty("clientes").EnumerateArray().Single(c => c.GetProperty("clienteId").GetGuid() == id);

    [Fact]
    public async Task Listar_agrupa_por_rfc_normalizado_y_excluye_genericos()
    {
        var (a, b, _, _, rfc) = await DosAsync();
        await CrearAsync(NuevaRef(), "XAXX010101000"); await CrearAsync(NuevaRef(), "XAXX010101000");
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);

        var g = await BuscarGrupoAsync(http, rfc);
        Assert.NotNull(g);
        Assert.False(g!.Value.GetProperty("resuelto").GetBoolean());
        Assert.Equal(new[] { a, b }.Order(), g.Value.GetProperty("clientes").EnumerateArray().Select(c => c.GetProperty("clienteId").GetGuid()).Order());
        Assert.Equal("Principal", g.Value.GetProperty("clientes")[0].GetProperty("accionSugerida").GetString());
        Assert.Null(await BuscarGrupoAsync(http, "XAXX010101000", soloPendientes: false));
    }

    [Fact]
    public async Task Unificar_sin_historial_fusiona_y_deja_inactivo_al_secundario()
    {
        var (a, b, _, _, rfc) = await DosAsync();
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);

        var r = await Unificar(http, rfc, a);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("resuelto").GetBoolean());
        Assert.Equal(a, doc.RootElement.GetProperty("principalSugeridoId").GetGuid());
        Assert.Equal(("Inactivo", (Guid?)a), await EstadoAsync(b));
        Assert.Equal(("Activo", (Guid?)null), await EstadoAsync(a));
        Assert.Equal("Fusionar", Cliente(doc.RootElement, b).GetProperty("accionSugerida").GetString());
        Assert.Null(await BuscarGrupoAsync(http, rfc)); // ya no está pendiente
        Assert.NotNull(await BuscarGrupoAsync(http, rfc, soloPendientes: false));
    }

    [Fact]
    public async Task Unificar_completa_al_principal_con_el_rfc_del_grupo_aunque_lo_tuviera_otro()
    {
        var (a, b, _, _, rfc) = await DosAsync();
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            var porId = await db.Clientes.AsNoTracking().Where(x => x.Id == a || x.Id == b).ToDictionaryAsync(x => x.Id, x => x.Rfc);
            Assert.Null(porId[b]); // el segundo de A+W llega sin RFC
        }

        Assert.Equal(HttpStatusCode.OK, (await Unificar(http, rfc, b)).StatusCode);

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var rfcs = await db2.Clientes.AsNoTracking().Where(x => x.Id == a || x.Id == b).ToDictionaryAsync(x => x.Id, x => x.Rfc);
        Assert.Equal(rfc, rfcs[b]);
        Assert.Null(rfcs[a]);
    }

    [Fact]
    public async Task Unificar_con_pedido_erp_agrupa_y_el_secundario_sigue_activo()
    {
        var (a, b, _, _, rfc) = await DosAsync();
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var fac = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            fac.Add(PedidoFacturable.CrearManual(Empresa, $"FIX-UNI-{Guid.NewGuid():N}", TestComprasFixtures.SucursalMid, b, "Cliente ficticio",
                1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, null, ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio, null, null));
            await fac.SaveChangesAsync();
        }
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);

        var g = await BuscarGrupoAsync(http, rfc);
        Assert.Equal(1, Cliente(g!.Value, b).GetProperty("pedidosErp").GetInt32());
        Assert.Equal(b, g.Value.GetProperty("principalSugeridoId").GetGuid()); // más pedidos en el ERP
        Assert.Equal("Principal", Cliente(g.Value, b).GetProperty("accionSugerida").GetString());
        Assert.Equal("Fusionar", Cliente(g.Value, a).GetProperty("accionSugerida").GetString());

        // Se elige a (sin historial) como principal: b conserva su historial, no se inactiva (Agrupar). El servidor decide.
        var r = await Unificar(http, rfc, a);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(("Activo", (Guid?)a), await EstadoAsync(b));
    }

    private async Task<List<CambioMaestroAw>> ConflictosRfcAsync(string referencia)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().CambiosMaestrosAw.AsNoTracking()
            .Where(c => c.ReferenciaExterna == referencia && c.Tipo == TipoCambioMaestroAw.RfcCompartido).ToListAsync();
    }

    [Fact]
    public async Task La_sincronizacion_crea_el_conflicto_rfc_pendiente_con_el_rfc_en_valor_despues_y_unificar_lo_resuelve()
    {
        var (a, _, refA, refB, rfc) = await DosAsync();
        var c = Assert.Single(await ConflictosRfcAsync(refB));
        Assert.Equal((EstadoCambioMaestroAw.Pendiente, rfc), (c.Estado, c.ValorDespues));
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);

        Assert.Equal(HttpStatusCode.OK, (await Unificar(http, rfc, a)).StatusCode);

        var resuelto = Assert.Single(await ConflictosRfcAsync(refB));
        Assert.Equal((EstadoCambioMaestroAw.Resuelto, "Unificado"), (resuelto.Estado, resuelto.Resolucion));
        Assert.NotNull(resuelto.ResueltoEnUtc); Assert.NotNull(resuelto.ResueltoPor);
        Assert.DoesNotContain(await ConflictosRfcAsync(refA), x => x.Estado == EstadoCambioMaestroAw.Pendiente);
    }

    [Fact]
    public async Task Obtener_por_rfc_devuelve_el_grupo_y_404_si_es_generico_o_no_se_repite()
    {
        var (a, b, _, _, rfc) = await DosAsync();
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);

        var r = await http.GetAsync($"{Base}/{rfc}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal(rfc, doc.RootElement.GetProperty("rfc").GetString());
        Assert.Equal(new[] { a, b }.Order(), doc.RootElement.GetProperty("clientes").EnumerateArray().Select(x => x.GetProperty("clienteId").GetGuid()).Order());

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"{Base}/XAXX010101000")).StatusCode);
        var solo = NuevoRfc(); await CrearAsync(NuevaRef(), solo);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"{Base}/{solo}")).StatusCode);
    }

    [Fact]
    public async Task Sin_permiso_unificar_es_403_y_no_cambia_nada()
    {
        var (a, b, _, _, rfc) = await DosAsync();
        var http = await UsuariosAwHttp.ConPermisosAsync(factory, PermisosCanonicos.DatosMaestrosClientesSincronizar);

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(Base)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Unificar(http, rfc, a)).StatusCode);
        Assert.Equal(("Activo", (Guid?)null), await EstadoAsync(b));

        var solo = await UsuariosAwHttp.ConPermisosAsync(factory, PermisosCanonicos.DatosMaestrosClientesUnificar);
        Assert.Equal(HttpStatusCode.Forbidden, (await solo.GetAsync(Base)).StatusCode);
    }

    [Fact]
    public async Task Unificar_con_principal_fuera_del_grupo_rfc_generico_o_motivo_vacio_es_422()
    {
        var (a, _, _, _, rfc) = await DosAsync();
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);

        var fuera = await Unificar(http, rfc, await CrearAsync(NuevaRef(), NuevoRfc()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, fuera.StatusCode);
        Assert.Contains("CLIENTE_UNIFICAR_PRINCIPAL_FUERA_DE_GRUPO", await fuera.Content.ReadAsStringAsync());
        var generico = await Unificar(http, "XAXX010101000", a);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, generico.StatusCode);
        var motivo = await Unificar(http, rfc, a, motivo: "  ");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, motivo.StatusCode);
    }

    [Fact]
    public async Task Despues_de_unificar_la_referencia_secundaria_resuelve_al_principal()
    {
        var (a, b, refA, refB, rfc) = await DosAsync();
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);
        using var scope = factory.Services.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IClientesReadPort>();
        Assert.Equal(b, (await port.ResolverPorReferenciaAsync(refB, CancellationToken.None))!.ClienteId);

        Assert.Equal(HttpStatusCode.OK, (await Unificar(http, rfc, a)).StatusCode);

        Assert.Equal(a, (await port.ResolverPorReferenciaAsync(refB, CancellationToken.None))!.ClienteId);
        Assert.Equal(a, (await port.ResolverPorReferenciaAsync(refA, CancellationToken.None))!.ClienteId);
    }

    [Fact]
    public async Task Una_corrida_de_sincronizacion_no_reactiva_a_un_fusionado()
    {
        var mapeo = new MapeoEstatusClientesAwOptions { Version = "t1", KzGesperrt = { [0] = false, [1] = true } };
        string rfc = NuevoRfc(), refA = NuevaRef(), refB = NuevaRef();
        var a = await CrearAsync(refA, rfc);
        var b = await CrearAsync(refB, rfc, mapeo, bloqueo: 1); // A+W lo bloqueó: Inactivo por A+W
        Assert.Equal("Inactivo", (await EstadoAsync(b)).Estatus);
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await Unificar(http, rfc, a)).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        await new AplicarClienteAwService(db, Options.Create(mapeo))
            .AplicarAsync(Snap(refB, rfc) with { BloqueoOrigenCrudo = 0, LeidoEnUtc = DateTime.UtcNow.AddMinutes(5) }, CancellationToken.None);

        Assert.Equal(("Inactivo", (Guid?)a), await EstadoAsync(b));
    }
}
