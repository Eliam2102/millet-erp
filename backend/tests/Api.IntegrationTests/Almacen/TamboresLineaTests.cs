using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Domain.Tambores;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using static Millet.Api.IntegrationTests.Almacen.CuarentenaRecepcionTests;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// M-172 con Postgres real: la marca del artículo se guarda por la API, el vale de un tambor descuenta una pieza (trigger),
/// el segundo tambor gasta al primero, la pieza fraccionada se rechaza sin tocar el saldo y la consulta lo muestra.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class TamboresLineaTests(WebApplicationFactory<Program> factory)
{
    private static readonly DateOnly Fecha = new(2027, 4, 12);

    private sealed class PeriodoAbierto : IPeriodoContableReadPort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class Identidad : IConversionUnidadPort
    {
        public Task<ConversionUnidad> ConvertirAsync(Guid articuloId, decimal cantidad, string? capturada, string documento, CancellationToken ct) =>
            Task.FromResult(new ConversionUnidad(cantidad, "PZA", cantidad, 1, "PZA", cantidad));
    }

    [Fact]
    public async Task Tambor_a_linea_descuenta_la_pieza_gasta_el_anterior_y_rechaza_fracciones()
    {
        var editor = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.CompartidoCatalogosAdministrar,
            PermisosCanonicos.CompartidoCatalogosLeer);
        var lector = await ContabTestKit.ClienteConPermisosAsync(factory, PermisosCanonicos.AlmacenSalidasLeerTodas,
            PermisosCanonicos.AlmacenSalidasLeerTodasSucursales);
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var compartido = sp.GetRequiredService<CompartidoDbContext>();
        var db = sp.GetRequiredService<AlmacenDbContext>();
        var n = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var articulo = new Articulo(Guid.CreateVersion7(), $"SIL-{n[..12]}", "Silicón componente A", "PZA");
        compartido.Add(articulo);
        await compartido.SaveChangesAsync();
        var mid = await compartido.Sucursales.AsNoTracking().Where(s => s.Clave == "MID").Select(s => s.Id).SingleAsync();
        var alm = new Millet.Almacen.Domain.Catalogo.Almacen(Guid.NewGuid(), $"T-{n[..16]}", "Tambores M-172", mid);
        var sub = new SubAlmacen(Guid.NewGuid(), alm.Id, "INS", "Insumos", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "R1", "Rack 1");
        var ins1 = new LineaProduccion(Guid.NewGuid(), mid, $"I1{n[..10]}", "Insulado 1", "INSULADO");
        var ins2 = new LineaProduccion(Guid.NewGuid(), mid, $"I2{n[..10]}", "Insulado 2", "INSULADO");
        db.AddRange(alm, sub, bin, ins1, ins2, new SaldoInventario(bin.Id, sub.Id, articulo.Id, 5, 1500m, null));
        await db.SaveChangesAsync();
        try
        {
            // La marca y los kilos se guardan por la API de artículos (editar) y se leen en el detalle.
            using (var patch = await editor.PatchAsJsonAsync($"/api/v1/catalogos/articulos/{articulo.Id}",
                       new { consumoPorTamborALinea = true, kgPorPresentacion = 200m }))
                Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);
            var detalle = await ContabTestKit.Json(await editor.GetAsync($"/api/v1/catalogos/articulos/{articulo.Id}"));
            Assert.True(detalle.GetProperty("consumoPorTamborALinea").GetBoolean());
            Assert.Equal(200m, detalle.GetProperty("kgPorPresentacion").GetDecimal());

            var empresa = Guid.NewGuid();
            Task<RegistrarSalidaResponse> Vale(decimal cantidad, Guid? linea) =>
                new RegistrarSalidaPorValeHandler(db, new CapturaEventos(), new UserFake(Guid.NewGuid()), new EmpresaFake(empresa),
                        new DecimalesNulo(), new PeriodoAbierto(), sp.GetRequiredService<ICalendarioHabil>(),
                        sp.GetRequiredService<ICentroCostoElegibilidadPort>(), new Identidad(),
                        new ApartadosRequisicionService(db, new AlmacenSaldoQueryAdapter(db)), sp.GetRequiredService<IArticuloReadPort>())
                    .Handle(new RegistrarSalidaPorValeCommand(Fecha, "vales/tambor.pdf", Guid.NewGuid(), null,
                        [new RegistrarSalidaLineaInput(articulo.Id, null, cantidad, null, null, null, null, bin.Id, LineaProduccionId: linea)],
                        "Cambio de tambor", "Operador de insulado"), default);

            var primero = await Vale(1, ins1.Id);
            Assert.Equal(4m, await LeerSaldoAsync(db, bin.Id, articulo.Id));
            var segundo = await Vale(1, ins1.Id);
            Assert.Equal(3m, await LeerSaldoAsync(db, bin.Id, articulo.Id));

            var fraccion = await Assert.ThrowsAsync<BusinessRuleException>(() => Vale(1.5m, ins2.Id));
            Assert.Equal("SALIDA_TAMBOR_PIEZAS_ENTERAS", fraccion.Code);
            var sinLinea = await Assert.ThrowsAsync<BusinessRuleException>(() => Vale(1, null));
            Assert.Equal("SALIDA_TAMBOR_SIN_LINEA", sinLinea.Code);
            Assert.Equal(3m, await LeerSaldoAsync(db, bin.Id, articulo.Id));

            // La consulta «Tambores por línea» muestra el activo y el historial.
            var lineas = await ContabTestKit.Json(await lector.GetAsync($"/api/v1/almacen/tambores-linea?sucursalId={mid}"));
            var l1 = Assert.Single(lineas.EnumerateArray(), x => x.GetProperty("id").GetGuid() == ins1.Id);
            var tambores = l1.GetProperty("tambores").EnumerateArray().ToList();
            Assert.Equal(2, tambores.Count);
            Assert.Equal((segundo.Folio, "ACTIVO"), (tambores[0].GetProperty("folio").GetString(), tambores[0].GetProperty("estado").GetString()));
            Assert.Equal((primero.Folio, "GASTADO", segundo.Folio),
                (tambores[1].GetProperty("folio").GetString(), tambores[1].GetProperty("estado").GetString(), tambores[1].GetProperty("folioReemplazo").GetString()));
            var l2 = Assert.Single(lineas.EnumerateArray(), x => x.GetProperty("id").GetGuid() == ins2.Id);
            Assert.Empty(l2.GetProperty("tambores").EnumerateArray());
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.tambores_linea WHERE articulo_id = {articulo.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.lineas_produccion WHERE id IN ({ins1.Id}, {ins2.Id})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.movimientos_inventario WHERE id IN (SELECT movimiento_id FROM almacen.lineas_movimiento WHERE articulo_id = {articulo.Id})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE sub_almacen_id = {sub.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE sub_almacen_id = {sub.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE almacen_id = {alm.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {alm.Id}");
            await ContabTestKit.LimpiarClienteAsync(editor);
            await ContabTestKit.LimpiarClienteAsync(lector);
        }
    }
}
