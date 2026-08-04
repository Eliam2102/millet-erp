using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.Cce;

/// <summary>
/// Complemento de Comercio Exterior (CCE) de una factura de venta de exportación
/// (§4.5 levantamiento). Va en el XML de la factura cuando el comportamiento
/// fiscal es <c>ExportacionConCce</c>: tipo de operación, INCOTERM, TC DOF del día
/// hábil anterior, receptor extranjero (TAX-ID + residencia fiscal) y las
/// mercancías (fracción arancelaria, valor USD, IVA 0%). Entidad hija 1:1 de
/// <c>FacturaVenta</c>.
/// </summary>
public sealed class ComplementoCce
{
    public Guid Id { get; private set; }
    public Guid FacturaVentaId { get; private set; }

    /// <summary>Tipo de operación CCE (<c>2</c> = exportación definitiva).</summary>
    public string TipoOperacion { get; private set; } = "2";

    /// <summary>INCOTERM (<c>c_INCOTERM</c>: EXW, FOB, CIF, …).</summary>
    public string Incoterm { get; private set; } = string.Empty;

    /// <summary>Tipo de cambio DOF del día hábil anterior (USD→MXN).</summary>
    public decimal TcDof { get; private set; }

    /// <summary>Clave de pedimento aduanal (<c>c_ClavePedimento</c>, p.ej. A1). F12-PR3.</summary>
    public string? ClaveDePedimento { get; private set; }

    /// <summary>True cuando el CFDI funge como certificado de origen. F12-PR3.</summary>
    public bool CertificadoOrigen { get; private set; }

    // ---- Receptor extranjero (§4.5) ----
    /// <summary>Identificador fiscal del receptor extranjero (TAX-ID / NumRegIdTrib).</summary>
    public string ReceptorNumRegIdTrib { get; private set; } = string.Empty;

    /// <summary>País de residencia fiscal del receptor (ISO 3166-1 alfa-3, p.ej. USA).</summary>
    public string ReceptorPaisResidencia { get; private set; } = string.Empty;

    // ---- Domicilio del receptor extranjero (nodo Domicilio del CCE 2.0; F12-PR3).
    // Nullable en el agregado: la obligatoriedad de Estado/CP se valida al
    // timbrar real (CfdiEmisionBuilder), sin quemar folio.
    public string? ReceptorDomicilioCalle { get; private set; }
    public string? ReceptorDomicilioEstado { get; private set; }
    public string? ReceptorDomicilioCodigoPostal { get; private set; }

    private readonly List<ComplementoCceLinea> _lineas = [];
    public IReadOnlyCollection<ComplementoCceLinea> Lineas => _lineas.AsReadOnly();

    private ComplementoCce() { }

    private ComplementoCce(
        Guid id, Guid facturaVentaId, string tipoOperacion, string incoterm, decimal tcDof,
        string receptorNumRegIdTrib, string receptorPaisResidencia,
        string? claveDePedimento, bool certificadoOrigen,
        string? receptorDomicilioCalle, string? receptorDomicilioEstado, string? receptorDomicilioCodigoPostal)
    {
        Id = id;
        FacturaVentaId = facturaVentaId;
        TipoOperacion = tipoOperacion;
        Incoterm = incoterm;
        TcDof = tcDof;
        ReceptorNumRegIdTrib = receptorNumRegIdTrib;
        ReceptorPaisResidencia = receptorPaisResidencia;
        ClaveDePedimento = claveDePedimento;
        CertificadoOrigen = certificadoOrigen;
        ReceptorDomicilioCalle = receptorDomicilioCalle;
        ReceptorDomicilioEstado = receptorDomicilioEstado;
        ReceptorDomicilioCodigoPostal = receptorDomicilioCodigoPostal;
    }

    /// <summary>Crea el CCE de una factura de exportación. El handler agrega las mercancías.</summary>
    public static ComplementoCce Crear(
        Guid facturaVentaId, string tipoOperacion, string incoterm, decimal tcDof,
        string receptorNumRegIdTrib, string receptorPaisResidencia,
        string? claveDePedimento = null, bool certificadoOrigen = false,
        string? receptorDomicilioCalle = null, string? receptorDomicilioEstado = null,
        string? receptorDomicilioCodigoPostal = null)
    {
        if (string.IsNullOrWhiteSpace(incoterm))
            throw new BusinessRuleException("CCE_INCOTERM_INVALIDO", "El INCOTERM es obligatorio en una factura de exportación.");
        if (tcDof <= 0)
            throw new BusinessRuleException("CCE_TC_DOF_INVALIDO", "El tipo de cambio DOF debe ser mayor que cero.");
        if (string.IsNullOrWhiteSpace(receptorNumRegIdTrib))
            throw new BusinessRuleException("CCE_RECEPTOR_TAXID_INVALIDO", "El TAX-ID del receptor extranjero es obligatorio.");
        if (string.IsNullOrWhiteSpace(receptorPaisResidencia))
            throw new BusinessRuleException("CCE_RECEPTOR_PAIS_INVALIDO", "El país de residencia fiscal del receptor es obligatorio.");

        return new ComplementoCce(
            Guid.CreateVersion7(), facturaVentaId,
            string.IsNullOrWhiteSpace(tipoOperacion) ? "2" : tipoOperacion,
            incoterm, tcDof, receptorNumRegIdTrib, receptorPaisResidencia.ToUpperInvariant(),
            claveDePedimento, certificadoOrigen,
            receptorDomicilioCalle, receptorDomicilioEstado, receptorDomicilioCodigoPostal);
    }

