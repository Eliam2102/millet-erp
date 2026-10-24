using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>86e3tabdp S4: bitácora de cancelación en el detalle de la recepción y remanente de costo (el kardex Alfak se prueba en Api.IntegrationTests/CancelarRecepcionTests: InMemory no traduce su join).</summary>
public class ObtenerRecepcionPorIdCancelacionTests
{
    private static readonly Guid SubId = Guid.NewGuid();
    private static readonly Guid SubRevId = Guid.NewGuid();
    private static readonly Guid BinId = Guid.NewGuid();
    private static readonly Guid RevBinId = Guid.NewGuid();
    private static readonly Guid ArtA = Guid.NewGuid();
    private static readonly Guid Registro = Guid.NewGuid();
    private static readonly Guid Cancela = Guid.NewGuid();
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateOnly Fecha = new(2026, 10, 17);

    private sealed record Escenario(InMemoryAlmacenDbContext Db, Guid PrincipalId, Guid HijoId);

    private static MovimientoInventario Mov(TipoMovimiento tipo, Guid ubicacion, decimal cantidad, int folio, Guid usuario, DateOnly? fecha = null)
    {
        var m = new MovimientoInventario(Guid.NewGuid(), tipo, Empresa, fecha ?? Fecha);
        m.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), m.Id, 1, ArtA, cantidad, "PZA", 10m, ubicacionId: ubicacion));
        m.Registrar(FolioMovimiento.Construir(tipo, 2026, folio), usuario);
        return m;
    }

    private static async Task<Escenario> NuevoAsync(bool cancelada)
    {
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>().UseInMemoryDatabase($"almacen-detalle-cancel-{Guid.NewGuid():N}").Options;
        var db = new InMemoryAlmacenDbContext(opts, new BypassedEmpresa());
        await db.Database.EnsureCreatedAsync();
        var almacen = Guid.NewGuid();
        db.Almacenes.Add(new Almacen.Domain.Catalogo.Almacen(almacen, "ALM", "Almacén", Guid.NewGuid()));
        db.SubAlmacenes.Add(new SubAlmacen(SubId, almacen, "SUB", "Insumos", TipoSubAlmacen.Insumos));
        db.SubAlmacenes.Add(new SubAlmacen(SubRevId, almacen, "REV", "Revisión", TipoSubAlmacen.MaterialEnRevision));
        db.Ubicaciones.Add(new Ubicacion(BinId, SubId, "R-1", "Rack 1"));
        db.Ubicaciones.Add(new Ubicacion(RevBinId, SubRevId, "REV-01", "Revisión 1"));

        var principal = Mov(TipoMovimiento.EntradaCompra, BinId, 8m, 1, Registro);
        var hijo = Mov(TipoMovimiento.EntradaCompra, RevBinId, 2m, 2, Registro);
        typeof(MovimientoInventario).GetProperty("RecepcionOrigenId")!.SetValue(hijo, principal.Id);
        db.Movimientos.AddRange(principal, hijo);
        if (cancelada)
        {
            foreach (var (origen, ubi, cant, folio) in new[] { (principal, BinId, 8m, 1), (hijo, RevBinId, 2m, 2) })
            {
                var cen = Mov(TipoMovimiento.CancelacionEntradaCompra, ubi, cant, folio, Cancela, new DateOnly(2026, 10, 23));
                typeof(MovimientoInventario).GetProperty("Motivo")!.SetValue(cen, "ARTICULO: era otro");
                typeof(MovimientoInventario).GetMethod("VincularCancelacion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(cen, [origen.Id]);
                origen.CancelarRecepcion("ARTICULO: era otro");
                db.Movimientos.Add(cen);
            }
        }
        await db.SaveChangesAsync();
        return new Escenario(db, principal.Id, hijo.Id);
    }

    private static ObtenerRecepcionPorIdHandler Handler(AlmacenDbContext db, Guid usuarioActual) =>
        new(db, new Articulos(), new Ocs(), CfdiFake.Valido(), new Centros(), new Usuarios(), new Actual(usuarioActual));

    [Fact]
    public async Task Cancelada_trae_quien_registro_quien_cancelo_cuando_motivo_y_folios_CEN_con_las_lineas_del_hijo()
    {
        var e = await NuevoAsync(cancelada: true);
        await using var db = e.Db;

        var dto = await Handler(db, Cancela).Handle(new ObtenerRecepcionPorIdQuery(e.PrincipalId), default);

        dto!.Estado.Should().Be(EstadoMovimiento.Cancelado);
        dto.Lineas.Should().HaveCount(2); // principal + hijo MAT-REV cancelado
        dto.RegistradoPor.Should().Be(Registro);
        dto.RegistradoPorNombre.Should().Be("Ana (registró)");
        dto.CanceladaPor.Should().Be(Cancela);
        dto.CanceladaPorNombre.Should().Be("Beto (canceló)");
        dto.CanceladaAt.Should().NotBeNull();
        dto.MotivoCancelacion.Should().Be("ARTICULO: era otro");
        dto.FoliosCancelacion.Should().Equal("M-CEN2026-000001", "M-CEN2026-000002");
        dto.RegistradaPorOtroUsuario.Should().BeTrue(); // quien consulta (Beto) no la registró
    }

    [Fact]
    public async Task Vigente_no_trae_datos_de_cancelacion_y_el_mismo_usuario_no_puede_cancelar()
    {
        var e = await NuevoAsync(cancelada: false);
        await using var db = e.Db;

        var dto = await Handler(db, Registro).Handle(new ObtenerRecepcionPorIdQuery(e.PrincipalId), default);

        dto!.Estado.Should().Be(EstadoMovimiento.Registrado);
        dto.RegistradoPorNombre.Should().Be("Ana (registró)");
        (dto.CanceladaAt, dto.CanceladaPor, dto.CanceladaPorNombre, dto.MotivoCancelacion, dto.FoliosCancelacion)
            .Should().Be((null, null, null, null, null));
        dto.RegistradaPorOtroUsuario.Should().BeFalse();
    }

    [Fact]
    public async Task Remanente_de_costo_ignora_el_CEN_de_la_recepcion_cancelada()
    {
        // Recepción A vigente (5 en R-1) y B cancelada (3 + su CEN): el remanente de A debe seguir completo.
        var e = await NuevoAsync(cancelada: true);
        await using var db = e.Db;
        var a = Mov(TipoMovimiento.EntradaCompra, BinId, 5m, 9, Registro, new DateOnly(2026, 10, 24));
        typeof(MovimientoInventario).GetProperty("FechaRegistro")!.SetValue(a, DateTimeOffset.UtcNow.AddHours(-1)); // A llegó antes que el CEN en el historial
        db.Movimientos.Add(a);
        await db.SaveChangesAsync();

        var remanente = await RecepcionesCostoReadAdapter.RemanenteAsync(db, Empresa, a.Lineas.Single().Id, BinId, ArtA, null, default);

        remanente.Should().Be(5m);
    }

    private sealed class Articulos : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) => Task.FromResult<ArticuloLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(new Dictionary<Guid, ArticuloLectura>());
    }

    private sealed class Ocs : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => Task.FromResult<OcLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class Centros : ICentroCostoReadPort
    {
        public Task<IReadOnlyDictionary<Guid, Dim3Lectura>> ObtenerAsync(IReadOnlyCollection<Guid> dim3Ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Dim3Lectura>>(new Dictionary<Guid, Dim3Lectura>());
    }

    private sealed class Usuarios : IUsuarioReadPort
    {
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>
            { [Registro] = "Ana (registró)", [Cancela] = "Beto (canceló)" }.Where(kv => usuarioIds.Contains(kv.Key)).ToDictionary());
    }

    private sealed class Actual(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "test";
    }

    private sealed class BypassedEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}
