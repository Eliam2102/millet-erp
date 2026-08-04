using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.Comprobantes;

/// <summary>
/// Snapshot de los datos fiscales del emisor al momento de emisión (F12-PR1,
/// espejo de <see cref="DatosFiscalesReceptor"/>). Inmutable — el CFDI 4.0
/// exige <c>Emisor@Nombre</c> y <c>LugarExpedicion</c> (CP fiscal de la
/// empresa) además de RFC y régimen; el comprobante los congela aunque la
/// empresa cambie después.
///
/// <para>
/// Se persiste como columnas planas en la tabla base <c>comprobante</c>
/// (mismo criterio que el receptor — sin owned types). El handler lo resuelve
/// vía <c>IEmpresaFiscalReadPort</c>; si la empresa no tiene CP capturado la
/// emisión falla con <c>EMISOR_SIN_LUGAR_EXPEDICION</c> antes de reservar folio.
/// </para>
/// </summary>
public sealed record DatosFiscalesEmisor(
    string Rfc,
    string Nombre,
    string RegimenFiscal,
    string LugarExpedicion)
{
    /// <summary>
    /// Valida el snapshot completo para timbrar. Nombre y LugarExpedicion son
    /// obligatorios en CFDI 4.0 (CFDI40139 / LugarExpedicion requerido).
    /// </summary>
    public DatosFiscalesEmisor Validar()
    {
        if (string.IsNullOrWhiteSpace(Rfc))
            throw new BusinessRuleException("COMPROBANTE_EMISOR_RFC_INVALIDO", "El RFC del emisor es obligatorio.");
        if (string.IsNullOrWhiteSpace(Nombre))
            throw new BusinessRuleException("EMISOR_SIN_NOMBRE", "La razón social del emisor es obligatoria (Emisor@Nombre del CFDI 4.0).");
        if (string.IsNullOrWhiteSpace(RegimenFiscal))
            throw new BusinessRuleException("EMISOR_SIN_REGIMEN", "El régimen fiscal del emisor es obligatorio.");
        if (string.IsNullOrWhiteSpace(LugarExpedicion))
            throw new BusinessRuleException(
                "EMISOR_SIN_LUGAR_EXPEDICION",
                "La empresa emisora no tiene código postal fiscal capturado (LugarExpedicion del CFDI 4.0). Captúralo en Administración → Empresas.");
        return this;
    }
}