    /// <summary>
    /// Agrega una mercancía validando los datos aduaneros que el SAT exige por
    /// mercancía (CCE 2.0), para NO quemar folio contra el rechazo del PAC (#8).
    /// Espejo de la validación del frontend (superRefine de <c>emitir-factura.ts</c>):
    /// <list type="bullet">
    /// <item><c>ValorDolares &gt; 0</c> siempre — nodo requerido que alimenta <c>TotalUSD</c>.</item>
    /// <item>Mercancía tangible (unidad aduanera ≠ <c>99</c>, caso de Millet): fracción
    /// arancelaria de 8-10 dígitos + trío <c>UnidadAduana</c>/<c>CantidadAduana</c>/
    /// <c>ValorUnitarioAduana</c> completo (SAT CCE160/CCE165, "todos o ninguno").</item>
    /// <item>Servicio / sin unidad aduanera (unidad <c>99</c>): la fracción NO debe
    /// registrarse (SAT CCE159).</item>
    /// </list>
    /// La pertenencia a catálogos (<c>c_FraccionArancelaria</c>, <c>c_UnidadAduana</c>)
    /// la valida el PAC.
    /// </summary>
    public ComplementoCceLinea AgregarLinea(
        string fraccionArancelaria, string unidadAduana, decimal cantidadAduana,
        decimal valorUnitarioAduana, decimal valorDolares, bool aplicaIva0)
    {
        var esServicioSinUnidad = string.Equals(unidadAduana.Trim(), "99", StringComparison.Ordinal);
        if (esServicioSinUnidad)
        {
            // CCE159: con unidad aduanera 99 (sin unidad) la fracción no aplica.
            if (!string.IsNullOrWhiteSpace(fraccionArancelaria))
                throw new BusinessRuleException(
                    "CCE_FRACCION_NO_APLICA",
                    "La fracción arancelaria no debe registrarse cuando la unidad aduanera es 99 (sin unidad, servicios).");
        }
        else
        {
            // Bien tangible: fracción + trío aduanero completos.
            if (string.IsNullOrWhiteSpace(fraccionArancelaria))
                throw new BusinessRuleException("CCE_FRACCION_INVALIDA", "La fracción arancelaria es obligatoria.");
            if (!EsFraccionArancelariaValida(fraccionArancelaria))
                throw new BusinessRuleException(
                    "CCE_FRACCION_FORMATO",
                    "La fracción arancelaria debe tener de 8 a 10 dígitos numéricos (c_FraccionArancelaria).");
            if (string.IsNullOrWhiteSpace(unidadAduana))
                throw new BusinessRuleException("CCE_UNIDAD_ADUANA_INVALIDA", "La unidad aduanera es obligatoria (c_UnidadAduana).");
            if (cantidadAduana <= 0)
                throw new BusinessRuleException("CCE_CANTIDAD_INVALIDA", "La cantidad aduanera debe ser mayor que cero.");
            if (valorUnitarioAduana <= 0)
                throw new BusinessRuleException("CCE_VALOR_UNITARIO_ADUANA_INVALIDO", "El valor unitario aduanero debe ser mayor que cero.");
        }

        if (valorDolares <= 0)
            throw new BusinessRuleException("CCE_VALOR_DOLARES_INVALIDO", "El valor en dólares (USD) de la mercancía debe ser mayor que cero.");

        var linea = new ComplementoCceLinea(
            Guid.CreateVersion7(), Id, fraccionArancelaria, unidadAduana, cantidadAduana,
            valorUnitarioAduana, valorDolares, aplicaIva0);
        _lineas.Add(linea);
        return linea;
    }

    /// <summary>Fracción arancelaria válida = 8 a 10 dígitos numéricos (<c>c_FraccionArancelaria</c>).</summary>
    private static bool EsFraccionArancelariaValida(string fraccion) =>
        FraccionArancelariaRegex.IsMatch(fraccion.Trim());

    private static readonly Regex FraccionArancelariaRegex =
        new(@"^\d{8,10}$", RegexOptions.Compiled);
}
