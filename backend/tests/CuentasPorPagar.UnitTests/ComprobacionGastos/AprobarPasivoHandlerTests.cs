using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.ComprobacionGastos.Reposiciones;
using Millet.CuentasPorPagar.Application.ComprobacionGastos.Transiciones;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.UnitTests.ComprobacionGastos;

public sealed class AprobarPasivoHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Hoy = new(2026, 10, 23);

    private readonly Guid _empresa = Guid.NewGuid();
    private readonly Guid _sucursal = Guid.NewGuid();
    private readonly Guid _usuario = Guid.NewGuid();
    private readonly Guid _empleado = Guid.NewGuid();
    private readonly RecordingPublisher _events = new();
    private readonly RecordingPublisher _gastos = new();
    private readonly CuentasPorPagarDbContext _db;
    private bool _conEmpleado = true;

    public AprobarPasivoHandlerTests()
    {
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase($"cxp-aprobar-pasivo-{Guid.NewGuid():N}").Options;
        _db = new CuentasPorPagarDbContext(options, new EmpresaContext(_empresa), new PeriodoAbiertoStub(), new PeriodoAbiertoStub());
    }

    public void Dispose() => _db.Dispose();

    private AprobarPasivoComprobacionHandler Handler() => new(
        _db, new ReposicionCajaChicaEmisor(_db, _events),
        new Empleados(this), new Usuario(_usuario), new Reloj(), _gastos);

    private Domain.ComprobacionGastos.ComprobacionGastos Pendiente(
        decimal total, string moneda = "MXN",
        DestinoReposicionCaja destino = DestinoReposicionCaja.CuentaSucursal)
    {
        var c = Domain.ComprobacionGastos.ComprobacionGastos.Crear(
            _empresa, TipoComprobacionGastos.ReembolsoCajaChica, _sucursal, Guid.NewGuid(),
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7), moneda, null, Ahora,
            destinoReposicion: destino);
        c.AgregarLinea(Guid.NewGuid(), null, null, Guid.NewGuid(), null, Ahora,
            total, 0m, 0m, total, moneda, null);
        c.EnviarARevision(Ahora);
        _db.ComprobacionesGastos.Add(c);
        return c;
    }

    private void Limite(decimal max, string moneda = "MXN",
        TipoGastoAprobador tipo = TipoGastoAprobador.ReembolsoCajaChica,
        DateOnly? desde = null, DateOnly? hasta = null, bool cerrar = false)
    {
        var l = AprobadorLimite.Crear(_empresa, _empleado, tipo, max, moneda, desde ?? Hoy.AddDays(-30), hasta);
        if (cerrar) l.Cerrar(Hoy.AddDays(-1));
        _db.AprobadoresLimites.Add(l);
    }

    private async Task<TransicionComprobacionResponse> Aprobar(Domain.ComprobacionGastos.ComprobacionGastos c)
    {
        await _db.SaveChangesAsync();
        return await Handler().Handle(new AprobarPasivoComprobacionCommand(c.Id, c.Version), default);
    }

    [Fact]
    public async Task Aprobar_desde_pendiente_aplica_y_emite_una_reposicion_con_evento()
    {
        var c = Pendiente(150m);
        c.Estado.Should().Be(EstadoComprobacionGastos.PendienteAprobarPasivo);

        var r = await Aprobar(c);

        r.Estado.Should().Be(EstadoComprobacionGastos.Aplicada);
        var repos = await _db.ReposicionesCajaChica.ToListAsync();
        repos.Should().ContainSingle();
        repos[0].MontoTotal.Should().Be(150m);
        repos[0].NumeroComprobaciones.Should().Be(1);
        c.ReposicionId.Should().Be(repos[0].Id);
        var ev = _events.Eventos.Should().ContainSingle().Which
            .Should().BeOfType<PasivoAutorizadoParaPagoIntegrationEvent>().Which;
        ev.OrigenTipo.Should().Be(PasivoAutorizadoParaPagoIntegrationEvent.OrigenReposicionCajaChica);
        ev.OrigenId.Should().Be(repos[0].Id);
        var gasto = _gastos.Eventos.Should().ContainSingle().Which.Should().BeOfType<GastoRegistradoIntegrationEvent>().Which;
        gasto.Origen.Should().Be("caja_chica");
        gasto.DocumentoId.Should().Be(c.Id);
        gasto.LineaId.Should().Be(c.Lineas.Single().Id);
        (gasto.Deducible, gasto.ClasificacionNoDeducible, gasto.Total, gasto.IvaNoAcreditable).Should().Be((true, null, 150m, 0m));
    }

    [Fact]
    public async Task Segunda_aprobacion_rechaza_y_no_duplica_reposicion()
    {
        var c = Pendiente(150m);
        await Aprobar(c);

        var again = () => Handler().Handle(new AprobarPasivoComprobacionCommand(c.Id, c.Version), default);

        (await again.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("COMP_NO_APROBABLE_PASIVO");
        (await _db.ReposicionesCajaChica.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Version_incorrecta_lanza_ConcurrencyException()
    {
        var c = Pendiente(150m);
        await _db.SaveChangesAsync();

        var act = () => Handler().Handle(new AprobarPasivoComprobacionCommand(c.Id, c.Version + 5), default);

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task Limite_menor_al_monto_rechaza_sin_reposicion_ni_cambio_de_estado()
    {
        Limite(100m);
        var c = Pendiente(150m);

        var act = () => Aprobar(c);

        var ex = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
        ex.Code.Should().Be("APROBADOR_LIMITE_EXCEDIDO");
        ex.Message.Should().Contain("excede tu límite de aprobación");
        (await _db.ReposicionesCajaChica.CountAsync()).Should().Be(0);
        _events.Eventos.Should().BeEmpty();
        _gastos.Eventos.Should().BeEmpty();
        // El rechazo ocurre antes de persistir: la BD conserva el estado original.
        var guardado = await _db.ComprobacionesGastos.AsNoTracking().SingleAsync(x => x.Id == c.Id);
        guardado.Estado.Should().Be(EstadoComprobacionGastos.PendienteAprobarPasivo);
    }

    [Theory]
    [InlineData(150)]
    [InlineData(149.99)]
    public async Task Limite_es_inclusivo(double monto)
    {
        Limite(150m);
        var c = Pendiente((decimal)monto);

        (await Aprobar(c)).Estado.Should().Be(EstadoComprobacionGastos.Aplicada);
    }

    [Fact]
    public async Task Sin_fila_de_limite_pasa()
    {
        var c = Pendiente(1_000_000m);

        (await Aprobar(c)).Estado.Should().Be(EstadoComprobacionGastos.Aplicada);
    }

    [Fact]
    public async Task Filas_vencidas_futuras_de_otra_moneda_o_tipo_o_cerradas_se_ignoran()
    {
        Limite(1m, hasta: Hoy.AddDays(-1), desde: Hoy.AddDays(-60));
        Limite(1m, desde: Hoy.AddDays(1));
        Limite(1m, moneda: "USD");
        Limite(1m, tipo: TipoGastoAprobador.Viaticos);
        Limite(1m, cerrar: true);
        var c = Pendiente(150m);

        (await Aprobar(c)).Estado.Should().Be(EstadoComprobacionGastos.Aplicada);
    }

    [Fact]
    public async Task Usuario_sin_empleado_vinculado_no_tiene_limite()
    {
        _conEmpleado = false;
        Limite(1m);
        var c = Pendiente(150m);

        (await Aprobar(c)).Estado.Should().Be(EstadoComprobacionGastos.Aplicada);
    }

    [Fact]
    public async Task Comprobacion_en_USD_emite_reposicion_en_USD()
    {
        var c = Pendiente(80m, "USD");

        await Aprobar(c);

        (await _db.ReposicionesCajaChica.SingleAsync()).Moneda.Should().Be("USD");
    }

    [Fact]
    public async Task Legada_en_PorRevisar_se_puede_aprobar()
    {
        var c = Pendiente(150m);
        // Fila legada: antes de P8.5 caja chica se enviaba a PorRevisar.
        c.GetType().GetProperty(nameof(c.Estado))!.SetValue(c, EstadoComprobacionGastos.PorRevisar);

        (await Aprobar(c)).Estado.Should().Be(EstadoComprobacionGastos.Aplicada);
    }

    [Theory]
    [InlineData(DestinoReposicionCaja.CuentaSucursal, PasivoAutorizadoParaPagoIntegrationEvent.BeneficiarioCajaSucursal)]
    [InlineData(DestinoReposicionCaja.Responsable, PasivoAutorizadoParaPagoIntegrationEvent.BeneficiarioEmpleado)]
    public async Task Forma_del_evento(DestinoReposicionCaja destino, string beneficiario)
    {
        var c = Pendiente(150m, destino: destino);

        await Aprobar(c);

        var ev = _events.Eventos.Single().Should().BeOfType<PasivoAutorizadoParaPagoIntegrationEvent>().Which;
        ev.FacturaProveedorId.Should().Be(Guid.Empty);
        ev.TipoBeneficiario.Should().Be(beneficiario);
        ev.MontoTotal.Should().Be(ev.SaldoPendiente).And.Be(150m);
        ev.Moneda.Should().Be("MXN");
    }

    private sealed class EmpresaContext(Guid id) : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = id;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }

    private sealed class Usuario(Guid id) : ICurrentUserContext
    {
        public Guid? UserId { get; } = id;
        public string? UserName => "test";
    }

    private sealed class Reloj : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class Empleados(AprobarPasivoHandlerTests t) : IEmpleadoReadPort
    {
        public Task<EmpleadoDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<EmpleadoDto?>(null);
        public Task<EmpleadoDto?> ObtenerPorUsuarioAsync(Guid usuarioId, CancellationToken ct) =>
            Task.FromResult<EmpleadoDto?>(t._conEmpleado
                ? new(t._empleado, t._empresa, "FIX aprobador", "fix@millet.test", null, null, true)
                : null);
    }

    private sealed class RecordingPublisher : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct)
        {
            Eventos.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
