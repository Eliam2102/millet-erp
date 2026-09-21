using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Cuentas;

/// <summary>
/// Catálogo de conceptos de movimiento bancario (§4.9 del levantamiento,
/// §5.2). Alimenta la clasificación del reporte de flujo de efectivo
/// (Operación / Inversión / Financiamiento). Seed provisional en PR-1
/// pendiente de validación de Javier contra su plantilla de flujo de
/// efectivo — corregir es editar filas, no código.
/// </summary>
public sealed class ConceptoMovimiento : BaseEntity, IAuditable
{
    public string Nombre { get; private set; } = default!;
    public ClasificacionFlujo ClasificacionFlujo { get; private set; }
    public bool Activo { get; private set; } = true;

    private ConceptoMovimiento() { }

    public ConceptoMovimiento(string nombre, ClasificacionFlujo clasificacionFlujo)
        : base(Guid.CreateVersion7())
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new BusinessRuleException("CONCEPTO_NOMBRE_VACIO", "El nombre del concepto es obligatorio.");

        Nombre = nombre.Trim();
        ClasificacionFlujo = clasificacionFlujo;
        Activo = true;
    }
}

/// <summary>Clasificación de flujo de efectivo del concepto (§5 DDL: 1=Operación 2=Inversión 3=Financiamiento).</summary>
public enum ClasificacionFlujo : short
{
    Operacion = 1,
    Inversion = 2,
    Financiamiento = 3,
}
