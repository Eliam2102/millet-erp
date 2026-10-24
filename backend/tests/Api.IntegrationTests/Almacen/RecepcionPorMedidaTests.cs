using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Revision;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using static Millet.Api.IntegrationTests.Almacen.CuarentenaRecepcionTests;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// F1-MP-01 S3 con Postgres real y los triggers del ledger: la misma línea de OC (m²) recibida en dos medidas deja dos filas de
/// saldo; sin medida, con medida ajena o con medida en un artículo sin liga se rechaza; el daño va a MAT-REV con su medida y la
/// cancelación regresa ambas existencias. Reusa el fixture aislado de ALM-04.
/// </summary>
[Collection(PeriodoContableSinParalelo.Nombre)]
public class RecepcionPorMedidaTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Fecha = new(2027, 4, 12);
    internal static readonly Guid VarA = Guid.NewGuid(); // 2440 x 3660 → 8.9304 m²
    internal static readonly Guid VarB = Guid.NewGuid(); // 2140 x 3300 → 7.0620 m²
    private static readonly Guid OtroUsuario = Guid.NewGuid();

    internal static RegistrarRecepcionLineaInput Linea(Ctx ctx, decimal hojas, Guid? variante, decimal danadas = 0m) =>
        new(ctx.ArticuloId, ctx.LineaOcId, hojas, null, null, ctx.RackId, "HOJA",
            CantidadDanada: danadas, MotivoDano: danadas > 0 ? "Hojas rotas" : null, ProductoAwVarianteId: variante);

    internal static Task<RegistrarRecepcionResponse> Recibir(AlmacenDbContext db, Ctx ctx, bool ligado, params RegistrarRecepcionLineaInput[] lineas) =>
        new RegistrarRecepcionConFacturaHandler(db, new OcM2(ctx), new CapturaEventos(), new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId),
            new DecimalesNulo(), ctx.Periodos, new ConversionM2(), new CxpNulo(), new ProveedorRfc(),
            new Articulos(ligado), new Variantes(ctx.ArticuloId)).Handle(
            new RegistrarRecepcionConFacturaCommand(ctx.OcId, Fecha, null, "AD662D33-6934-459C-A128-BDF0393E0062", null, lineas), default);

    internal static async Task<(decimal Cantidad, decimal Costo)?> Fila(AlmacenDbContext db, Guid rack, Guid articulo, Guid? variante)
    {
        db.ChangeTracker.Clear();
        var f = await db.SaldosInventario.AsNoTracking()
            .Where(s => s.UbicacionId == rack && s.ArticuloId == articulo && s.ProductoAwVarianteId == variante).FirstOrDefaultAsync();
        return f is null ? null : (f.Cantidad, f.CostoPromedioMxn);
    }

    [Fact]
    public async Task Dos_medidas_en_una_linea_de_OC_son_dos_filas_y_la_cancelacion_las_regresa()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var r = await Recibir(db, ctx, true, Linea(ctx, 20m, VarA), Linea(ctx, 15m, VarB));

            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(178.6080m);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarB))!.Value.Cantidad.Should().Be(105.9300m);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, null)).Should().BeNull();
            var lineas = await db.LineasMovimiento.AsNoTracking().Where(l => l.MovimientoId == r.RecepcionId).OrderBy(l => l.Posicion).ToListAsync();
            lineas.Select(l => (l.ProductoAwVarianteId, l.AnchoMm, l.AltoMm, l.CantidadCapturada, l.UnidadCapturada))
                .Should().Equal((VarA, 2440m, 3660m, 20m, "HOJA"), (VarB, 2140m, 3300m, 15m, "HOJA"));

            // Pendiente exacto sobre la línea de OC en m²: 1000 − 284.538 = 715.462 disponibles; 100 hojas de B (706.2) caben, 102 no.
            var excede = () => Recibir(db, ctx, true, Linea(ctx, 102m, VarB));
            (await excede.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RECEPCION_EXCEDE_PENDIENTE");

            await new CancelarRecepcionHandler(db, new OcM2(ctx), new CapturaEventos(), new UserFake(OtroUsuario), new EmpresaFake(ctx.EmpresaId),
                ctx.Periodos, new ConversionM2(), new CxpNulo(), new RelojFijo())
                .Handle(new CancelarRecepcionCommand(r.RecepcionId, "OC", "Medida equivocada"), default);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(0m);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarB))!.Value.Cantidad.Should().Be(0m);
        });
    }

    [Fact]
    public async Task Vidrio_sin_medida_ajena_o_en_articulo_sin_liga_se_rechaza_sin_tocar_saldos()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var sin = () => Recibir(db, ctx, true, Linea(ctx, 5m, null));
            (await sin.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("VARIANTE_VIDRIO_REQUERIDA");

            var ajena = () => Recibir(db, ctx, true, Linea(ctx, 5m, Guid.NewGuid()));
            (await ajena.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("VARIANTE_NO_PERTENECE_AL_ARTICULO");

            var sinLiga = () => Recibir(db, ctx, false, Linea(ctx, 5m, VarA));
            (await sinLiga.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("VARIANTE_NO_APLICA");

            var fraccion = () => Recibir(db, ctx, true, Linea(ctx, 2.5m, VarA));
            (await fraccion.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("HOJAS_ENTERAS");

            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA)).Should().BeNull();
        });
    }

    [Fact]
    public async Task Hojas_danadas_van_a_material_en_revision_con_su_medida_y_el_dictamen_la_hereda()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await Recibir(db, ctx, true, Linea(ctx, 10m, VarA, danadas: 3m), Linea(ctx, 4m, VarB));

            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(62.5128m);       // 7 hojas
            (await Fila(db, ctx.RackRevisionId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(26.7912m); // 3 hojas
            (await Fila(db, ctx.RackRevisionId, ctx.ArticuloId, null)).Should().BeNull();
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarB))!.Value.Cantidad.Should().Be(28.2480m);

            // Liberar 2 hojas: sale de MAT-REV y entra al rack, ambas en la medida A.
            var linea = await db.LineasMovimiento.AsNoTracking().SingleAsync(l => l.UbicacionId == ctx.RackRevisionId);
            await new DictaminarMaterialEnRevisionHandler(db, new UserFake(OtroUsuario), new EmpresaFake(ctx.EmpresaId), new DecimalesNulo(), ctx.Periodos,
                    new AdjuntosVacios(), new OcM2(ctx))
                .Handle(new DictaminarMaterialEnRevisionCommand(linea.Id, ResultadoDictamen.Liberar, 17.8608m, "Sin daño real", Fecha, ctx.RackId), default);
            (await Fila(db, ctx.RackRevisionId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(8.9304m);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(80.3736m);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, null)).Should().BeNull();
        });
    }

    // ─────────────────────────── Dobles ───────────────────────────

    internal sealed class RelojFijo : IClock
    {
        public DateTimeOffset UtcNow => new(Fecha.Year, Fecha.Month, Fecha.Day, 18, 0, 0, TimeSpan.Zero);
    }

    /// <summary>OC de 1000 m² (unidad de existencia) a 180 MXN/m².</summary>
    internal sealed class OcM2(Ctx ctx) : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => Task.FromResult<OcLectura?>(
            new(ocId, "OC-MEDIDA", Guid.NewGuid(), ctx.EmpresaId, "Autorizada",
                [new OcLineaLectura(ctx.LineaOcId, ctx.ArticuloId, "M2", 1000m, 0m, 180m)]));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    internal sealed class Articulos(bool ligado) : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) =>
            Task.FromResult<ArticuloLectura?>(new ArticuloLectura(articuloId, "VID-CLARO-6", "Vidrio", "M2", null, true,
                ProductoAwId: ligado ? Guid.NewGuid() : null));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(ids.ToDictionary(i => i, i =>
                new ArticuloLectura(i, "VID-CLARO-6", "Vidrio", "M2", null, true, ProductoAwId: ligado ? Guid.NewGuid() : null)));
    }

    internal sealed class Variantes(Guid articuloId) : IVarianteVidrioReadPort
    {
        private readonly VarianteVidrioLectura[] _todas =
        [
            new(VarA, "2440x3660", 2440m, 3660m, 6m),
            new(VarB, "2140x3300", 2140m, 3300m, 6m),
        ];
        public Task<VarianteVidrioLectura> ObtenerAsync(Guid articulo, Guid varianteId, CancellationToken ct) =>
            Task.FromResult(articulo == articuloId ? _todas.FirstOrDefault(v => v.VarianteId == varianteId) : null) is { Result: { } v }
                ? Task.FromResult(v)
                : throw new BusinessRuleException("VARIANTE_NO_PERTENECE_AL_ARTICULO", "Medida ajena.");
        public Task<IReadOnlyDictionary<Guid, VarianteVidrioLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, VarianteVidrioLectura>>(_todas.Where(v => ids.Contains(v.VarianteId)).ToDictionary(v => v.VarianteId));
        public Task<IReadOnlyList<VarianteVidrioLectura>> ListarPorArticuloAsync(Guid articulo, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<VarianteVidrioLectura>>(_todas);
    }

    private sealed class AdjuntosVacios : IAdjuntosEntidadReadPort
    {
        public Task<IReadOnlyList<AdjuntoEvidenciaLectura>> ListarVigentesAsync(string tipoEntidad, Guid entidadId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AdjuntoEvidenciaLectura>>([]);
    }

    private sealed class CxpNulo : ICxpDocumentosReadPort
    {
        public Task<CfdiRecibidoLectura?> ObtenerCfdiAsync(Guid id, CancellationToken ct) => Task.FromResult<CfdiRecibidoLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosFacturaAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Vacio();
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerUuidsFiscalesCfdiAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Vacio();
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosNotaCreditoAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Vacio();
        public Task<IReadOnlyList<FacturaLigadaRecepcionLectura>> ObtenerFacturasLigadasRecepcionAsync(
            IReadOnlyCollection<Guid> recepcionIds, IReadOnlyCollection<Guid> facturaIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FacturaLigadaRecepcionLectura>>([]);
        private static Task<IReadOnlyDictionary<Guid, string>> Vacio() => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class ProveedorRfc : IProveedorReadPort
    {
        public Task<ProveedorLectura?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ProveedorLectura?>(new ProveedorLectura(id, "P", "Proveedor", "AAA010101AAA", true));
        public Task<IReadOnlyDictionary<Guid, ProveedorLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorLectura>>(new Dictionary<Guid, ProveedorLectura>());
    }

    /// <summary>Catálogo mínimo: la unidad de existencia es M2 y el factor a documento M2 es 1 (HOJA nunca pasa por aquí).</summary>
    internal sealed class ConversionM2 : IConversionUnidadPort
    {
        public Task<ConversionUnidad> ConvertirAsync(Guid articuloId, decimal cantidad, string? capturada, string documento, CancellationToken ct) =>
            Task.FromResult(new ConversionUnidad(cantidad, "M2", cantidad, 1, "M2", cantidad));
    }
}
