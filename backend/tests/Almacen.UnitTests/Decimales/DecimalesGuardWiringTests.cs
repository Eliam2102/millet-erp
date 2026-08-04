using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Conteos;
using Millet.Almacen.Application.DevolucionesInternas;
using Millet.Almacen.Application.DevolucionesProveedor;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.Decimales;

/// <summary>
/// Verifica el CABLEADO de los handlers de captura de Almacén hacia el guard
/// de decimales (ADR-0046 Etapa 2, PR-2b). La regla en sí ya tiene 21 unit en
/// 2a (<c>SharedKernel.UnitTests</c>); aquí se usa el guard REAL
/// (<see cref="DecimalesUnidadGuard"/>) + un fake de
/// <see cref="IUnidadMedidaReadPort"/>, sobre EF InMemory, para probar que cada
/// patrón de resolución de <c>articuloId</c> invoca el guard tras armar las
/// líneas. Se cubre UN representativo por patrón (handlers que comparten patrón
/// comparten riesgo):
/// <list type="number">
///   <item>articuloId del <b>input de línea</b> → IniciarDevolucionAProveedor
///         (8.B). El más barato de montar del grupo input-de-línea (sin
///         sub-almacén/saldo/RQ/período). MatRev/recepción/salida/vale comparten
///         este patrón y el call-site; no se duplican.</item>
///   <item>articuloId de la <b>LineaConteo cargada</b> → CapturarLineaConteo
///         (+ AgregarRecuento como extra del mismo patrón).</item>
///   <item>articuloId de la <b>línea de salida origen cargada</b> →
///         AplicarDevolucionInterna (8.A).</item>
/// </list>
/// </summary>
public class DecimalesGuardWiringTests
{
    private static readonly Guid ArtPieza = Guid.NewGuid();  // unidad decimales=0
    private static readonly Guid ArtSinFk = Guid.NewGuid();  // FK NULL → guard skip
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static DecimalesUnidadGuard GuardReal() =>
        new(new FakeUnidadMedidaReadPort(new Dictionary<Guid, int?>
        {
            [ArtPieza] = 0,
            [ArtSinFk] = null,
        }));

