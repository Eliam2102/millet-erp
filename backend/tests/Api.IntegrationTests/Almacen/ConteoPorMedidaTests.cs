using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Conteos;
using Millet.Almacen.Application.Reportes;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using static Millet.Api.IntegrationTests.Almacen.CuarentenaRecepcionTests;
using static Millet.Api.IntegrationTests.Almacen.RecepcionPorMedidaTests;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// F1-MP-01 S5 con Postgres real: el conteo del vidrio por medida genera una línea por (rack, medida), se captura en hojas
/// (20 hojas de 2440×3660 contadas como 19 → ajuste de -1 hoja solo en esa medida, en M2) y los reportes muestran la medida.
/// La fila en cero sin medida que deja una asignación no entra al conteo.
/// </summary>
[Collection(PeriodoContableSinParalelo.Nombre)]
public class ConteoPorMedidaTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Fecha = new(2027, 4, 12);

    private sealed class PeriodoAbierto : IPeriodoContableReadPort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }

    [Fact]
    public async Task Conteo_por_medida_captura_en_hojas_y_ajusta_solo_la_medida_con_diferencia()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await Recibir(db, ctx, true, Linea(ctx, 20m, VarA), Linea(ctx, 15m, VarB));
            await ComoLoteDeApertura(db, ctx.ArticuloId); // existencia sin lote (S4a): el conteo sigue una línea por (rack, medida)
            // Resto de la asignación del vidrio ligado: fila en cero sin medida (no debe contarse).
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.saldos_inventario (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
                VALUES ({ctx.RackId}, {ctx.SubDisponibleId}, {ctx.ArticuloId}, 0, 0, NOW())");

            Guid? conteoId = null;
            try
            {
            using var scope = factory.Services.CreateScope();
            var articulos = new Articulos(true);
            var variantes = new Variantes(ctx.ArticuloId);
            var crear = await new CrearConteoHandler(db, new EmpresaFake(ctx.EmpresaId)).Handle(
                new CrearConteoCommand(TipoConteo.Rotativo, Fecha, ctx.UserId, ctx.SubDisponibleId, null), default);
            conteoId = crear.ConteoId;
            await new IniciarConteoHandler(db, scope.ServiceProvider.GetRequiredService<IConteoUmbralesProvider>(), articulos)
                .Handle(new IniciarConteoCommand(crear.ConteoId), default);

            var vista = await new VistaPreviaConteoHandler(db, articulos)
                .Handle(new VistaPreviaConteoQuery(ctx.SubDisponibleId, null), default);
            vista.CantidadLineas.Should().Be(2);

            var lineas = await new ListarLineasParaCapturarHandler(db, articulos, variantes)
                .Handle(new ListarLineasParaCapturarQuery(crear.ConteoId), default);
            lineas.Should().HaveCount(2);
            lineas.Select(l => l.AreaHojaM2).Should().BeEquivalentTo([8.9304m, 7.062m]);
            var lineaA = lineas.Single(l => l.ProductoAwVarianteId == VarA);
            var lineaB = lineas.Single(l => l.ProductoAwVarianteId == VarB);

            // Hojas con fracción se rechazan; 19 y 15 hojas se guardan en m².
            db.ChangeTracker.Clear();
            var fraccion = () => new CapturarLineaConteoHandler(db, new UserFake(ctx.UserId), new DecimalesNulo(), variantes)
                .Handle(new CapturarLineaConteoCommand(crear.ConteoId, lineaA.Id, 19.5m), default);
            (await fraccion.Should().ThrowAsync<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>()).Which.Code.Should().Be("HOJAS_ENTERAS");
            var capturar = new CapturarLineaConteoHandler(db, new UserFake(ctx.UserId), new DecimalesNulo(), variantes);
            // Una petición real usa un contexto nuevo: el Include filtrado de la captura no debe ver líneas ya rastreadas.
            db.ChangeTracker.Clear();
            await capturar.Handle(new CapturarLineaConteoCommand(crear.ConteoId, lineaA.Id, 19m), default);
            db.ChangeTracker.Clear();
            await capturar.Handle(new CapturarLineaConteoCommand(crear.ConteoId, lineaB.Id, 15m), default);
            await new EnviarConteoAConciliacionHandler(db).Handle(new EnviarConteoAConciliacionCommand(crear.ConteoId), default);

            var comparacion = await new ListarLineasComparacionHandler(db, articulos, variantes)
                .Handle(new ListarLineasComparacionQuery(crear.ConteoId), default);
            var a = comparacion.Single(l => l.ProductoAwVarianteId == VarA);
            (a.CantidadTeorica, a.CantidadRealCapturada, a.VariacionAbsoluta).Should().Be((178.608m, (decimal?)169.6776m, (decimal?)-8.9304m));
            comparacion.Single(l => l.ProductoAwVarianteId == VarB).VariacionAbsoluta.Should().Be(0m);

            // Aprobación (domino) y aplicación: un solo ajuste negativo de 1 hoja (8.9304 m²) en la medida A, en M2.
            db.ChangeTracker.Clear();
            var conteo = await db.Set<ConteoInventario>().Include(c => c.Lineas).SingleAsync(c => c.Id == crear.ConteoId);
            conteo.Aprobar(ctx.UserId);
            await db.SaveChangesAsync();
            await new AplicarConteoHandler(db, new CapturaEventos(), new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId),
                    new PeriodoAbierto(), articulos, variantes)
                .Handle(new AplicarConteoCommand(crear.ConteoId), default);

            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(169.6776m);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarB))!.Value.Cantidad.Should().Be(105.93m);
            var ajuste = await db.LineasMovimiento.AsNoTracking().SingleAsync(l => l.ProductoAwVarianteId == VarA && l.CantidadRealContada != null);
            (ajuste.Cantidad, ajuste.UnidadMedida, ajuste.AnchoMm, ajuste.AltoMm).Should().Be((8.9304m, "M2", 2440m, 3660m));

            // Reportes: una fila por medida con su texto.
            var alfak = await new AlfakHistorialAlmacenHandler(db, articulos, variantes)
                .Handle(new AlfakHistorialAlmacenQuery(Fecha, Fecha, ctx.SubDisponibleId, ctx.ArticuloId), default);
            alfak.Filas.Select(f => f.Medida).Should().BeEquivalentTo(["2440 × 3660 mm · 19 hojas", "2140 × 3300 mm · 15 hojas"]);
            }
            finally
            {
                // El fixture no conoce los conteos: sus líneas apuntan a las ubicaciones que borra al final.
                if (conteoId is Guid id)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.movimientos_inventario WHERE conteo_id = {id}");
                    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.conteos_inventario WHERE id = {id}");
                }
            }
        });
    }

    [Fact]
    public async Task Conteo_con_dos_lotes_de_la_misma_medida_crea_dos_lineas_y_ajusta_el_lote_correcto()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            // 10 hojas del lote L-1 y 10 del L-2, misma medida y mismo rack.
            await Recibir(db, ctx, true, Linea(ctx, 10m, VarA, lote: "L-1"));
            await Recibir(db, ctx, true, Linea(ctx, 10m, VarA, lote: "L-2"));

            Guid? conteoId = null;
            try
            {
                using var scope = factory.Services.CreateScope();
                var articulos = new Articulos(true);
                var variantes = new Variantes(ctx.ArticuloId);
                var crear = await new CrearConteoHandler(db, new EmpresaFake(ctx.EmpresaId)).Handle(
                    new CrearConteoCommand(TipoConteo.Rotativo, Fecha, ctx.UserId, ctx.SubDisponibleId, null), default);
                conteoId = crear.ConteoId;
                await new IniciarConteoHandler(db, scope.ServiceProvider.GetRequiredService<IConteoUmbralesProvider>(), articulos)
                    .Handle(new IniciarConteoCommand(crear.ConteoId), default);

                var lineas = await new ListarLineasParaCapturarHandler(db, articulos, variantes)
                    .Handle(new ListarLineasParaCapturarQuery(crear.ConteoId), default);
                lineas.Select(l => l.Lote).OrderBy(l => l).Should().Equal("L-1", "L-2");
                var lineaUno = lineas.Single(l => l.Lote == "L-1");
                var lineaDos = lineas.Single(l => l.Lote == "L-2");

                // Se cuentan 9 hojas del L-1 y las 10 del L-2.
                var capturar = new CapturarLineaConteoHandler(db, new UserFake(ctx.UserId), new DecimalesNulo(), variantes);
                db.ChangeTracker.Clear();
                await capturar.Handle(new CapturarLineaConteoCommand(crear.ConteoId, lineaUno.Id, 9m), default);
                db.ChangeTracker.Clear();
                await capturar.Handle(new CapturarLineaConteoCommand(crear.ConteoId, lineaDos.Id, 10m), default);
                await new EnviarConteoAConciliacionHandler(db).Handle(new EnviarConteoAConciliacionCommand(crear.ConteoId), default);

                var comparacion = await new ListarLineasComparacionHandler(db, articulos, variantes)
                    .Handle(new ListarLineasComparacionQuery(crear.ConteoId), default);
                comparacion.Single(l => l.Lote == "L-1").VariacionAbsoluta.Should().Be(-8.9304m);
                comparacion.Single(l => l.Lote == "L-2").VariacionAbsoluta.Should().Be(0m);

                db.ChangeTracker.Clear();
                var conteo = await db.Set<ConteoInventario>().Include(c => c.Lineas).SingleAsync(c => c.Id == crear.ConteoId);
                conteo.Aprobar(ctx.UserId);
                await db.SaveChangesAsync();
                await new AplicarConteoHandler(db, new CapturaEventos(), new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId),
                        new PeriodoAbierto(), articulos, variantes)
                    .Handle(new AplicarConteoCommand(crear.ConteoId), default);

                db.ChangeTracker.Clear();
                var filas = await db.SaldosInventario.AsNoTracking().Where(s => s.ArticuloId == ctx.ArticuloId && s.UbicacionId == ctx.RackId).ToListAsync();
                filas.Single(f => f.Lote == "L-1").Cantidad.Should().Be(80.3736m);
                filas.Single(f => f.Lote == "L-2").Cantidad.Should().Be(89.304m);
                var ajuste = await db.LineasMovimiento.AsNoTracking().SingleAsync(l => l.ArticuloId == ctx.ArticuloId && l.CantidadRealContada != null);
                (ajuste.Lote, ajuste.Cantidad).Should().Be(("L-1", 8.9304m));
            }
            finally
            {
                if (conteoId is Guid id)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.movimientos_inventario WHERE conteo_id = {id}");
                    await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.conteos_inventario WHERE id = {id}");
                }
            }
        });
    }
}
