using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>ADM-06: dos clientes de A+W con la misma clave fiscal no se fusionan y dejan un conflicto resoluble.</summary>
public sealed class ConflictosClientesAwTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Base = "/api/v1/datos-maestros/clientes/conflictos-aw";
    private static string NuevaRef() => "T" + Guid.NewGuid().ToString("N")[..8];
    private static string NuevoRfc() => "ZZZ" + Random.Shared.Next(0, 1_000_000).ToString("D6") + Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();

    private static AplicarClienteAwSnapshot Snap(string referencia, string? rfc, string? ust = null) => new(
        referencia, "CLIENTE DEMO CONFLICTO", DateTime.UtcNow, "1", "0-borrador",
        Rfc: rfc, CandidatoFiscalUstId: ust, MonedaDefault: "MXN", MonedaNormalizada: "MXN", MonedaCodigoOrigen: "MXN");

    private async Task<AplicarClienteAwResultado> Aplicar(AplicarClienteAwSnapshot s)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AplicarClienteAwService>().AplicarAsync(s, CancellationToken.None);
    }

    private async Task<List<CambioMaestroAw>> Conflictos(string referencia)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().CambiosMaestrosAw.AsNoTracking()
            .Where(c => c.ReferenciaExterna == referencia && c.Tipo == TipoCambioMaestroAw.RfcCompartido).ToListAsync();
    }

    private async Task<(string Ref1, string Ref2)> DosClientesAsync(bool modoReal)
    {
        var rfc = NuevoRfc(); var r1 = NuevaRef(); var r2 = NuevaRef();
        string? Ust() => $"{rfc[..3]}-{rfc[3..9]}-{rfc[9..]}";
        var a = await Aplicar(modoReal ? Snap(r1, null, Ust()) : Snap(r1, rfc));
        var b = await Aplicar((modoReal ? Snap(r2, null, Ust()) : Snap(r2, rfc)) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(1) });
        Assert.NotEqual(a.Cliente!.Id, b.Cliente!.Id);
        Assert.Empty(await Conflictos(r1));
        return (r1, r2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mismo_rfc_en_dos_clientes_aw_crea_ambos_y_un_conflicto_pendiente_sin_duplicarse(bool modoReal)
    {
        var (r1, r2) = await DosClientesAsync(modoReal);
        var c = Assert.Single(await Conflictos(r2));
        Assert.Equal(EstadoCambioMaestroAw.Pendiente, c.Estado); // decisión 22-oct: entre clientes de A+W también va a revisión (doc 05 §10.8)
        Assert.Contains(r2, c.Causa);
        Assert.Contains("AW-" + r1, c.Causa);

        await Aplicar((modoReal ? Snap(r2, null, $"X") : Snap(r2, "XAXX010101000")) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(2) });
        Assert.Single(await Conflictos(r2));

        // Reenvío idéntico del segundo cliente (misma clave fiscal): no crea otro conflicto.
        var rfc = c.ValorDespues!;
        await Aplicar((modoReal ? Snap(r2, null, $"{rfc[..3]}-{rfc[3..9]}-{rfc[9..]}") : Snap(r2, rfc)) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(3) });
        await Aplicar((modoReal ? Snap(r2, null, $"{rfc[..3]}-{rfc[3..9]}-{rfc[9..]}") : Snap(r2, rfc)) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(4) });
        Assert.Single(await Conflictos(r2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Al_actualizar_un_cliente_aw_con_un_rfc_ocupado_por_otro_aw_registra_conflicto_pendiente_y_no_fusiona(bool modoReal)
    {
        var rfc = NuevoRfc(); var r1 = NuevaRef(); var r2 = NuevaRef();
        string Ust() => $"{rfc[..3]}-{rfc[3..9]}-{rfc[9..]}";
        var a = await Aplicar(modoReal ? Snap(r1, null, Ust()) : Snap(r1, rfc));
        var b = await Aplicar(Snap(r2, null) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(1) });
        Assert.Empty(await Conflictos(r2));

        var actualizado = await Aplicar((modoReal ? Snap(r2, null, Ust()) : Snap(r2, rfc)) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(2) });
        Assert.Equal(AplicarClienteAwAccion.Actualizado, actualizado.Accion);
        Assert.Equal(b.Cliente!.Id, actualizado.Cliente!.Id); Assert.NotEqual(a.Cliente!.Id, actualizado.Cliente.Id);
        Assert.Null(actualizado.Cliente.Rfc); // no se completa un RFC que ya ocupa otro cliente
        var c = Assert.Single(await Conflictos(r2));
        Assert.Equal(EstadoCambioMaestroAw.Pendiente, c.Estado); Assert.Contains("AW-" + r1, c.Causa);

        await Aplicar((modoReal ? Snap(r2, null, Ust()) : Snap(r2, rfc)) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(3), NombreComercialOrigen = "OTRO NOMBRE" });
        Assert.Single(await Conflictos(r2));
    }

    [Fact]
    public async Task Al_actualizar_un_cliente_aw_con_un_rfc_de_un_cliente_manual_registra_conflicto_y_no_completa_el_rfc()
    {
        var (r, rfc) = await ManualYAwAsync();
        var c = Assert.Single(await Conflictos(r));
        Assert.Equal(EstadoCambioMaestroAw.Pendiente, c.Estado); Assert.Equal(rfc, c.ValorDespues);
    }

    /// <summary>Cliente manual con un RFC y un cliente de A+W que después trae ese mismo RFC: conflicto pendiente de revisión.</summary>
    private async Task<(string Referencia, string Rfc)> ManualYAwAsync()
    {
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);
        var rfc = NuevoRfc(); var r = NuevaRef();
        var alta = await http.PostAsJsonAsync("/api/v1/datos-maestros/clientes", new
        {
            clave = "M" + Guid.NewGuid().ToString("N")[..10], razonSocial = "CLIENTE MANUAL CONFLICTO", rfc, regimenFiscal = "601",
            codigoPostalFiscal = "97000", usoCfdiDefault = "G03", formaPagoDefault = "03", metodoPagoDefault = "PUE", monedaDefault = "MXN",
            clasificacionId = "00000012-0001-0000-0000-000000000001",
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var manualId = JsonDocument.Parse(await alta.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var creado = await Aplicar(Snap(r, null));
        var res = await Aplicar(Snap(r, rfc) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(1) });
        Assert.Equal(AplicarClienteAwAccion.Actualizado, res.Accion);
        Assert.Equal(creado.Cliente!.Id, res.Cliente!.Id); Assert.NotEqual(manualId, res.Cliente.Id);
        Assert.Null(res.Cliente.Rfc);
        return (r, rfc);
    }

    [Fact]
    public async Task Resolver_mantener_separados_es_204_version_vieja_409_y_aplicar_aw_422()
    {
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);
        var (r2, _) = await ManualYAwAsync();
        var version = (await Conflictos(r2)).Single().Version;
        var id = (await Conflictos(r2)).Single().Id;

        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync($"{Base}/{id}/resolver", new { resolucion = "MantenerSeparados", version })).StatusCode);
        var resuelto = (await Conflictos(r2)).Single();
        Assert.Equal(EstadoCambioMaestroAw.Resuelto, resuelto.Estado); Assert.Equal("MantenerSeparados", resuelto.Resolucion);
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"{Base}/{id}/resolver", new { resolucion = "MantenerSeparados", version })).StatusCode);

        var (r4, _) = await ManualYAwAsync();
        var otro = (await Conflictos(r4)).Single();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await http.PostAsJsonAsync($"{Base}/{otro.Id}/resolver", new { resolucion = "AplicarAw", version = otro.Version })).StatusCode);

        var (_, r6) = await DosClientesAsync(false);
        var entreAw = (await Conflictos(r6)).Single();
        var lista = await http.GetFromJsonAsync<JsonElement>($"{Base}?estado=Pendiente");
        Assert.Contains(lista.EnumerateArray(), e => e.GetProperty("id").GetGuid() == otro.Id);
        Assert.Contains(lista.EnumerateArray(), e => e.GetProperty("id").GetGuid() == entreAw.Id); // decisión 22-oct: entre clientes de A+W también va a la bandeja
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync($"{Base}/{entreAw.Id}/resolver", new { resolucion = "MantenerSeparados", version = entreAw.Version })).StatusCode);
    }

    [Fact]
    public async Task Sin_clientes_sincronizar_es_403()
    {
        var sinPermiso = await UsuariosAwHttp.ConPermisosAsync(factory, PermisosCanonicos.DatosMaestrosClientesOrigenVer);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync(Base)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.PostAsJsonAsync($"{Base}/{Guid.NewGuid()}/resolver", new { resolucion = "MantenerSeparados", version = 1 })).StatusCode);
    }
}
