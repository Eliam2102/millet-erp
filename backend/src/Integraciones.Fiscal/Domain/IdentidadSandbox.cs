using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.Domain;

/// <summary>
/// Identidad fiscal de pruebas (persona de la LCO sintética del SAT) que
/// sustituye al emisor o receptor real cuando la <see cref="ConfiguracionPac"/>
/// apunta al sandbox de FiscalAPI. El SAT obliga a usar las personas de
/// prueba publicadas (docs.fiscalapi.com/testing-data): RFC, nombre y CP se
/// validan como conjunto contra la lista sintética (CFDI40145/40148), por lo
/// que los tres campos + régimen viajan juntos.
///
/// <para>
/// Value object inmutable, owned de <see cref="ConfiguracionPac"/> — solo
/// existe cuando la BaseUrl es la de sandbox (invariante del agregado).
/// Nunca toca los datos reales de la empresa ni del cliente: la sustitución
/// ocurre únicamente en el payload hacia el PAC
/// (<c>FiscalApiTimbradoAdapter</c>).
/// </para>
/// </summary>
public sealed partial class IdentidadSandbox
{
    public string Rfc { get; private set; } = string.Empty;

    /// <summary>Nombre oficial de la persona de prueba, SIN régimen societario (CFDI40139).</summary>
    public string RazonSocial { get; private set; } = string.Empty;

    /// <summary>c_RegimenFiscal de la persona de prueba (p.ej. 601).</summary>
    public string RegimenFiscal { get; private set; } = string.Empty;

    /// <summary>CP fiscal de la persona de prueba — debe coincidir exacto con la LCO sintética (CFDI40148).</summary>
    public string CodigoPostal { get; private set; } = string.Empty;

    private IdentidadSandbox() { } // EF Core

    public IdentidadSandbox(string rfc, string razonSocial, string regimenFiscal, string codigoPostal)
    {
        rfc = rfc?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!RfcRegex().IsMatch(rfc))
            throw new BusinessRuleException("IDENTIDAD_SANDBOX_RFC_INVALIDO",
                "El RFC de la identidad sandbox debe tener formato válido (12-13 caracteres).");

        if (string.IsNullOrWhiteSpace(razonSocial) || razonSocial.Trim().Length > 254)
            throw new BusinessRuleException("IDENTIDAD_SANDBOX_RAZON_SOCIAL_INVALIDA",
                "La razón social de la identidad sandbox es requerida (máx. 254).");

        if (string.IsNullOrWhiteSpace(regimenFiscal) || regimenFiscal.Trim().Length > 10)
            throw new BusinessRuleException("IDENTIDAD_SANDBOX_REGIMEN_INVALIDO",
                "El régimen fiscal de la identidad sandbox es requerido (máx. 10).");

        if (codigoPostal is null || !CpRegex().IsMatch(codigoPostal.Trim()))
            throw new BusinessRuleException("IDENTIDAD_SANDBOX_CP_INVALIDO",
                "El código postal de la identidad sandbox debe ser de 5 dígitos.");

        Rfc = rfc;
        RazonSocial = razonSocial.Trim();
        RegimenFiscal = regimenFiscal.Trim();
        CodigoPostal = codigoPostal.Trim();
    }

    [GeneratedRegex(@"^[A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3}$")]
    private static partial Regex RfcRegex();

    [GeneratedRegex(@"^\d{5}$")]
    private static partial Regex CpRegex();
}
