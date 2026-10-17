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
        Assert.Equal(EstadoCambioMaestroAw.Pendiente, c.Estado);
        Assert.Contains(r2, c.Causa);
        Assert.Contains("AW-" + r1, c.Causa);

        await Aplicar((modoReal ? Snap(r2, null, $"X") : Snap(r2, "XAXX010101000")) with { LeidoEnUtc = DateTime.UtcNow.AddSeconds(2) });
        Assert.Single(await Conflictos(r2));
    }

    [Fact]
    public async Task Resolver_mantener_separados_es_204_version_vieja_409_y_aplicar_aw_422()
    {
        var http = await UsuariosAwHttp.SuperAdminAsync(factory);
        var (_, r2) = await DosClientesAsync(false);
        var version = (await Conflictos(r2)).Single().Version;
        var id = (await Conflictos(r2)).Single().Id;

        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync($"{Base}/{id}/resolver", new { resolucion = "MantenerSeparados", version })).StatusCode);
        var resuelto = (await Conflictos(r2)).Single();
        Assert.Equal(EstadoCambioMaestroAw.Resuelto, resuelto.Estado); Assert.Equal("MantenerSeparados", resuelto.Resolucion);
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync($"{Base}/{id}/resolver", new { resolucion = "MantenerSeparados", version })).StatusCode);

        var (_, r4) = await DosClientesAsync(false);
        var otro = (await Conflictos(r4)).Single();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await http.PostAsJsonAsync($"{Base}/{otro.Id}/resolver", new { resolucion = "AplicarAw", version = otro.Version })).StatusCode);

        var lista = await http.GetFromJsonAsync<JsonElement>($"{Base}?estado=Pendiente");
        Assert.Contains(lista.EnumerateArray(), e => e.GetProperty("id").GetGuid() == otro.Id);
    }

    [Fact]
    public async Task Sin_clientes_sincronizar_es_403()
    {
        var sinPermiso = await UsuariosAwHttp.ConPermisosAsync(factory, PermisosCanonicos.DatosMaestrosClientesOrigenVer);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync(Base)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.PostAsJsonAsync($"{Base}/{Guid.NewGuid()}/resolver", new { resolucion = "MantenerSeparados", version = 1 })).StatusCode);
    }
}
