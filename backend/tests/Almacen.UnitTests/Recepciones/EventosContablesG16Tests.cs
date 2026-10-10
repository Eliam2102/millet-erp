using Millet.SharedKernel.Application.Exceptions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Almacen.Application.Conteos;
using Millet.Almacen.Application.DevolucionesInternas;
using Millet.Almacen.Application.DevolucionesProveedor;
using Millet.Almacen.Application.EventListeners;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// G1.6 (Bloque A): campos contables opcionales (almacén, sucursal, sub-almacén,
/// ubicación) en los eventos de Almacén. Un test por publicador + compatibilidad
/// con JSON antiguo (sin los campos nuevos → null).
/// </summary>
public class EventosContablesG16Tests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly Guid AlmacenId = Guid.NewGuid();
    private static readonly Guid SubId = Guid.NewGuid();
    private static readonly Guid BinId = Guid.NewGuid();
    private static readonly Guid ArticuloId = Guid.NewGuid();

    private static async Task<InMemoryAlmacenDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-g16-{Guid.NewGuid():N}").Options;
        var db = new InMemoryAlmacenDbContext(opts, new FakeEmpresa());
        await db.Database.EnsureCreatedAsync();
        db.Almacenes.Add(new Almacen.Domain.Catalogo.Almacen(AlmacenId, "ALM", "Almacén", SucursalId));
        db.SubAlmacenes.Add(new SubAlmacen(SubId, AlmacenId, "SUB", "Sub", TipoSubAlmacen.Insumos));
        db.Ubicaciones.Add(new Ubicacion(BinId, SubId, "R-1", "Rack 1"));
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), BinId, ArticuloId));
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<bool> VerificarPeriodoAsync(
        Func<Task> registrar, bool periodoAbierto, AlmacenDbContext db, CapturaEventos events)
    {
        if (periodoAbierto)
        {
            await registrar();
            return true;
        }

        var movimientosAntes = await db.Movimientos.CountAsync();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(registrar);
        ex.Code.Should().Be("PERIODO_CONTABLE_NO_ADMITE");
        events.Eventos.Should().BeEmpty();
        (await db.Movimientos.CountAsync()).Should().Be(movimientosAntes);
        db.ChangeTracker.HasChanges().Should().BeFalse();
        return false;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recepcion_variante_A_publica_almacen_sucursal_y_subalmacen_por_linea(bool periodoAbierto)
    {
        await using var db = await NuevaDbAsync();
        var events = new CapturaEventos();
        var h = new RegistrarRecepcionConFacturaHandler(db, new FakeOc(), new FakeArticulos(), events,
            new FakeUser(), new FakeEmpresa(), new FakeDecimales(), new PeriodoContableStub(periodoAbierto), new P7Support.Conversion());

        Func<Task> registrar = async () => await h.Handle(new RegistrarRecepcionConFacturaCommand(Guid.NewGuid(), new DateOnly(2026, 5, 23),
            Guid.NewGuid(), null, null,
            [new RegistrarRecepcionLineaInput(ArticuloId, ArticuloId, 5m, null, null, BinId)]), default);

        if (!await VerificarPeriodoAsync(registrar, periodoAbierto, db, events)) return;

        var e = events.Unico<OcRecepcionRegistradaIntegrationEvent>();
        e.AlmacenId.Should().Be(AlmacenId);
        e.SucursalId.Should().Be(SucursalId);
        e.Lineas.Single().SubAlmacenId.Should().Be(SubId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Recepcion_variante_B_publica_almacen_sucursal_y_subalmacen_por_linea(bool periodoAbierto)
    {
        await using var db = await NuevaDbAsync();
        var events = new CapturaEventos();
        var h = new RegistrarRecepcionConPackingListHandler(db, new FakeOc(), new FakeArticulos(), events,
            new FakeUser(), new FakeEmpresa(), new FakeDecimales(), new PeriodoContableStub(periodoAbierto), new P7Support.Conversion());

        Func<Task> registrar = async () => await h.Handle(new RegistrarRecepcionConPackingListCommand(Guid.NewGuid(), new DateOnly(2026, 5, 23),
            "blob://pl", null,
            [new RegistrarRecepcionLineaInput(ArticuloId, ArticuloId, 5m, null, null, BinId)]), default);

        if (!await VerificarPeriodoAsync(registrar, periodoAbierto, db, events)) return;

        var e = events.Unico<OcRecepcionRegistradaIntegrationEvent>();
        e.AlmacenId.Should().Be(AlmacenId);
        e.SucursalId.Should().Be(SucursalId);
        e.Lineas.Single().SubAlmacenId.Should().Be(SubId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Salida_con_requisicion_publica_almacen_y_sucursal(bool periodoAbierto)
    {
        await using var db = await NuevaDbAsync();
        var events = new CapturaEventos();
        db.SaldosInventario.Add(new SaldoInventario(BinId, SubId, ArticuloId, 100, 25));
        await db.SaveChangesAsync();
        var h = new RegistrarSalidaConRequisicionHandler(db, new FakeRq(), events,
            new FakeUser(), new FakeEmpresa(), new FakeDecimales(), new PeriodoContableStub(periodoAbierto), new P7Support.Conversion(), P7Support.Apartados(db));

        Func<Task> registrar = async () => await h.Handle(new RegistrarSalidaConRequisicionCommand(Guid.NewGuid(), new DateOnly(2026, 5, 23),
            null, null,
            [new RegistrarSalidaLineaInput(ArticuloId, ArticuloId, 1m, null, null, null, null, BinId)]), default);

        if (!await VerificarPeriodoAsync(registrar, periodoAbierto, db, events)) return;

        var e = events.Unico<SalidaRequisicionRegistradaIntegrationEvent>();
        e.AlmacenId.Should().Be(AlmacenId);
        e.SucursalId.Should().Be(SucursalId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Salida_por_vale_publica_almacen_y_sucursal(bool periodoAbierto)
    {
        await using var db = await NuevaDbAsync();
        var events = new CapturaEventos();
        db.SaldosInventario.Add(new SaldoInventario(BinId, SubId, ArticuloId, 100, 25));
        await db.SaveChangesAsync();
        var h = new RegistrarSalidaPorValeHandler(db, events, new FakeUser(), new FakeEmpresa(), new FakeDecimales(), new PeriodoContableStub(periodoAbierto), new P1Fixture.Calendario(), new P1Fixture.Centros(), new P7Support.Conversion(), P7Support.Apartados(db));

        Func<Task> registrar = async () => await h.Handle(new RegistrarSalidaPorValeCommand(new DateOnly(2026, 5, 23), "blob://vale", null, null,
            [new RegistrarSalidaLineaInput(ArticuloId, ArticuloId, 1m, null, null, null, null, BinId)]), default);

        if (!await VerificarPeriodoAsync(registrar, periodoAbierto, db, events)) return;

        var e = events.Unico<SalidaRequisicionRegistradaIntegrationEvent>();
        e.AlmacenId.Should().Be(AlmacenId);
        e.SucursalId.Should().Be(SucursalId);
    }

    [Fact]
    public async Task Ajuste_de_conteo_publica_almacen_sucursal_y_deja_ceco_y_motivo_null()
    {
        await using var db = await NuevaDbAsync();
        var conteo = new ConteoInventario(Guid.NewGuid(), EmpresaId, TipoConteo.Rotativo,
            new DateOnly(2026, 5, 25), Guid.NewGuid(), SubId);
        conteo.AgregarLinea(new LineaConteo(Guid.NewGuid(), conteo.Id, ArticuloId, SubId, BinId, 100m, 50m));
        conteo.Iniciar(new Millet.Almacen.Domain.Conteos.ConteoUmbrales(5m, 1000m, 1000m, 10000m));
        conteo.Lineas.Single().Capturar(95m, Guid.NewGuid());
        conteo.EnviarAConciliacion();
        conteo.Aprobar(Guid.NewGuid());
        db.Set<ConteoInventario>().Add(conteo);
        await db.SaveChangesAsync();

        var events = new CapturaEventos();
        await new AplicarConteoHandler(db, events, new FakeUser(), new FakeEmpresa(), new PeriodoContableStub(true))
            .Handle(new AplicarConteoCommand(conteo.Id), default);

        var e = events.Unico<AjusteInventarioAplicadoIntegrationEvent>();
        e.AlmacenId.Should().Be(AlmacenId);
        e.SucursalId.Should().Be(SucursalId);
        e.CentroCostoId.Should().BeNull();
        e.Motivo.Should().BeNull();
    }

    [Fact]
    public async Task D18_diferencia_de_precio_no_crea_movimientos_ni_eventos_de_valoracion()
    {
        await using var db = await NuevaDbAsync();
        var ocId = Guid.NewGuid();
        var events = new CapturaEventos();
        // Recepción real (misma BD) y saldo remanente para que el handler valore.
        await new RegistrarRecepcionConFacturaHandler(db, new FakeOc(), new FakeArticulos(), events,
            new FakeUser(), new FakeEmpresa(), new FakeDecimales(), new PeriodoContableStub(true), new P7Support.Conversion())
            .Handle(new RegistrarRecepcionConFacturaCommand(ocId, new DateOnly(2026, 5, 23), Guid.NewGuid(),
                null, null, [new RegistrarRecepcionLineaInput(ArticuloId, ArticuloId, 5m, null, null, BinId)]), default);
        db.SaldosInventario.Add(new SaldoInventario(BinId, SubId, ArticuloId, 5m, 10m));
        await db.SaveChangesAsync();
        events.Eventos.Clear();

        var movimientosAntes = await db.Movimientos.CountAsync();
        await new DiferenciaPrecioFacturaDetectadaHandler(db,
                NullLogger<DiferenciaPrecioFacturaDetectadaHandler>.Instance)
            .Handle(new DiferenciaPrecioFacturaDetectadaCommand(Guid.NewGuid(),
                new DiferenciaPrecioFacturaDetectadaPayload(EmpresaId, DateTimeOffset.UtcNow,
                    Guid.NewGuid(), ocId, ArticuloId, 5m, 11m, 10m, 1m, 5m)), default);

        events.Eventos.Should().BeEmpty();
        (await db.Movimientos.CountAsync()).Should().Be(movimientosAntes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Devolucion_interna_publica_almacen_y_sucursal_del_subalmacen_destino(bool periodoAbierto)
    {
        await using var db = await NuevaDbAsync();
        var mov = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.SalidaConsumo, EmpresaId,
            new DateOnly(2026, 6, 1));
        var lineaSalidaId = Guid.NewGuid();
        mov.AgregarLinea(new LineaMovimiento(lineaSalidaId, mov.Id, 1, ArticuloId, 10m, "PZA", 10m, ubicacionId: BinId));
        mov.Registrar(FolioMovimiento.Construir(TipoMovimiento.SalidaConsumo, 2026, 1), Guid.NewGuid());
        db.Movimientos.Add(mov);
        await db.SaveChangesAsync();

        var events = new CapturaEventos();
        Func<Task> registrar = async () => await new AplicarDevolucionInternaHandler(db, events, new FakeUser(), new FakeEmpresa(), new FakeDecimales(), new PeriodoContableStub(periodoAbierto))
            .Handle(new AplicarDevolucionInternaCommand(mov.Id, SubId, new DateOnly(2026, 6, 1), "Integro",
                "test", null, [new DevolucionInternaLineaInput(lineaSalidaId, 2m, BinId)]), default);

        if (!await VerificarPeriodoAsync(registrar, periodoAbierto, db, events)) return;

        var e = events.Unico<DevolucionInternaAplicadaIntegrationEvent>();
        e.AlmacenId.Should().Be(AlmacenId);
        e.SucursalId.Should().Be(SucursalId);
    }

    [Fact]
    public async Task Devolucion_a_proveedor_publica_almacen_y_sucursal_del_subalmacen_de_salida()
    {
        await using var db = await NuevaDbAsync();
        var origen = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.EntradaCompra, EmpresaId, new DateOnly(2026, 5, 23));
        origen.VincularRecepcionVarianteA(Guid.NewGuid(), null, Guid.NewGuid(), null);
        var lineaOrigen = new LineaMovimiento(Guid.NewGuid(), origen.Id, 1, ArticuloId, 10, "PZA", 10, ubicacionId: BinId);
        origen.AgregarLinea(lineaOrigen);
        origen.Registrar(FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, 99), EmpresaId);
        db.Movimientos.Add(origen);
        var dev = new DevolucionAProveedor(Guid.NewGuid(), EmpresaId, EmpresaId, "no conforme", Guid.NewGuid(), recepcionOrigenId: origen.Id, ordenCompraOrigenId: origen.OcId);
        dev.AgregarLinea(new LineaDevolucionProveedor(Guid.NewGuid(), dev.Id, 1, ArticuloId, 2m, "PZA", 10m, lineaOrigen.Id));
        dev.AgregarEvidencia(new EvidenciaDevolucionProveedor(Guid.NewGuid(), dev.Id, "Foto", "e.jpg", "blob://e.jpg"));
        dev.SolicitarAutorizacion();
        dev.Autorizar(Guid.NewGuid());
        db.Set<DevolucionAProveedor>().Add(dev);
        await db.SaveChangesAsync();

        var events = new CapturaEventos();
        await new RegistrarSalidaDevolucionAProveedorHandler(db, events, new FakeUser(), new FakeEmpresa(), new FakeOc(), new PeriodoContableStub(true))
            .Handle(new RegistrarSalidaDevolucionAProveedorCommand(dev.Id, SubId, new DateOnly(2026, 6, 1),
                [new DevolucionProveedorSalidaLineaBin(dev.Lineas.Single().Id, BinId)]), default);

        var e = events.Unico<OcDevolucionRegistradaIntegrationEvent>();
        e.AlmacenId.Should().Be(AlmacenId);
        e.SucursalId.Should().Be(SucursalId);
    }

    // ─── Compatibilidad: JSON antiguo (sin campos nuevos) → null ─────────────

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Json_antiguo_sin_campos_nuevos_deserializa_con_null()
    {
        var recep = JsonSerializer.Deserialize<OcRecepcionRegistradaIntegrationEvent>(
            $$"""{"empresaId":"{{EmpresaId}}","ocurridoEn":"2026-05-23T00:00:00Z","recepcionId":"{{Guid.NewGuid()}}","folioRecepcion":"F","ordenCompraId":"{{Guid.NewGuid()}}","fechaMovimiento":"2026-05-23","facturaPendiente":false,"cfdiRecibidoId":null,"cfdiUuidFiscal":null,"observaciones":null,"lineas":[{"lineaRecepcionId":"{{Guid.NewGuid()}}","lineaOcId":null,"articuloId":"{{ArticuloId}}","unidadMedida":"PZA","cantidad":1,"costoUnitarioMxn":1,"montoTotalMxn":1,"ubicacionId":"{{BinId}}"}]}""", Json)!;
        recep.AlmacenId.Should().BeNull();
        recep.SucursalId.Should().BeNull();
        recep.Lineas.Single().SubAlmacenId.Should().BeNull();

        var valorada = JsonSerializer.Deserialize<EntradaInventarioValoradaIntegrationEvent>(
            $$"""{"empresaId":"{{EmpresaId}}","ocurridoEn":"2026-05-23T00:00:00Z","recepcionId":"{{Guid.NewGuid()}}","ordenCompraId":"{{Guid.NewGuid()}}","montoTotalMxn":1,"esAjuste":true,"conceptoContableSugerido":"X","lineas":[{"lineaRecepcionId":"{{Guid.NewGuid()}}","articuloId":"{{ArticuloId}}","cantidad":1,"costoUnitarioMxn":1,"montoLineaMxn":1}]}""", Json)!;
        valorada.AlmacenId.Should().BeNull();
        valorada.SucursalId.Should().BeNull();
        valorada.Lineas.Single().SubAlmacenId.Should().BeNull();
        valorada.Lineas.Single().UbicacionId.Should().BeNull();

        var ajuste = JsonSerializer.Deserialize<AjusteInventarioAplicadoIntegrationEvent>(
            $$"""{"empresaId":"{{EmpresaId}}","ocurridoEn":"2026-05-23T00:00:00Z","conteoId":"{{Guid.NewGuid()}}","montoNetoMxn":1,"aprobadorId":"{{Guid.NewGuid()}}","movimientosGenerados":[]}""", Json)!;
        ajuste.AlmacenId.Should().BeNull();
        ajuste.SucursalId.Should().BeNull();
        ajuste.CentroCostoId.Should().BeNull();
        ajuste.Motivo.Should().BeNull();

        var salida = JsonSerializer.Deserialize<SalidaRequisicionRegistradaIntegrationEvent>(
            $$"""{"empresaId":"{{EmpresaId}}","ocurridoEn":"2026-05-23T00:00:00Z","salidaId":"{{Guid.NewGuid()}}","folioSalida":"S","fechaMovimiento":"2026-05-23","rqId":null,"esPorVale":false,"personaDestinatariaId":null,"valeBlobRef":null,"lineas":[]}""", Json)!;
        salida.AlmacenId.Should().BeNull();
        salida.SucursalId.Should().BeNull();

        var devInt = JsonSerializer.Deserialize<DevolucionInternaAplicadaIntegrationEvent>(
            $$"""{"empresaId":"{{EmpresaId}}","ocurridoEn":"2026-05-23T00:00:00Z","devolucionId":"{{Guid.NewGuid()}}","folioDevolucion":"D","salidaOrigenId":"{{Guid.NewGuid()}}","subAlmacenDestinoId":"{{SubId}}","estadoMaterial":"Integro","costoTotalRevertidoMxn":1,"lineas":[]}""", Json)!;
        devInt.AlmacenId.Should().BeNull();
        devInt.SucursalId.Should().BeNull();

        var devProv = JsonSerializer.Deserialize<OcDevolucionRegistradaIntegrationEvent>(
            $$"""{"empresaId":"{{EmpresaId}}","ocurridoEn":"2026-05-23T00:00:00Z","devolucionId":"{{Guid.NewGuid()}}","folioMovimiento":"D","proveedorId":"{{Guid.NewGuid()}}","recepcionOrigenId":null,"facturaProveedorOrigenId":null,"ordenCompraOrigenId":null,"motivo":"m","montoTotalMxn":1,"lineas":[]}""", Json)!;
        devProv.AlmacenId.Should().BeNull();
        devProv.SucursalId.Should().BeNull();
    }

    // ─── Fakes ───────────────────────────────────────────────────────────────

    private sealed class CapturaEventos : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct)
        {
            Eventos.Add(integrationEvent);
            return Task.CompletedTask;
        }
        public T Unico<T>() => Eventos.OfType<T>().Single();
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeOc : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => Task.FromResult<OcLectura?>(new(ocId, "OC-P1", EmpresaId, EmpresaId, "Autorizada", [new(ArticuloId, ArticuloId, "PZA", 100, 0, 10)]));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class FakeRq : IComprasRequisicionReadPort
    {
        public Task<RequisicionLectura?> ObtenerAsync(Guid rqId, CancellationToken ct) =>
            Task.FromResult<RequisicionLectura?>(new(rqId, "RQ-P1", EmpresaId, EmpresaId, AlmacenId, null, "EnSurtido", [new(ArticuloId, ArticuloId, "PZA", 100, 0, null, null, 100)]));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> rqIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class FakeArticulos : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) =>
            Task.FromResult<ArticuloLectura?>(new ArticuloLectura(articuloId, "ART", "Artículo", "PZA", null, null, true));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(new Dictionary<Guid, ArticuloLectura>());
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId => EmpresaId;
        public string? UserName => "test";
    }

    private sealed class FakeDecimales : IDecimalesUnidadGuard
    {
        public Task ValidarAsync(IEnumerable<CantidadAValidar> cantidades, CancellationToken ct) => Task.CompletedTask;
    }
}