    private static AlmacenDbContext NuevaDb()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-decimales-{Guid.NewGuid():N}")
            .Options;
        var db = new AlmacenDbContext(opts, new FakeEmpresa(EmpresaId));
        db.Database.EnsureCreated();
        return db;
    }

    // ─────────────────────────── Patrón 1: input de línea ───────────────────────────
    // Representativo: IniciarDevolucionAProveedor (8.B).

    private static IniciarDevolucionAProveedorHandler HandlerP1(AlmacenDbContext db) =>
        new(db, new FakeUser(), new FakeEmpresa(EmpresaId), GuardReal());

    private static IniciarDevolucionAProveedorCommand CmdP1(Guid articuloId, decimal cantidad) =>
        new(ProveedorId: Guid.NewGuid(), Motivo: "test 2b", RecepcionOrigenId: null,
            FacturaProveedorOrigenId: null, OrdenCompraOrigenId: null, SubAlmacenOrigenId: null,
            Lineas: new[] { new DevolucionProveedorLineaInput(articuloId, cantidad, "PZA", 10m, null) });

    [Fact]
    public async Task P1_InputLinea_Pieza_ConDecimal_Rechaza()
    {
        await using var db = NuevaDb();
        var act = () => HandlerP1(db).Handle(CmdP1(ArtPieza, 1.5m), CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be(DecimalesUnidad.CodigoError);
    }

    [Fact]
    public async Task P1_InputLinea_Pieza_Entero_Pasa()
    {
        await using var db = NuevaDb();
        var act = () => HandlerP1(db).Handle(CmdP1(ArtPieza, 2m), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task P1_InputLinea_FkNull_Pasa_Skip()
    {
        await using var db = NuevaDb();
        var act = () => HandlerP1(db).Handle(CmdP1(ArtSinFk, 1.5m), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    // ─────────────────────── Patrón 2: LineaConteo cargada ───────────────────────
    // Representativo: CapturarLineaConteo (+ AgregarRecuento como extra).

    private static async Task<(AlmacenDbContext Db, Guid ConteoId, Guid LineaId)> DbConConteoAsync(Guid articuloId)
    {
        var db = NuevaDb();
        var subAlmacenId = Guid.NewGuid();
        var conteo = new ConteoInventario(
            id: Guid.NewGuid(), empresaId: EmpresaId, tipo: TipoConteo.Rotativo,
            fechaPlanificada: new DateOnly(2026, 6, 1), responsableId: Guid.NewGuid(),
            subAlmacenId: subAlmacenId);
        var linea = new LineaConteo(
            id: Guid.NewGuid(), conteoId: conteo.Id, articuloId: articuloId,
            subAlmacenId: subAlmacenId, ubicacionId: Guid.NewGuid(),
            cantidadTeorica: 100m, costoPromedioSnapshot: 50m);
        conteo.AgregarLinea(linea);
        conteo.Iniciar(); // → EnCurso (snapshot)
        db.Set<ConteoInventario>().Add(conteo);
        await db.SaveChangesAsync();
        return (db, conteo.Id, linea.Id);
    }

    [Fact]
    public async Task P2_LineaConteo_Pieza_ConDecimal_Rechaza()
    {
        var (db, conteoId, lineaId) = await DbConConteoAsync(ArtPieza);
        await using var _ = db;
        var handler = new CapturarLineaConteoHandler(db, new FakeUser(), GuardReal());
        var act = () => handler.Handle(new CapturarLineaConteoCommand(conteoId, lineaId, 1.5m), CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be(DecimalesUnidad.CodigoError);
    }

    [Fact]
    public async Task P2_LineaConteo_Pieza_Entero_Pasa()
    {
        var (db, conteoId, lineaId) = await DbConConteoAsync(ArtPieza);
        await using var _ = db;
        var handler = new CapturarLineaConteoHandler(db, new FakeUser(), GuardReal());
        var act = () => handler.Handle(new CapturarLineaConteoCommand(conteoId, lineaId, 2m), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task P2_LineaConteo_FkNull_Pasa_Skip()
    {
        var (db, conteoId, lineaId) = await DbConConteoAsync(ArtSinFk);
        await using var _ = db;
        var handler = new CapturarLineaConteoHandler(db, new FakeUser(), GuardReal());
        var act = () => handler.Handle(new CapturarLineaConteoCommand(conteoId, lineaId, 1.5m), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact] // extra barato: el segundo call-site del patrón 2 (recuento) también dispara
    public async Task P2_Recuento_Pieza_ConDecimal_Rechaza()
    {
        var (db, conteoId, lineaId) = await DbConConteoAsync(ArtPieza);
        await using var _ = db;
        var handler = new AgregarRecuentoHandler(db, new FakeUser(), GuardReal());
        var act = () => handler.Handle(new AgregarRecuentoCommand(conteoId, lineaId, 1.5m), CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be(DecimalesUnidad.CodigoError);
    }

    // ─────────────────── Patrón 3: línea de salida origen cargada ───────────────────
    // Representativo: AplicarDevolucionInterna (8.A).

    private static async Task<(AlmacenDbContext Db, Guid SalidaId, Guid LineaSalidaId, Guid SubDestinoId, Guid UbicacionId)>
        DbConSalidaRegistradaAsync(Guid articuloId)
    {
        var db = NuevaDb();
        var subDestinoId = Guid.NewGuid();
        db.SubAlmacenes.Add(new SubAlmacen(
            subDestinoId, almacenId: Guid.NewGuid(), clave: "ALM-DST",
            nombre: "Destino", tipo: TipoSubAlmacen.Insumos));

        // C7.2b: bin real destino (rack, no default) + asignación activa del
        // artículo — el guard de entrada de la devolución interna lo exige.
        var ubicacionId = Guid.NewGuid();
        db.Ubicaciones.Add(new Ubicacion(
            ubicacionId, subDestinoId, clave: "R-1", nombre: "Rack 1", esDefault: false));
        db.AsignacionesArticuloUbicacion.Add(
            new AsignacionArticuloUbicacion(Guid.NewGuid(), ubicacionId, articuloId));

        var mov = new MovimientoInventario(
            id: Guid.NewGuid(), tipo: TipoMovimiento.SalidaConsumo, empresaId: EmpresaId,
            fechaMovimiento: new DateOnly(2026, 6, 1));
        var lineaSalidaId = Guid.NewGuid();
        mov.AgregarLinea(new LineaMovimiento(lineaSalidaId, mov.Id, 1, articuloId, 10m, "PZA", 10m));
        mov.Registrar(FolioMovimiento.Construir(TipoMovimiento.SalidaConsumo, 2026, 1), UserId);

        db.Movimientos.Add(mov);
        await db.SaveChangesAsync();
        return (db, mov.Id, lineaSalidaId, subDestinoId, ubicacionId);
    }

    private static AplicarDevolucionInternaHandler HandlerP3(AlmacenDbContext db) =>
        new(db, new NoOpEvents(), new FakeUser(), new FakeEmpresa(EmpresaId), GuardReal());

    private static AplicarDevolucionInternaCommand CmdP3(Guid salidaId, Guid lineaSalidaId, Guid subDestinoId, Guid ubicacionId, decimal cantidad) =>
        new(SalidaOrigenId: salidaId, SubAlmacenDestinoId: subDestinoId,
            FechaMovimiento: new DateOnly(2026, 6, 1), EstadoMaterial: "Integro",
            Motivo: "test 2b", Observaciones: null,
            Lineas: new[] { new DevolucionInternaLineaInput(lineaSalidaId, cantidad, UbicacionId: ubicacionId) });

    [Fact]
    public async Task P3_SalidaOrigen_Pieza_ConDecimal_Rechaza()
    {
        var (db, salidaId, lineaSalidaId, subDestinoId, ubicacionId) = await DbConSalidaRegistradaAsync(ArtPieza);
        await using var _ = db;
        var act = () => HandlerP3(db).Handle(CmdP3(salidaId, lineaSalidaId, subDestinoId, ubicacionId,1.5m), CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be(DecimalesUnidad.CodigoError);
    }

    [Fact]
    public async Task P3_SalidaOrigen_Pieza_Entero_Pasa()
    {
        var (db, salidaId, lineaSalidaId, subDestinoId, ubicacionId) = await DbConSalidaRegistradaAsync(ArtPieza);
        await using var _ = db;
        var act = () => HandlerP3(db).Handle(CmdP3(salidaId, lineaSalidaId, subDestinoId, ubicacionId,2m), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task P3_SalidaOrigen_FkNull_Pasa_Skip()
    {
        var (db, salidaId, lineaSalidaId, subDestinoId, ubicacionId) = await DbConSalidaRegistradaAsync(ArtSinFk);
        await using var _ = db;
        var act = () => HandlerP3(db).Handle(CmdP3(salidaId, lineaSalidaId, subDestinoId, ubicacionId,1.5m), CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    // ─────────────────────────────── Fakes ───────────────────────────────

    private sealed class FakeUnidadMedidaReadPort(IReadOnlyDictionary<Guid, int?> mapa) : IUnidadMedidaReadPort
    {
        public Task<IReadOnlyDictionary<Guid, int?>> ObtenerDecimalesPorArticulosAsync(
            IEnumerable<Guid> articuloIds, CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<Guid, int?> res = articuloIds
                .Where(mapa.ContainsKey).Distinct().ToDictionary(id => id, id => mapa[id]);
            return Task.FromResult(res);
        }
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId => DecimalesGuardWiringTests.UserId;
        public string? UserName => "Test 2b";
    }

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current => current;
        public bool IsBypassed => true; // InMemory: sin filtro por empresa
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }

    private sealed class NoOpEvents : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
