using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Almacen;
using Millet.Compras.Application.Oc;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// F1-COM-06 punta a punta: la OC de importación se autoriza sin pedimento/contenedor (C3.4-a),
// la recepción en Almacén los exige y el detalle de la OC lista los documentos por recepción.
public partial class OrdenesCompraEndpointsTests
{
    [Fact]
    public async Task F1COM06_OcImportacion_SinPedimento_Autoriza_Recibe_Y_Lista_Documentos_Por_Recepcion()
    {
        var admin = await CreateSuperAdminClientAsync();
        var articulo = await SembrarArticuloImportacionAsync();
        var id = await CrearOcImportacionBorradorAsync(admin);
        (await admin.PatchAsJsonAsync($"{EndpointBase}/{id}/informacion-importacion",
            new { IncotermId = Guid.NewGuid(), PaisOrigen = "US" })).EnsureSuccessStatusCode();
        await SembrarUmbralGeneralAsync(5000m, 1000m); // monto <= menor cuantía: basta N1
        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
                var evaluador = scope.ServiceProvider.GetRequiredService<EvaluadorNivelOc>();
                var oc = await db.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == id);
                oc.AgregarLineaManual(Guid.NewGuid(), articulo, 10, "PZA", 100, Guid.NewGuid(), centroCostoId: CentroCostoSeedId);
                oc.EnviarAAutorizacion(DateTimeOffset.UtcNow, await evaluador.RequiereNivel2Async(oc));
                await db.SaveChangesAsync();
            }

            // C3.4-a: sin pedimento ni contenedor la OC se autoriza.
            var firmante = await CreateClientConPermisosAsync("compras.ordenes.autorizar-nivel1", "compras.ordenes.gestionar-todas-sucursales");
            (await firmante.PostAsJsonAsync($"{EndpointBase}/{id}/autorizaciones", new { Nivel = 1 })).EnsureSuccessStatusCode();
            var autorizada = await ReadJsonAsync(await admin.GetAsync($"{EndpointBase}/{id}"));
            Assert.Equal((int)EstadoOrdenCompra.Autorizada, autorizada.GetProperty("estado").GetInt32());
            var lineaId = autorizada.GetProperty("lineas").EnumerateArray().Single().GetProperty("id").GetGuid();

            var hoy = DateOnly.FromDateTime(DateTime.Now);
            using var scopeBd = _factory.Services.CreateScope();
            var cs = scopeBd.ServiceProvider.GetRequiredService<AlmacenDbContext>().Database.GetConnectionString()!;
            var periodo = new PeriodoContableFixture(cs, EmpresaBootstrapId, hoy.Year);
            try { await periodo.SembrarAsync(hoy.Month, cerrarAnteriores: true); } catch { /* el ejercicio ya existía */ }
            await using var _ = periodo;
            var bin = await SembrarBinImportacionAsync(articulo);

            object Recepcion(decimal cantidad, object? importacion) => new
            {
                OrdenCompraId = id, FechaMovimiento = hoy, CfdiUuidFiscal = Guid.NewGuid().ToString().ToUpperInvariant(),
                Importacion = importacion,
                Lineas = new[] { new { ArticuloId = articulo, LineaOcId = lineaId, Cantidad = cantidad, UbicacionId = bin, FraccionArancelaria = "84719000" } },
            };

            // Sin documentos de importación: se rechaza con el código del guard.
            var sinDatos = await admin.PostAsJsonAsync("/api/v1/almacen/recepciones/", Recepcion(4m, null));
            Assert.True(sinDatos.StatusCode == HttpStatusCode.UnprocessableEntity, await sinDatos.Content.ReadAsStringAsync());
            Assert.Contains("RECEPCION_IMPORTACION_INCOMPLETA", await sinDatos.Content.ReadAsStringAsync());

            var primera = await admin.PostAsJsonAsync("/api/v1/almacen/recepciones/", Recepcion(4m,
                new { NumeroPedimento = "26 16 1234 6000001", NumeroBl = "BL-1", NumeroContenedor = "CONT-1", Aduana = "16", ClavePedimento = "A1" }));
            Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
            var segunda = await admin.PostAsJsonAsync("/api/v1/almacen/recepciones/", Recepcion(3m,
                new { NumeroPedimento = "26 16 1234 6000002", NumeroBl = "BL-2", NumeroContenedor = "CONT-2", Aduana = "43", ClavePedimento = "IN" }));
            Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);

            var detalle = await ReadJsonAsync(await admin.GetAsync($"{EndpointBase}/{id}"));
            var docs = detalle.GetProperty("recepcionesImportacion").EnumerateArray().ToList();
            Assert.Equal(2, docs.Count);
            Assert.Equal(["261612346000001", "261612346000002"], docs.Select(d => d.GetProperty("numeroPedimento").GetString()).Order());
            var d1 = docs.Single(d => d.GetProperty("numeroPedimento").GetString() == "261612346000001");
            Assert.Equal("BL-1", d1.GetProperty("numeroBl").GetString());
            Assert.Equal("CONT-1", d1.GetProperty("numeroContenedor").GetString());
            Assert.Equal("16", d1.GetProperty("aduana").GetString());
            Assert.Equal("A1", d1.GetProperty("clavePedimento").GetString());
            Assert.Equal("84719000", d1.GetProperty("partidas").EnumerateArray().Single().GetProperty("fraccionArancelaria").GetString());
            // La OC conserva su pedimento propio: lo capturado al recibir no se copia de vuelta.
            Assert.Equal(JsonValueKind.Null, detalle.GetProperty("infoImportNumeroPedimento").ValueKind);

            // El detalle de la recepción de Almacén muestra los mismos documentos y la fracción por línea.
            var recepcion = await ReadJsonAsync(await admin.GetAsync(primera.Headers.Location!.ToString()));
            Assert.Equal("BL-1", recepcion.GetProperty("importacion").GetProperty("numeroBl").GetString());
            Assert.Equal("84719000", recepcion.GetProperty("lineas").EnumerateArray().Single().GetProperty("fraccionArancelaria").GetString());
        }
        finally
        {
            await LimpiarUmbralGeneralAsync();
        }
    }

    private async Task<Guid> SembrarArticuloImportacionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var cat = scope.ServiceProvider.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
        var pza = await cat.UnidadesMedida.SingleAsync(u => u.Codigo == "PZA");
        var art = new Millet.DatosMaestros.Domain.Articulo(Guid.NewGuid(), $"IM{Guid.NewGuid():N}"[..16], "DEMO importación", "PZA", unidadMedidaId: pza.Id);
        cat.Add(art);
        await cat.SaveChangesAsync();
        return art.Id;
    }

    private async Task<Guid> SembrarBinImportacionAsync(Guid articulo)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"IM{Guid.NewGuid():N}"[..16], "DEMO importación", SucursalIdFija);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "IM", "DEMO", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "IM", "DEMO");
        almacen.AddRange(alm, sub, bin, new AsignacionArticuloUbicacion(Guid.NewGuid(), bin.Id, articulo));
        await almacen.SaveChangesAsync();
        return bin.Id;
    }
}
