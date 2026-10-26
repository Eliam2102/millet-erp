using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.DevolucionesInternas;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Saldos;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application.Exceptions;
using static Millet.Api.IntegrationTests.Almacen.CuarentenaRecepcionTests;
using static Millet.Api.IntegrationTests.Almacen.RecepcionPorMedidaTests;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// F1-MP-01 S4 con Postgres real: la existencia se lee y se surte por medida de hoja. Vale por medida, rechazo por
/// medida sin existencia, devolución interna a la misma medida y consultas de saldos con la medida.
/// </summary>
[Collection(PeriodoContableSinParalelo.Nombre)]
public class SalidaPorMedidaTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Fecha = new(2027, 4, 12);

    private static RegistrarSalidaLineaInput Sale(Ctx ctx, decimal hojas, Guid? variante) =>
        new(ctx.ArticuloId, null, hojas, null, null, null, null, ctx.RackId, "HOJA", variante);

    private static Task<Millet.Almacen.Application.Salidas.RegistrarSalidaResponse> Vale(AlmacenDbContext db, Ctx ctx, params RegistrarSalidaLineaInput[] lineas) =>
        new RegistrarSalidaPorValeHandler(db, new CapturaEventos(), new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId), new DecimalesNulo(),
            ctx.Periodos, ctx.Calendario, ctx.Centros, new ConversionM2(), new ApartadosRequisicionService(db, new AlmacenSaldoQueryAdapter(db)),
            new Articulos(true), new Variantes(ctx.ArticuloId))
            .Handle(new RegistrarSalidaPorValeCommand(Fecha, "vales/medida.pdf", null, null, lineas), default);

    [Fact]
    public async Task Vale_por_medida_descuenta_solo_esa_medida_y_la_devolucion_interna_regresa_a_la_misma()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await Recibir(db, ctx, true, Linea(ctx, 20m, VarA), Linea(ctx, 15m, VarB));
            await ComoLoteDeApertura(db, ctx.ArticuloId); // existencia sin lote (S4a): la salida y la devolución interna siguen funcionando

            var vale = await Vale(db, ctx, Sale(ctx, 5m, VarA));
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(133.956m);   // 15 hojas
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarB))!.Value.Cantidad.Should().Be(105.93m);   // 15 hojas
            var linea = await db.LineasMovimiento.AsNoTracking().SingleAsync(l => l.MovimientoId == vale.SalidaId);
            (linea.ProductoAwVarianteId, linea.AnchoMm, linea.AltoMm, linea.CantidadCapturada).Should().Be((VarA, 2440m, 3660m, 5m));
            linea.CostoUnitarioMxn.Should().Be(180m);

            // Devolver 2.5 m² dejaría hojas fraccionarias: la devolución de una medida es de hojas enteras.
            var fraccion = () => new AplicarDevolucionInternaHandler(db, new CapturaEventos(), new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId),
                    new DecimalesNulo(), ctx.Periodos)
                .Handle(new AplicarDevolucionInternaCommand(vale.SalidaId, ctx.SubDisponibleId, Fecha, "Bueno", "Sobró", null,
                    [new DevolucionInternaLineaInput(linea.Id, 2.5m, ctx.RackId)]), default);
            (await fraccion.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("HOJAS_ENTERAS");

            var dev = await new AplicarDevolucionInternaHandler(db, new CapturaEventos(), new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId),
                    new DecimalesNulo(), ctx.Periodos)
                .Handle(new AplicarDevolucionInternaCommand(vale.SalidaId, ctx.SubDisponibleId, Fecha, "Bueno", "Sobró", null,
                    [new DevolucionInternaLineaInput(linea.Id, 17.8608m, ctx.RackId)]), default);
            dev.DevolucionId.Should().NotBeEmpty();
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(151.8168m);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, null)).Should().BeNull();

            // Consultas: una fila por medida, con sus m² y medidas; la salida por medida lista solo ese rack.
            var variantes = new Variantes(ctx.ArticuloId);
            var saldos = await new ListarSaldosHandler(db, new Articulos(true), variantes)
                .Handle(new ListarSaldosQuery(ctx.SubDisponibleId, ctx.ArticuloId, true, 0, 50), default);
            saldos.Items.Select(i => (i.ProductoAwVarianteId, i.Cantidad, i.AreaHojaM2)).OrderBy(x => x.Cantidad)
                .Should().Equal((VarB, 105.93m, 7.062m), (VarA, 151.8168m, 8.9304m));
            var porRack = await new SaldosPorUbicacionHandler(db, variantes)
                .Handle(new SaldosPorUbicacionQuery(ctx.ArticuloId, ctx.SubDisponibleId, VarB), default);
            porRack.Should().ContainSingle().Which.AnchoMm.Should().Be(2140m);
        });
    }

    [Fact]
    public async Task Salida_sin_medida_o_de_una_medida_sin_existencia_se_rechaza_sin_tocar_saldos()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await Recibir(db, ctx, true, Linea(ctx, 15m, VarB));
            await ComoLoteDeApertura(db, ctx.ArticuloId);

            var sin = () => Vale(db, ctx, Sale(ctx, 1m, null));
            (await sin.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("VARIANTE_VIDRIO_REQUERIDA");

            var otraMedida = () => Vale(db, ctx, Sale(ctx, 1m, VarA));
            (await otraMedida.Should().ThrowAsync<BusinessRuleException>()).Which.Message
                .Should().Be("No hay hojas suficientes de 2440 × 3660 mm en el rack: hay 0 y pides 1.");

            var demasiadas = () => Vale(db, ctx, Sale(ctx, 16m, VarB)); // 112.992 m² > 105.93
            // El mensaje nombra la medida y las hojas; el apartado por sucursal y el trigger por medida son el respaldo.
            (await demasiadas.Should().ThrowAsync<BusinessRuleException>()).Which.Message
                .Should().Be("No hay hojas suficientes de 2140 × 3300 mm en el rack: hay 15 y pides 16.");

            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarB))!.Value.Cantidad.Should().Be(105.93m);
        });
    }

    [Fact]
    public async Task Salida_sin_lote_no_consume_existencia_con_lote_hasta_que_S4b_elija_lote()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await Recibir(db, ctx, true, Linea(ctx, 15m, VarB)); // llega con lote del proveedor

            var sinLote = () => Vale(db, ctx, Sale(ctx, 1m, VarB));
            (await sinLote.Should().ThrowAsync<BusinessRuleException>()).Which.Message
                .Should().Be("No hay hojas suficientes de 2140 × 3300 mm en el rack: hay 0 y pides 1.");
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarB))!.Value.Cantidad.Should().Be(105.93m);
        });
    }

    [Fact]
    public async Task Reincorporar_lo_reservado_para_dictamen_de_una_medida_se_rechaza()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            // 10 hojas de 2440×3660 llegan dañadas: todo queda en MAT-REV, reservado para el dictamen (sin dictaminar).
            await Recibir(db, ctx, true, Linea(ctx, 10m, VarA, danadas: 10m));
            await ComoLoteDeApertura(db, ctx.ArticuloId);
            (await Fila(db, ctx.RackRevisionId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(89.304m);

            var reincorporar = () => new ReincorporacionTrasRevisionHandler(db, new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId),
                    new DecimalesNulo(), ctx.Periodos, new Articulos(true), new Variantes(ctx.ArticuloId))
                .Handle(new ReincorporacionTrasRevisionCommand(ctx.SubDisponibleId, Fecha, "Intento sin dictamen",
                    [new MatRevLineaInput(ctx.ArticuloId, 44.652m, ctx.RackId, VarA)]), default);

            (await reincorporar.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("MATREV_RESERVADO_PARA_DICTAMEN");
            (await Fila(db, ctx.RackRevisionId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(89.304m);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA)).Should().BeNull();
        });
    }
}
