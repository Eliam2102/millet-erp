using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>ADM-06: bitácora de cambios por campo de clientes A+W, matriz campo → dueño y su lectura por API.</summary>
[Collection(ApiCompartida.Nombre)]
public sealed class BitacoraClientesAwTests(WebApplicationFactory<Program> factory)
{
    private static string NuevaRef() => "T" + Guid.NewGuid().ToString("N")[..8];

    private static AplicarClienteAwSnapshot Snap(string referencia, string nombre = "DEMO NAME1") => new(
        referencia, "CLIENTE DEMO BITACORA", DateTime.UtcNow, "1", "0-borrador",
        Rfc: "XAXX010101000", CodigoPostalFiscal: "06600",
        MandantOrigen: 1, NombreComercialOrigen: nombre,
        DomicilioCalle: "CALLE DEMO 1", DomicilioCiudad: "CIUDAD DEMO", DomicilioCp: "06600", DomicilioProvincia: "PROV", DomicilioPais: "MX",
        CondicionCodigoOrigen: "Z030", CondicionNumeroOrigen: 30, DiasNominalesOrigen: 30,
        MonedaCodigoOrigen: "MXN", MonedaNormalizada: "MXN", MonedaDefault: "MXN", CreditoReferenciaLimite: 1000m);

    private async Task<T> ConDbAsync<T>(Func<CompartidoDbContext, Task<T>> f)
    {
        using var scope = factory.Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<CompartidoDbContext>());
    }

    private Task<AplicarClienteAwResultado> Aplicar(AplicarClienteAwSnapshot s, MapeoEstatusClientesAwOptions? mapeo = null) =>
        ConDbAsync(db => new AplicarClienteAwService(db, mapeo is null ? null : Options.Create(mapeo)).AplicarAsync(s, CancellationToken.None));

    private Task<List<CambioMaestroAw>> Cambios(string referencia) =>
        ConDbAsync(db => db.CambiosMaestrosAw.AsNoTracking().Where(c => c.ReferenciaExterna == referencia).OrderBy(c => c.OcurridoEnUtc).ToListAsync());

    [Fact]
    public async Task Cambio_de_nombre_de_origen_queda_una_fila_y_reaplicar_no_duplica()
    {
        var r = NuevaRef();
        await Aplicar(Snap(r));
        var cambiado = Snap(r, "DEMO NAME1 NORTE") with { LeidoEnUtc = DateTime.UtcNow.AddMinutes(1) };
        await Aplicar(cambiado);
        var filas = await Cambios(r);
        var fila = Assert.Single(filas);
        Assert.Equal("Nombre de origen", fila.Campo); Assert.Equal(TipoCambioMaestroAw.Aplicado, fila.Tipo);
        Assert.Equal("DEMO NAME1", fila.ValorAntes); Assert.Equal("DEMO NAME1 NORTE", fila.ValorDespues);
        var hash = await ConDbAsync(db => db.ClientesSincronizacionAw.AsNoTracking().Where(x => x.ReferenciaExterna == r).Select(x => x.HashOrigen).SingleAsync());
        Assert.Equal(hash, fila.VersionEvento);

        await Aplicar(cambiado with { LeidoEnUtc = DateTime.UtcNow.AddMinutes(2) });
        Assert.Single(await Cambios(r));
    }

    [Fact]
    public async Task Bloqueo_y_desbloqueo_dejan_baja_y_reactivacion_sobre_el_mismo_cliente()
    {
        var r = NuevaRef();
        var mapeo = new MapeoEstatusClientesAwOptions { Version = "DEMO-v1", KzGesperrt = new() { [0] = false, [1] = true } };
        var alta = await Aplicar(Snap(r) with { BloqueoOrigenCrudo = 0 }, mapeo);
        await Aplicar(Snap(r) with { BloqueoOrigenCrudo = 1, LeidoEnUtc = DateTime.UtcNow.AddMinutes(1) }, mapeo);
        var desbloqueo = await Aplicar(Snap(r) with { BloqueoOrigenCrudo = 0, LeidoEnUtc = DateTime.UtcNow.AddMinutes(2) }, mapeo);
        Assert.Equal(alta.Cliente!.Id, desbloqueo.Cliente!.Id);
        Assert.Equal(AplicarClienteAwAccion.Actualizado, desbloqueo.Accion);
        Assert.Equal(EstatusCatalogo.Activo, desbloqueo.Cliente.Estatus);
        var estatus = (await Cambios(r)).Where(c => c.Campo == "Estatus").OrderBy(c => c.OcurridoEnUtc).ToList();
        Assert.Equal([TipoCambioMaestroAw.Baja, TipoCambioMaestroAw.Reactivacion], estatus.Select(c => c.Tipo));
        Assert.Equal(["Activo", "Inactivo"], estatus.Select(c => c.ValorAntes));
        Assert.All(estatus, c => Assert.Equal(EstadoCambioMaestroAw.Informativo, c.Estado));
        Assert.Equal(2, (await Cambios(r)).Count(c => c.Campo == "Bloqueo A+W")); // 0→1 y 1→0
    }

    [Fact]
    public async Task Bloqueo_desbloqueo_bloqueo_deja_dos_bajas_y_un_reenvio_identico_no_agrega_filas()
    {
        var r = NuevaRef();
        var mapeo = new MapeoEstatusClientesAwOptions { Version = "DEMO-v1", KzGesperrt = new() { [0] = false, [1] = true } };
        await Aplicar(Snap(r) with { BloqueoOrigenCrudo = 0 }, mapeo);
        await Aplicar(Snap(r) with { BloqueoOrigenCrudo = 1, LeidoEnUtc = DateTime.UtcNow.AddMinutes(1) }, mapeo);
        await Aplicar(Snap(r) with { BloqueoOrigenCrudo = 0, LeidoEnUtc = DateTime.UtcNow.AddMinutes(2) }, mapeo);
        var segundoBloqueo = Snap(r) with { BloqueoOrigenCrudo = 1, LeidoEnUtc = DateTime.UtcNow.AddMinutes(3) };
        var res = await Aplicar(segundoBloqueo, mapeo);
        Assert.Equal(EstatusCatalogo.Inactivo, res.Cliente!.Estatus);

        var antes = await Cambios(r);
        Assert.Equal(2, antes.Count(c => c.Campo == "Estatus" && c.Tipo == TipoCambioMaestroAw.Baja));
        Assert.Equal(1, antes.Count(c => c.Campo == "Estatus" && c.Tipo == TipoCambioMaestroAw.Reactivacion));

        await Aplicar(segundoBloqueo with { LeidoEnUtc = DateTime.UtcNow.AddMinutes(4) }, mapeo);
        Assert.Equal(antes.Count, (await Cambios(r)).Count);
    }

    [Fact]
    public async Task Diferencias_en_campos_de_dueno_erp_quedan_en_la_bitacora_como_informativas_sin_duplicarse()
    {
        var r = NuevaRef();
        await Aplicar(Snap(r));
        var distinta = Snap(r, "DEMO NAME1 NORTE") with { RazonSocial = "OTRA RAZON SOCIAL A+W", LeidoEnUtc = DateTime.UtcNow.AddMinutes(1) };
        var res = await Aplicar(distinta);
        Assert.Equal(AplicarClienteAwAccion.Actualizado, res.Accion);

        var dif = Assert.Single(await Cambios(r), c => c.Tipo == TipoCambioMaestroAw.Diferencia);
        Assert.Equal("Razón social", dif.Campo); Assert.Equal("ERP", dif.Dueno); Assert.Equal(EstadoCambioMaestroAw.Informativo, dif.Estado);
        Assert.Equal("CLIENTE DEMO BITACORA", dif.ValorAntes); Assert.Equal("OTRA RAZON SOCIAL A+W", dif.ValorDespues);
        var hash = await ConDbAsync(db => db.ClientesSincronizacionAw.AsNoTracking().Where(x => x.ReferenciaExterna == r).Select(x => x.HashOrigen).SingleAsync());
        Assert.Equal(hash, dif.VersionEvento);
        Assert.Equal("CLIENTE DEMO BITACORA", res.Cliente!.RazonSocial); // el ERP conserva

        var total = (await Cambios(r)).Count;
        await Aplicar(distinta with { LeidoEnUtc = DateTime.UtcNow.AddMinutes(2) });
        await Aplicar(distinta with { LeidoEnUtc = DateTime.UtcNow.AddMinutes(3) });
        Assert.Equal(total, (await Cambios(r)).Count);

        var admin = await UsuariosAwHttp.SuperAdminAsync(factory);
        var lista = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/datos-maestros/clientes/{res.Cliente.Id}/cambios-aw");
        Assert.Contains(lista.EnumerateArray(), e => e.GetProperty("tipo").GetString() == "Diferencia");
    }

    [Fact]
    public async Task Matriz_expone_version_y_17_campos()
    {
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);
        using var j = JsonDocument.Parse(await http.GetStringAsync("/api/v1/datos-maestros/sincronizacion-aw/matriz"));
        Assert.Equal("0-propuesta", j.RootElement.GetProperty("version").GetString());
        Assert.Equal(17, j.RootElement.GetProperty("campos").GetArrayLength());

        var sinPermiso = await UsuariosAwHttp.ConPermisosAsync(factory, PermisosCanonicos.DatosMaestrosClientesSincronizar);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync("/api/v1/datos-maestros/sincronizacion-aw/matriz")).StatusCode);
    }

    [Fact]
    public async Task Cambios_aw_sin_origen_ver_responde_403()
    {
        var r = NuevaRef();
        var id = (await Aplicar(Snap(r))).Cliente!.Id;
        var sinPermiso = await UsuariosAwHttp.ConPermisosAsync(factory, PermisosCanonicos.DatosMaestrosClientesSincronizar);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync($"/api/v1/datos-maestros/clientes/{id}/cambios-aw")).StatusCode);
        var admin = await UsuariosAwHttp.SuperAdminAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/datos-maestros/clientes/{id}/cambios-aw")).StatusCode);
    }
}

