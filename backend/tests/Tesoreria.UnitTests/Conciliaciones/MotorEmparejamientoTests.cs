using FluentAssertions;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Conciliaciones.Emparejamiento;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.UnitTests.Conciliaciones;

public class MotorEmparejamientoTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);

    private static CuentaBancaria CrearCuenta() =>
        new(EmpresaId, "BBVA", "0123456789", null, "MXN");

    [Fact]
    public void Criterio1_50_lineas_46_con_referencia_exacta_empareja_46_y_deja_4_pendientes()
    {
        // 50 líneas del banco, 46 con referencia exacta.
        // Se corre el emparejamiento -> 46 quedan emparejadas; 4 pendientes
        var cuenta = CrearCuenta();
        var conciliacionId = Guid.NewGuid();
        var lineas = new List<ExtractoLinea>();
        var movimientos = new List<MovimientoBancario>();

        // 46 líneas con coincidencia exacta de referencia e importe
        for (int i = 1; i <= 46; i++)
        {
            string referencia = $"REF-EXACTA-{i:D3}";
            decimal monto = 100m * i;
            var fecha = new DateOnly(2026, 9, (i % 25) + 1);

            lineas.Add(ExtractoLinea.Crear(
                conciliacionId, i, fecha, $"Cargo extracto {i}", referencia, cargo: monto, abono: null));

            var mov = MovimientoBancario.RegistrarPagoProveedor(
                EmpresaId, cuenta, Guid.NewGuid(), monto, fecha, referencia, null, UsuarioId, Ahora);
            movimientos.Add(mov);
        }

        // 4 líneas del banco sin match en el sistema
        for (int i = 47; i <= 50; i++)
        {
            lineas.Add(ExtractoLinea.Crear(
                conciliacionId, i, new DateOnly(2026, 9, 28), $"Comisión o cargo bancario {i}", $"SIN-MATCH-{i}", cargo: 50m * i, abono: null));
        }

        var resultado = MotorEmparejamiento.Ejecutar(
            lineas,
            movimientos,
            new EmparejamientoOptions(),
            UsuarioId,
            Ahora);

        resultado.TotalLineasProcesadas.Should().Be(50);
        resultado.EmparejadasPaso1.Should().Be(46);
        resultado.Pendientes.Should().Be(4);
        resultado.TotalEmparejadas.Should().Be(46);

        lineas.Count(l => l.Estado == EstadoExtractoLinea.Emparejada).Should().Be(46);
        lineas.Count(l => l.Estado == EstadoExtractoLinea.Pendiente).Should().Be(4);
        movimientos.Count(m => m.EstadoConciliacion == EstadoConciliacionMovimiento.Conciliado).Should().Be(46);
    }

    [Fact]
    public void Criterio2_linea_de_1000_con_dos_movimientos_posibles_va_a_revision_y_no_se_empareja_sola()
    {
        // Línea de $1,000 con dos movimientos posibles de $1,000
        // Se corre el emparejamiento -> No se empareja sola; aparece en revisión con los dos candidatos
        var cuenta = CrearCuenta();
        var conciliacionId = Guid.NewGuid();
        var fecha = new DateOnly(2026, 9, 15);

        // Línea sin referencia específica o con referencia no coincidente
        var linea = ExtractoLinea.Crear(
            conciliacionId, 1, fecha, "Transferencia bancaria recibida", null, cargo: null, abono: 1_000m);

        // Dos movimientos idénticos en fecha e importe
        var mov1 = MovimientoBancario.RegistrarIngreso(
            EmpresaId, cuenta, 1_000m, fecha, null, null, null, null, UsuarioId, Ahora);
        var mov2 = MovimientoBancario.RegistrarIngreso(
            EmpresaId, cuenta, 1_000m, fecha, null, null, null, null, UsuarioId, Ahora);

        var resultado = MotorEmparejamiento.Ejecutar(
            new[] { linea },
            new[] { mov1, mov2 },
            new EmparejamientoOptions(),
            UsuarioId,
            Ahora);

        resultado.TotalEmparejadas.Should().Be(0);
        resultado.EnRevision.Should().Be(1);

        linea.Estado.Should().Be(EstadoExtractoLinea.EnRevision);
        linea.CandidatosRevisionIds.Should().HaveCount(2);
        linea.CandidatosRevisionIds.Should().Contain(mov1.Id);
        linea.CandidatosRevisionIds.Should().Contain(mov2.Id);

        mov1.EstadoConciliacion.Should().Be(EstadoConciliacionMovimiento.NoConciliado);
        mov2.EstadoConciliacion.Should().Be(EstadoConciliacionMovimiento.NoConciliado);
    }

    [Fact]
    public void Paso2_importe_y_fecha_exacta_empareja_cuando_no_hay_referencia()
    {
        var cuenta = CrearCuenta();
        var conciliacionId = Guid.NewGuid();
        var fecha = new DateOnly(2026, 9, 10);

        var linea = ExtractoLinea.Crear(
            conciliacionId, 1, fecha, "Cobro con tarjeta", null, cargo: null, abono: 2_500m);

        var mov = MovimientoBancario.RegistrarIngreso(
            EmpresaId, cuenta, 2_500m, fecha, null, null, null, null, UsuarioId, Ahora);

        var resultado = MotorEmparejamiento.Ejecutar(
            new[] { linea },
            new[] { mov },
            new EmparejamientoOptions(),
            UsuarioId,
            Ahora);

        resultado.EmparejadasPaso2.Should().Be(1);
        linea.Estado.Should().Be(EstadoExtractoLinea.Emparejada);
        linea.MatchTipo.Should().Be(MatchTipo.AutoImporteFecha);
        mov.EstadoConciliacion.Should().Be(EstadoConciliacionMovimiento.Conciliado);
    }

    [Fact]
    public void Paso3_tolerancia_un_peso_en_ventana_de_tres_dias_empareja_si_es_unico()
    {
        var cuenta = CrearCuenta();
        var conciliacionId = Guid.NewGuid();
        var fechaLinea = new DateOnly(2026, 9, 15);
        var fechaMov = new DateOnly(2026, 9, 17); // 2 días de diferencia (<= 3)

        // Banco reporta $1,500.50, ERP tiene registrado $1,500.00 (diferencia de $0.50 <= $1.00)
        var linea = ExtractoLinea.Crear(
            conciliacionId, 1, fechaLinea, "Cargo SPEI", null, cargo: 1_500.50m, abono: null);

        var mov = MovimientoBancario.RegistrarPagoProveedor(
            EmpresaId, cuenta, Guid.NewGuid(), 1_500.00m, fechaMov, null, null, UsuarioId, Ahora);

        var resultado = MotorEmparejamiento.Ejecutar(
            new[] { linea },
            new[] { mov },
            new EmparejamientoOptions(ToleranciaImporte: 1.00m, VentanaDias: 3),
            UsuarioId,
            Ahora);

        resultado.EmparejadasPaso3.Should().Be(1);
        linea.Estado.Should().Be(EstadoExtractoLinea.Emparejada);
        linea.MatchTipo.Should().Be(MatchTipo.AutoTolerancia);
        mov.EstadoConciliacion.Should().Be(EstadoConciliacionMovimiento.Conciliado);
    }

    [Fact]
    public void Paso3_fuera_de_tolerancia_o_dias_no_empareja()
    {
        var cuenta = CrearCuenta();
        var conciliacionId = Guid.NewGuid();
        var fechaLinea = new DateOnly(2026, 9, 10);
        var fechaMov = new DateOnly(2026, 9, 15); // 5 días de diferencia (> 3)

        var linea = ExtractoLinea.Crear(
            conciliacionId, 1, fechaLinea, "Cargo SPEI", null, cargo: 1_500.00m, abono: null);

        var mov = MovimientoBancario.RegistrarPagoProveedor(
            EmpresaId, cuenta, Guid.NewGuid(), 1_500.00m, fechaMov, null, null, UsuarioId, Ahora);

        var resultado = MotorEmparejamiento.Ejecutar(
            new[] { linea },
            new[] { mov },
            new EmparejamientoOptions(ToleranciaImporte: 1.00m, VentanaDias: 3),
            UsuarioId,
            Ahora);

        resultado.TotalEmparejadas.Should().Be(0);
        linea.Estado.Should().Be(EstadoExtractoLinea.Pendiente);
    }
}
