using System.Text.RegularExpressions;

namespace Millet.SharedKernel.Domain.Bancos;

/// <summary>
/// Cuenta de un pago tal como la pide el complemento de pagos del SAT
/// (<c>CtaOrdenante</c>/<c>CtaBeneficiario</c>, catálogo <c>c_FormaPago</c>).
/// 86e3tfdve: Tesorería captura la ordenante al registrar el ingreso, cuando
/// todavía no se conoce la forma de pago; por eso aquí solo vive el patrón
/// general (todo patrón de ordenante del catálogo cabe en él). La regla por
/// forma de pago está en <c>Facturacion.Domain.Repp.CuentasPagoSat</c> y se
/// aplica al revisar y al emitir el REP. PII (ADR-0006): mostrar siempre con
/// <see cref="Enmascarar"/>.
/// </summary>
public static class CuentaPagoSat
{
    public const int LongitudMaxima = 50;

    /// <summary>Patrón genérico del catálogo c_FormaPago.</summary>
    public const string PatronGeneral = "[0-9]{10,11}|[0-9]{15,16}|[0-9]{18}|[A-Z0-9_]{10,50}";

    public const string DescripcionGeneral = "10, 11, 15, 16 o 18 dígitos, o de 10 a 50 caracteres alfanuméricos";

    private static readonly Regex General = new($"^(?:{PatronGeneral})$", RegexOptions.CultureInvariant);

    /// <summary>Quita espacios y guiones de captura y pasa a mayúsculas; vacío = sin cuenta.</summary>
    public static string? Normalizar(string? cuenta)
    {
        if (string.IsNullOrWhiteSpace(cuenta)) return null;
        var limpia = new string(cuenta.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        return limpia.Length == 0 ? null : limpia;
    }

    /// <summary>¿La cuenta (ya normalizada) cumple el patrón general?</summary>
    public static bool CumpleGeneral(string cuenta) => General.IsMatch(cuenta);

    /// <summary>Enmascarado para mostrar (ADR-0006): «•••• 1234».</summary>
    public static string? Enmascarar(string? cuenta)
    {
        var v = cuenta?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= 4 ? "••••" : $"•••• {v[^4..]}";
    }
}
