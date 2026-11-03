using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// F1-MP-05 S1 (A4.2) sobre HTTP y PostgreSQL real: el lote del proveedor se captura en la línea de OC (POST/PATCH), el detalle lo
/// devuelve recortado, se puede limpiar, se rechaza fuera de Borrador/Rechazada y duplicar la OC no lo copia.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class OcLineaLoteProveedorTests(WebApplicationFactory<Program> factory)
{
    private const string EndpointBase = "/api/v1/compras/ordenes";
    private static readonly Guid Insumo = Guid.Parse("00000007-0001-0000-0000-000000000001");
    private static readonly Guid Depto = Guid.Parse("00000004-0001-0000-0000-000000000001");
    private static readonly Guid CentroCosto = Guid.Parse("0000000c-0005-0000-0000-000000000001");
    private static readonly Guid Proveedor = Guid.Parse("00000005-0001-0000-0000-000000000001");

    [Fact]
    public async Task Patch_guarda_el_lote_recortado_el_detalle_lo_devuelve_y_limpiar_lo_borra()
    {
        var client = await ClienteAsync();
        var oc = await CrearOcAsync(client);
        var linea = await AgregarLineaAsync(client, oc, lote: null);
        (await Linea(client, oc, linea)).GetProperty("loteProveedor").ValueKind.Should().Be(JsonValueKind.Null);

        (await client.PatchAsJsonAsync($"{EndpointBase}/{oc}/lineas/{linea}", new { LoteProveedor = " L-7781 " }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Linea(client, oc, linea)).GetProperty("loteProveedor").GetString().Should().Be("L-7781");

        // PATCH sin el campo no lo toca.
        (await client.PatchAsJsonAsync($"{EndpointBase}/{oc}/lineas/{linea}", new { Cantidad = 3m })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Linea(client, oc, linea)).GetProperty("loteProveedor").GetString().Should().Be("L-7781");

        (await client.PatchAsJsonAsync($"{EndpointBase}/{oc}/lineas/{linea}", new { LimpiarLoteProveedor = true }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Linea(client, oc, linea)).GetProperty("loteProveedor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Post_de_linea_con_lote_lo_guarda_y_uno_de_51_caracteres_se_rechaza()
    {
        var client = await ClienteAsync();
        var oc = await CrearOcAsync(client);
        var linea = await AgregarLineaAsync(client, oc, lote: "A-9");
        (await Linea(client, oc, linea)).GetProperty("loteProveedor").GetString().Should().Be("A-9");

        var largo = new string('L', 51);
        var patch = await client.PatchAsJsonAsync($"{EndpointBase}/{oc}/lineas/{linea}", new { LoteProveedor = largo });
        patch.StatusCode.Should().Be(HttpStatusCode.BadRequest, await patch.Content.ReadAsStringAsync());
        var post = await client.PostAsJsonAsync($"{EndpointBase}/{oc}/lineas", NuevaLinea(largo));
        post.StatusCode.Should().Be(HttpStatusCode.BadRequest, await post.Content.ReadAsStringAsync());
        (await Linea(client, oc, linea)).GetProperty("loteProveedor").GetString().Should().Be("A-9");
    }

    [Fact]
    public async Task Con_la_OC_autorizada_editar_el_lote_da_422()
    {
        var client = await ClienteAsync();
        var oc = await CrearOcAsync(client);
        var linea = await AgregarLineaAsync(client, oc, lote: "L-1");
        await CambiarEstadoAsync(oc, EstadoOrdenCompra.Autorizada);

        var respuesta = await client.PatchAsJsonAsync($"{EndpointBase}/{oc}/lineas/{linea}", new { LoteProveedor = "L-2" });
        var json = await respuesta.Content.ReadAsStringAsync();
        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, json);
        JsonDocument.Parse(json).RootElement.GetProperty("code").GetString().Should().Be("OC_LINEAS_SOLO_EN_BORRADOR_O_RECHAZADA");
        (await Linea(client, oc, linea)).GetProperty("loteProveedor").GetString().Should().Be("L-1");
    }

    [Fact]
    public async Task Duplicar_una_OC_no_copia_el_lote()
    {
        var client = await ClienteAsync();
        var oc = await CrearOcAsync(client);
        await AgregarLineaAsync(client, oc, lote: "L-7781");
        await CambiarEstadoAsync(oc, EstadoOrdenCompra.Cancelada);

        var dup = await client.PostAsJsonAsync($"{EndpointBase}/{oc}/duplicar",
            new { SucursalCodigo = "MID", FolioAnio = (short)2026, FechaDocumento = DateOnly.FromDateTime(DateTime.UtcNow) });
        dup.StatusCode.Should().Be(HttpStatusCode.Created, await dup.Content.ReadAsStringAsync());
        var nueva = (await dup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ordenCompraNuevaId").GetGuid();

        var lineas = (await client.GetFromJsonAsync<JsonElement>($"{EndpointBase}/{nueva}")).GetProperty("lineas").EnumerateArray().ToList();
        lineas.Should().ContainSingle();
        lineas[0].GetProperty("loteProveedor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private async Task CambiarEstadoAsync(Guid oc, EstadoOrdenCompra estado)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var entidad = await db.OrdenesCompra.FirstAsync(o => o.Id == oc);
        db.Entry(entidad).Property(nameof(OrdenCompra.Estado)).CurrentValue = estado;
        await db.SaveChangesAsync();
    }

    private static object NuevaLinea(string? lote) => new
    {
        ArticuloId = Insumo,
        Cantidad = 5m,
        UnidadMedida = "PZA",
        PrecioUnitario = 100m,
        DepartamentoSolicitanteId = Depto,
        CentroCostoId = CentroCosto,
        LoteProveedor = lote,
    };

    private static async Task<Guid> AgregarLineaAsync(HttpClient client, Guid oc, string? lote)
    {
        var respuesta = await client.PostAsJsonAsync($"{EndpointBase}/{oc}/lineas", NuevaLinea(lote));
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());
        return (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lineaId").GetGuid();
    }

    private static async Task<JsonElement> Linea(HttpClient client, Guid oc, Guid linea) =>
        (await client.GetFromJsonAsync<JsonElement>($"{EndpointBase}/{oc}")).GetProperty("lineas").EnumerateArray()
            .Single(l => l.GetProperty("id").GetGuid() == linea).Clone();

    private static async Task<Guid> CrearOcAsync(HttpClient client)
    {
        var respuesta = await client.PostAsJsonAsync(EndpointBase, new
        {
            SucursalDestinoId = TestComprasFixtures.SucursalMid,
            SucursalCodigo = "MID",
            FolioAnio = (short)2026,
            ProveedorId = Proveedor,
            CondicionesPagoId = Guid.CreateVersion7(),
            UsoPrincipalId = Guid.CreateVersion7(),
            FechaDocumento = DateOnly.FromDateTime(DateTime.UtcNow),
            Moneda = "MXN",
            TipoCambio = (decimal?)null,
            SinRequisicionPrevia = true,
            EsImportacion = false,
            CotizacionExcepcionada = false,
            Observaciones = "OC lote proveedor",
            MotivoSinRequisicion = "Prueba lote del proveedor",
            FechaEntregaEsperada = (DateOnly?)null,
            EncargadoComprasId = (Guid?)null,
            OcOrigenId = (Guid?)null,
        });
        respuesta.EnsureSuccessStatusCode();
        return (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<HttpClient> ClienteAsync()
    {
        var client = factory.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login",
            new { EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "Super Admin Dev", EmpresaId = (Guid?)null });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
