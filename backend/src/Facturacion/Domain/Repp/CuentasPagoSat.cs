using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Domain.Repp;

/// <summary>
/// Reglas del SAT para <c>CtaOrdenante</c> y <c>CtaBeneficiario</c> del nodo
/// <c>pago20:Pago</c> (86e3tfdve). Única fuente de la tabla en el backend; el
/// frontend la replica en <c>features/facturacion/lib/cuentas-pago-sat.ts</c>.
///
/// <para>
/// Fuente: catálogo <c>c_FormaPago</c> del Anexo 20 (columnas «Bancarizado»,
/// «Cuenta Ordenante», «Patrón para cuenta ordenante», «Cuenta de Beneficiario»,
/// «Patrón para cuenta Beneficiaria») y la guía de llenado del complemento
/// para recepción de pagos 2.0. Solo las formas bancarizadas admiten cuentas;
/// cualquier otra forma (01 efectivo, 08 vales, 12–27 extinciones, 30 anticipos,
/// 31 intermediario, 99) no admite ninguna. 06 (dinero electrónico) admite
/// solo la ordenante. Las cuentas son opcionales: el SAT solo exige el patrón
/// cuando se informan.
/// </para>
///
/// <list type="table">
/// <listheader><term>Forma</term><description>Ordenante · Beneficiaria</description></listheader>
/// <item><term>02 Cheque nominativo</term><description>11 o 18 dígitos · 10, 11, 15, 16 o 18 dígitos, o 10–50 alfanuméricos</description></item>
/// <item><term>03 Transferencia electrónica</term><description>10, 16 o 18 dígitos · 10 o 18 dígitos</description></item>
/// <item><term>04 Tarjeta de crédito</term><description>16 dígitos · 10, 11, 15, 16 o 18 dígitos, o 10–50 alfanuméricos</description></item>
/// <item><term>05 Monedero electrónico</term><description>10, 11, 15, 16 o 18 dígitos, o 10–50 alfanuméricos · igual</description></item>
/// <item><term>06 Dinero electrónico</term><description>10 dígitos · no admite</description></item>
/// <item><term>28 Tarjeta de débito</term><description>16 dígitos · 10, 11, 15, 16 o 18 dígitos, o 10–50 alfanuméricos</description></item>
/// <item><term>29 Tarjeta de servicios</term><description>15 o 16 dígitos · 10, 11, 15, 16 o 18 dígitos, o 10–50 alfanuméricos</description></item>
/// </list>
///
/// <para>
/// Fuera de alcance (decisión pendiente de Eliam): <c>RfcEmisorCtaOrd</c>,
/// <c>NomBancoOrdExt</c> y <c>RfcEmisorCtaBen</c>.
/// </para>
/// </summary>
public static class CuentasPagoSat
{
    public const string CodigoError = "REPP_CUENTA_INVALIDA";

    /// <summary>Patrón y texto legible de un formato de cuenta.</summary>
    public sealed record FormatoCuenta(string Patron, string Descripcion)
    {
        private readonly Regex _regex = new($"^(?:{Patron})$", RegexOptions.CultureInvariant);
        public bool Cumple(string valor) => _regex.IsMatch(valor);
    }

    /// <summary>Regla de una forma de pago bancarizada; <c>null</c> = no admite esa cuenta.</summary>
    public sealed record ReglaCuentas(string Clave, string Nombre, FormatoCuenta? Ordenante, FormatoCuenta? Beneficiaria);

    private static readonly FormatoCuenta Generico = new(
        "[0-9]{10,11}|[0-9]{15,16}|[0-9]{18}|[A-Z0-9_]{10,50}",
        "10, 11, 15, 16 o 18 dígitos, o de 10 a 50 caracteres alfanuméricos");

