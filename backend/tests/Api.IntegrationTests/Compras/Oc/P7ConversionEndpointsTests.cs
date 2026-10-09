using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras.Oc;

public partial class OrdenesCompraEndpointsTests
{
    [Fact]
    public async Task P7_Endpoints_RecibirDosCajasDeDoce_YSalirUna_ActualizaStockBaseYPreservaCaptura()
    {
        using var client = await CreateSuperAdminClientAsync();
        await Contabilidad.ContabTestKit.AsegurarPeriodosAbiertosAsync(client, new DateOnly(2026, 10, 9));
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var catalogos = sp.GetRequiredService<CompartidoDbContext>();
        var compras = sp.GetRequiredService<ComprasDbContext>();
        var almacen = sp.GetRequiredService<AlmacenDbContext>();
        var pza = await catalogos.UnidadesMedida.SingleAsync(u => u.Codigo == "PZA");
        var caja = new UnidadMedida(Guid.NewGuid(), $"P7C{Guid.NewGuid():N}"[..16], "DEMO P7 Caja de 12", DimensionUnidad.Conteo, 12, 0, false);
        var articulo = new Articulo(Guid.NewGuid(), $"P7A{Guid.NewGuid():N}"[..16], "DEMO P7 conversión", "PZA", unidadMedidaId: pza.Id);
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"P7M{Guid.NewGuid():N}"[..16], "DEMO P7", SucursalIdFija);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "P7", "DEMO P7", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "P7", "DEMO P7");
        var asignacion = new AsignacionArticuloUbicacion(Guid.NewGuid(), bin.Id, articulo.Id);
        Guid ocId = Guid.Empty;
        try
        {
            catalogos.AddRange(caja, articulo); await catalogos.SaveChangesAsync();
            almacen.AddRange(alm, sub, bin, asignacion); await almacen.SaveChangesAsync();
            var creada = await client.PostAsJsonAsync(EndpointBase, ValidBody() with { SinRequisicionPrevia = true, MotivoSinRequisicion = "DEMO P7 conversión" });
            creada.EnsureSuccessStatusCode();
            ocId = (await ReadJsonAsync(creada)).GetProperty("id").GetGuid();
            var oc = await compras.OrdenesCompra.SingleAsync(o => o.Id == ocId);
            var lineaOc = Guid.NewGuid();
            oc.AgregarLineaManual(lineaOc, articulo.Id, 24, "PZA", 10, Guid.NewGuid(), centroCostoId: CentroCostoSeedId);
            // Fixture autorizado: requisitos documentales probados por separado.
            oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
            oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow);
            oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, Guid.NewGuid(), DateTimeOffset.UtcNow);
            await compras.SaveChangesAsync();
            var recibida = await client.PostAsJsonAsync("/api/v1/almacen/recepciones/packing-list",
                new RegistrarRecepcionConPackingListCommand(ocId, new(2026, 10, 9), "blob://DEMO/P7", null,
                    [new RegistrarRecepcionLineaInput(articulo.Id, lineaOc, 2, null, null, bin.Id, caja.Codigo)]));
            recibida.EnsureSuccessStatusCode();
            var recepcionId = (await ReadJsonAsync(recibida)).GetProperty("recepcionId").GetGuid();
            var linea = (await almacen.Movimientos.AsNoTracking().Include(m => m.Lineas).SingleAsync(m => m.Id == recepcionId)).Lineas.Single();
            Assert.Equal(24, linea.Cantidad); Assert.Equal("PZA", linea.UnidadMedida);
            Assert.Equal(2, linea.CantidadCapturada); Assert.Equal(caja.Codigo, linea.UnidadCapturada);
            Assert.Equal(24, (await almacen.SaldosInventario.AsNoTracking().SingleAsync(s => s.UbicacionId == bin.Id && s.ArticuloId == articulo.Id)).Cantidad);
            var salida = await client.PostAsJsonAsync("/api/v1/almacen/salidas/vale",
                new RegistrarSalidaPorValeCommand(new(2026, 10, 9), "blob://DEMO/P7-vale", null, "DEMO P7",
                    [new RegistrarSalidaLineaInput(articulo.Id, null, 1, CentroCostoSeedId, null, null, null, bin.Id, caja.Codigo)]));
            salida.EnsureSuccessStatusCode();
            var salidaId = (await ReadJsonAsync(salida)).GetProperty("salidaId").GetGuid();
            var vale = await almacen.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Id == salidaId);
            Assert.Equal(12, vale.Lineas.Single().Cantidad); Assert.Equal(1, vale.Lineas.Single().CantidadCapturada);
            Assert.Equal(12, (await almacen.SaldosInventario.AsNoTracking().SingleAsync(s => s.UbicacionId == bin.Id && s.ArticuloId == articulo.Id)).Cantidad);
            vale.EstablecerPlazoRegularizacion(DateTimeOffset.UtcNow.AddHours(-1));
            await almacen.SaveChangesAsync();
            var aviso = await client.GetAsync("/api/v1/almacen/salidas/?soloVales=true&soloPendientesRegularizacion=true&soloVencidos=true&limit=500");
            aviso.EnsureSuccessStatusCode();
            Assert.Contains((await ReadJsonAsync(aviso)).GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == salidaId);
        }
        finally
        {
            almacen.ChangeTracker.Clear(); compras.ChangeTracker.Clear(); catalogos.ChangeTracker.Clear();
            await almacen.Movimientos.Where(m => m.Lineas.Any(l => l.UbicacionId == bin.Id)).ExecuteDeleteAsync();
            await almacen.SaldosInventario.Where(s => s.UbicacionId == bin.Id).ExecuteDeleteAsync();
            await almacen.AsignacionesArticuloUbicacion.Where(a => a.Id == asignacion.Id).ExecuteDeleteAsync();
            await almacen.Ubicaciones.Where(b => b.Id == bin.Id).ExecuteDeleteAsync();
            await almacen.SubAlmacenes.Where(s => s.Id == sub.Id).ExecuteDeleteAsync();
            await almacen.Almacenes.Where(a => a.Id == alm.Id).ExecuteDeleteAsync();
            await compras.OrdenesCompra.Where(o => o.Id == ocId).ExecuteDeleteAsync();
            await catalogos.Articulos.Where(a => a.Id == articulo.Id).ExecuteDeleteAsync();
            await catalogos.UnidadesMedida.Where(u => u.Id == caja.Id).ExecuteDeleteAsync();
        }
    }
}
