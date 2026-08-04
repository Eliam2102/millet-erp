using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Tesoreria.Domain.Cuentas;

/// <summary>
/// Value object CLABE interbancaria (§4.3 del 01-diseño; ADR-0018 §CLABE):
/// 18 dígitos con validación de dígito verificador (ponderación 3-7-1
/// sobre los primeros 17). PII (ADR-0006): <see cref="ToString"/> devuelve
/// SIEMPRE la forma enmascarada — el valor completo solo sale por
/// <see cref="Valor"/> explícito (permiso
/// <c>tesoreria.movimientos.ver-cuenta-completa</c> en la capa de
/// queries). El enricher Serilog <c>MaskSensitivePropertiesEnricher</c>
/// ya cubre la key <c>clabe</c> en logs.
/// </summary>
public readonly record struct Clabe
{
    /// <summary>Valor completo (18 dígitos). Usar solo con permiso de des-enmascarado.</summary>
    public string Valor { get; }

    private Clabe(string valor) => Valor = valor;

    public static Clabe Crear(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new BusinessRuleException("CLABE_VACIA", "La CLABE es obligatoria.");

        var v = valor.Trim();
        if (v.Length != 18 || !v.All(char.IsAsciiDigit))
            throw new BusinessRuleException("CLABE_FORMATO", "La CLABE debe tener 18 dígitos.");

        if (CalcularDigitoControl(v) != v[17] - '0')
            throw new BusinessRuleException("CLABE_DIGITO_CONTROL",
                "La CLABE no pasa la validación del dígito verificador.");

        return new Clabe(v);
    }

    /// <summary>Ponderación estándar Banxico 3-7-1 sobre los primeros 17 dígitos.</summary>
    private static int CalcularDigitoControl(string clabe)
    {
        ReadOnlySpan<int> pesos = [3, 7, 1];
        var suma = 0;
        for (var i = 0; i < 17; i++)
        {
            suma += (clabe[i] - '0') * pesos[i % 3] % 10;
        }
        return (10 - suma % 10) % 10;
    }

    /// <summary>Forma enmascarada: solo los últimos 4 dígitos visibles (<c>**************1234</c>).</summary>
    public string Enmascarada => Enmascarar(Valor);

    /// <summary>Enmascara cualquier CLABE/cuenta ya persistida sin re-validar (helper para queries).</summary>
    public static string Enmascarar(string valor)
    {
        var v = valor.Trim();
        return v.Length <= 4 ? new string('*', v.Length)
            : string.Concat(new string('*', v.Length - 4), v.AsSpan(v.Length - 4));
    }

    public override string ToString() => Enmascarada;
}

/// <summary>
/// Value object número de cuenta bancaria (§4.3): formato libre por banco
/// (6–40 caracteres alfanuméricos). Mismo tratamiento PII que
/// <see cref="Clabe"/>: <see cref="ToString"/> enmascara; el enricher
/// Serilog cubre la key <c>numeroCuenta</c>.
/// </summary>
public readonly record struct NumeroCuenta
{
    public string Valor { get; }

    private NumeroCuenta(string valor) => Valor = valor;

    public static NumeroCuenta Crear(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new BusinessRuleException("NUMERO_CUENTA_VACIO", "El número de cuenta es obligatorio.");

        var v = valor.Trim();
        if (v.Length is < 6 or > 40 || !v.All(char.IsAsciiLetterOrDigit))
            throw new BusinessRuleException("NUMERO_CUENTA_FORMATO",
                "El número de cuenta debe tener entre 6 y 40 caracteres alfanuméricos.");

        return new NumeroCuenta(v);
    }

    public string Enmascarado => Clabe.Enmascarar(Valor);

    public override string ToString() => Enmascarado;
}
