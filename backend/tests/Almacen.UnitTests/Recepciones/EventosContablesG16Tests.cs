using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Almacen.Application.Conteos;
using Millet.Almacen.Application.EventListeners;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Conteos;
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

    [Fact]
    public async Task Recepcion_variante_A_publica_almacen_sucursal_y_subalmacen_por_linea()
    {
        await using var db = await NuevaDbAsync();
        var events = new CapturaEventos();
        var h = new RegistrarRecepcionConFacturaHandler(db, new FakeOc(), new FakeArticulos(), events,
            new FakeUser(), new FakeEmpresa(), new FakeDecimales());

        await h.Handle(new RegistrarRecepcionConFacturaCommand(Guid.NewGuid(), new DateOnly(2026, 5, 23),
            Guid.NewGuid(), null, null,
            [new RegistrarRecepcionLineaInput(ArticuloId, null, 5m, null, null, BinId)]), default);

        var e = events.Unico<OcRecepcionRegistradaIntegrationEvent>();
        e.AlmacenId.Should().Be(AlmacenId);
        e.SucursalId.Should().Be(SucursalId);
        e.Lineas.Single().SubAlmacenId.Should().Be(SubId);
    }

    [Fact]
    public async Task Recepcion_variante_B_publica_almacen_sucursal_y_subalmacen_por_linea()
    {
        await using var db = await NuevaDbAsync();
        var events = new CapturaEventos();
        var h = new RegistrarRecepcionConPackingListHandler(db, new FakeOc(), new FakeArticulos(), events,
            new FakeUser(), new FakeEmpresa(), new FakeDecimales());

        await h.Handle(new RegistrarRecepcionConPackingListCommand(Guid.NewGuid(), new DateOnly(2026, 5, 23),
            "blob://pl", null,
            [new RegistrarRecepcionLineaInput(ArticuloId, null, 5m, null, null, BinId)]), default);

        var e = events.Unico<OcRecepcionRegistradaIntegrationEvent>();
        e.AlmacenId.Should().Be(AlmacenId);
        e.SucursalId.Should().Be(SucursalId);
        e.Lineas.Single().SubAlmacenId.Should().Be(SubId);
    }

    [Fact]
    public async Task Salida_con_requisicion_publica_almacen_y_sucursal()
    {
        await using var db = await NuevaDbAsync();
        var events = new CapturaEventos();
        var h = new RegistrarSalidaConRequisicionHandler(db, new FakeRq(), events,
            new FakeUser(), new FakeEmpresa(), new FakeDecimales());

        await h.Handle(new RegistrarSalidaConRequisicionCommand(Guid.NewGuid(), new DateOnly(2026, 5, 23),
            null, null,
            [new RegistrarSalidaLineaInput(ArticuloId, null, 1m, null, null, null, null, BinId)]), default);

        var e = events.Unico<SalidaRequisicionRegistradaIntegrationEvent>();
        e.AlmacenId.Should().Be(AlmacenId);
        e.SucursalId.Should().Be(SucursalId);
    }

    [Fact]
    public async Task Salida_por_vale_publica_almacen_y_sucursal()
    {
        await using var db = await NuevaDbAsync();
        var events = new CapturaEventos();
        var h = new RegistrarSalidaPorValeHandler(db, events, new FakeUser(), new FakeEmpresa(), new FakeDecimales());

        await h.Handle(new RegistrarSalidaPorValeCommand(new DateOnly(2026, 5, 23), "blob://vale", null, null,
            [new RegistrarSalidaLineaInput(ArticuloId, null, 1m, null, null, null, null, BinId)]), default);

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
        conteo.Iniciar();
        conteo.Lineas.Single().Capturar(95m, Guid.NewGuid());
        conteo.EnviarAConciliacion();
        conteo.Aprobar(Guid.NewGuid());
        db.Set<ConteoInventario>().Add(conteo);
        await db.SaveChangesAsync();

        var events = new CapturaEventos();
        await new AplicarConteoHandler(db, events, new FakeUser(), new FakeEmpresa())
            .Handle(new AplicarConteoCommand(conteo.Id), default);

        var e = events.Unico<AjusteInventarioAplicadoIntegrationEvent>();
        e.AlmacenId.Should().Be(AlmacenId);
        e.SucursalId.Should().Be(SucursalId);
        e.CentroCostoId.Should().BeNull();
        e.Motivo.Should().BeNull();
    }

    [Fact]
    public async Task Entrada_valorada_por_diferencia_de_precio_publica_dimensiones()
    {
        await using var db = await NuevaDbAsync();
        var ocId = Guid.NewGuid();
        var events = new CapturaEventos();
        // Recepción real (misma BD) y saldo remanente para que el handler valore.
        await new RegistrarRecepcionConFacturaHandler(db, new FakeOc(), new FakeArticulos(), events,
            new FakeUser(), new FakeEmpresa(), new FakeDecimales())
            .Handle(new RegistrarRecepcionConFacturaCommand(ocId, new DateOnly(2026, 5, 23), Guid.NewGuid(),
                null, null, [new RegistrarRecepcionLineaInput(ArticuloId, null, 5m, null, null, BinId)]), default);
        db.SaldosInventario.Add(new SaldoInventario(BinId, SubId, ArticuloId, 5m, 10m));
        await db.SaveChangesAsync();
        events.Eventos.Clear();

        await new DiferenciaPrecioFacturaDetectadaHandler(db, events,
                NullLogger<DiferenciaPrecioFacturaDetectadaHandler>.Instance)
            .Handle(new DiferenciaPrecioFacturaDetectadaCommand(Guid.NewGuid(),
                new DiferenciaPrecioFacturaDetectadaPayload(EmpresaId, DateTimeOffset.UtcNow,
                    Guid.NewGuid(), ocId, ArticuloId, 5m, 11m, 10m, 1m, 5m)), default);

        var e = events.Unico<EntradaInventarioValoradaIntegrationEvent>();
        e.AlmacenId.Should().Be(AlmacenId);
        e.SucursalId.Should().Be(SucursalId);
        e.Lineas.Single().SubAlmacenId.Should().Be(SubId);
        e.Lineas.Single().UbicacionId.Should().Be(BinId);
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
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => Task.FromResult<OcLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class FakeRq : IComprasRequisicionReadPort
    {
        public Task<RequisicionLectura?> ObtenerAsync(Guid rqId, CancellationToken ct) =>
            Task.FromResult<RequisicionLectura?>(null);
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
