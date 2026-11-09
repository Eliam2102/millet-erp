using System.ComponentModel.DataAnnotations;

namespace Millet.Integraciones.Aw.Application.Pinturas.Catalogos;

/// <summary>
/// Opciones de configuración para la sincronización periódica de catálogos hacia el Portal de Pinturas.
/// Sección: <c>IntegracionesAw:Pinturas:Catalogos</c>.
/// </summary>
public sealed class PinturasCatalogosOptions : IValidatableObject
{
    public const string SectionName = "IntegracionesAw:Pinturas:Catalogos";

    /// <summary>Indica si el worker de publicación periódica de catálogos está habilitado.</summary>
    public bool Habilitado { get; set; } = true;

    /// <summary>Intervalo en minutos entre cada sincronización (por defecto 30 minutos).</summary>
    [Range(1, 1440, ErrorMessage = "IntervaloMinutos debe ser mayor a 0.")]
    public int IntervaloMinutos { get; set; } = 30;

    /// <summary>Tamaño del lote para procesar y escribir catálogos en base de datos.</summary>
    [Range(1, 10000, ErrorMessage = "BatchSize debe ser mayor a 0.")]
    public int BatchSize { get; set; } = 500;

    /// <summary>Timeout por consulta o comando SQL (segundos).</summary>
    [Range(1, 600, ErrorMessage = "SqlQueryTimeoutSeconds debe ser mayor a 0.")]
    public int SqlQueryTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Valida que las opciones configuradas sean consistentes y mayores a cero.
    /// </summary>
    public void Validar()
    {
        if (IntervaloMinutos <= 0)
        {
            throw new InvalidOperationException($"{nameof(IntervaloMinutos)} debe ser mayor a 0.");
        }

        if (BatchSize <= 0)
        {
            throw new InvalidOperationException($"{nameof(BatchSize)} debe ser mayor a 0.");
        }

        if (SqlQueryTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException($"{nameof(SqlQueryTimeoutSeconds)} debe ser mayor a 0.");
        }
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (IntervaloMinutos <= 0)
        {
            yield return new ValidationResult("IntervaloMinutos debe ser mayor a 0.", [nameof(IntervaloMinutos)]);
        }

        if (BatchSize <= 0)
        {
            yield return new ValidationResult("BatchSize debe ser mayor a 0.", [nameof(BatchSize)]);
        }

        if (SqlQueryTimeoutSeconds <= 0)
        {
            yield return new ValidationResult("SqlQueryTimeoutSeconds debe ser mayor a 0.", [nameof(SqlQueryTimeoutSeconds)]);
        }
    }
}
