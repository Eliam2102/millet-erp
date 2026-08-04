using System.Text.Json;
using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Administracion.Domain;

/// <summary>
/// Parámetro de configuración global del sistema (TimezoneDefault,
/// FormatoFecha, RedondeoMonetario, etc.) o de un módulo específico
/// si <see cref="Modulo"/> es no-null. Vive en
/// <c>compartido.parametros_globales</c> (F-Admin-PR7.1, ADR-0034).
///
/// <para>
/// La <see cref="Clave"/> es el identificador funcional (formato
/// <c>modulo.nombre-parametro</c> en kebab-case). Inmutable.
/// El <see cref="Valor"/> se almacena como string; el handler valida
/// según <see cref="Tipo"/> antes de persistir.
/// </para>
/// </summary>
public sealed class ParametroGlobal : BaseEntity, IAuditable
{
    private static readonly Regex ClaveRegex = new(
        "^[a-z][a-z0-9._-]*$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    public string Clave { get; private set; } = string.Empty;

    public string Valor { get; private set; } = string.Empty;

    public TipoParametro Tipo { get; private set; }

    /// <summary>
    /// Módulo dueño del parámetro (lowercase, ej. <c>compras</c>) o
    /// <c>null</c> para parámetros globales del sistema.
    /// </summary>
    public string? Modulo { get; private set; }

    public string Descripcion { get; private set; } = string.Empty;

    private ParametroGlobal() { } // EF Core

    public ParametroGlobal(
        Guid id,
        string clave,
        string valor,
        TipoParametro tipo,
        string descripcion,
        string? modulo = null) : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("PARAMETRO_GLOBAL_ID_INVALIDO", "El id es obligatorio.");
        ValidarClave(clave);
        ValidarValorPorTipo(valor, tipo);
        ValidarDescripcion(descripcion);
        ValidarModulo(modulo);

        Clave = clave;
        Valor = valor;
        Tipo = tipo;
        Descripcion = descripcion;
        Modulo = modulo;
    }

    /// <summary>
    /// Actualiza el valor del parámetro validando contra
    /// <see cref="Tipo"/>. Tipo y Clave son inmutables — cambios de
    /// shape requieren nueva migración.
    /// </summary>
    public void ActualizarValor(string nuevoValor)
    {
        ValidarValorPorTipo(nuevoValor, Tipo);
        Valor = nuevoValor;
    }

    /// <summary>Factory para parámetro global del sistema (sin módulo).</summary>
    public static ParametroGlobal CrearGlobal(
        Guid id,
        string clave,
        string valor,
        TipoParametro tipo,
        string descripcion) =>
        new(id, clave, valor, tipo, descripcion, modulo: null);

    /// <summary>Factory para parámetro asociado a un módulo específico.</summary>
    public static ParametroGlobal CrearDeModulo(
        Guid id,
        string clave,
        string valor,
        TipoParametro tipo,
        string modulo,
        string descripcion) =>
        new(id, clave, valor, tipo, descripcion, modulo);

    private static void ValidarClave(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 100)
            throw new BusinessRuleException("PARAMETRO_GLOBAL_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 100 caracteres.");
        if (!ClaveRegex.IsMatch(clave))
            throw new BusinessRuleException("PARAMETRO_GLOBAL_CLAVE_FORMATO",
                "La clave debe estar en kebab/snake_case (lowercase, dígitos, '.', '-', '_').");
    }

    private static void ValidarDescripcion(string descripcion)
    {
        if (descripcion is { Length: > 500 })
            throw new BusinessRuleException("PARAMETRO_GLOBAL_DESCRIPCION_INVALIDA",
                "La descripción no puede exceder 500 caracteres.");
    }

    private static void ValidarModulo(string? modulo)
    {
        if (modulo is null) return;
        if (modulo.Length is 0 or > 50)
            throw new BusinessRuleException("PARAMETRO_GLOBAL_MODULO_INVALIDO",
                "El módulo debe tener entre 1 y 50 caracteres.");
    }

    private static void ValidarValorPorTipo(string valor, TipoParametro tipo)
    {
        if (valor is null || valor.Length > 2000)
            throw new BusinessRuleException("PARAMETRO_GLOBAL_VALOR_INVALIDO",
                "El valor es requerido y no puede exceder 2000 caracteres.");

        switch (tipo)
        {
            case TipoParametro.Texto:
                // Cualquier string válido.
                break;
            case TipoParametro.Numero:
                if (!decimal.TryParse(valor, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                    throw new BusinessRuleException("PARAMETRO_GLOBAL_VALOR_NO_NUMERO",
                        $"El valor '{valor}' no es un número válido (TipoParametro.Numero).");
                break;
            case TipoParametro.Booleano:
                if (valor is not ("true" or "false"))
                    throw new BusinessRuleException("PARAMETRO_GLOBAL_VALOR_NO_BOOLEANO",
                        "El valor debe ser 'true' o 'false' (TipoParametro.Booleano).");
                break;
            case TipoParametro.Json:
                try
                {
                    using var _ = JsonDocument.Parse(valor);
                }
                catch (JsonException ex)
                {
                    throw new BusinessRuleException("PARAMETRO_GLOBAL_VALOR_NO_JSON",
                        $"El valor no es JSON válido: {ex.Message}");
                }
                break;
        }
    }
}
