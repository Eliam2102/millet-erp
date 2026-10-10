using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.TestSupport;

internal sealed class P1Fixture : IAsyncDisposable
{
    public static readonly Guid EmpresaId = Guid.NewGuid();
    public Guid ArticuloId { get; } = Guid.NewGuid();
    public Guid BinId { get; } = Guid.NewGuid();
    public Guid SubId { get; } = Guid.NewGuid();
    public Guid LineaId { get; } = Guid.NewGuid();
    public Guid DocumentoId { get; } = Guid.NewGuid();
    public static readonly DateOnly Fecha = new(2026, 10, 9);
    public InMemoryAlmacenDbContext Db { get; }
    public Contexto Context { get; } = new();
    public Eventos Events { get; } = new();
    public Decimales Guard { get; } = new();
    public OcPort Oc { get; }
    public RqPort Rq { get; }
    public P1Fixture()
    {
        Db = new(new DbContextOptionsBuilder<InMemoryAlmacenDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Context);
        Db.Database.EnsureCreated();
        var alm = new Domain.Catalogo.Almacen(Guid.NewGuid(), "P1", "Almacén P1", Guid.NewGuid());
        Db.Almacenes.Add(alm);
        Db.SubAlmacenes.Add(new SubAlmacen(SubId, alm.Id, "P1", "Revisión P1", TipoSubAlmacen.MaterialEnRevision));
        Db.Ubicaciones.Add(new Ubicacion(BinId, SubId, "R1", "Rack P1"));
        Db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), BinId, ArticuloId));
        Db.SaldosInventario.Add(new Millet.Almacen.Domain.Saldos.SaldoInventario(BinId, SubId, ArticuloId, 100, 25));
        Db.SaveChanges();
        Oc = new(new(DocumentoId, "OC-P1", Guid.NewGuid(), EmpresaId, "Autorizada", [new(LineaId, ArticuloId, "PZA", 10, 0, 25)]));
        Rq = new(new(DocumentoId, "RQ-P1", EmpresaId, Guid.NewGuid(), alm.Id, null, "EnSurtido", [new(LineaId, ArticuloId, "PZA", 10, 0, null, null, 10)]));
    }
    public async Task<MovimientoInventario> MovimientoAsync(TipoMovimiento tipo, decimal cantidad = 10)
    {
        var m = new MovimientoInventario(Guid.NewGuid(), tipo, EmpresaId, Fecha);
        if (tipo == TipoMovimiento.EntradaCompra) m.VincularRecepcionVarianteA(DocumentoId, null, Guid.NewGuid(), null);
        if (tipo == TipoMovimiento.SalidaPorVale)
        {
            m.VincularSalida(null, "vale.pdf", null);
            m.EstablecerPlazoRegularizacion(await new Calendario().SumarHorasAsync(Fecha, 48, default));
        }
        m.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), m.Id, 1, ArticuloId, cantidad, "PZA", 25, ubicacionId: BinId, lineaOcId: LineaId));
        m.Registrar(FolioMovimiento.Construir(tipo, Fecha.Year, 100), EmpresaId);
        Db.Movimientos.Add(m);
        await Db.SaveChangesAsync();
        return m;
    }
    public ValueTask DisposeAsync() => Db.DisposeAsync();
    public sealed class Contexto : ICurrentUserContext, ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public Guid? UserId => EmpresaId;
        public string? UserName => "P1";
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
    public sealed class Eventos : IIntegrationEventPublisher
    {
        public List<object> Items { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct) { Items.Add(integrationEvent); return Task.CompletedTask; }
    }
    public sealed class Decimales : IDecimalesUnidadGuard
    {
        public Task ValidarAsync(IEnumerable<CantidadAValidar> cantidades, CancellationToken ct) => Task.CompletedTask;
    }
    public sealed class Calendario : ICalendarioHabil
    {
        public Task<DateTimeOffset> SumarHorasAsync(DateOnly fecha, int horas, CancellationToken ct) => Task.FromResult(
            CalendarioHabil.SumarHoras(fecha, horas, CalendarioHabil.LeerFestivos(CalendarioHabil.FestivosIniciales), TimeZoneInfo.FindSystemTimeZoneById("America/Merida")));
    }
    public sealed class Centros : ICentroCostoElegibilidadPort
    {
        public Task ValidarAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
    }
    public sealed class OcPort(OcLectura? lectura) : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(lectura);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
    public sealed class RqPort(RequisicionLectura? lectura) : IComprasRequisicionReadPort
    {
        public Task<RequisicionLectura?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(lectura);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
    public sealed class Articulos : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ArticuloLectura?>(new(id, "P1", "Artículo P1", "PZA", 10m, null, true));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(new Dictionary<Guid, ArticuloLectura>());
    }
}
