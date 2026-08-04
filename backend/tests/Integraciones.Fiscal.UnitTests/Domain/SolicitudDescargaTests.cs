using Millet.Integraciones.Fiscal.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.UnitTests.Domain;

/// <summary>
/// Tests del FSM de <see cref="SolicitudDescarga"/>. Cada test cubre una
/// transición del diagrama documentado en doc 02 §13.3 implicación 3.
/// </summary>
public sealed class SolicitudDescargaTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DownloadRuleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Ahora = new(2026, 5, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_inicia_en_esperando_sat_con_next_poll_inmediato()
    {
        var s = NewSolicitud();

        s.Estado.Should().Be(EstadoSolicitudDescarga.EsperandoSat);
        s.NextPollAt.Should().Be(Ahora);
        s.AttemptsPoll.Should().Be(0);
        s.LastPollAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_rechaza_ventana_invertida()
    {
        var act = () => new SolicitudDescarga(
            Guid.NewGuid(), EmpresaId, DownloadRuleId, "ext",
            startDate: Ahora,
            endDate: Ahora.AddDays(-1),
            ahora: Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SOLICITUD_DESCARGA_VENTANA_INVALIDA");
    }

    [Fact]
    public void AplicarPoll_satStatus_aceptada_mantiene_esperando_sat()
    {
        var s = NewSolicitud();

        s.AplicarPoll(satStatusExterno: 1, downloadRequestStatusExterno: 1,
                      invoiceCount: 0, ahora: Ahora.AddMinutes(15));

        s.Estado.Should().Be(EstadoSolicitudDescarga.EsperandoSat);
        s.AttemptsPoll.Should().Be(1);
        s.SatRequestStatusExterno.Should().Be(1);
    }

    [Fact]
    public void AplicarPoll_sat_terminada_pero_api_procesando_va_a_esperando_api()
    {
        var s = NewSolicitud();

        s.AplicarPoll(satStatusExterno: 3, downloadRequestStatusExterno: 2,
                      invoiceCount: 5, ahora: Ahora.AddMinutes(30));

        s.Estado.Should().Be(EstadoSolicitudDescarga.EsperandoApi);
        s.InvoiceCount.Should().Be(5);
    }

    [Fact]
    public void AplicarPoll_completada_3_va_a_terminada()
    {
        var s = NewSolicitud();

        s.AplicarPoll(satStatusExterno: 3, downloadRequestStatusExterno: 3,
                      invoiceCount: 12, ahora: Ahora.AddMinutes(45));

        s.Estado.Should().Be(EstadoSolicitudDescarga.Terminada);
        s.InvoiceCount.Should().Be(12);
    }

    [Theory]
    [InlineData(4)]  // Error
    [InlineData(5)]  // Rechazada
    [InlineData(6)]  // Vencida
    [InlineData(-1)] // Abandonada
    public void AplicarPoll_sat_error_terminal_marca_error(int satStatus)
    {
        var s = NewSolicitud();

        s.AplicarPoll(satStatusExterno: satStatus, downloadRequestStatusExterno: 1,
                      invoiceCount: null, ahora: Ahora);

        s.Estado.Should().Be(EstadoSolicitudDescarga.Error);
        s.ErrorCodigo.Should().Contain($"SAT={satStatus}");
    }

    [Fact]
    public void AplicarPoll_download_request_abandonada_marca_error()
    {
        var s = NewSolicitud();

        s.AplicarPoll(satStatusExterno: 0, downloadRequestStatusExterno: -1,
                      invoiceCount: null, ahora: Ahora);

        s.Estado.Should().Be(EstadoSolicitudDescarga.Error);
    }

    [Fact]
    public void AplicarPoll_estado_terminal_es_no_op()
    {
        var s = NewSolicitud();
        s.AplicarPoll(3, 3, 5, Ahora);                   // → Terminada
        s.MarcarCosechada(Ahora);                         // → Cosechada
        s.Cerrar(Ahora);                                  // → Cerrada

        s.AplicarPoll(1, 1, 99, Ahora.AddDays(1));        // ignorado

        s.Estado.Should().Be(EstadoSolicitudDescarga.Cerrada);
        s.InvoiceCount.Should().Be(5);                    // sin cambio
    }

    [Fact]
    public void AplicarPoll_backoff_exponencial_va_creciendo()
    {
        var s = NewSolicitud();
        var t0 = Ahora;

        s.AplicarPoll(1, 1, 0, t0.AddMinutes(15));
        var primero = s.NextPollAt - t0.AddMinutes(15);

        s.AplicarPoll(1, 1, 0, t0.AddMinutes(30));
        var segundo = s.NextPollAt - t0.AddMinutes(30);

        s.AplicarPoll(1, 1, 0, t0.AddMinutes(45));
        var tercero = s.NextPollAt - t0.AddMinutes(45);

        primero.Should().Be(TimeSpan.FromMinutes(15));    // 15 * 2^0
        segundo.Should().Be(TimeSpan.FromMinutes(30));    // 15 * 2^1
        tercero.Should().Be(TimeSpan.FromMinutes(60));    // 15 * 2^2
    }

    [Fact]
    public void AplicarPoll_backoff_se_capa_en_max_minutos()
    {
        var s = NewSolicitud();
        // 8 polls — sin cap llegaría a 15 * 2^7 = 1920 min. Con cap 360 (6h).
        for (var i = 0; i < 8; i++)
        {
            s.AplicarPoll(1, 1, 0, Ahora.AddMinutes(i * 15), maxBackoffMinutes: 360);
        }
        var lastBackoff = s.NextPollAt - Ahora.AddMinutes(7 * 15);
        lastBackoff.TotalMinutes.Should().BeLessThanOrEqualTo(360);
    }

    [Fact]
    public void MarcarCosechada_solo_valido_desde_terminada()
    {
        var s = NewSolicitud();

        var act = () => s.MarcarCosechada(Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SOLICITUD_DESCARGA_NO_TERMINADA");
    }

    [Fact]
    public void MarcarCosechada_desde_terminada_avanza_y_setea_timestamp()
    {
        var s = NewSolicitud();
        s.AplicarPoll(3, 3, 5, Ahora);
        var t = Ahora.AddMinutes(60);

        s.MarcarCosechada(t);

        s.Estado.Should().Be(EstadoSolicitudDescarga.Cosechada);
        s.CosechadaAt.Should().Be(t);
    }

    [Fact]
    public void Cerrar_desde_cosechada_avanza_y_setea_timestamp()
    {
        var s = NewSolicitud();
        s.AplicarPoll(3, 3, 5, Ahora);
        s.MarcarCosechada(Ahora);
        var t = Ahora.AddHours(2);

        s.Cerrar(t);

        s.Estado.Should().Be(EstadoSolicitudDescarga.Cerrada);
        s.CerradaAt.Should().Be(t);
    }

    [Fact]
    public void Cerrar_sin_cosechar_falla()
    {
        var s = NewSolicitud();

        var act = () => s.Cerrar(Ahora);

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SOLICITUD_DESCARGA_NO_COSECHADA");
    }

    [Fact]
    public void MarcarError_aplica_codigo_y_mensaje()
    {
        var s = NewSolicitud();

        s.MarcarError("PAC_500", "FiscalAPI devolvió 500", Ahora);

        s.Estado.Should().Be(EstadoSolicitudDescarga.Error);
        s.ErrorCodigo.Should().Be("PAC_500");
        s.ErrorMensaje.Should().Be("FiscalAPI devolvió 500");
    }

    private static SolicitudDescarga NewSolicitud() => new(
        id: Guid.NewGuid(),
        empresaId: EmpresaId,
        downloadRuleId: DownloadRuleId,
        requestIdExterno: "8fc80885-89cc-468e-9a4e-c1fe7bfbf9a2",
        startDate: Ahora.AddDays(-7),
        endDate: Ahora,
        ahora: Ahora);
}