/// <summary>Autenticación de pruebas ADM-06: superadmin y un usuario con solo los permisos indicados.</summary>
internal static class UsuariosAwHttp
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");

    public static Task<HttpClient> SuperAdminAsync(WebApplicationFactory<Program> factory) => LoginAsync(factory, "dev-superadmin", "superadmin@dev.local");

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory, string oid, string email)
    {
        var client = factory.CreateClientWithIdempotency();
        var resp = await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null });
        resp.EnsureSuccessStatusCode();
        using var j = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", j.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    public static async Task<HttpClient> ConPermisosAsync(WebApplicationFactory<Program> factory, params string[] codigos)
    {
        var admin = await SuperAdminAsync(factory);
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"aw-test-{sufijo}"; var email = $"aw-test-{sufijo}@test.local";
        var primer = factory.CreateClientWithIdempotency();
        var login = await primer.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null });
        login.EnsureSuccessStatusCode();
        using var pj = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var usuarioId = pj.RootElement.GetProperty("usuario").GetProperty("id").GetGuid();
        var rol = await admin.PostAsJsonAsync("/api/v1/identidad/roles", new { Id = Guid.Empty, Codigo = $"rol-aw-{sufijo}", Nombre = $"Rol AW {sufijo}", Descripcion = (string?)null });
        rol.EnsureSuccessStatusCode();
        using var rj = JsonDocument.Parse(await rol.Content.ReadAsStringAsync());
        var rolId = rj.RootElement.GetProperty("id").GetGuid();
        var ids = codigos.Select(c => PermisosCanonicos.Todos.First(p => p.Codigo == c).Id).ToArray();
        (await admin.PutAsJsonAsync($"/api/v1/identidad/roles/{rolId}/permisos", new { PermisoIds = ids })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/identidad/usuarios/{usuarioId}/asignaciones", new { EmpresaId = Empresa, RolId = rolId })).EnsureSuccessStatusCode();
        return await LoginAsync(factory, oid, email);
    }
}
