using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.Viaticos;

public sealed class SolicitudViaticosAggregateTests
{
    private static SolicitudViaticos Solicitar(
        decimal monto = 5000m,
        int diasViaje = 3,
        decimal topePolitica = 6000m,
        int diasMaxPolitica = 10,
        string? justificacion = null)
    {
        var salida = new DateOnly(2026, 6, 1);
        var regreso = salida.AddDays(diasViaje - 1);
        return SolicitudViaticos.Solicitar(
            empresaId: Guid.NewGuid(),
            empleadoId: Guid.NewGuid(),
            puestoId: Guid.NewGuid(),
            jefeDirectoId: Guid.NewGuid(),
            destino: "Cancún",
            tipoDestino: TipoDestinoViatico.Nacional,
            fechaSalida: salida,
            fechaRegreso: regreso,
            moneda: "MXN",
            montoSolicitado: monto,
            topePolitica: topePolitica,
            diasMaxPolitica: diasMaxPolitica,
            justificacionExceso: justificacion,
            ahora: DateTimeOffset.UtcNow);
    }

    private static void AgregarLinea(SolicitudViaticos s, decimal total)
    {
        s.CapturarComprobacion(
            new[]
            {
                new LineaComprobacionViaticosInput(
                    CfdiRecibidoId: null,
                    UuidCfdi: $"{Guid.NewGuid():N}".PadLeft(36, '0')[..36].ToUpperInvariant(),
                    ProveedorId: Guid.NewGuid(),
                    FolioProveedor: "F-001",
                    FechaGasto: DateTimeOffset.UtcNow,
                    Subtotal: total / 1.16m,
                    ImpuestosTrasladados: total - (total / 1.16m),
                    Retenciones: 0m,
                    Total: total,
                    Moneda: "MXN",
                    Concepto: "Hospedaje",
                    EsTicketNoFiscal: false),
            },
            ahora: DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Solicitar_dentro_de_politica_no_marca_excede()
    {
        var s = Solicitar(monto: 5000m, topePolitica: 6000m);
        s.Estado.Should().Be(EstadoSolicitudViaticos.Solicitada);
        s.ExcedePolitica.Should().BeFalse();
        s.TopePoliticaSnapshot.Should().Be(6000m);
    }

    [Fact]
    public void Solicitar_excede_monto_marca_excede_requiere_justificacion()
    {
        var actSinJustificacion = () => Solicitar(monto: 7000m, topePolitica: 6000m);
        actSinJustificacion.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "VIA_JUSTIFICACION_REQUERIDA");

        var s = Solicitar(monto: 7000m, topePolitica: 6000m, justificacion: "Cliente VIP");
        s.ExcedePolitica.Should().BeTrue();
    }

    [Fact]
    public void Solicitar_excede_dias_marca_excede()
    {
        var s = Solicitar(diasViaje: 15, diasMaxPolitica: 10,
            topePolitica: 100000m, // monto OK
            justificacion: "Proyecto largo");
        s.ExcedePolitica.Should().BeTrue();
    }

    [Fact]
    public void Solicitar_jefe_igual_empleado_rechaza()
    {
        var mismoId = Guid.NewGuid();
        var act = () => SolicitudViaticos.Solicitar(
            empresaId: Guid.NewGuid(),
            empleadoId: mismoId,
            puestoId: Guid.NewGuid(),
            jefeDirectoId: mismoId,
            destino: "X", tipoDestino: TipoDestinoViatico.Nacional,
            fechaSalida: new DateOnly(2026, 6, 1),
            fechaRegreso: new DateOnly(2026, 6, 3),
            moneda: "MXN", montoSolicitado: 1000m,
            topePolitica: 10000m, diasMaxPolitica: 10,
            justificacionExceso: null, ahora: DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VIA_EMPLEADO_JEFE_MISMO");
    }

    [Fact]
    public void AutorizarPorJefe_dentro_politica_pasa_a_AutorizadaPorJefe()
    {
        var s = Solicitar();
        s.AutorizarPorJefe(s.JefeDirectoId, DateTimeOffset.UtcNow);
        s.Estado.Should().Be(EstadoSolicitudViaticos.AutorizadaPorJefe);
        s.AutorizadoPorJefe.Should().Be(s.JefeDirectoId);
    }

    [Fact]
    public void AutorizarPorJefe_excede_politica_pasa_a_RequiereDireccionFinanzas()
    {
        var s = Solicitar(monto: 7000m, topePolitica: 6000m, justificacion: "X");
        s.AutorizarPorJefe(s.JefeDirectoId, DateTimeOffset.UtcNow);
        s.Estado.Should().Be(EstadoSolicitudViaticos.RequiereDireccionFinanzas);
    }

    [Fact]
    public void AutorizarPorJefe_otro_usuario_no_permitido()
    {
        var s = Solicitar();
        var act = () => s.AutorizarPorJefe(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VIA_JEFE_NO_AUTORIZADO");
    }

    [Fact]
    public void AutorizarPorDf_solo_desde_RequiereDireccionFinanzas()
    {
        var s = Solicitar();
        var act = () => s.AutorizarPorDireccionFinanzas(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VIA_NO_AUTORIZABLE_DF");
    }

    [Fact]
    public void AutorizarPorDf_mismo_que_jefe_rechaza()
    {
        var s = Solicitar(monto: 7000m, topePolitica: 6000m, justificacion: "X");
        s.AutorizarPorJefe(s.JefeDirectoId, DateTimeOffset.UtcNow);
        var act = () => s.AutorizarPorDireccionFinanzas(s.JefeDirectoId, DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VIA_DF_MISMO_QUE_JEFE");
    }

    [Fact]
    public void Ciclo_dentro_politica_completo()
    {
        var s = Solicitar();
        s.AutorizarPorJefe(s.JefeDirectoId, DateTimeOffset.UtcNow);
        s.MarcarAnticipoPagado(DateTimeOffset.UtcNow);
        AgregarLinea(s, total: 4800m);
        s.LiberarComprobacion(DateTimeOffset.UtcNow);

        s.Estado.Should().Be(EstadoSolicitudViaticos.Liquidada);
        s.MontoComprobado.Should().Be(4800m);
        s.DiferenciaLiquidacion.Should().Be(-200m); // gastó menos
    }

    [Fact]
    public void Ciclo_excede_politica_completo()
    {
        var s = Solicitar(monto: 7000m, topePolitica: 6000m, justificacion: "X");
        s.AutorizarPorJefe(s.JefeDirectoId, DateTimeOffset.UtcNow);
        s.AutorizarPorDireccionFinanzas(Guid.NewGuid(), DateTimeOffset.UtcNow);
        s.MarcarAnticipoPagado(DateTimeOffset.UtcNow);
        AgregarLinea(s, total: 7500m);
        s.LiberarComprobacion(DateTimeOffset.UtcNow);

        s.Estado.Should().Be(EstadoSolicitudViaticos.Liquidada);
        s.DiferenciaLiquidacion.Should().Be(500m); // Tesorería reembolsa
    }

    [Fact]
    public void MarcarAnticipoPagado_sin_autorizar_rechaza()
    {
        var s = Solicitar();
        var act = () => s.MarcarAnticipoPagado(DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VIA_NO_ANTICIPABLE");
    }

    [Fact]
    public void CapturarComprobacion_sin_anticipo_rechaza()
    {
        var s = Solicitar();
        var act = () => AgregarLinea(s, total: 1000m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VIA_NO_COMPROBABLE");
    }

    [Fact]
    public void Rechazar_motivo_obligatorio()
    {
        var s = Solicitar();
        var act = () => s.Rechazar(Guid.NewGuid(), "  ", DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VIA_MOTIVO_VACIO");
    }

    [Fact]
    public void Rechazar_desde_Anticipada_no_permitido()
    {
        var s = Solicitar();
        s.AutorizarPorJefe(s.JefeDirectoId, DateTimeOffset.UtcNow);
        s.MarcarAnticipoPagado(DateTimeOffset.UtcNow);
        var act = () => s.Rechazar(Guid.NewGuid(), "x", DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "VIA_NO_RECHAZABLE");
    }

    [Fact]
    public void DiasEstimados_calcula_inclusive()
    {
        var s = Solicitar(diasViaje: 5);
        s.DiasEstimados.Should().Be(5);
    }
}
