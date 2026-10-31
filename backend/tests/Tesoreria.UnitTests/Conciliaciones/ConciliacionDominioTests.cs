using FluentAssertions;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Conciliaciones;

namespace Millet.Tesoreria.UnitTests.Conciliaciones;

public class ConciliacionDominioTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid CuentaBancariaId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 9, 30, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CA9_5_reimportar_extracto_dos_veces_no_duplica_lineas()
    {
        // CA9.5: El estado de cuenta de BBVA de septiembre se importa dos veces -> Los movimientos no se duplican
        var conc = Conciliacion.Crear(EmpresaId, CuentaBancariaId, 2026, 9, 10_000m);

        var linea1 = ExtractoLinea.Crear(conc.Id, 1, new DateOnly(2026, 9, 1), "PAGO 1", "REF1", cargo: 100m, abono: null, lineaHash: "hash-001");
        var linea2 = ExtractoLinea.Crear(conc.Id, 2, new DateOnly(2026, 9, 2), "PAGO 2", "REF2", cargo: 200m, abono: null, lineaHash: "hash-002");

        int agregadas1 = conc.CargarLineas(new[] { linea1, linea2 }, 10_000m);
        agregadas1.Should().Be(2);
        conc.Lineas.Should().HaveCount(2);

        // Reimportar exactamente las mismas líneas
        var linea1Dup = ExtractoLinea.Crear(conc.Id, 1, new DateOnly(2026, 9, 1), "PAGO 1", "REF1", cargo: 100m, abono: null, lineaHash: "hash-001");
        var linea2Dup = ExtractoLinea.Crear(conc.Id, 2, new DateOnly(2026, 9, 2), "PAGO 2", "REF2", cargo: 200m, abono: null, lineaHash: "hash-002");

        int agregadas2 = conc.CargarLineas(new[] { linea1Dup, linea2Dup }, 10_000m);
        agregadas2.Should().Be(0);
        conc.Lineas.Should().HaveCount(2); // No se duplicaron
    }

    [Fact]
    public void CA9_9_y_Criterio4_diferencia_de_50_pesos_rechaza_cierre()
    {
        // CA9.9: Una conciliación con $50 de diferencia se intenta cerrar -> No cierra; con el saldo cuadrado, sí
        var conc = Conciliacion.Crear(EmpresaId, CuentaBancariaId, 2026, 9, 10_050m);
        var linea = ExtractoLinea.Crear(conc.Id, 1, new DateOnly(2026, 9, 1), "Línea única", "REF", cargo: null, abono: 10_050m);
        linea.Emparejar(Guid.NewGuid(), MatchTipo.AutoReferenciaImporte, 10_050m, UsuarioId, Ahora);
        conc.CargarLineas(new[] { linea }, 10_050m);

        // Saldo en libros calculado es $10,000, pero saldo banco es $10,050 (Diferencia = $50.00)
        decimal saldoLibros = 10_000m;

        var act = () => conc.Cerrar(UsuarioId, "Lic. Roberto Garza", "Gerente de Tesorería", saldoLibros, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CONCILIACION_DESCUADRADA" && e.Message.Contains("50.00"));

        conc.Estado.Should().Be(EstadoConciliacion.Abierta);
    }

    [Fact]
    public void CA9_9_y_Criterio4_con_diferencia_cero_cierra_y_guarda_firmante()
    {
        var conc = Conciliacion.Crear(EmpresaId, CuentaBancariaId, 2026, 9, 10_000m);
        var linea = ExtractoLinea.Crear(conc.Id, 1, new DateOnly(2026, 9, 1), "Línea única", "REF", cargo: null, abono: 10_000m);
        linea.Emparejar(Guid.NewGuid(), MatchTipo.AutoReferenciaImporte, 10_000m, UsuarioId, Ahora);
        conc.CargarLineas(new[] { linea }, 10_000m);

        // Saldo banco $10,000 == Saldo libros $10,000 (Diferencia = $0.00)
        conc.Cerrar(UsuarioId, "Lic. Roberto Garza", "Gerente de Tesorería", 10_000m, Ahora);

        conc.Estado.Should().Be(EstadoConciliacion.Cerrada);
        conc.FirmanteNombre.Should().Be("Lic. Roberto Garza");
        conc.FirmantePuesto.Should().Be("Gerente de Tesorería");
        conc.CerradaEn.Should().Be(Ahora);
        conc.Diferencia.Should().Be(0m);
    }

    [Fact]
    public void Cierre_con_firmante_rol_no_autorizado_lanza_excepcion()
    {
        var conc = Conciliacion.Crear(EmpresaId, CuentaBancariaId, 2026, 9, 5_000m);
        var linea = ExtractoLinea.Crear(conc.Id, 1, new DateOnly(2026, 9, 1), "Línea", "R", cargo: null, abono: 5_000m);
        linea.Emparejar(Guid.NewGuid(), MatchTipo.Manual, 5_000m, UsuarioId, Ahora);
        conc.CargarLineas(new[] { linea }, 5_000m);

        var act = () => conc.Cerrar(UsuarioId, "Juan Pérez", "Auxiliar Contable", 5_000m, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CONCILIACION_FIRMANTE_ROL_INVALIDO");
    }

    [Fact]
    public void Cierre_con_lineas_pendientes_rechaza_cierre()
    {
        var conc = Conciliacion.Crear(EmpresaId, CuentaBancariaId, 2026, 9, 1_000m);
        var linea = ExtractoLinea.Crear(conc.Id, 1, new DateOnly(2026, 9, 1), "Línea pendiente", "R", cargo: null, abono: 1_000m);
        conc.CargarLineas(new[] { linea }, 1_000m);

        var act = () => conc.Cerrar(UsuarioId, "Ing. Carlos Slim", "Jefe de Tesorería", 1_000m, Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CONCILIACION_LINEAS_PENDIENTES");
    }

    [Fact]
    public void Reabrir_conciliacion_exige_motivo_obligatorio_y_guarda_bitacora()
    {
        var conc = Conciliacion.Crear(EmpresaId, CuentaBancariaId, 2026, 9, 2_000m);
        var linea = ExtractoLinea.Crear(conc.Id, 1, new DateOnly(2026, 9, 1), "Línea", "R", cargo: null, abono: 2_000m);
        linea.Emparejar(Guid.NewGuid(), MatchTipo.Manual, 2_000m, UsuarioId, Ahora);
        conc.CargarLineas(new[] { linea }, 2_000m);
        conc.Cerrar(UsuarioId, "Lic. Sofía Morales", "Jefe de Tesorería", 2_000m, Ahora);

        // Sin motivo lanza
        var actSinMotivo = () => conc.Reabrir(UsuarioId, "   ", Ahora.AddHours(1));
        actSinMotivo.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CONCILIACION_MOTIVO_REAPERTURA_OBLIGATORIO");

        // Con motivo reabre
        conc.Reabrir(UsuarioId, "Ajuste de partida detectado por auditoría", Ahora.AddHours(1));
        conc.Estado.Should().Be(EstadoConciliacion.Abierta);
        conc.MotivoReapertura.Should().Be("Ajuste de partida detectado por auditoría");
        conc.Bitacora.Should().ContainSingle(b => b.Accion == "Reapertura de conciliación");
    }
}