    public static readonly IReadOnlyDictionary<string, ReglaCuentas> Reglas = new Dictionary<string, ReglaCuentas>
    {
        ["02"] = new("02", "Cheque nominativo", new("[0-9]{11}|[0-9]{18}", "11 o 18 dígitos"), Generico),
        ["03"] = new("03", "Transferencia electrónica de fondos",
            new("[0-9]{10}|[0-9]{16}|[0-9]{18}", "10, 16 o 18 dígitos"), new("[0-9]{10}|[0-9]{18}", "10 o 18 dígitos")),
        ["04"] = new("04", "Tarjeta de crédito", new("[0-9]{16}", "16 dígitos"), Generico),
        ["05"] = new("05", "Monedero electrónico", Generico, Generico),
        ["06"] = new("06", "Dinero electrónico", new("[0-9]{10}", "10 dígitos"), null),
        ["28"] = new("28", "Tarjeta de débito", new("[0-9]{16}", "16 dígitos"), Generico),
        ["29"] = new("29", "Tarjeta de servicios", new("[0-9]{15,16}", "15 o 16 dígitos"), Generico),
    };

    public static bool AdmiteOrdenante(string? formaPago) => Regla(formaPago)?.Ordenante is not null;
    public static bool AdmiteBeneficiaria(string? formaPago) => Regla(formaPago)?.Beneficiaria is not null;

    /// <summary>Quita espacios y guiones de captura; vacío = sin cuenta.</summary>
    public static string? Normalizar(string? cuenta)
    {
        if (string.IsNullOrWhiteSpace(cuenta)) return null;
        var limpia = new string(cuenta.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        return limpia.Length == 0 ? null : limpia;
    }

    /// <summary>
    /// Valida ambas cuentas (ya normalizadas o no) contra la forma de pago.
    /// Lanza <c>REPP_CUENTA_INVALIDA</c> con forma, cuenta enmascarada y formato esperado.
    /// </summary>
    public static void Validar(string formaPago, string? cuentaOrdenante, string? cuentaBeneficiaria)
    {
        ValidarUna(formaPago, Normalizar(cuentaOrdenante), "ordenante", r => r.Ordenante);
        ValidarUna(formaPago, Normalizar(cuentaBeneficiaria), "beneficiaria", r => r.Beneficiaria);
    }

    /// <summary>
    /// Cuenta beneficiaria sugerida a partir de una cuenta bancaria de Millet:
    /// la CLABE si la forma la admite (18 dígitos), si no el número de cuenta;
    /// <c>null</c> si la forma no admite beneficiaria o ninguna cumple el patrón.
    /// </summary>
    public static string? SugerirBeneficiaria(string? formaPago, string? clabe, string? numeroCuenta)
    {
        var formato = Regla(formaPago)?.Beneficiaria;
        if (formato is null) return null;
        foreach (var candidata in new[] { Normalizar(clabe), Normalizar(numeroCuenta) })
            if (candidata is not null && formato.Cumple(candidata)) return candidata;
        return null;
    }

    /// <summary>Enmascarado para mostrar (ADR-0006): «•••• 1234».</summary>
    public static string? Enmascarar(string? cuenta)
    {
        var v = cuenta?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= 4 ? "••••" : $"•••• {v[^4..]}";
    }

    private static ReglaCuentas? Regla(string? formaPago) =>
        formaPago is not null && Reglas.TryGetValue(formaPago.Trim(), out var r) ? r : null;

    private static void ValidarUna(string formaPago, string? cuenta, string tipo, Func<ReglaCuentas, FormatoCuenta?> formato)
    {
        if (cuenta is null) return;
        var regla = Regla(formaPago);
        var esperado = regla is null ? null : formato(regla);
        if (esperado is null)
            throw new BusinessRuleException(CodigoError,
                $"La forma de pago {formaPago}{(regla is null ? string.Empty : $" ({regla.Nombre})")} no admite cuenta {tipo}; " +
                $"quita la cuenta {Enmascarar(cuenta)} o cambia la forma de pago.");
        if (!esperado.Cumple(cuenta))
            throw new BusinessRuleException(CodigoError,
                $"La cuenta {tipo} {Enmascarar(cuenta)} no tiene el formato del SAT para la forma de pago " +
                $"{regla!.Clave} ({regla.Nombre}): se esperan {esperado.Descripcion}.");
    }
}
