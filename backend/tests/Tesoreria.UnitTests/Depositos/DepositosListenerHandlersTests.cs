using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Depositos;

/// <summary>
/// Tests de los handlers de los listeners de ingresos (TES-PR7):
/// proyección de propuestas CxC, expectativa de depósito de Caja y marca
/// de REPP timbrado por desglose de facturas.
/// </summary>
public sealed class DepositosListenerHandlersTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();

    private static readonly Guid FacturaA = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");
    private static readonly Guid FacturaB = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

    private static PropuestaAplicacionCreadaPayload Propuesta(
        Guid? propuestaId = null,
        IReadOnlyList<PropuestaFacturaPayload>? facturas = null) =>
        new(
            EmpresaId: EmpresaId,
            OcurridoEn: Ahora,
            PropuestaId: propuestaId ?? Guid.NewGuid(),
            ClienteId: Guid.NewGuid(),
            DepositoRef: "DEP-123",
            MontoDeposito: 7_500m,
            Moneda: "MXN",
            AjusteNoFiscal: 0m,
            NumeroFacturas: facturas?.Count ?? 2,
            Facturas: facturas ??
            [
                new PropuestaFacturaPayload(FacturaA, "VEN-1", 5_000m),
                new PropuestaFacturaPayload(FacturaB, "VEN-2", 2_500m),
            ]);

    [Fact]
    public async Task Propuesta_se_proyecta_como_deposito_pendiente()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarPropuestaAplicacionHandler(db, new FakeClock(Ahora));
        var payload = Propuesta();
        var eventoId = Guid.NewGuid();

        await handler.Handle(new ProyectarPropuestaAplicacionCommand(eventoId, payload), CancellationToken.None);

        var deposito = await db.DepositosConfirmacion.SingleAsync();
        deposito.PropuestaCxcId.Should().Be(payload.PropuestaId);
        deposito.ClienteId.Should().Be(payload.ClienteId);
        deposito.Estado.Should().Be(EstadoDepositoConfirmacion.Pendiente);
        deposito.MontoEsperado.Should().Be(7_500m);
        deposito.FacturasJson.Should().Contain(FacturaA.ToString());

        var marca = await db.EventosProcesados.SingleAsync();
        marca.EventoId.Should().Be(eventoId);
    }

    [Fact]
    public async Task Propuesta_reentregada_no_duplica()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarPropuestaAplicacionHandler(db, new FakeClock(Ahora));
        var propuestaId = Guid.NewGuid();

        await handler.Handle(
            new ProyectarPropuestaAplicacionCommand(Guid.NewGuid(), Propuesta(propuestaId)), CancellationToken.None);
        await handler.Handle(
            new ProyectarPropuestaAplicacionCommand(Guid.NewGuid(), Propuesta(propuestaId)), CancellationToken.None);

        (await db.DepositosConfirmacion.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Propuesta_sin_facturas_pre_extension_proyecta_json_vacio()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarPropuestaAplicacionHandler(db, new FakeClock(Ahora));
        var payload = Propuesta() with { Facturas = null };

        await handler.Handle(new ProyectarPropuestaAplicacionCommand(Guid.NewGuid(), payload), CancellationToken.None);

        (await db.DepositosConfirmacion.SingleAsync()).FacturasJson.Should().Be("[]");
    }

    [Fact]
    public async Task Caja_cerrada_genera_expectativa_y_sin_efectivo_no()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarExpectativaCajaHandler(db, new FakeClock(Ahora));

        await handler.Handle(new ProyectarExpectativaCajaCommand(Guid.NewGuid(), new CajaSesionCerradaPayload(
            EmpresaId, Ahora, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 7, 14), EfectivoDeclarado: 12_000m)), CancellationToken.None);

        await handler.Handle(new ProyectarExpectativaCajaCommand(Guid.NewGuid(), new CajaSesionCerradaPayload(
            EmpresaId, Ahora, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 7, 14), EfectivoDeclarado: 0m)), CancellationToken.None);

        var expectativa = await db.DepositosConfirmacion.SingleAsync();
        expectativa.CajaSesionId.Should().NotBeNull();
        expectativa.MontoEsperado.Should().Be(12_000m);
        expectativa.ClienteId.Should().BeNull();

        (await db.EventosProcesados.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Repp_timbrado_marca_confirmacion_con_desglose_identico()
    {
        using var db = CrearDbContext();
        var proyector = new ProyectarPropuestaAplicacionHandler(db, new FakeClock(Ahora));
        await proyector.Handle(
            new ProyectarPropuestaAplicacionCommand(Guid.NewGuid(), Propuesta()), CancellationToken.None);

        var deposito = await db.DepositosConfirmacion.SingleAsync();
        ConfirmarDirecto(deposito);
        await db.SaveChangesAsync();

        var handler = new MarcarReppTimbradoHandler(db, new FakeClock(Ahora), NullLogger<MarcarReppTimbradoHandler>.Instance);
        await handler.Handle(new MarcarReppTimbradoCommand(Guid.NewGuid(), new ReciboPagoTimbradoPayload(
            EmpresaId, Ahora, Guid.NewGuid(), "UUID-REPP", 7_500m,
            [
                new ReppFacturaPagadaPayload(FacturaB, 2_500m),
                new ReppFacturaPagadaPayload(FacturaA, 5_000m),
            ])), CancellationToken.None);

        (await db.DepositosConfirmacion.SingleAsync()).ReppTimbrado.Should().BeTrue();
    }

    [Fact]
    public async Task Repp_timbrado_sin_confirmacion_correspondiente_se_procesa_sin_efecto()
    {
        using var db = CrearDbContext();
        var proyector = new ProyectarPropuestaAplicacionHandler(db, new FakeClock(Ahora));
        await proyector.Handle(
            new ProyectarPropuestaAplicacionCommand(Guid.NewGuid(), Propuesta()), CancellationToken.None);
        var deposito = await db.DepositosConfirmacion.SingleAsync();
        ConfirmarDirecto(deposito);
        await db.SaveChangesAsync();

        var handler = new MarcarReppTimbradoHandler(db, new FakeClock(Ahora), NullLogger<MarcarReppTimbradoHandler>.Instance);

        // REPP manual de otro cobro: mismas facturas pero importes distintos.
        var eventoId = Guid.NewGuid();
        await handler.Handle(new MarcarReppTimbradoCommand(eventoId, new ReciboPagoTimbradoPayload(
            EmpresaId, Ahora, Guid.NewGuid(), "UUID-OTRO", 1_000m,
            [new ReppFacturaPagadaPayload(FacturaA, 1_000m)])), CancellationToken.None);

        (await db.DepositosConfirmacion.SingleAsync()).ReppTimbrado.Should().BeFalse();
        (await db.EventosProcesados.AnyAsync(e => e.EventoId == eventoId)).Should().BeTrue();
    }

    // ------------------------------------------------------------ helpers

    private static void ConfirmarDirecto(DepositoConfirmacion deposito)
    {
        var cuenta = new Millet.Tesoreria.Domain.Cuentas.CuentaBancaria(
            EmpresaId, "BBVA", "0123456789", clabe: null, moneda: "MXN");
        var movimiento = Millet.Tesoreria.Domain.Movimientos.MovimientoBancario.RegistrarIngreso(
            EmpresaId, cuenta, deposito.MontoEsperado!.Value, new DateOnly(2026, 7, 15),
            "SPEI-1", null, null, null, Guid.NewGuid(), Ahora);
        deposito.Confirmar(movimiento, Guid.NewGuid(), Ahora);
    }

    private static TesoreriaDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<TesoreriaDbContext>()
            .UseInMemoryDatabase(databaseName: $"tesoreria_test_{Guid.NewGuid()}")
            .Options;
        return new TesoreriaDbContext(options, new FakeEmpresaContext());
    }

    private sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class FakeClock(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset UtcNow => ahora;
    }
}
