using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.DevolucionesProveedor;
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
/// F1-MP-01 S3 (E1) con Postgres real y los triggers del ledger: la medida la trae la línea de la OC (no se captura al recibir);
/// dos líneas de OC (20 × 2440×3660 y 15 × 2140×3300, en m²) dejan dos filas de saldo; una línea de OC sin medida (legado), con
/// medida ajena o con medida en un artículo sin liga se rechaza; el daño va a MAT-REV con su medida y la cancelación regresa
/// ambas existencias. Reusa el fixture aislado de ALM-04.
/// </summary>
[Collection(PeriodoContableSinParalelo.Nombre)]
public class RecepcionPorMedidaTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Fecha = new(2027, 4, 12);
    internal static readonly Guid VarA = Guid.NewGuid(); // 2440 x 3660 → 8.9304 m²
    internal static readonly Guid VarB = Guid.NewGuid(); // 2140 x 3300 → 7.0620 m²
    private static readonly Guid OtroUsuario = Guid.NewGuid();

    internal static readonly Guid LineaOcB = Guid.NewGuid(); // segunda línea de la OC: 15 hojas de 2140 x 3300

    /// <summary>La OC de vidrio: una línea por medida (E1). La medida no viaja en la captura: <paramref name="variante"/> solo elige la línea.</summary>
    internal static RegistrarRecepcionLineaInput Linea(Ctx ctx, decimal hojas, Guid? variante, decimal danadas = 0m) =>
        new(ctx.ArticuloId, variante == VarB ? LineaOcB : ctx.LineaOcId, hojas, null, null, ctx.RackId, "HOJA",
            CantidadDanada: danadas, MotivoDano: danadas > 0 ? "Hojas rotas" : null);

    internal static Task<RegistrarRecepcionResponse> Recibir(AlmacenDbContext db, Ctx ctx, bool ligado, params RegistrarRecepcionLineaInput[] lineas) =>
        Recibir(db, ctx, ligado, new DecimalesNulo(), lineas);

    internal static Task<RegistrarRecepcionResponse> Recibir(
        AlmacenDbContext db, Ctx ctx, bool ligado, IDecimalesUnidadGuard decimales, params RegistrarRecepcionLineaInput[] lineas) =>
        Recibir(db, ctx, ligado, decimales, null, lineas);

    internal static Task<RegistrarRecepcionResponse> Recibir(
        AlmacenDbContext db, Ctx ctx, bool ligado, IDecimalesUnidadGuard decimales, IReadOnlyList<OcLineaLectura>? lineasOc,
        params RegistrarRecepcionLineaInput[] lineas) =>
        new RegistrarRecepcionConFacturaHandler(db, new OcM2(ctx, lineasOc), new CapturaEventos(), new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId),
            decimales, ctx.Periodos, new ConversionM2(), new CxpNulo(), new ProveedorRfc(),
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
    public async Task Dos_lineas_de_OC_con_medida_son_dos_filas_y_la_cancelacion_las_regresa()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            // La línea de la OC trae 20 hojas: 21 es excedente (recepción exacta).
            var veintiuna = () => Recibir(db, ctx, true, Linea(ctx, 21m, VarA));
            (await veintiuna.Should().ThrowAsync<BusinessRuleException>()).Which.Should().Match<BusinessRuleException>(e =>
                e.Code == "RECEPCION_EXCEDE_PENDIENTE" && e.Message == "Excede lo pendiente de la orden de compra (20 hojas). Almacén recibe exacto.");

            var r = await Recibir(db, ctx, true, Linea(ctx, 20m, VarA), Linea(ctx, 15m, VarB));

            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(178.6080m);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarB))!.Value.Cantidad.Should().Be(105.9300m);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, null)).Should().BeNull();
            var lineas = await db.LineasMovimiento.AsNoTracking().Where(l => l.MovimientoId == r.RecepcionId).OrderBy(l => l.Posicion).ToListAsync();
            lineas.Select(l => (l.ProductoAwVarianteId, l.AnchoMm, l.AltoMm, l.CantidadCapturada, l.UnidadCapturada))
                .Should().Equal((VarA, 2440m, 3660m, 20m, "HOJA"), (VarB, 2140m, 3300m, 15m, "HOJA"));

            // Ya no queda pendiente en ninguna de las dos líneas.
            var excede = () => Recibir(db, ctx, true, Linea(ctx, 1m, VarB));
            (await excede.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("(0 hojas)");

            await new CancelarRecepcionHandler(db, new OcM2(ctx), new CapturaEventos(), new UserFake(OtroUsuario), new EmpresaFake(ctx.EmpresaId),
                ctx.Periodos, new ConversionM2(), new CxpNulo(), new RelojFijo())
                .Handle(new CancelarRecepcionCommand(r.RecepcionId, "OC", "Medida equivocada"), default);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(0m);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarB))!.Value.Cantidad.Should().Be(0m);
        });
    }

    [Fact]
    public async Task Linea_de_OC_sin_medida_ajena_o_en_articulo_sin_liga_se_rechaza_sin_tocar_saldos()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var sinMedida = new OcLineaLectura(ctx.LineaOcId, ctx.ArticuloId, "M2", 1000m, 0m, 180m);
            var legado = () => Recibir(db, ctx, true, new DecimalesNulo(), [sinMedida], Linea(ctx, 5m, null));
            (await legado.Should().ThrowAsync<BusinessRuleException>()).Which
                .Should().Match<BusinessRuleException>(e => e.Code == "VARIANTE_VIDRIO_REQUERIDA"
                    && e.Message == "La línea de la OC no tiene medida; corrígela en Compras.");

            var ajena = () => Recibir(db, ctx, true, new DecimalesNulo(),
                [sinMedida with { ProductoAwVarianteId = Guid.NewGuid(), AnchoMm = 1000m, AltoMm = 1000m }], Linea(ctx, 5m, null));
            (await ajena.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("VARIANTE_NO_PERTENECE_AL_ARTICULO");

            var sinLiga = () => Recibir(db, ctx, false, Linea(ctx, 5m, VarA));
            (await sinLiga.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("VARIANTE_NO_APLICA");

            var fraccion = () => Recibir(db, ctx, true, Linea(ctx, 2.5m, VarA));
            (await fraccion.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("HOJAS_ENTERAS");

            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA)).Should().BeNull();
            (await Fila(db, ctx.RackId, ctx.ArticuloId, null)).Should().BeNull();
        });
    }

    [Fact]
    public async Task Con_los_2_decimales_reales_de_M2_las_hojas_enteras_se_reciben_y_en_m2_se_rechaza()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var guard = new DecimalesUnidadGuard(new DecimalesM2());

            // 20 hojas de 2440 x 3660 = 178.6080 m² (4 decimales): lo capturado son hojas, no m².
            await Recibir(db, ctx, true, guard, Linea(ctx, 20m, VarA));
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(178.6080m);

            // Con medida, la cantidad no se captura en m² (dejaría hojas fraccionarias).
            var enM2 = () => Recibir(db, ctx, true, guard, Linea(ctx, 10m, VarA) with { UnidadCapturada = "M2" });
            (await enM2.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Be("Con medida, captura la cantidad en hojas enteras.");
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
            (linea.ProductoAwVarianteId, linea.AnchoMm, linea.AltoMm, linea.CantidadCapturada).Should().Be((VarA, 2440m, 3660m, 3m));
            await new DictaminarMaterialEnRevisionHandler(db, new UserFake(OtroUsuario), new EmpresaFake(ctx.EmpresaId), new DecimalesNulo(), ctx.Periodos,
                    new AdjuntosVacios(), new OcM2(ctx))
                .Handle(new DictaminarMaterialEnRevisionCommand(linea.Id, ResultadoDictamen.Liberar, 17.8608m, "Sin daño real", Fecha, ctx.RackId), default);
            (await Fila(db, ctx.RackRevisionId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(8.9304m);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(80.3736m);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, null)).Should().BeNull();
        });
    }

    [Fact]
    public async Task Devolucion_a_proveedor_hereda_la_medida_acepta_hojas_enteras_y_rebaja_el_saldo_de_esa_medida()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var guard = new DecimalesUnidadGuard(new DecimalesM2());
            var proveedor = Guid.NewGuid();
            var oc = new OcProveedor(new OcM2(ctx), proveedor);
            var r = await Recibir(db, ctx, true, Linea(ctx, 20m, VarA));
            var origen = await db.LineasMovimiento.AsNoTracking().SingleAsync(l => l.MovimientoId == r.RecepcionId);
            Task<IniciarDevolucionAProveedorResponse> Iniciar(decimal m2) =>
                new IniciarDevolucionAProveedorHandler(db, new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId), guard, oc).Handle(
                    new IniciarDevolucionAProveedorCommand(proveedor, "Hojas rayadas", r.RecepcionId, null, null, null,
                        [new DevolucionProveedorLineaInput(ctx.ArticuloId, m2, "M2", 0m, origen.Id)]), default);

            // 2.5 hojas y más de lo recibido se rechazan.
            var fraccion = () => Iniciar(22.3260m);
            (await fraccion.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("HOJAS_ENTERAS");
            var exceso = () => Iniciar(21m * 8.9304m);
            (await exceso.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("DEVOLUCION_EXCEDE_RECEPCION");

            // 2 de 20 hojas (17.8608 m², 4 decimales con M2 a 2: derivado de hojas, no se revisa) → saldo de esa medida baja 17.8608.
            var dev = await Iniciar(17.8608m);
            var lineaDev = await db.Set<Millet.Almacen.Domain.DevolucionesProveedor.LineaDevolucionProveedor>().AsNoTracking().SingleAsync(l => l.DevolucionId == dev.DevolucionId);
            await new AdjuntarEvidenciaDevolucionAProveedorHandler(db).Handle(
                new AdjuntarEvidenciaDevolucionAProveedorCommand(dev.DevolucionId, "FOTO", "hojas.jpg", "blob://hojas.jpg", null), default);
            await new SolicitarAutorizacionDevolucionAProveedorHandler(db).Handle(new SolicitarAutorizacionDevolucionAProveedorCommand(dev.DevolucionId), default);
            await new AutorizarDevolucionAProveedorHandler(db, new UserFake(ctx.UserId)).Handle(new AutorizarDevolucionAProveedorCommand(dev.DevolucionId), default);
            await new RegistrarSalidaDevolucionAProveedorHandler(db, new CapturaEventos(), new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId),
                oc, ctx.Periodos, new ConversionM2()).Handle(
                new RegistrarSalidaDevolucionAProveedorCommand(dev.DevolucionId, ctx.SubDisponibleId, Fecha, [new DevolucionProveedorSalidaLineaBin(lineaDev.Id, ctx.RackId)]), default);
            (await Fila(db, ctx.RackId, ctx.ArticuloId, VarA))!.Value.Cantidad.Should().Be(178.6080m - 17.8608m);

            // El detalle muestra la medida de la línea de recepción origen.
            db.ChangeTracker.Clear();
            var detalle = await new ObtenerDevolucionProveedorPorIdHandler(db, new ProveedorRfc(), new Articulos(true), new CxpNulo(), oc)
                .Handle(new ObtenerDevolucionProveedorPorIdQuery(dev.DevolucionId), default);
            detalle!.Lineas.Single().Should().Match<DevolucionProveedorLineaItem>(l => l.ProductoAwVarianteId == VarA && l.AnchoMm == 2440m && l.AltoMm == 3660m);
        });
    }

    sealed class OcProveedor(IComprasOcReadPort inner, Guid proveedorId) : IComprasOcReadPort
    {
        public async Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => (await inner.ObtenerAsync(ocId, ct)) is { } o ? o with { ProveedorId = proveedorId } : null;
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) => inner.ObtenerFoliosAsync(ocIds, ct);
    }

    // ─────────────────────────── Dobles ───────────────────────────

    internal sealed class RelojFijo : IClock
    {
        public DateTimeOffset UtcNow => new(Fecha.Year, Fecha.Month, Fecha.Day, 18, 0, 0, TimeSpan.Zero);
    }

    /// <summary>OC en m² (unidad de existencia) a 180 MXN/m²: 20 hojas de 2440 x 3660 y 15 de 2140 x 3300, una línea por medida.</summary>
    internal sealed class OcM2(Ctx ctx, IReadOnlyList<OcLineaLectura>? lineas = null) : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => Task.FromResult<OcLectura?>(
            new(ocId, "OC-MEDIDA", Guid.NewGuid(), ctx.EmpresaId, "Autorizada",
                lineas ??
                [
                    new OcLineaLectura(ctx.LineaOcId, ctx.ArticuloId, "M2", 178.6080m, 0m, 180m, ProductoAwVarianteId: VarA, AnchoMm: 2440m, AltoMm: 3660m),
                    new OcLineaLectura(LineaOcB, ctx.ArticuloId, "M2", 105.9300m, 0m, 180m, ProductoAwVarianteId: VarB, AnchoMm: 2140m, AltoMm: 3300m),
                ]));
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

    /// <summary>M2 tal como se siembra en el catálogo: 2 decimales.</summary>
    private sealed class DecimalesM2 : IUnidadMedidaReadPort
    {
        public Task<IReadOnlyDictionary<Guid, int?>> ObtenerDecimalesPorArticulosAsync(IEnumerable<Guid> articuloIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int?>>(articuloIds.Distinct().ToDictionary(i => i, _ => (int?)2));
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
