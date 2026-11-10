using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using static Millet.Api.IntegrationTests.Almacen.CuarentenaRecepcionTests;
using static Millet.Api.IntegrationTests.Almacen.RecepcionPorMedidaTests;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Acuerdo 28-oct con Postgres real y los triggers del ledger: consumibles y refacciones sin A+W,
/// nuevos o con existencia previa, entran a la ÚNICA ("almacén general") sin asignación.
/// El rack asignado sigue admitiendo entradas; el vidrio con factura también entra a la ÚNICA (M-159); cancelar regresa la ÚNICA a 0 y
/// crear un sub-almacén crea su ÚNICA. Reusa el fixture aislado de ALM-04.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class RecepcionAlmacenGeneralTests(WebApplicationFactory<Program> factory)
{
    private static readonly DateOnly Fecha = new(2027, 4, 12);
    private static readonly Guid LineaNoVidrio = Guid.NewGuid();

    private sealed class ArticulosMixto(Guid vidrioId) : IArticuloReadPort
    {
        private ArticuloLectura Art(Guid id) => new(id, "ART", "Artículo", "M2", null, true, ProductoAwId: id == vidrioId ? Guid.NewGuid() : null);
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) => Task.FromResult<ArticuloLectura?>(Art(articuloId));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(ids.ToDictionary(i => i, Art));
    }

    /// <summary>Ctx del fixture + ÚNICA del sub disponible y un artículo que no es vidrio, sin asignación a ningún rack.</summary>
    private async Task EjecutarAsync(Func<AlmacenDbContext, Ctx, Guid, Guid, IReadOnlyList<OcLineaLectura>, Task> cuerpo) =>
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var unicaId = Guid.NewGuid();
            var otroArticulo = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({unicaId}, {ctx.SubDisponibleId}, 'ÚNICA', 'Almacén general', 0, true, 0, NOW(), NOW())");
            IReadOnlyList<OcLineaLectura> lineasOc =
            [
                new(ctx.LineaOcId, ctx.ArticuloId, "M2", 178.6080m, 0m, 180m, ProductoAwVarianteId: VarA, AnchoMm: 2440m, AltoMm: 3660m),
                new(LineaNoVidrio, otroArticulo, "M2", 10m, 0m, 50m),
            ];
            await cuerpo(db, ctx, unicaId, otroArticulo, lineasOc);
        });

    private static Task<RegistrarRecepcionResponse> Recibir(
        AlmacenDbContext db, Ctx ctx, Guid vidrioId, IReadOnlyList<OcLineaLectura> lineasOc, params RegistrarRecepcionLineaInput[] lineas) =>
        new RegistrarRecepcionConFacturaHandler(db, new OcM2(ctx, lineasOc), new CapturaEventos(), new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId),
            new DecimalesNulo(), ctx.Periodos, new ConversionM2(), new CxpFake(), new ProveedorFake(),
            new ArticulosMixto(vidrioId), new Variantes(ctx.ArticuloId)).Handle(
            new RegistrarRecepcionConFacturaCommand(ctx.OcId, Fecha, null, "AD662D33-6934-459C-A128-BDF0393E0062", null, lineas), default);

    private static RegistrarRecepcionLineaInput NoVidrio(Guid articulo, Guid ubicacion, decimal cantidad = 10m) =>
        new(articulo, LineaNoVidrio, cantidad, null, null, ubicacion);

    [Fact]
    public async Task No_vidrio_a_la_unica_sin_asignacion_sube_su_saldo_y_cancelar_lo_regresa_a_cero()
    {
        await EjecutarAsync(async (db, ctx, unica, otro, lineasOc) =>
        {
            var r = await Recibir(db, ctx, ctx.ArticuloId, lineasOc, NoVidrio(otro, unica));

            (await LeerSaldoAsync(db, unica, otro)).Should().Be(10m);
            (await db.AsignacionesArticuloUbicacion.AsNoTracking().AnyAsync(a => a.ArticuloId == otro)).Should().BeFalse();

            await new CancelarRecepcionHandler(db, new OcM2(ctx, lineasOc), new CapturaEventos(), new UserFake(Guid.NewGuid()), new EmpresaFake(ctx.EmpresaId),
                ctx.Periodos, new ConversionM2(), new CxpFake(), new RelojFijo())
                .Handle(new CancelarRecepcionCommand(r.RecepcionId, "OC", "Captura equivocada"), default);
            (await LeerSaldoAsync(db, unica, otro)).Should().Be(0m);
        });
    }

    [Fact]
    public async Task Recepcion_mixta_vidrio_a_rack_y_no_vidrio_a_la_unica_del_mismo_sub_pasa()
    {
        await EjecutarAsync(async (db, ctx, unica, otro, lineasOc) =>
        {
            await Recibir(db, ctx, ctx.ArticuloId, lineasOc, Linea(ctx, 20m, VarA), NoVidrio(otro, unica));

            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(178.6080m);
            (await LeerSaldoAsync(db, unica, otro)).Should().Be(10m);
        });
    }

    [Fact]
    public async Task Vidrio_a_la_unica_con_factura_pasa_sin_rack_y_no_vidrio_a_rack_sin_asignacion_se_rechaza()
    {
        await EjecutarAsync(async (db, ctx, unica, otro, lineasOc) =>
        {
            // Primero el rechazo: una recepción registrada con el mismo CFDI haría que la segunda pidiera motivo (G-3).
            var noVidrioARack = () => Recibir(db, ctx, ctx.ArticuloId, lineasOc, NoVidrio(otro, ctx.RackId));
            (await noVidrioARack.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ENTRADA_SIN_ASIGNACION");

            // M-159: la medida de A+W entra al almacén general de la sucursal en la recepción con factura.
            await Recibir(db, ctx, ctx.ArticuloId, lineasOc, Linea(ctx, 20m, VarA) with { UbicacionId = unica });
            (await Fila(db, unica, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(178.6080m);
        });
    }

    [Fact]
    public async Task Articulo_nuevo_sin_aw_y_sin_existencia_previa_entra_a_la_unica_sin_asignacion()
    {
        await EjecutarAsync(async (db, ctx, unica, otro, lineasOc) =>
        {
            (await db.SaldosInventario.AnyAsync(s => s.UbicacionId == unica && s.ArticuloId == otro)).Should().BeFalse();
            (await db.AsignacionesArticuloUbicacion.AnyAsync(a => a.ArticuloId == otro)).Should().BeFalse();

            await Recibir(db, ctx, ctx.ArticuloId, lineasOc, NoVidrio(otro, unica));

            (await LeerSaldoAsync(db, unica, otro)).Should().Be(10m);
            (await db.AsignacionesArticuloUbicacion.AnyAsync(a => a.ArticuloId == otro)).Should().BeFalse();
        });
    }

    [Fact]
    public async Task Articulo_sin_aw_con_rack_asignado_entra_a_su_rack()
    {
        await EjecutarAsync(async (db, ctx, unica, otro, lineasOc) =>
        {
            db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), ctx.RackId, otro));
            await db.SaveChangesAsync();

            await Recibir(db, ctx, ctx.ArticuloId, lineasOc, NoVidrio(otro, ctx.RackId));

            (await LeerSaldoAsync(db, ctx.RackId, otro)).Should().Be(10m);
            (await db.SaldosInventario.AnyAsync(s => s.UbicacionId == unica && s.ArticuloId == otro)).Should().BeFalse();
        });
    }

    [Fact]
    public async Task Sin_aw_a_la_unica_de_mat_rev_se_rechaza()
    {
        await EjecutarAsync(async (db, ctx, _, otro, lineasOc) =>
        {
            // MAT-REV no tiene almacén general de entrada: lo dañado entra a sus racks y sale por dictamen.
            var aMatRev = () => Recibir(db, ctx, ctx.ArticuloId, lineasOc, NoVidrio(otro, ctx.UnicaRevisionId));
            (await aMatRev.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("ENTRADA_A_UBICACION_UNICA");
        });
    }

    [Fact]
    public async Task Crear_sub_almacen_crea_su_unica_almacen_general()
    {
        await EjecutarAsync(async (db, ctx, _, _, _) =>
        {
            var resp = await new CrearSubAlmacenHandler(db).Handle(
                new CrearSubAlmacenCommand(ctx.AlmacenId, "NUEVO", "Sub nuevo", TipoSubAlmacen.Insumos, EstatusCatalogo.Activo), default);
            try
            {
                var u = await db.Ubicaciones.AsNoTracking().SingleAsync(x => x.SubAlmacenId == resp.Id);
                (u.Clave, u.Nombre, u.EsDefault, u.Estatus).Should().Be(("ÚNICA", "Almacén general", true, EstatusCatalogo.Activo));
            }
            finally
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE sub_almacen_id = {resp.Id}");
            }
        });
    }
}
