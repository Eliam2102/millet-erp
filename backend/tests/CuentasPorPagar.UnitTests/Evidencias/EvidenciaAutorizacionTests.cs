using Millet.CuentasPorPagar.Domain.Evidencias;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.Evidencias;

public sealed class EvidenciaAutorizacionTests
{
    private static EvidenciaAutorizacion Adjuntar(
        TipoEvidencia tipo = TipoEvidencia.CapturaWhatsapp,
        EstadoFirmaFisica estadoFirma = EstadoFirmaFisica.NoAplica,
        DateOnly? fechaLimite = null,
        string? comentario = "Autorizó Director Finanzas por WhatsApp 2026-05-22") =>
        EvidenciaAutorizacion.Adjuntar(
            empresaId: Guid.NewGuid(),
            tipoDocumento: TipoDocumentoEvidencia.FacturaProveedor,
            documentoId: Guid.NewGuid(),
            tipo: tipo,
            archivoBlobRef: "cxp/2026/05/evidencias/abc.png",
            nombreArchivo: "captura.png",
            contentType: "image/png",
            tamanioBytes: 1024,
            comentario: comentario!,
            estadoFirmaFisica: estadoFirma,
            fechaLimiteFirmaFisica: fechaLimite,
            capturadoPor: Guid.NewGuid(),
            ahora: DateTimeOffset.UtcNow);

    [Fact]
    public void Adjuntar_crea_evidencia_con_metadata()
    {
        var e = Adjuntar();
        e.Tipo.Should().Be(TipoEvidencia.CapturaWhatsapp);
        e.EstadoFirmaFisica.Should().Be(EstadoFirmaFisica.NoAplica);
        e.ArchivoBlobRef.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Adjuntar_con_FirmaPendiente_sin_fecha_limite_lanza()
    {
        var act = () => Adjuntar(estadoFirma: EstadoFirmaFisica.Pendiente);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EVIDENCIA_FECHA_LIMITE_REQUERIDA");
    }

    [Fact]
    public void Adjuntar_rechaza_comentario_vacio()
    {
        var act = () => Adjuntar(comentario: "");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EVIDENCIA_COMENTARIO_VACIO");
    }

    [Fact]
    public void MarcarFirmaFisicaRecibida_desde_Pendiente_la_pone_Recibida()
    {
        var e = Adjuntar(
            estadoFirma: EstadoFirmaFisica.Pendiente,
            fechaLimite: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));

        e.MarcarFirmaFisicaRecibida(DateTimeOffset.UtcNow);

        e.EstadoFirmaFisica.Should().Be(EstadoFirmaFisica.Recibida);
        e.FechaRecepcionFirmaFisica.Should().NotBeNull();
    }

    [Fact]
    public void MarcarFirmaFisicaRecibida_desde_NoAplica_lanza()
    {
        var e = Adjuntar(); // NoAplica
        var act = () => e.MarcarFirmaFisicaRecibida(DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "EVIDENCIA_FIRMA_NO_PENDIENTE");
    }
}
