using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Revision;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// 86e3tabdp S2: cancelar con motivo una recepción. La recepción se registra con el handler real (usuario A) y se cancela
/// con otro usuario (B). EF InMemory no corre el trigger de saldos, por eso cada prueba siembra el saldo como queda
/// DESPUÉS de la salida CEN (cantidad restada, costo promedio sin recalcular).
/// </summary>
public class CancelarRecepcionHandlerTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid AlmacenId = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly Guid SubId = Guid.NewGuid();
    private static readonly Guid SubRevId = Guid.NewGuid();
    private static readonly Guid BinId = Guid.NewGuid();
    private static readonly Guid RevBinId = Guid.NewGuid();
    private static readonly Guid ArticuloId = Guid.NewGuid();
    private static readonly Guid UsuarioA = Guid.NewGuid();
    private static readonly Guid UsuarioB = Guid.NewGuid();

    private sealed class Mundo : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
        public required InMemoryAlmacenDbContext Db { get; init; }
        public CapturaEventos Eventos { get; } = new();
        public CfdiFake Cxp { get; } = CfdiFake.Valido();
        public bool PeriodoAbierto { get; set; } = true;
        public Guid OcId { get; } = Guid.NewGuid();
        public Guid RecepcionId { get; set; }

        public async Task RegistrarAsync(decimal danada = 0m)
        {
            var h = new RegistrarRecepcionConFacturaHandler(Db, new FakeOc(), Eventos, new FakeUser(UsuarioA), new FakeEmpresa(),
                new FakeDecimales(), new PeriodoContableStub(true), new P7Support.Conversion(), Cxp, new ProveedorRfcFake());
            var r = await h.Handle(new RegistrarRecepcionConFacturaCommand(OcId, new DateOnly(2026, 10, 17), Guid.NewGuid(), null, null,
                [new RegistrarRecepcionLineaInput(ArticuloId, ArticuloId, 10m, null, null, BinId, CantidadDanada: danada,
                    MotivoDano: danada > 0 ? "Rotas" : null)]), default);
            RecepcionId = r.RecepcionId;
            Eventos.Eventos.Clear();
        }

        /// <summary>Saldo tras la salida CEN: cantidad restada, costo promedio todavía el de la entrada.</summary>
        public async Task SembrarSaldoPostSalidaAsync(Guid ubicacion, decimal cantidad, decimal costoPromedio)
        {
            Db.SaldosInventario.Add(new SaldoInventario(ubicacion, ubicacion == RevBinId ? SubRevId : SubId, ArticuloId, cantidad, costoPromedio));
            await Db.SaveChangesAsync();
        }

        public CancelarRecepcionHandler Handler(Guid? usuario = null) =>
            new(Db, new FakeOc(), Eventos, new FakeUser(usuario ?? UsuarioB), new FakeEmpresa(), new PeriodoContableStub(PeriodoAbierto),
                new P7Support.Conversion(), Cxp, new FakeClock());

        public Task<CancelarRecepcionResponse> CancelarAsync(Guid? usuario = null, string codigo = "ARTICULO") =>
            Handler(usuario).Handle(new CancelarRecepcionCommand(RecepcionId, codigo, "Era otro artículo"), default);
    }

    private static async Task<Mundo> NuevoMundoAsync()
    {
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-cancelar-{Guid.NewGuid():N}").Options;
        var db = new InMemoryAlmacenDbContext(opts, new FakeEmpresa());
        await db.Database.EnsureCreatedAsync();
        db.Almacenes.Add(new Almacen.Domain.Catalogo.Almacen(AlmacenId, "ALM", "Almacén", SucursalId));
        db.SubAlmacenes.Add(new SubAlmacen(SubId, AlmacenId, "SUB", "Sub", TipoSubAlmacen.Insumos));
        db.SubAlmacenes.Add(new SubAlmacen(SubRevId, AlmacenId, "MAT-REV", "Material en revisión", TipoSubAlmacen.MaterialEnRevision));
        db.Ubicaciones.Add(new Ubicacion(BinId, SubId, "R-1", "Rack 1"));
        db.Ubicaciones.Add(new Ubicacion(Guid.NewGuid(), SubRevId, "UNICA", "Única", esDefault: true));
        db.Ubicaciones.Add(new Ubicacion(RevBinId, SubRevId, "REV-01", "Revisión 1"));
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), BinId, ArticuloId));
        await db.SaveChangesAsync();
        return new Mundo { Db = db };
    }

    private static async Task Rechaza(Task<CancelarRecepcionResponse> tarea, string codigo) =>
        (await FluentActions.Awaiting(() => tarea).Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(codigo);

    [Fact]
    public async Task Feliz_con_hijo_crea_dos_CEN_cancela_ambas_entradas_restaura_costo_y_publica()
    {
        await using var m = await NuevoMundoAsync();
        await m.RegistrarAsync(danada: 2m); // principal 8 en BIN + hijo 2 en MAT-REV, costo OC = 10
        await m.SembrarSaldoPostSalidaAsync(BinId, cantidad: 20m, costoPromedio: 12m);
        await m.SembrarSaldoPostSalidaAsync(RevBinId, cantidad: 5m, costoPromedio: 10m);

        var r = await m.CancelarAsync();

        var movs = await m.Db.Movimientos.Include(x => x.Lineas).ToListAsync();
        var entradas = movs.Where(x => x.Tipo == TipoMovimiento.EntradaCompra).ToList();
        entradas.Should().HaveCount(2).And.OnlyContain(x => x.Estado == EstadoMovimiento.Cancelado && x.Motivo == "ARTICULO: Era otro artículo");
        var cen = movs.Where(x => x.Tipo == TipoMovimiento.CancelacionEntradaCompra).ToList();
        cen.Should().HaveCount(2).And.OnlyContain(x => x.Estado == EstadoMovimiento.Registrado && x.RegistradoPor == UsuarioB);
        cen.Select(x => x.RecepcionOrigenId).Should().BeEquivalentTo(entradas.Select(x => (Guid?)x.Id));
        cen.Sum(x => x.Lineas.Sum(l => l.Cantidad)).Should().Be(10m);
        r.RecepcionId.Should().Be(m.RecepcionId);
        r.FoliosCancelacion.Should().BeEquivalentTo(cen.Select(x => x.Folio!)).And.OnlyContain(f => f.StartsWith("M-CEN2026-"));

        var saldo = await m.Db.SaldosInventario.AsNoTracking().SingleAsync(s => s.UbicacionId == BinId);
        // Valor total previo a la salida = 28*12 = 336; quitar 8 a su costo de entrada (10) → 256 sobre 20 unidades = 12.8.
        saldo.CostoPromedioMxn.Should().Be(12.8m);

        var e = m.Eventos.Unico<OcRecepcionCanceladaIntegrationEvent>();
        e.EventType.Should().Be("almacen.recepcion_oc.cancelada.v1");
        e.RecepcionId.Should().Be(m.RecepcionId);
        e.OrdenCompraId.Should().Be(m.OcId);
        e.CanceladaPor.Should().Be(UsuarioB);
        (e.MotivoCodigo, e.MotivoTexto).Should().Be(("ARTICULO", "Era otro artículo"));
        e.FoliosCancelacion.Should().BeEquivalentTo(r.FoliosCancelacion);
        e.Lineas.Should().HaveCount(2);
        e.Lineas.Sum(l => l.CantidadDocumento).Should().Be(10m);
        e.Lineas.Should().OnlyContain(l => l.LineaOcId == ArticuloId && l.CostoUnitarioMxn == 10m);
        e.SucursalId.Should().Be(SucursalId);
    }

    [Fact]
    public async Task Restaurar_costo_devuelve_el_promedio_previo_a_la_recepcion()
    {
        // Antes: 20 a 100 (valor 2000). Entran 10 a 130 → promedio 110 con 30. Tras la salida CEN: 20 a 110 (valor 2200).
        await using var m = await NuevoMundoAsync();
        await m.RegistrarAsync();
        var fake = await m.Db.Movimientos.Include(x => x.Lineas).SingleAsync();
        typeof(LineaMovimiento).GetProperty("CostoUnitarioMxn")!.SetValue(fake.Lineas.Single(), 130m);
        await m.Db.SaveChangesAsync();
        await m.SembrarSaldoPostSalidaAsync(BinId, cantidad: 20m, costoPromedio: 110m);

        await m.CancelarAsync();

        (await m.Db.SaldosInventario.AsNoTracking().SingleAsync()).CostoPromedioMxn.Should().Be(100m);
    }

    [Fact]
    public async Task Repetir_es_idempotente_no_crea_otro_CEN_ni_otro_evento()
    {
        await using var m = await NuevoMundoAsync();
        await m.RegistrarAsync(danada: 2m);
        await m.SembrarSaldoPostSalidaAsync(BinId, 20m, 10m);
        await m.SembrarSaldoPostSalidaAsync(RevBinId, 5m, 10m);
        var primera = await m.CancelarAsync();
        m.Eventos.Eventos.Clear();

        var segunda = await m.CancelarAsync();

        segunda.FoliosCancelacion.Should().BeEquivalentTo(primera.FoliosCancelacion);
        (await m.Db.Movimientos.CountAsync(x => x.Tipo == TipoMovimiento.CancelacionEntradaCompra)).Should().Be(2);
        m.Eventos.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task Mismo_usuario_que_registro_no_puede_cancelar()
    {
        await using var m = await NuevoMundoAsync();
        await m.RegistrarAsync();
        await Rechaza(m.CancelarAsync(UsuarioA), "RECEPCION_CANCELA_MISMO_USUARIO");
        (await m.Db.Movimientos.CountAsync(x => x.Tipo == TipoMovimiento.CancelacionEntradaCompra)).Should().Be(0);
    }

    [Fact]
    public async Task Periodo_cerrado_bloquea()
    {
        await using var m = await NuevoMundoAsync();
        await m.RegistrarAsync();
        m.PeriodoAbierto = false;
        await Rechaza(m.CancelarAsync(), "PERIODO_CONTABLE_NO_ADMITE");
    }

    [Fact]
    public async Task Con_activos_fijos_bloquea()
    {
        await using var m = await NuevoMundoAsync();
        await m.RegistrarAsync();
        m.Db.RecepcionesActivos.Add(new RecepcionActivo(Guid.NewGuid(), EmpresaId, m.RecepcionId, m.OcId, Guid.NewGuid(), 1m));
        await m.Db.SaveChangesAsync();
        await Rechaza(m.CancelarAsync(), "RECEPCION_CON_ACTIVOS");
    }

    [Fact]
    public async Task Recepcion_solo_de_activos_sin_movimiento_tambien_bloquea_con_activos()
    {
        await using var m = await NuevoMundoAsync();
        m.RecepcionId = Guid.NewGuid();
        m.Db.RecepcionesActivos.Add(new RecepcionActivo(Guid.NewGuid(), EmpresaId, m.RecepcionId, m.OcId, Guid.NewGuid(), 1m));
        await m.Db.SaveChangesAsync();
        await Rechaza(m.CancelarAsync(), "RECEPCION_CON_ACTIVOS");
    }

    [Fact]
    public async Task Recepcion_inexistente_es_404()
    {
        await using var m = await NuevoMundoAsync();
        m.RecepcionId = Guid.NewGuid();
        await FluentActions.Awaiting(() => m.CancelarAsync()).Should().ThrowAsync<EntityNotFoundException>();
    }

    [Fact]
    public async Task Con_dictamen_del_material_en_revision_bloquea()
    {
        await using var m = await NuevoMundoAsync();
        await m.RegistrarAsync(danada: 2m);
        var hijo = await m.Db.Movimientos.Include(x => x.Lineas).SingleAsync(x => x.RecepcionOrigenId == m.RecepcionId);
        m.Db.DictamenesRevision.Add(new DictamenRevision(Guid.NewGuid(), EmpresaId, hijo.Lineas.Single().Id, hijo.Id,
            ResultadoDictamen.Baja, 1m, "Baja", UsuarioB, movimientoSalidaRevisionId: Guid.NewGuid()));
        await m.Db.SaveChangesAsync();
        await Rechaza(m.CancelarAsync(), "RECEPCION_CON_DICTAMEN");
    }

    [Fact]
    public async Task Con_devolucion_a_proveedor_activa_bloquea()
    {
        await using var m = await NuevoMundoAsync();
        await m.RegistrarAsync();
        await m.SembrarSaldoPostSalidaAsync(BinId, 20m, 10m);
        m.Db.DevolucionesProveedor.Add(new DevolucionAProveedor(Guid.NewGuid(), EmpresaId, Guid.NewGuid(), "Mala", UsuarioA, recepcionOrigenId: m.RecepcionId));
        await m.Db.SaveChangesAsync();
        await Rechaza(m.CancelarAsync(), "RECEPCION_CON_DEVOLUCION");
    }

    [Fact]
    public async Task Con_salida_posterior_del_mismo_articulo_en_el_bin_bloquea()
    {
        await using var m = await NuevoMundoAsync();
        await m.RegistrarAsync();
        await m.SembrarSaldoPostSalidaAsync(BinId, 20m, 10m);
        var salida = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.SalidaConsumo, EmpresaId, new DateOnly(2026, 10, 20));
        salida.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), salida.Id, 1, ArticuloId, 1m, "PZA", 10m, ubicacionId: BinId));
        salida.Registrar(FolioMovimiento.Construir(TipoMovimiento.SalidaConsumo, 2026, 1), UsuarioA);
        m.Db.Movimientos.Add(salida);
        await m.Db.SaveChangesAsync();
        await Rechaza(m.CancelarAsync(), "RECEPCION_CON_SALIDAS");
    }

    [Fact]
    public async Task Con_saldo_menor_a_lo_recibido_bloquea()
    {
        await using var m = await NuevoMundoAsync();
        await m.RegistrarAsync();
        await m.SembrarSaldoPostSalidaAsync(BinId, 4m, 10m);
        await Rechaza(m.CancelarAsync(), "RECEPCION_CON_SALIDAS");
    }

    [Theory]
    [InlineData(0.01, "Autorizada", true)]
    [InlineData(0, "Pagada", true)]
    [InlineData(0, "Autorizada", false)]
    [InlineData(0, "Capturada", false)]
    public async Task Factura_con_dinero_aplicado_o_pagada_bloquea(double saldado, string estado, bool bloquea)
    {
        await using var m = await NuevoMundoAsync();
        await m.RegistrarAsync();
        await m.SembrarSaldoPostSalidaAsync(BinId, 20m, 10m);
        m.Cxp.FacturasLigadas.Add(new FacturaLigadaRecepcionLectura(Guid.NewGuid(), "F-1", estado, (decimal)saldado));

        if (bloquea) await Rechaza(m.CancelarAsync(), "RECEPCION_FACTURA_PAGADA");
        else (await m.CancelarAsync()).FoliosCancelacion.Should().HaveCount(1);
    }

    [Fact]
    public async Task Si_un_ajuste_de_costo_posterior_impide_revertir_el_costo_falla_y_no_cancela()
    {
        await using var m = await NuevoMundoAsync();
        await m.RegistrarAsync();
        // Promedio muy bajo frente al costo de entrada (10): quitar 10 unidades a 10 dejaría valor negativo.
        await m.SembrarSaldoPostSalidaAsync(BinId, 12m, 1m);
        await Rechaza(m.CancelarAsync(), "RECEPCION_COSTO_NO_REVERTIBLE");
    }

    [Theory]
    [InlineData("ARTICULO", "x", true)]
    [InlineData("otro", "x", true)]
    [InlineData("NO_EXISTE", "x", false)]
    [InlineData("OC", "", false)]
    [InlineData("OC", "  ", false)]
    public void Validador_exige_motivo_del_catalogo_y_texto(string codigo, string texto, bool valido) =>
        new CancelarRecepcionValidator().Validate(new CancelarRecepcionCommand(Guid.NewGuid(), codigo, texto)).IsValid.Should().Be(valido);

    [Fact]
    public void Validador_limita_el_texto_a_450()
    {
        var v = new CancelarRecepcionValidator();
        v.Validate(new CancelarRecepcionCommand(Guid.NewGuid(), "OC", new string('a', 450))).IsValid.Should().BeTrue();
        v.Validate(new CancelarRecepcionCommand(Guid.NewGuid(), "OC", new string('a', 451))).IsValid.Should().BeFalse();
    }

    private sealed class CapturaEventos : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct) { Eventos.Add(integrationEvent); return Task.CompletedTask; }
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
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class FakeUser(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "test";
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 23, 18, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeDecimales : IDecimalesUnidadGuard
    {
        public Task ValidarAsync(IEnumerable<CantidadAValidar> cantidades, CancellationToken ct) => Task.CompletedTask;
    }
}
