using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.DatosMaestros.Application.Catalogos;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// F1-MP-01 S1 (E1, A4.1-b) sobre HTTP y PostgreSQL real: la línea de OC de un artículo de vidrio crudo (ligado a A+W) exige la
/// medida de la hoja; la de un insumo la rechaza; hojas enteras y unidad M2; una línea nacida de RQ (sin medida) bloquea
/// «Enviar a autorización» hasta que el comprador elige la medida; duplicar copia la medida; el endpoint de medidas del artículo.
/// </summary>
public sealed class OcLineaMedidaTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string EndpointBase = "/api/v1/compras/ordenes";
    private static readonly Guid Insumo = Guid.Parse("00000007-0001-0000-0000-000000000001");
    private static readonly Guid Depto = Guid.Parse("00000004-0001-0000-0000-000000000001");
    private static readonly Guid CentroCosto = Guid.Parse("0000000c-0005-0000-0000-000000000001");
    private static readonly Guid Proveedor = Guid.Parse("00000005-0001-0000-0000-000000000001");
    private const decimal HojaA = 8.9304m; // 2440 x 3660
    private const decimal HojaB = 7.0620m; // 2140 x 3300

    [Fact]
    public async Task Vidrio_sin_medida_da_422_y_el_insumo_con_medida_da_422()
    {
        var (client, vidrio, varA, _) = await SembrarAsync();
        var oc = await CrearOcAsync(client, sinRq: true);

        var sin = await client.PostAsJsonAsync($"{EndpointBase}/{oc}/lineas", Linea(vidrio, 20 * HojaA, "M2", null));
        await AssertCodigo(sin, "VARIANTE_VIDRIO_REQUERIDA");

        var insumoConMedida = await client.PostAsJsonAsync($"{EndpointBase}/{oc}/lineas", Linea(Insumo, 1m, "PZA", varA));
        await AssertCodigo(insumoConMedida, "VARIANTE_NO_APLICA");

        var ajena = await client.PostAsJsonAsync($"{EndpointBase}/{oc}/lineas", Linea(vidrio, 20 * HojaA, "M2", Guid.NewGuid()));
        await AssertCodigo(ajena, "VARIANTE_NO_PERTENECE_AL_ARTICULO");

        var fraccion = await client.PostAsJsonAsync($"{EndpointBase}/{oc}/lineas", Linea(vidrio, 100m, "M2", varA));
        await AssertCodigo(fraccion, "HOJAS_ENTERAS");

        var unidad = await client.PostAsJsonAsync($"{EndpointBase}/{oc}/lineas", Linea(vidrio, 20 * HojaA, "PZA", varA));
        await AssertCodigo(unidad, "LINEA_OC_VIDRIO_UNIDAD");

        (await Lineas(client, oc)).Should().BeEmpty();
    }

    [Fact]
    public async Task Vidrio_con_medida_y_hojas_enteras_se_guarda_y_el_detalle_la_expone()
    {
        var (client, vidrio, varA, varB) = await SembrarAsync();
        var oc = await CrearOcAsync(client, sinRq: true);

        (await client.PostAsJsonAsync($"{EndpointBase}/{oc}/lineas", Linea(vidrio, 20 * HojaA, "M2", varA))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync($"{EndpointBase}/{oc}/lineas", Linea(vidrio, 15 * HojaB, "M2", varB))).StatusCode.Should().Be(HttpStatusCode.Created);

        var lineas = await Lineas(client, oc);
        lineas.Select(l => (l.GetProperty("cantidad").GetDecimal(), l.GetProperty("anchoMm").GetDecimal(), l.GetProperty("altoMm").GetDecimal(),
                l.GetProperty("areaHojaM2").GetDecimal()))
            .Should().Equal((178.6080m, 2440m, 3660m, HojaA), (105.9300m, 2140m, 3300m, HojaB));

        // Editar la cantidad revalida hojas enteras; cambiar de artículo borra la medida y el vidrio nuevo la vuelve a exigir.
        var lineaA = lineas[0].GetProperty("id").GetGuid();
        await AssertCodigo(await client.PatchAsJsonAsync($"{EndpointBase}/{oc}/lineas/{lineaA}", new { Cantidad = 100m }), "HOJAS_ENTERAS");
        (await client.PatchAsJsonAsync($"{EndpointBase}/{oc}/lineas/{lineaA}", new { Cantidad = 21 * HojaA })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PatchAsJsonAsync($"{EndpointBase}/{oc}/lineas/{lineaA}", new { ArticuloId = Insumo, UnidadMedida = "PZA", Cantidad = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        var tras = (await Lineas(client, oc)).Single(l => l.GetProperty("id").GetGuid() == lineaA);
        tras.GetProperty("productoAwVarianteId").ValueKind.Should().Be(JsonValueKind.Null);
        tras.GetProperty("anchoMm").ValueKind.Should().Be(JsonValueKind.Null);

        // Medidas del artículo: las dos variantes de su producto A+W; un insumo no tiene.
        var medidas = await client.GetFromJsonAsync<JsonElement>($"/api/v1/catalogos/articulos/{vidrio}/medidas");
        medidas.EnumerateArray().Select(m => m.GetProperty("areaHojaM2").GetDecimal()).Should().BeEquivalentTo([HojaA, HojaB]);
        (await client.GetFromJsonAsync<JsonElement>($"/api/v1/catalogos/articulos/{Insumo}/medidas")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Duplicar_una_OC_cancelada_copia_la_medida_de_la_linea()
    {
        var (client, vidrio, varA, _) = await SembrarAsync();
        var oc = await CrearOcAsync(client, sinRq: true);
        (await client.PostAsJsonAsync($"{EndpointBase}/{oc}/lineas", Linea(vidrio, 20 * HojaA, "M2", varA))).StatusCode.Should().Be(HttpStatusCode.Created);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var entidad = await db.OrdenesCompra.FirstAsync(o => o.Id == oc);
            db.Entry(entidad).Property(nameof(OrdenCompra.Estado)).CurrentValue = EstadoOrdenCompra.Cancelada;
            await db.SaveChangesAsync();
        }

        var dup = await client.PostAsJsonAsync($"{EndpointBase}/{oc}/duplicar",
            new { SucursalCodigo = "MID", FolioAnio = (short)2026, FechaDocumento = DateOnly.FromDateTime(DateTime.UtcNow) });
        dup.StatusCode.Should().Be(HttpStatusCode.Created);
        var nueva = (await dup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("ordenCompraNuevaId").GetGuid();

        var linea = (await Lineas(client, nueva)).Single();
        linea.GetProperty("productoAwVarianteId").GetGuid().Should().Be(varA);
        linea.GetProperty("anchoMm").GetDecimal().Should().Be(2440m);
        linea.GetProperty("cantidad").GetDecimal().Should().Be(20 * HojaA);
    }

    [Fact]
    public async Task Linea_de_vidrio_nacida_de_RQ_sin_medida_bloquea_el_envio_hasta_elegir_medida()
    {
        var (client, vidrio, varA, _) = await SembrarAsync();
        var oc = await CrearOcAsync(client, sinRq: false);
        Guid lineaId;
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var entidad = await db.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == oc);
            var numero = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999);
            var rq = new Requisicion(Guid.NewGuid(), entidad.EmpresaId, Millet.Compras.Domain.Folio.Parse($"MID2026-{numero:D6}"), 2026,
                Clasificacion.MateriaPrima, entidad.SucursalDestinoId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Prioridad.Normal, DateTimeOffset.UtcNow, null);
            var lr = rq.AgregarLinea(Guid.NewGuid(), vidrio, 300m, "M2", Money.Mxn(100), centroCostoId: CentroCosto);
            rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
            rq.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow, RequiereNivel.SoloN1);
            rq.RegistrarCubrimiento([new CubrimientoLinea(lr.Id, 0, 300m)], DateTimeOffset.UtcNow);
            // Como las crean los flujos de RQ: sin medida y con la cantidad de la RQ (300 m² no son hojas enteras).
            lineaId = entidad.AgregarLineaDesdeRequisicion(Guid.NewGuid(), vidrio, 300m, "M2", 100m, rq.DepartamentoId, rq.Id, lr.Id, centroCostoId: CentroCosto).Id;
            rq.ComprometerEnOc(oc);
            var tipoId = (await db.TiposDocumentoOc.SingleAsync(t => t.Clave == "cotizacion")).Id;
            entidad.AdjuntarDocumento(Guid.NewGuid(), tipoId, "cotizacion.pdf", "cotizacion", "application/pdf", 10, DateTimeOffset.UtcNow, Guid.NewGuid());
            db.Requisiciones.Add(rq);
            await db.SaveChangesAsync();
        }

        var bloqueada = await client.PostAsync($"{EndpointBase}/{oc}/transmitir", null);
        (await AssertCodigo(bloqueada, "VARIANTE_VIDRIO_REQUERIDA")).GetProperty("detail").GetString().Should().Contain("líneas 1 antes de enviar");

        // El comprador elige medida y ajusta la cantidad a hojas enteras dentro del saldo de la RQ (33 hojas = 294.7032 m²).
        await AssertCodigo(await client.PatchAsJsonAsync($"{EndpointBase}/{oc}/lineas/{lineaId}", new { ProductoAwVarianteId = varA }), "HOJAS_ENTERAS");
        (await client.PatchAsJsonAsync($"{EndpointBase}/{oc}/lineas/{lineaId}", new { Cantidad = 33 * HojaA, ProductoAwVarianteId = varA }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.PostAsync($"{EndpointBase}/{oc}/transmitir", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static async Task<JsonElement> AssertCodigo(HttpResponseMessage response, string codigo)
    {
        var json = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, json);
        var cuerpo = JsonDocument.Parse(json).RootElement;
        cuerpo.GetProperty("code").GetString().Should().Be(codigo);
        return cuerpo;
    }

    private static object Linea(Guid articulo, decimal cantidad, string unidad, Guid? variante) => new
    {
        ArticuloId = articulo,
        Cantidad = cantidad,
        UnidadMedida = unidad,
        PrecioUnitario = 100m,
        DepartamentoSolicitanteId = Depto,
        CentroCostoId = CentroCosto,
        ProductoAwVarianteId = variante,
    };

    private static async Task<List<JsonElement>> Lineas(HttpClient client, Guid oc) =>
        (await client.GetFromJsonAsync<JsonElement>($"{EndpointBase}/{oc}")).GetProperty("lineas").EnumerateArray().Select(l => l.Clone()).ToList();

    private static async Task<Guid> CrearOcAsync(HttpClient client, bool sinRq)
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
            SinRequisicionPrevia = sinRq,
            EsImportacion = false,
            CotizacionExcepcionada = false,
            Observaciones = "OC medida de hoja",
            MotivoSinRequisicion = sinRq ? "Prueba medida de hoja" : null,
            FechaEntregaEsperada = (DateOnly?)null,
            EncargadoComprasId = (Guid?)null,
            OcOrigenId = (Guid?)null,
        });
        respuesta.EnsureSuccessStatusCode();
        return (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Cliente superadmin + artículo de vidrio crudo ligado a un producto A+W con dos medidas (2440×3660 y 2140×3300).</summary>
    private async Task<(HttpClient Client, Guid Vidrio, Guid VarA, Guid VarB)> SembrarAsync()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var referencia = "OCM" + sufijo;
        Guid vidrio, varA, varB;
        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<AplicarProductoAwService>().AplicarAsync(
                new AplicarProductoAwSnapshot(referencia, "VIDRIO " + sufijo, "PZA", false,
                    [new ProductoAwVarianteDato("2440x3660", 3660m, 2440m, 6m, null),
                     new ProductoAwVarianteDato("2140x3300", 3300m, 2140m, 6m, null)],
                    DateTime.UtcNow, "1", "m1"), CancellationToken.None);
            var db = sp.GetRequiredService<CompartidoDbContext>();
            var producto = await db.ProductosAw.Include(p => p.Variantes).SingleAsync(p => p.ReferenciaExterna == referencia);
            var articulo = new Articulo(Guid.NewGuid(), "OCM" + sufijo, "Vidrio OC " + sufijo, "M2");
            db.Add(articulo);
            await db.SaveChangesAsync();
            await sp.GetRequiredService<IMediator>().Send(
                new ActualizarArticuloCommand(articulo.Id, null, null, null, null, null, null, null, false, false, false, producto.Id));
            vidrio = articulo.Id;
            varA = producto.Variantes.Single(v => v.ClaveVariante == "2440x3660").Id;
            varB = producto.Variantes.Single(v => v.ClaveVariante == "2140x3300").Id;
        }

        var client = factory.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login",
            new { EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "Super Admin Dev", EmpresaId = (Guid?)null });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, vidrio, varA, varB);
    }
}
