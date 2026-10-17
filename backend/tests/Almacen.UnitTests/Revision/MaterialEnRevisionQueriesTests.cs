using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Revision;

public class MaterialEnRevisionQueriesTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly Guid AlmacenId = Guid.NewGuid();
    private static readonly Guid SubId = Guid.NewGuid();
    private static readonly Guid SubRevId = Guid.NewGuid();
    private static readonly Guid BinId = Guid.NewGuid();
    private static readonly Guid RevBinId = Guid.NewGuid();
    private static readonly Guid ArticuloId = Guid.NewGuid();
    private static readonly Guid LineaOcId = Guid.NewGuid();

    private sealed record Sembrado(InMemoryAlmacenDbContext Db, Guid PrincipalLineaId, Guid RevisionLineaId);

    private static async Task<Sembrado> SembrarAsync()
    {
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-mrev-{Guid.NewGuid():N}").Options;
        var db = new InMemoryAlmacenDbContext(opts, new FakeEmpresa());
        await db.Database.EnsureCreatedAsync();
        db.Almacenes.Add(new Almacen.Domain.Catalogo.Almacen(AlmacenId, "ALM", "Almacén", SucursalId));
        db.SubAlmacenes.Add(new SubAlmacen(SubId, AlmacenId, "INS", "Insumos", TipoSubAlmacen.Insumos));
        db.SubAlmacenes.Add(new SubAlmacen(SubRevId, AlmacenId, "MAT-REV", "Material en revisión", TipoSubAlmacen.MaterialEnRevision));
        db.Ubicaciones.Add(new Ubicacion(BinId, SubId, "R-1", "Rack 1"));
        db.Ubicaciones.Add(new Ubicacion(RevBinId, SubRevId, "REV-01", "Revisión 1"));

        var principal = Entrada(1, null, BinId, 8m, null, out var lp);
        var revision = Entrada(2, principal.Id, RevBinId, 2m, "Vidrio roto", out var lr);
        db.Movimientos.AddRange(principal, revision);
        await db.SaveChangesAsync();
        return new Sembrado(db, lp, lr);
    }

    private static MovimientoInventario Entrada(int n, Guid? principalId, Guid binId, decimal cantidad, string? motivo, out Guid lineaId)
    {
        var mov = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.EntradaCompra, EmpresaId, new DateOnly(2026, 10, 1));
        lineaId = Guid.NewGuid();
        mov.AgregarLinea(new LineaMovimiento(lineaId, mov.Id, 1, ArticuloId, cantidad, "PZA", 10m,
            comentarioLinea: motivo, ubicacionId: binId, lineaOcId: LineaOcId));
        mov.Registrar(FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, n), Guid.NewGuid());
        if (principalId is { } p) mov.VincularRecepcionPrincipal(p);
        return mov;
    }

    private static ListarMaterialEnRevisionHandler Handler(InMemoryAlmacenDbContext db, ICalendarioHabil? cal = null) =>
        new(db, new FakeArticulos(), new FakeOc(), cal ?? new FakeCalendario());

    [Fact]
    public async Task Principal_y_hermano_dan_un_item_pendiente_2_con_sugerida_R1()
    {
        var s = await SembrarAsync();
        var items = await Handler(s.Db).Handle(new ListarMaterialEnRevisionQuery(), default);

        var i = items.Should().ContainSingle().Subject;
        i.CantidadPendiente.Should().Be(2m);
        i.UbicacionDisponibleSugeridaId.Should().Be(BinId);
        i.RecepcionFolio.Should().Be("M-ENT2026-000001");
        i.MotivoDano.Should().Be("Vidrio roto");
    }

    [Fact]
    public async Task Devolucion_no_rechazada_descuenta_y_rechazada_no()
    {
        var s = await SembrarAsync();
        var dev = new DevolucionAProveedor(Guid.NewGuid(), EmpresaId, Guid.NewGuid(), "Daño", Guid.NewGuid());
        dev.AgregarLinea(new LineaDevolucionProveedor(Guid.NewGuid(), dev.Id, 1, ArticuloId, 1m, "PZA", 10m, s.RevisionLineaId));
        s.Db.DevolucionesProveedor.Add(dev);
        await s.Db.SaveChangesAsync();

        (await Handler(s.Db).Handle(new ListarMaterialEnRevisionQuery(), default))
            .Single().CantidadPendiente.Should().Be(1m);

        dev.SolicitarAutorizacion();
        dev.Rechazar("No procede");
        await s.Db.SaveChangesAsync();

        (await Handler(s.Db).Handle(new ListarMaterialEnRevisionQuery(), default))
            .Single().CantidadPendiente.Should().Be(2m);
    }

    [Fact]
    public async Task Documentos_permitidos_vacio_no_devuelve_nada()
    {
        var s = await SembrarAsync();
        var q = new ListarMaterialEnRevisionQuery { DocumentosPermitidos = [] };
        (await Handler(s.Db).Handle(q, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_calendario_la_fecha_limite_es_null()
    {
        var s = await SembrarAsync();
        var items = await Handler(s.Db, new FakeCalendario(sinCalendario: true)).Handle(new ListarMaterialEnRevisionQuery(), default);
        items.Single().FechaLimite.Should().BeNull();
        items.Single().Vencida.Should().BeFalse();
    }

    [Fact]
    public async Task Linea_del_rack_disponible_no_es_linea_de_revision()
    {
        var s = await SembrarAsync();
        (await new ObtenerLineaRevisionHandler(s.Db).Handle(new ObtenerLineaRevisionQuery(s.PrincipalLineaId), default)).Should().BeNull();
        var info = await new ObtenerLineaRevisionHandler(s.Db).Handle(new ObtenerLineaRevisionQuery(s.RevisionLineaId), default);
        info.Should().NotBeNull();
        info!.SucursalId.Should().Be(SucursalId);
    }

    private sealed class FakeCalendario(bool sinCalendario = false) : ICalendarioHabil
    {
        public Task<DateTimeOffset> SumarHorasAsync(DateOnly fecha, int horas, CancellationToken ct) =>
            sinCalendario
                ? throw new BusinessRuleException("CALENDARIO_NO_CONFIGURADO", "Sin calendario.")
                : Task.FromResult(new DateTimeOffset(fecha.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(5));
    }

    private sealed class FakeArticulos : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) =>
            Task.FromResult<ArticuloLectura?>(null);

        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(ids.ToDictionary(
                id => id, id => new ArticuloLectura(id, "ART-1", "Vidrio", "PZA", null, null, true)));
    }

    private sealed class FakeOc : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => Task.FromResult<OcLectura?>(null);

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
