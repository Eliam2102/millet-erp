using System.Globalization;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Corridas;

/// <summary>
/// Perfil de layout bancario configurable (§4.5, T9.3, 86e3ke5ck).
/// Permite generar archivos de pagos masivos según las especificaciones
/// de cada banco (BBVA primero, después Santander y Mifel) sin modificar código.
/// </summary>
public sealed class PerfilLayoutBanco : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    /// <summary>Código único del perfil (ej. 'BBVA_SPEI', 'BBVA_INT').</summary>
    public string Codigo { get; private set; } = default!;

    /// <summary>Banco emisor/receptor (ej. 'BBVA', 'SANTANDER', 'MIFEL').</summary>
    public string Banco { get; private set; } = default!;

    public string Nombre { get; private set; } = default!;

    public TipoPagoLayout TipoPago { get; private set; } = TipoPagoLayout.NacionalSpei;

    /// <summary>Separador de campos: ',' (CSV), '\t' (TSV), '|', etc. Si es vacío, usa longitud fija.</summary>
    public string Separador { get; private set; } = ",";

    public bool IncluyeEncabezado { get; private set; }

    /// <summary>Formato numérico para el importe: '0.00' (con punto), 'CentavosSinPunto' (enteros de 2 decimales sin punto), etc.</summary>
    public string FormatoImporte { get; private set; } = "0.00";

    public int LongitudCuenta { get; private set; } = 18;
    public int LongitudTitular { get; private set; } = 40;
    public int LongitudReferencia { get; private set; } = 20;
    public int LongitudConcepto { get; private set; } = 40;

    public bool Activo { get; private set; } = true;

    private PerfilLayoutBanco() { }

    public PerfilLayoutBanco(
        Guid empresaId,
        string codigo,
        string banco,
        string nombre,
        TipoPagoLayout tipoPago,
        string separador = ",",
        bool incluyeEncabezado = false,
        string formatoImporte = "0.00",
        int longitudCuenta = 18,
        int longitudTitular = 40,
        int longitudReferencia = 20,
        int longitudConcepto = 40) : base(Guid.CreateVersion7())
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new BusinessRuleException("PERFIL_CODIGO_VACIO", "El código del perfil es obligatorio.");
        if (string.IsNullOrWhiteSpace(banco))
            throw new BusinessRuleException("PERFIL_BANCO_VACIO", "El banco es obligatorio.");

        EmpresaId = empresaId;
        Codigo = codigo.Trim().ToUpperInvariant();
        Banco = banco.Trim().ToUpperInvariant();
        Nombre = string.IsNullOrWhiteSpace(nombre) ? Codigo : nombre.Trim();
        TipoPago = tipoPago;
        Separador = separador ?? string.Empty;
        IncluyeEncabezado = incluyeEncabezado;
        FormatoImporte = string.IsNullOrWhiteSpace(formatoImporte) ? "0.00" : formatoImporte.Trim();
        LongitudCuenta = longitudCuenta > 0 ? longitudCuenta : 18;
        LongitudTitular = longitudTitular > 0 ? longitudTitular : 40;
        LongitudReferencia = longitudReferencia > 0 ? longitudReferencia : 20;
        LongitudConcepto = longitudConcepto > 0 ? longitudConcepto : 40;
        Activo = true;
    }

    /// <summary>Perfil predeterminado BBVA SPEI tomado de la guía pública de BBVA Net Cash.</summary>
    public static PerfilLayoutBanco BbvaNacionalSpei(Guid empresaId) =>
        new(empresaId, "BBVA_SPEI", "BBVA", "BBVA Multipagos SPEI", TipoPagoLayout.NacionalSpei, separador: ",", incluyeEncabezado: false);

    /// <summary>Perfil predeterminado BBVA Internacional.</summary>
    public static PerfilLayoutBanco BbvaInternacional(Guid empresaId) =>
        new(empresaId, "BBVA_INT", "BBVA", "BBVA Transferencia Internacional", TipoPagoLayout.Internacional, separador: ",", incluyeEncabezado: false);

    public string GenerarEncabezado()
    {
        if (!IncluyeEncabezado) return string.Empty;
        return string.IsNullOrEmpty(Separador)
            ? $"{"Cuenta".PadRight(LongitudCuenta)}{"Importe".PadLeft(15)}{"Titular".PadRight(LongitudTitular)}{"Referencia".PadRight(LongitudReferencia)}{"Concepto".PadRight(LongitudConcepto)}"
            : string.Join(Separador, "Cuenta", "ImporteNeto", "Titular", "Referencia", "Concepto");
    }

    public string GenerarLinea(string cuentaOClabe, decimal importeNeto, string titular, string referencia, string concepto)
    {
        var cta = Truncar(cuentaOClabe?.Trim() ?? string.Empty, LongitudCuenta);
        var imp = FormatearImporte(importeNeto);
        var tit = Truncar(Sanitizar(titular?.Trim() ?? string.Empty), LongitudTitular);
        var rf = Truncar(Sanitizar(referencia?.Trim() ?? string.Empty), LongitudReferencia);
        var cp = Truncar(Sanitizar(concepto?.Trim() ?? string.Empty), LongitudConcepto);

        if (string.IsNullOrEmpty(Separador))
        {
            // Longitud fija
            return $"{cta.PadRight(LongitudCuenta)}{imp.PadLeft(15)}{tit.PadRight(LongitudTitular)}{rf.PadRight(LongitudReferencia)}{cp.PadRight(LongitudConcepto)}";
        }

        return string.Join(Separador, cta, imp, tit, rf, cp);
    }

    private string FormatearImporte(decimal importe)
    {
        if (string.Equals(FormatoImporte, "CentavosSinPunto", StringComparison.OrdinalIgnoreCase))
        {
            var centavos = (long)Math.Round(importe * 100m, MidpointRounding.AwayFromZero);
            return centavos.ToString("D15", CultureInfo.InvariantCulture);
        }

        return importe.ToString(FormatoImporte, CultureInfo.InvariantCulture);
    }

    private string Sanitizar(string valor)
    {
        if (string.IsNullOrEmpty(valor)) return string.Empty;
        if (Separador == ",") valor = valor.Replace(",", " ");
        if (Separador == "\t") valor = valor.Replace("\t", " ");
        if (Separador == "|") valor = valor.Replace("|", " ");
        return valor.Trim();
    }

    private static string Truncar(string valor, int maxLongitud) =>
        valor.Length <= maxLongitud ? valor : valor[..maxLongitud];

    public void Desactivar() => Activo = false;
    public void Reactivar() => Activo = true;
}
