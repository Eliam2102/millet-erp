using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Application.PagosACuenta;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Pagos;

/// <summary>
/// G1.6 (Bloque C): el evento de pago aplicado a factura trae la cuenta
/// bancaria del movimiento y el tipo de cambio del pasivo (null si no hay),
/// tanto en el pago directo como en la liga tardía de un pago a cuenta.
/// </summary>
public sealed class EventosContablesG16Tests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Fecha = new(2026, 7, 15);

    private static (TesoreriaDbContext Db, CuentaBancaria Cuenta, PasivoPendientePago Pasivo) Escenario(decimal? tipoCambio)
    {
        var db = new TesoreriaDbContext(
            new DbContextOptionsBuilder<TesoreriaDbContext>()
                .UseInMemoryDatabase($"tesoreria-g16-{Guid.NewGuid():N}").Options,
            new FakeEmpresa());
        var cuenta = new CuentaBancaria(EmpresaId, "BBVA", "0123456789", null, "MXN");
        var pasivo = new PasivoPendientePago(EmpresaId, Guid.NewGuid(), Guid.NewGuid(), null,
            1000m, 1000m, "MXN", tipoCambio, new DateOnly(2026, 8, 1), null, "F-1", Ahora, "PUE");
        db.CuentasBancarias.Add(cuenta);
        db.PasivosPendientesPago.Add(pasivo);
        return (db, cuenta, pasivo);
    }

    [Fact]
    public async Task P3_pago_manual_respeta_recibido_y_no_reutiliza_pagado_ante_evento_atrasado()
    {
        var (db, cuenta, pasivo) = Escenario(null);
        await using var _ = db;
        await db.SaveChangesAsync();
        var limite = new ElegibleFijo();
        var handler = new RegistrarPagoProveedorHandler(db, new FakeEmpresa(), new FakeUser(), new Abierto(),
            new SinDatosDeProveedor(), limite, new Captura(), new FakeClock());
        var excesivo = () => handler.Handle(new(cuenta.Id, Fecha, [new(pasivo.FacturaProveedorId, 1000m)]), default);
        await excesivo.Should().ThrowAsync<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>()
            .Where(e => e.Code == "PAGO_EXCEDE_ELEGIBLE");
        await handler.Handle(new(cuenta.Id, Fecha, [new(pasivo.FacturaProveedorId, 800m)]), default);
        var duplicado = () => handler.Handle(new(cuenta.Id, Fecha, [new(pasivo.FacturaProveedorId, 1m)]), default);
        await duplicado.Should().ThrowAsync<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>()
            .Where(e => e.Code == "PAGO_EXCEDE_ELEGIBLE");
        limite.Monto = 1000m;
        await handler.Handle(new(cuenta.Id, Fecha, [new(pasivo.FacturaProveedorId, 200m)]), default);
        (await db.AplicacionesPagoProveedor.SumAsync(a => a.ImporteAplicado)).Should().Be(1000m);
    }

    private sealed class ElegibleFijo : IElegibleFacturaReadPort
    {
        public decimal Monto { get; set; } = 800m;
        public Task<decimal> ObtenerLimiteAcumuladoAsync(Guid facturaId, CancellationToken cancellationToken) => Task.FromResult(Monto);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(17.5)]
    public async Task Pago_directo_publica_cuenta_bancaria_y_tipo_de_cambio_del_pasivo(double? tc)
    {
        var (db, cuenta, pasivo) = Escenario((decimal?)tc);
        await using var _ = db;
        await db.SaveChangesAsync();
        var publisher = new Captura();

        await new RegistrarPagoProveedorHandler(db, new FakeEmpresa(), new FakeUser(), new Abierto(), new SinDatosDeProveedor(), new ElegibleSinLimite(), publisher, new FakeClock())
            .Handle(new RegistrarPagoProveedorCommand(cuenta.Id, Fecha,
                [new AplicacionPagoItem(pasivo.FacturaProveedorId, 400m)]), default);

        var e = publisher.Eventos.OfType<PagoFacturaProveedorAplicadoIntegrationEvent>().Single();
        e.CuentaBancariaId.Should().Be(cuenta.Id);
        e.TipoCambio.Should().Be((decimal?)tc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(17.5)]
    public async Task Liga_tardia_de_pago_a_cuenta_publica_cuenta_bancaria_y_tipo_de_cambio(double? tc)
    {
        var (db, cuenta, pasivo) = Escenario((decimal?)tc);
        await using var _ = db;
        var movimiento = MovimientoBancario.RegistrarPagoACuenta(EmpresaId, cuenta, pasivo.ProveedorId,
            400m, Fecha, "spei-1", null, "urgente", UsuarioId, Ahora);
        db.MovimientosBancarios.Add(movimiento);
        await db.SaveChangesAsync();
        var publisher = new Captura();

        await new LigarPagoACuentaHandler(db, new FakeEmpresa(), publisher, new FakeClock())
            .Handle(new LigarPagoACuentaCommand(movimiento.Id, pasivo.FacturaProveedorId, 400m), default);

        var e = publisher.Eventos.OfType<PagoFacturaProveedorAplicadoIntegrationEvent>().Single();
        e.CuentaBancariaId.Should().Be(cuenta.Id);
        e.TipoCambio.Should().Be((decimal?)tc);
    }

    private sealed class Captura : IIntegrationEventPublisher
    {
        public List<object> Eventos { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct)
        {
            Eventos.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId => UsuarioId;
        public string? UserName => "test";
    }

    private sealed class Abierto : IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }

    /// <summary>G1.1: sin datos del proveedor el pago no se bloquea; esta prueba solo revisa los campos contables del evento.</summary>
    private sealed class SinDatosDeProveedor : IProveedorBancoReadPort
    {
        public Task<ProveedorBancoDto?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken) =>
            Task.FromResult<ProveedorBancoDto?>(null);

        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(
            IReadOnlyCollection<Guid> proveedorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(new Dictionary<Guid, ProveedorBancoDto>());
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }
    private sealed class ElegibleSinLimite : Millet.Tesoreria.Domain.Ports.IElegibleFacturaReadPort
    {
        public Task<decimal> ObtenerLimiteAcumuladoAsync(Guid id, CancellationToken ct) => Task.FromResult(decimal.MaxValue);
    }

}
