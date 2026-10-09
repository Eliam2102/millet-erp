using System.Text.RegularExpressions;

namespace Millet.Facturacion.Domain.Comprobantes;

public sealed record CampoFiscalInvalido(string Campo, string Motivo);

public static partial class ValidacionReceptorFiscal
{
    [GeneratedRegex(@"^[A-ZÑ&]{3,4}[0-9]{6}[A-Z0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex RfcSat();
    [GeneratedRegex(@"^[0-9]{5}$", RegexOptions.CultureInvariant)]
    private static partial Regex CpSat();

    // c_UsoCFDI «Aplica para tipo persona» y c_RegimenFiscal (catCFDI_V_4_20261001):
    // usos y regímenes exclusivos de un tipo de persona. 610 y 626 aplican a ambos.
    private static readonly HashSet<string> UsosSoloFisica =
        ["D01", "D02", "D03", "D04", "D05", "D06", "D07", "D08", "D09", "D10", "CN01"];
    private static readonly HashSet<string> RegimenesSoloMoral = ["601", "603", "620", "622", "623", "624"];
    private static readonly HashSet<string> RegimenesSoloFisica =
        ["605", "606", "607", "608", "611", "612", "614", "615", "616", "621", "625"];

    public static string Normalizar(string? valor) =>
        string.Concat((valor ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    public static IReadOnlyList<CampoFiscalInvalido> Validar(
        DatosFiscalesReceptor receptor, string lugarExpedicion,
        bool regimenExiste, bool usoExiste, bool esRep = false,
        bool exportacionConCce = false, string? numRegIdTrib = null, string? paisResidencia = null)
    {
        var errores = new List<CampoFiscalInvalido>();
        var rfc = receptor.Rfc ?? string.Empty;
        var regimen = receptor.RegimenFiscal ?? string.Empty;
        var cp = receptor.CodigoPostal ?? string.Empty;
        var uso = esRep ? "CP01" : (receptor.UsoCfdi ?? string.Empty);
        if (!RfcSat().IsMatch(rfc)) errores.Add(new("rfc", "El RFC es obligatorio y debe tener formato SAT."));
        if (string.IsNullOrWhiteSpace(receptor.Nombre) || receptor.Nombre.Length > 254) errores.Add(new("razonSocial", "La razón social es obligatoria y no puede exceder 254 caracteres."));
        if (regimen.Length == 0 || !regimenExiste) errores.Add(new("regimenFiscal", "Falta el régimen fiscal o no existe en el catálogo SAT."));
        if (!CpSat().IsMatch(cp)) errores.Add(new("codigoPostalFiscal", "El CP fiscal debe tener cinco dígitos."));
        if (string.IsNullOrWhiteSpace(receptor.Pais) || receptor.Pais.Length > 5)
            errores.Add(new("paisResidencia", "El país del receptor es obligatorio y no puede exceder cinco caracteres."));
        if (!usoExiste || !CompatibilidadUsoRegimen.EsCompatible(uso, regimen))
            errores.Add(new("usoCfdi", $"El uso de CFDI '{uso}' no existe o no es compatible con el régimen '{regimen}'."));
        // CN01 es exclusivo del recibo de nómina; el ERP no emite nómina.
        if (uso == "CN01")
            errores.Add(new("usoCfdi", "El uso de CFDI CN01 (Nómina) es exclusivo del recibo de nómina; elige el uso que corresponda a la venta."));

        // RFC de 12 caracteres = persona moral; 13 = persona física (los genéricos se validan aparte).
        if (RfcSat().IsMatch(rfc) && !DatosFiscalesReceptor.EsRfcGenerico(rfc))
        {
            var moral = rfc.Length == 12;
            if (moral ? RegimenesSoloFisica.Contains(regimen) : RegimenesSoloMoral.Contains(regimen))
                errores.Add(new("regimenFiscal", $"El régimen fiscal '{regimen}' no aplica a una persona {(moral ? "moral" : "física")}."));
            if (moral && UsosSoloFisica.Contains(uso) && uso != "CN01")
                errores.Add(new("usoCfdi", $"El uso de CFDI '{uso}' solo aplica a personas físicas."));
        }

        if (DatosFiscalesReceptor.EsRfcGenerico(rfc))
        {
            if (regimen != "616") errores.Add(new("regimenFiscal", "El RFC genérico requiere régimen fiscal 616."));
            if (uso != (esRep ? "CP01" : "S01")) errores.Add(new("usoCfdi", "El RFC genérico requiere uso de CFDI S01 (CP01 para REP)."));
            if (cp != Normalizar(lugarExpedicion)) errores.Add(new("codigoPostalFiscal", "El CP fiscal del receptor genérico debe coincidir con el lugar de expedición."));
        }
        if (rfc == DatosFiscalesReceptor.RfcGenericoExtranjero)
        {
            var pais = Normalizar(receptor.Pais);
            if (pais.Length == 0 || pais == "MEX") errores.Add(new("paisResidencia", "El receptor extranjero requiere un país distinto de MEX."));
            if (exportacionConCce)
            {
                if (string.IsNullOrWhiteSpace(numRegIdTrib)) errores.Add(new("numRegIdTrib", "Falta la identificación fiscal extranjera (NumRegIdTrib) para comercio exterior."));
                var residencia = Normalizar(paisResidencia);
                if (residencia.Length == 0 || residencia == "MEX") errores.Add(new("paisResidencia", "Falta un país de residencia extranjero para comercio exterior."));
            }
        }
        return errores;
    }
}
