using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.Almacen.UnitTests.TestSupport;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// Tests para el criterio CA2.10 (GAP-9 / G1.12):
/// Los artículos de servicio no se reciben en Almacén ni generan existencia.
/// Verifica que:
/// 1) Si se intenta recibir especificando LineaOcId de una línea que no está en OcLectura.Lineas
///    (filtrada por ser servicio), el handler rechaza con LINEA_OC_NO_ENCONTRADA.
/// 2) Si se intenta recibir sin LineaOcId con un ArticuloId que no pertenece a las líneas recibibles
///    de la OC, el handler rechaza con RECEPCION_ARTICULO_NO_EN_OC.
/// 3) Una línea física válida se registra exitosamente.
/// </summary>
public class RegistrarRecepcionServiciosCa210Tests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();

    [Fact]
    public async Task Factura_Rechaza_LineaId_De_Servicio_No_Presente_En_OcLectura()
    {
        var db = NuevaDb();
        await using var _ = db;
        var (subId, binId, artFisico, artServicio, ocId, lineaFisicaId, lineaServicioId) = await SembrarDatosAsync(db);

        var handler = NuevoFacturaHandler(db, ocId, artFisico, lineaFisicaId);

        var cmd = new RegistrarRecepcionConFacturaCommand(
            OrdenCompraId: ocId,
            FechaMovimiento: new DateOnly(2026, 10, 8),
            CfdiRecibidoId: Guid.NewGuid(),
            CfdiUuidFiscal: null,
            Observaciones: null,
            Lineas: new List<RegistrarRecepcionLineaInput>
            {
                new(
                    ArticuloId: artServicio,
                    LineaOcId: lineaServicioId,
                    Cantidad: 1m,
                    UbicacionReferencia: null,
                    Comentario: null,
                    UbicacionId: binId)
            });

        var act = () => handler.Handle(cmd, CancellationToken.None);
        (await act.Should().ThrowAsync<EntityNotFoundException>())
            .Which.Code.Should().Be("LINEA_OC_NO_ENCONTRADA");
    }

    [Fact]
    public async Task Factura_Rechaza_ArticuloId_De_Servicio_Sin_LineaOcId()
    {
        var db = NuevaDb();
        await using var _ = db;
        var (subId, binId, artFisico, artServicio, ocId, lineaFisicaId, _) = await SembrarDatosAsync(db);

        var handler = NuevoFacturaHandler(db, ocId, artFisico, lineaFisicaId);

        var cmd = new RegistrarRecepcionConFacturaCommand(
            OrdenCompraId: ocId,
            FechaMovimiento: new DateOnly(2026, 10, 8),
            CfdiRecibidoId: Guid.NewGuid(),
            CfdiUuidFiscal: null,
            Observaciones: null,
            Lineas: new List<RegistrarRecepcionLineaInput>
            {
                new(
                    ArticuloId: artServicio,
                    LineaOcId: null,
                    Cantidad: 1m,
                    UbicacionReferencia: null,
                    Comentario: null,
                    UbicacionId: binId)
            });

        var act = () => handler.Handle(cmd, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("RECEPCION_ARTICULO_NO_EN_OC");
    }

    [Fact]
    public async Task Factura_Acepta_LineaFisica_Valida()
    {
        var db = NuevaDb();
        await using var _ = db;
        var (subId, binId, artFisico, _, ocId, lineaFisicaId, _) = await SembrarDatosAsync(db);

        var handler = NuevoFacturaHandler(db, ocId, artFisico, lineaFisicaId);

        var cmd = new RegistrarRecepcionConFacturaCommand(
            OrdenCompraId: ocId,
            FechaMovimiento: new DateOnly(2026, 10, 8),
            CfdiRecibidoId: Guid.NewGuid(),
            CfdiUuidFiscal: null,
            Observaciones: null,
            Lineas: new List<RegistrarRecepcionLineaInput>
            {
                new(
                    ArticuloId: artFisico,
                    LineaOcId: lineaFisicaId,
                    Cantidad: 2m,
                    UbicacionReferencia: null,
                    Comentario: null,
                    UbicacionId: binId)
            });

        var resp = await handler.Handle(cmd, CancellationToken.None);
        resp.Folio.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PackingList_Rechaza_LineaId_De_Servicio_No_Presente_En_OcLectura()
    {
        var db = NuevaDb();
        await using var _ = db;
        var (subId, binId, artFisico, artServicio, ocId, lineaFisicaId, lineaServicioId) = await SembrarDatosAsync(db);

        var handler = NuevoPackingListHandler(db, ocId, artFisico, lineaFisicaId);

        var cmd = new RegistrarRecepcionConPackingListCommand(
            OrdenCompraId: ocId,
            FechaMovimiento: new DateOnly(2026, 10, 8),
            PackingListBlobRef: "blob-123",
            Observaciones: null,
            Lineas: new List<RegistrarRecepcionLineaInput>
            {
                new(
                    ArticuloId: artServicio,
                    LineaOcId: lineaServicioId,
                    Cantidad: 1m,
                    UbicacionReferencia: null,
                    Comentario: null,
                    UbicacionId: binId)
            });

        var act = () => handler.Handle(cmd, CancellationToken.None);
        (await act.Should().ThrowAsync<EntityNotFoundException>())
            .Which.Code.Should().Be("LINEA_OC_NO_ENCONTRADA");
    }

    [Fact]
    public async Task PackingList_Rechaza_ArticuloId_De_Servicio_Sin_LineaOcId()
    {
        var db = NuevaDb();
        await using var _ = db;
        var (subId, binId, artFisico, artServicio, ocId, lineaFisicaId, _) = await SembrarDatosAsync(db);

        var handler = NuevoPackingListHandler(db, ocId, artFisico, lineaFisicaId);

        var cmd = new RegistrarRecepcionConPackingListCommand(
            OrdenCompraId: ocId,
            FechaMovimiento: new DateOnly(2026, 10, 8),
            PackingListBlobRef: "blob-123",
            Observaciones: null,
            Lineas: new List<RegistrarRecepcionLineaInput>
            {
                new(
                    ArticuloId: artServicio,
                    LineaOcId: null,
                    Cantidad: 1m,
                    UbicacionReferencia: null,
                    Comentario: null,
                    UbicacionId: binId)
            });

        var act = () => handler.Handle(cmd, CancellationToken.None);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("RECEPCION_ARTICULO_NO_EN_OC");
    }

    // ─── Helpers y Fakes ──────────────────────────────────────────────────────

    private static async Task<(Guid subId, Guid binId, Guid artFisico, Guid artServicio, Guid ocId, Guid lineaFisicaId, Guid lineaServicioId)>
        SembrarDatosAsync(AlmacenDbContext db)
    {
        var subId = Guid.NewGuid();
        var binId = Guid.NewGuid();
        var artFisico = Guid.NewGuid();
        var artServicio = Guid.NewGuid();
        var ocId = Guid.NewGuid();
        var lineaFisicaId = Guid.NewGuid();
        var lineaServicioId = Guid.NewGuid();

        db.Ubicaciones.Add(new Ubicacion(binId, subId, "R-1", "Rack 1", esDefault: false));
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), binId, artFisico));
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), binId, artServicio));
        await db.SaveChangesAsync();

        return (subId, binId, artFisico, artServicio, ocId, lineaFisicaId, lineaServicioId);
    }

    private static AlmacenDbContext NuevaDb()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-recep-ca210-{Guid.NewGuid():N}")
            .Options;
        var db = new AlmacenDbContext(opts, new FakeEmpresa(EmpresaId));
        db.Database.EnsureCreated();
        return db;
    }

    private static RegistrarRecepcionConFacturaHandler NuevoFacturaHandler(
        AlmacenDbContext db, Guid ocId, Guid artFisico, Guid lineaFisicaId)
    {
        var fakeOc = new FakeOcConFisico(ocId, artFisico, lineaFisicaId);
        return new RegistrarRecepcionConFacturaHandler(
            db, fakeOc, new FakeArticulos(), new FakeEvents(),
            new FakeUser(), new FakeEmpresa(EmpresaId), new FakeDecimales(), new PeriodoContableStub(abierto: true));
    }

    private static RegistrarRecepcionConPackingListHandler NuevoPackingListHandler(
        AlmacenDbContext db, Guid ocId, Guid artFisico, Guid lineaFisicaId)
    {
        var fakeOc = new FakeOcConFisico(ocId, artFisico, lineaFisicaId);
        return new RegistrarRecepcionConPackingListHandler(
            db, fakeOc, new FakeArticulos(), new FakeEvents(),
            new FakeUser(), new FakeEmpresa(EmpresaId), new FakeDecimales(), new PeriodoContableStub(abierto: true));
    }

    private sealed class FakeOcConFisico(Guid ocId, Guid artFisicoId, Guid lineaFisicaId) : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid id, CancellationToken ct)
        {
            if (id != ocId) return Task.FromResult<OcLectura?>(null);

            // Simula el contrato filtrado de ComprasOcReadAdapter: solo líneas NO servicio.
            var lineas = new List<OcLineaLectura>
            {
                new(
                    LineaId: lineaFisicaId,
                    ArticuloId: artFisicoId,
                    UnidadMedida: "PZA",
                    CantidadSolicitada: 10m,
                    CantidadRecibida: 0m,
                    PrecioUnitarioMxn: 50m)
            };

            return Task.FromResult<OcLectura?>(new OcLectura(
                Id: ocId,
                Folio: "OC-MID2026-000100",
                ProveedorId: Guid.NewGuid(),
                EmpresaId: EmpresaId,
                Estado: "Autorizada",
                Lineas: lineas));
        }

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>
            {
                [ocId] = "OC-MID2026-000100"
            });
    }

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = current;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeArticulos : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) =>
            Task.FromResult<ArticuloLectura?>(
                new ArticuloLectura(articuloId, "ART", "Artículo", "PZA", null, null, true));

        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
                ids.ToDictionary(id => id, id => new ArticuloLectura(id, "ART", "Artículo", "PZA", null, null, true)));
    }

    private sealed class FakeEvents : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId => EmpresaId;
        public string? UserName => "test";
    }

    private sealed class FakeDecimales : IDecimalesUnidadGuard
    {
        public Task ValidarAsync(IEnumerable<CantidadAValidar> cantidades, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
