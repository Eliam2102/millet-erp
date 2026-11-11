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

        // M-202: la firma de Tesorería deja la conciliación esperando el visto bueno de Contabilidad.
        conc.Estado.Should().Be(EstadoConciliacion.PendienteVistoBueno);
        conc.FirmanteNombre.Should().Be("Lic. Roberto Garza");
        conc.FirmantePuesto.Should().Be("Gerente de Tesorería");
        conc.CerradaEn.Should().Be(Ahora);
        conc.Diferencia.Should().Be(0m);
    }

    [Fact]
    public void Cierre_no_depende_del_texto_del_puesto_la_facultad_la_exige_el_endpoint()
    {
        var conc = Conciliacion.Crear(EmpresaId, CuentaBancariaId, 2026, 9, 5_000m);
        var linea = ExtractoLinea.Crear(conc.Id, 1, new DateOnly(2026, 9, 1), "Línea", "R", cargo: null, abono: 5_000m);
        linea.Emparejar(Guid.NewGuid(), MatchTipo.Manual, 5_000m, UsuarioId, Ahora);
        conc.CargarLineas(new[] { linea }, 5_000m);

        conc.Cerrar(UsuarioId, "Juan Pérez", "Auxiliar Contable", 5_000m, Ahora);

        conc.Estado.Should().Be(EstadoConciliacion.PendienteVistoBueno);

        var sinPuesto = Conciliacion.Crear(EmpresaId, CuentaBancariaId, 2026, 10, 0m);
        var act = () => sinPuesto.Cerrar(UsuarioId, "Juan Pérez", " ", 0m, Ahora);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CONCILIACION_FIRMANTE_OBLIGATORIO");
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
        conc.DarVistoBueno(Contadora, Ahora);

        // Sin motivo lanza
        var actSinMotivo = () => conc.Reabrir(UsuarioId, "   ", Ahora.AddHours(1));
        actSinMotivo.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CONCILIACION_MOTIVO_REAPERTURA_OBLIGATORIO");

        // Con motivo reabre
        conc.Reabrir(UsuarioId, "Ajuste de partida detectado por auditoría", Ahora.AddHours(1));
        conc.Estado.Should().Be(EstadoConciliacion.Abierta);
        conc.MotivoReapertura.Should().Be("Ajuste de partida detectado por auditoría");
        conc.Bitacora.Should().ContainSingle(b => b.Accion == "Reapertura de conciliación");
        conc.VistoBuenoPor.Should().BeNull();
    }

    private static readonly Guid Contadora = Guid.NewGuid();

    private static Conciliacion Firmada()
    {
        var conc = Conciliacion.Crear(EmpresaId, CuentaBancariaId, 2026, 10, 3_000m);
        var linea = ExtractoLinea.Crear(conc.Id, 1, new DateOnly(2026, 10, 5), "Depósito", "SPEI 778812", cargo: null, abono: 3_000m);
        linea.Emparejar(Guid.NewGuid(), MatchTipo.Manual, 3_000m, UsuarioId, Ahora);
        conc.CargarLineas(new[] { linea }, 3_000m);
        conc.Cerrar(UsuarioId, "Javier Canché", "Tesorería", 3_000m, Ahora);
        return conc;
    }

    [Fact]
    public void M202_visto_bueno_de_otra_persona_cierra_y_quien_firmo_no_puede()
    {
        var conc = Firmada();

        var mismo = () => conc.DarVistoBueno(UsuarioId, Ahora);
        mismo.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CONCILIACION_VISTO_BUENO_MISMO_USUARIO");
        conc.Estado.Should().Be(EstadoConciliacion.PendienteVistoBueno);

        conc.DarVistoBueno(Contadora, Ahora.AddHours(2));
        conc.Estado.Should().Be(EstadoConciliacion.Cerrada);
        (conc.VistoBuenoPor, conc.VistoBuenoEn).Should().Be((Contadora, Ahora.AddHours(2)));
    }

    [Fact]
    public void M202_devolver_exige_motivo_y_regresa_a_abierta()
    {
        var conc = Firmada();

        var sinMotivo = () => conc.DevolverATesoreria(Contadora, " ", Ahora);
        sinMotivo.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CONCILIACION_MOTIVO_DEVOLUCION_OBLIGATORIO");

        conc.DevolverATesoreria(Contadora, "Línea mal emparejada", Ahora);
        conc.Estado.Should().Be(EstadoConciliacion.Abierta);
        conc.CerradaPor.Should().BeNull();
        conc.Bitacora.Should().ContainSingle(b => b.Accion == "Devuelta a Tesorería" && b.Motivo == "Línea mal emparejada");
    }

    [Fact]
    public void M202_pendiente_de_visto_bueno_no_acepta_lineas_ni_se_reabre()
    {
        var conc = Firmada();
        var nueva = ExtractoLinea.Crear(conc.Id, 2, new DateOnly(2026, 10, 6), "Otra", null, cargo: 10m, abono: null);

        ((Action)(() => conc.CargarLineas(new[] { nueva }))).Should().Throw<BusinessRuleException>();
        ((Action)(() => conc.Reabrir(Contadora, "motivo", Ahora))).Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CONCILIACION_NO_CERRADA");
    }

    [Fact]
    public void M201_partida_en_transito_exige_causa_y_fecha_no_anterior()
    {
        var linea = ExtractoLinea.Crear(Guid.NewGuid(), 1, new DateOnly(2026, 10, 31), "CHEQUE 1234", "CH1234", cargo: 8_500m, abono: null);

        ((Action)(() => linea.MarcarPartidaTransito(PartidaTransitoTipo.ChequeNoCobrado, "Javier Canché", Contadora, " ", new DateOnly(2026, 11, 15))))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "TRANSITO_CAUSA_VACIA");
        ((Action)(() => linea.MarcarPartidaTransito(PartidaTransitoTipo.ChequeNoCobrado, "Javier Canché", Contadora, "Cheque no cobrado", new DateOnly(2026, 10, 30))))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "TRANSITO_FECHA_INVALIDA");
        linea.Estado.Should().Be(EstadoExtractoLinea.Pendiente);

        linea.MarcarPartidaTransito(PartidaTransitoTipo.ChequeNoCobrado, "Javier Canché", Contadora, "Cheque no cobrado", new DateOnly(2026, 11, 15));
        (linea.Estado, linea.PartidaTransitoCausa, linea.PartidaTransitoFechaPrevista)
            .Should().Be((EstadoExtractoLinea.EnTransito, "Cheque no cobrado", new DateOnly(2026, 11, 15)));
    }
}
