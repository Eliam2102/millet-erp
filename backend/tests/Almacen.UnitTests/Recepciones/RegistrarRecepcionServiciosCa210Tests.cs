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
///    (filtrada por ser servicio), el handler rechaza con RECEPCION_ARTICULO_NO_EN_OC.
/// 2) Sin LineaOcId la recepción se rechaza con RECEPCION_LINEA_OC_REQUERIDA (P1: cada artículo indica su línea),
///    así un servicio tampoco entra por artículo suelto.
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
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("RECEPCION_ARTICULO_NO_EN_OC");
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
            .Which.Code.Should().Be("RECEPCION_LINEA_OC_REQUERIDA");
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
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("RECEPCION_ARTICULO_NO_EN_OC");
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
            .Which.Code.Should().Be("RECEPCION_LINEA_OC_REQUERIDA");
    }

    [Fact]
    public async Task Compra_mixta_recibe_diez_cajas_y_un_activo_sin_generar_existencia_del_activo()
    {
        await using var db = NuevaDb();
        var (_, bin, silicon, montacargas, oc, lineaSilicon, lineaActivo) = await SembrarDatosAsync(db);
        var eventos = new FakeEvents();
        var handler = new RegistrarRecepcionConPackingListHandler(db,
            new FakeOcMixta(oc, silicon, montacargas, lineaSilicon, lineaActivo), eventos,
            new FakeUser(), new FakeEmpresa(EmpresaId), new FakeDecimales(), new PeriodoContableStub(abierto: true), new P7Support.Conversion());
        var respuesta = await handler.Handle(new RegistrarRecepcionConPackingListCommand(oc, new DateOnly(2026,10,8), "FIX-AF-packing.pdf", null,
            [new(silicon, lineaSilicon, 10, null, null, bin), new(montacargas, lineaActivo, 1, null, null, null)]), CancellationToken.None);
        var movimiento = await db.Movimientos.Include(x=>x.Lineas).SingleAsync(x=>x.Id==respuesta.RecepcionId);
        Assert.Single(movimiento.Lineas); Assert.Equal(silicon, movimiento.Lineas.Single().ArticuloId);
        var ledger = Assert.Single(await db.RecepcionesActivos.ToListAsync()); Assert.Equal(1, ledger.Cantidad); Assert.Equal(lineaActivo, ledger.LineaOcId);
        var publicado = Assert.Single(eventos.Publicados.OfType<Millet.Almacen.Application.Integration.OcRecepcionRegistradaIntegrationEvent>());
        Assert.Equal(2, publicado.Lineas.Count); Assert.Equal(1, publicado.Lineas.Single(x=>x.EsActivoFijo).Cantidad);
    }
    private sealed class FakeOcMixta(Guid oc, Guid silicon, Guid montacargas, Guid lineaSilicon, Guid lineaActivo) : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<OcLectura?>(new OcLectura(oc,"OC-MID2026-000100",Guid.NewGuid(),EmpresaId,"Autorizada",
            [new(lineaSilicon,silicon,"PZA",10,0,100), new(lineaActivo,montacargas,"PZA",1,0,500000,EsActivoFijo:true,ClaseActivoId:Guid.NewGuid())]));
        public Task<IReadOnlyDictionary<Guid,string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ids,CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<Guid,string>>(new Dictionary<Guid,string>{{oc,"OC-MID2026-000100"}});
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CA39_Persiste_importacion_en_ambas_variantes_incluyendo_activos(bool packing, bool activo)
    {
        await using var db = NuevaDb();
        var (_, bin, articulo, _, oc, linea, _) = await SembrarDatosAsync(db);
        var puerto = new FakeOcConFisico(oc, articulo, linea, importacion: true, activo: activo);
        var eventos = new FakeEvents();
        var datos = new ImportacionRecepcionInput("26 16 1234 6000001", "BL-DEMO", "CONT-DEMO", "16", "A1");
        RegistrarRecepcionLineaInput[] partidas = [new(articulo, linea, 1, null, null, bin, FraccionArancelaria: "7007199900")];
        RegistrarRecepcionResponse respuesta;
        if (packing)
            respuesta = await new RegistrarRecepcionConPackingListHandler(db, puerto, eventos, new FakeUser(), new FakeEmpresa(EmpresaId),
                new FakeDecimales(), new PeriodoContableStub(true), new P7Support.Conversion())
                .Handle(new(oc, new(2026,10,22), "DEMO-packing.pdf", null, partidas, Importacion: datos), default);
        else
            respuesta = await new RegistrarRecepcionConFacturaHandler(db, puerto, eventos, new FakeUser(), new FakeEmpresa(EmpresaId),
                new FakeDecimales(), new PeriodoContableStub(true), new P7Support.Conversion(), CfdiFake.Valido(), new ProveedorRfcFake())
                .Handle(new(oc, new(2026,10,22), Guid.NewGuid(), null, null, partidas, Importacion: datos), default);
        db.ChangeTracker.Clear();
        var guardada = await db.RecepcionesImportacion.Include(x => x.Partidas).SingleAsync();
        Assert.Equal(respuesta.RecepcionId, guardada.RecepcionId);
        Assert.Equal("261612346000001", guardada.NumeroPedimento);
        Assert.Equal("BL-DEMO", guardada.NumeroBl); Assert.Equal("CONT-DEMO", guardada.NumeroContenedor);
        Assert.Equal("16", guardada.Aduana); Assert.Equal("A1", guardada.ClavePedimento);
        Assert.Equal("7007199900", Assert.Single(guardada.Partidas).FraccionArancelaria);
        var lectura = Assert.Single(await new Millet.Almacen.Infrastructure.PublicAdapters.RecepcionesImportacionReadAdapter(db).ObtenerPorOcAsync(oc, default));
        Assert.Equal(guardada.NumeroPedimento, lectura.NumeroPedimento);
        Assert.Equal(guardada.NumeroBl, lectura.NumeroBl); Assert.Equal(guardada.NumeroContenedor, lectura.NumeroContenedor);
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
            db, fakeOc, new FakeEvents(),
            new FakeUser(), new FakeEmpresa(EmpresaId), new FakeDecimales(), new PeriodoContableStub(abierto: true), new P7Support.Conversion(),
            CfdiFake.Valido(), new ProveedorRfcFake());
    }

    private static RegistrarRecepcionConPackingListHandler NuevoPackingListHandler(
        AlmacenDbContext db, Guid ocId, Guid artFisico, Guid lineaFisicaId)
    {
        var fakeOc = new FakeOcConFisico(ocId, artFisico, lineaFisicaId);
        return new RegistrarRecepcionConPackingListHandler(
            db, fakeOc, new FakeEvents(),
            new FakeUser(), new FakeEmpresa(EmpresaId), new FakeDecimales(), new PeriodoContableStub(abierto: true), new P7Support.Conversion());
    }

    private sealed class FakeOcConFisico(Guid ocId, Guid artFisicoId, Guid lineaFisicaId, bool importacion = false, bool activo = false) : IComprasOcReadPort
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
                    PrecioUnitarioMxn: 50m, EsActivoFijo: activo, ClaseActivoId: activo ? Guid.NewGuid() : null)
            };

            return Task.FromResult<OcLectura?>(new OcLectura(
                Id: ocId,
                Folio: "OC-MID2026-000100",
                ProveedorId: Guid.NewGuid(),
                EmpresaId: EmpresaId,
                Estado: "Autorizada",
                Lineas: lineas, EsImportacion: importacion));
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
                new ArticuloLectura(articuloId, "ART", "Artículo", "PZA", null, true));

        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
                ids.ToDictionary(id => id, id => new ArticuloLectura(id, "ART", "Artículo", "PZA", null, true)));
    }

    private sealed class FakeEvents : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct) { Publicados.Add(integrationEvent); return Task.CompletedTask; }
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
