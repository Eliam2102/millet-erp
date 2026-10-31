using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Application.Conciliaciones;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Conciliaciones;

public sealed class ConciliacionesCommandsTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);

    private static TesoreriaDbContext Db() => new(
        new DbContextOptionsBuilder<TesoreriaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options,
        new TestEmpresaContext());

    private static async Task<CuentaBancaria> CrearCuentaAsync(TesoreriaDbContext db, decimal saldoInicial = 10_000m)
    {
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "0123456789", null, "MXN");
        cuenta.RegistrarSaldoInicial(saldoInicial, new DateOnly(2026, 8, 31), "Saldo apertura");
        db.CuentasBancarias.Add(cuenta);

        var conceptoComision = new ConceptoMovimiento("Comisión bancaria", ClasificacionFlujo.Operacion);
        typeof(ConceptoMovimiento).GetProperty("Id")!.SetValue(conceptoComision, Guid.Parse("0000000b-1001-0000-0000-000000000004"));
        db.ConceptosMovimiento.Add(conceptoComision);

        var conceptoImpuestos = new ConceptoMovimiento("Impuestos", ClasificacionFlujo.Operacion);
        typeof(ConceptoMovimiento).GetProperty("Id")!.SetValue(conceptoImpuestos, Guid.Parse("0000000b-1001-0000-0000-000000000005"));
        db.ConceptosMovimiento.Add(conceptoImpuestos);

        await db.SaveChangesAsync();
        return cuenta;
    }

    [Fact]
    public async Task Criterio3_comision_350_mas_iva_alta_asistida_crea_movimientos_con_concepto_y_quedan_emparejados()
    {
        // 3 Comisión de $350 + IVA que no está en el sistema
        // Se da de alta desde la línea
        // Se crean los movimientos con su concepto y quedan emparejados
        await using var db = Db();
        var cuenta = await CrearCuentaAsync(db);
        var publisher = new FakePublisher();

        var conc = Conciliacion.Crear(Empresa, cuenta.Id, 2026, 9, 10_000m);
        // Línea en el banco por total de $406.00 ($350 de comisión + $56 de IVA)
        var linea = ExtractoLinea.Crear(
            conc.Id, 1, new DateOnly(2026, 9, 15), "COMISION POR TRANSFERENCIA SPEI", "COM-991",
            cargo: 406.00m, abono: null);

        conc.CargarLineas(new[] { linea }, 10_000m);
        db.Conciliaciones.Add(conc);
        await db.SaveChangesAsync();

        var handler = new AltaAsistidaDesdeExtractoHandler(db, new TestUser(Usuario), new TestClock(), publisher);
        var res = await handler.Handle(new AltaAsistidaDesdeExtractoCommand(
            conc.Id,
            linea.Id,
            Guid.Parse("0000000b-1001-0000-0000-000000000004"), // Comisión bancaria
            IncluyeIva: true,
            TasaIva: 0.16m,
            MontoBase: 350.00m,
            MontoIva: 56.00m), default);

        res.Estado.Should().Be(EstadoExtractoLinea.Emparejada);
        res.MatchTipo.Should().Be(MatchTipo.AltaAsistida);
        res.Matches.Should().HaveCount(2);

        // Se crearon ambos movimientos en la base de datos
        var movs = await db.MovimientosBancarios.ToListAsync();
        movs.Should().HaveCount(2);

        var movComision = movs.Single(m => m.Monto == 350.00m);
        movComision.ConceptoId.Should().Be(Guid.Parse("0000000b-1001-0000-0000-000000000004"));
        movComision.Sentido.Should().Be(SentidoMovimiento.Egreso);
        movComision.EstadoConciliacion.Should().Be(EstadoConciliacionMovimiento.Conciliado);

        var movIva = movs.Single(m => m.Monto == 56.00m);
        movIva.ConceptoId.Should().Be(Guid.Parse("0000000b-1001-0000-0000-000000000005"));
        movIva.Sentido.Should().Be(SentidoMovimiento.Egreso);
        movIva.EstadoConciliacion.Should().Be(EstadoConciliacionMovimiento.Conciliado);

        // Se emitieron eventos de integración para la póliza contable (C1 / normalizador C15)
        publisher.PublishedEvents.Should().HaveCount(2);
        publisher.PublishedEvents.Should().AllBeOfType<MovimientoBancarioRegistradoIntegrationEvent>();
    }

    [Fact]
    public async Task Criterio5_y_T9_2_a_desemparejar_regresa_a_pendiente_con_bitacora_de_usuario_y_motivo()
    {
        // 5 Línea emparejada por error -> Se desempareja -> Vuelve a pendiente; la bitácora guarda quién y por qué
        await using var db = Db();
        var cuenta = await CrearCuentaAsync(db);
        var conc = Conciliacion.Crear(Empresa, cuenta.Id, 2026, 9, 10_000m);

        var linea = ExtractoLinea.Crear(
            conc.Id, 1, new DateOnly(2026, 9, 12), "PAGO PROVEEDOR VIDRIOS", "REF-100",
            cargo: 2_500m, abono: null);

        var mov = MovimientoBancario.RegistrarPagoProveedor(
            Empresa, cuenta, Guid.NewGuid(), 2_500m, new DateOnly(2026, 9, 12), "REF-100", null, Usuario, Ahora);

        linea.Emparejar(mov.Id, MatchTipo.AutoReferenciaImporte, 2_500m, Usuario, Ahora);
        mov.MarcarConciliado();

        conc.CargarLineas(new[] { linea }, 10_000m);
        db.Conciliaciones.Add(conc);
        db.MovimientosBancarios.Add(mov);
        await db.SaveChangesAsync();

        linea.Estado.Should().Be(EstadoExtractoLinea.Emparejada);
        mov.EstadoConciliacion.Should().Be(EstadoConciliacionMovimiento.Conciliado);

        // Desemparejar con motivo
        var handler = new DesemparejarLineaHandler(db, new TestUser(Usuario), new TestClock());
        var res = await handler.Handle(new DesemparejarLineaCommand(
            conc.Id, linea.Id, "Se emparejó con factura incorrecta de otro proveedor"), default);

        res.Estado.Should().Be(EstadoExtractoLinea.Pendiente);
        res.Matches.Should().BeEmpty();
        res.MatchMovimientoId.Should().BeNull();

        // El movimiento bancario regresa a NoConciliado
        var movRecargado = await db.MovimientosBancarios.FindAsync(mov.Id);
        movRecargado!.EstadoConciliacion.Should().Be(EstadoConciliacionMovimiento.NoConciliado);

        // La bitácora registra la acción con el motivo y el usuario
        var concRecargada = await db.Conciliaciones.Include(c => c.Bitacora).FirstAsync(c => c.Id == conc.Id);
        concRecargada.Bitacora.Should().ContainSingle(b =>
            b.Accion == "Desemparejamiento" &&
            b.Motivo == "Se emparejó con factura incorrecta de otro proveedor" &&
            b.UsuarioId == Usuario);
    }

    [Fact]
    public async Task Desemparejar_sin_motivo_falla_validacion()
    {
        var validator = new DesemparejarLineaValidator();
        var result = validator.Validate(new DesemparejarLineaCommand(Guid.NewGuid(), Guid.NewGuid(), "   "));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Motivo");
    }

    [Fact]
    public async Task EmparejarManual_varios_a_uno_empareja_tres_movimientos_a_una_linea()
    {
        // Emparejamiento varios a uno: un depósito en banco de $10,000 cubre 3 pagos en libros: $3,000 + $2,000 + $5,000
        await using var db = Db();
        var cuenta = await CrearCuentaAsync(db);
        var conc = Conciliacion.Crear(Empresa, cuenta.Id, 2026, 9, 20_000m);

        var linea = ExtractoLinea.Crear(
            conc.Id, 1, new DateOnly(2026, 9, 20), "DEPOSITO SUCURSAL MERIDA", null,
            cargo: null, abono: 10_000m);

        var mov1 = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 3_000m, new DateOnly(2026, 9, 20), "P1", null, null, null, Usuario, Ahora);
        var mov2 = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 2_000m, new DateOnly(2026, 9, 20), "P2", null, null, null, Usuario, Ahora);
        var mov3 = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 5_000m, new DateOnly(2026, 9, 20), "P3", null, null, null, Usuario, Ahora);

        conc.CargarLineas(new[] { linea }, 20_000m);
        db.Conciliaciones.Add(conc);
        db.MovimientosBancarios.AddRange(mov1, mov2, mov3);
        await db.SaveChangesAsync();

        var handler = new EmparejarManualHandler(db, new TestUser(Usuario), new TestClock());
        var res = await handler.Handle(new EmparejarManualCommand(
            conc.Id, linea.Id, new[] { mov1.Id, mov2.Id, mov3.Id }), default);

        res.Estado.Should().Be(EstadoExtractoLinea.Emparejada);
        res.Matches.Should().HaveCount(3);
        res.MatchTipo.Should().Be(MatchTipo.Manual);

        var movs = await db.MovimientosBancarios.ToListAsync();
        movs.Should().AllSatisfy(m => m.EstadoConciliacion.Should().Be(EstadoConciliacionMovimiento.Conciliado));
    }

    [Fact]
    public async Task CerrarConciliacion_con_diferencia_cero_genera_acta_pdf_valida()
    {
        await using var db = Db();
        var cuenta = await CrearCuentaAsync(db, saldoInicial: 5_000m);

        // Movimiento en libros de $5,000 en el mes
        var mov = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 5_000m, new DateOnly(2026, 9, 15), "DEP1", null, null, null, Usuario, Ahora);
        db.MovimientosBancarios.Add(mov);

        // Saldo inicial $5,000 + ingreso $5,000 = Saldo libros $10,000
        var conc = Conciliacion.Crear(Empresa, cuenta.Id, 2026, 9, 10_000m);
        var linea = ExtractoLinea.Crear(conc.Id, 1, new DateOnly(2026, 9, 15), "DEPOSITO BANCO", "DEP1", cargo: null, abono: 5_000m);
        linea.Emparejar(mov.Id, MatchTipo.AutoReferenciaImporte, 5_000m, Usuario, Ahora);
        mov.MarcarConciliado();

        conc.CargarLineas(new[] { linea }, 10_000m);
        db.Conciliaciones.Add(conc);
        await db.SaveChangesAsync();

        var handler = new CerrarConciliacionHandler(db, new TestUser(Usuario), new TestClock());
        byte[] pdfBytes = await handler.Handle(new CerrarConciliacionCommand(
            conc.Id, "Lic. Fernando Ancona", "Gerente de Tesorería"), default);

        pdfBytes.Should().NotBeEmpty();
        // Verificar firma de archivo PDF (%PDF)
        System.Text.Encoding.ASCII.GetString(pdfBytes[..4]).Should().Be("%PDF");

        var concCerrada = await db.Conciliaciones.FindAsync(conc.Id);
        concCerrada!.Estado.Should().Be(EstadoConciliacion.Cerrada);
        concCerrada.FirmanteNombre.Should().Be("Lic. Fernando Ancona");
        concCerrada.FirmantePuesto.Should().Be("Gerente de Tesorería");
    }

    // Contextos de prueba
    private sealed class TestEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => Empresa;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed class TestUser(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "UsuarioPrueba";
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> PublishedEvents { get; } = new();

        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }
}
