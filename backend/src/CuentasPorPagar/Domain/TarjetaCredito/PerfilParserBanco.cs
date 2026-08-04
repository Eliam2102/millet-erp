using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.TarjetaCredito;

/// <summary>
/// Configuración del parser para un banco emisor de TC (§4.1, §6
/// anexo TC, F7-PR5). El parser único <c>IEstadoCuentaTcParserPort</c>
/// lee este perfil y aplica las reglas — agregar banco = agregar seed,
/// no clase nueva (Strategy por configuración).
///
/// <para>
/// PK: <see cref="Codigo"/> (string). No usa <see cref="Guid"/> — el
/// código (`AMEX_MX`, `BANAMEX`, etc.) es estable, búscable y se usa
/// en queries logs.
/// </para>
/// </summary>
public sealed class PerfilParserBanco : IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }

    /// <summary>Código único — 'AMEX_MX', 'BANAMEX', 'BBVA_MX'.</summary>
    public string Codigo { get; private set; } = default!;
    public string Nombre { get; private set; } = default!;

    /// <summary>'XLSX', 'CSV', 'CSV_TAB'.</summary>
    public string FormatoArchivo { get; private set; } = "XLSX";
    public string Encoding { get; private set; } = "UTF-8";
    public int FilaInicioDatos { get; private set; } = 1;

    /// <summary>'A' (letra) o nombre de columna ('Fecha de cargo').</summary>
    public string ColumnaFecha { get; private set; } = default!;
    public string FormatoFecha { get; private set; } = "yyyy-MM-dd";
    public string ColumnaMonto { get; private set; } = default!;
    public string? ColumnaMoneda { get; private set; }
    public string ColumnaMerchant { get; private set; } = default!;
    public string? ColumnaReferencia { get; private set; }
    public string? ColumnaTipo { get; private set; }

    /// <summary>'NegativoEsRefund', 'PositivoEsRefund', 'PorColumnaTipo'.</summary>
    public string ReglaSignoRefund { get; private set; } = "NegativoEsRefund";

    /// <summary>'es-MX' (coma) o 'en-US' (punto).</summary>
    public string LocaleMontos { get; private set; } = "es-MX";

    public bool Activo { get; private set; } = true;

    private PerfilParserBanco() { }

    public static PerfilParserBanco Crear(
        Guid empresaId,
        string codigo,
        string nombre,
        string formatoArchivo,
        string encoding,
        int filaInicioDatos,
        string columnaFecha,
        string formatoFecha,
        string columnaMonto,
        string? columnaMoneda,
        string columnaMerchant,
        string? columnaReferencia,
        string? columnaTipo,
        string reglaSignoRefund,
        string localeMontos)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new BusinessRuleException("PERFIL_CODIGO_VACIO", "El código del perfil es obligatorio.");
        if (filaInicioDatos < 1)
            throw new BusinessRuleException("PERFIL_FILA_INICIO_INVALIDA",
                "La fila de inicio de datos debe ser >= 1.");
        if (formatoArchivo is not ("XLSX" or "CSV" or "CSV_TAB"))
            throw new BusinessRuleException("PERFIL_FORMATO_INVALIDO",
                $"Formato '{formatoArchivo}' no soportado. Use XLSX, CSV o CSV_TAB.");
        if (reglaSignoRefund is not ("NegativoEsRefund" or "PositivoEsRefund" or "PorColumnaTipo"))
            throw new BusinessRuleException("PERFIL_REGLA_REFUND_INVALIDA",
                $"Regla de signo refund '{reglaSignoRefund}' no soportada.");

        return new PerfilParserBanco
        {
            EmpresaId = empresaId,
            Codigo = codigo.Trim().ToUpperInvariant(),
            Nombre = nombre.Trim(),
            FormatoArchivo = formatoArchivo,
            Encoding = encoding,
            FilaInicioDatos = filaInicioDatos,
            ColumnaFecha = columnaFecha,
            FormatoFecha = formatoFecha,
            ColumnaMonto = columnaMonto,
            ColumnaMoneda = columnaMoneda,
            ColumnaMerchant = columnaMerchant,
            ColumnaReferencia = columnaReferencia,
            ColumnaTipo = columnaTipo,
            ReglaSignoRefund = reglaSignoRefund,
            LocaleMontos = localeMontos,
            Activo = true,
        };
    }

    public void Desactivar() => Activo = false;
    public void Reactivar() => Activo = true;
}
