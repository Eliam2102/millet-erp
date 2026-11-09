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
    /// <summary>Concepto sembrado de egreso para toda devolución de dinero a un cliente (C-04).</summary>
    public static readonly Guid DevolucionClienteId = Guid.Parse("0000000b-1001-0000-0000-00000000000e");

    /// <summary>Concepto sembrado "Pago a proveedor": default de todo egreso a proveedor sin concepto elegido.</summary>
    public static readonly Guid PagoProveedorId = Guid.Parse("0000000b-1001-0000-0000-000000000001");

    /// <summary>M-203 (86e3zr7k1): concepto de sistema de todo traspaso entre cuentas propias; clasificación <see cref="ClasificacionFlujo.TraspasoInterno"/>.</summary>
    public static readonly Guid TraspasoId = Guid.Parse("0000000b-1001-0000-0000-000000000007");

    public string Nombre { get; private set; } = default!;
    public ClasificacionFlujo ClasificacionFlujo { get; private set; }
    public bool EsEjemplo { get; private set; }
    public bool Activo { get; private set; } = true;

    private ConceptoMovimiento() { }

    public ConceptoMovimiento(string nombre, ClasificacionFlujo clasificacionFlujo)
        : base(Guid.CreateVersion7())
    {
        if (string.IsNullOrWhiteSpace(nombre))
            throw new BusinessRuleException("CONCEPTO_NOMBRE_VACIO", "El nombre del concepto es obligatorio.");

        if (!Enum.IsDefined(clasificacionFlujo))
            throw new BusinessRuleException("CONCEPTO_CLASIFICACION_INVALIDA", "Selecciona operación, inversión, financiamiento o traspaso interno.");
        Nombre = nombre.Trim();
        ClasificacionFlujo = clasificacionFlujo;
        Activo = true;
    }
    public void Actualizar(string nombre, ClasificacionFlujo clasificacion, bool activo)
    {
        if (string.IsNullOrWhiteSpace(nombre) || !Enum.IsDefined(clasificacion))
            throw new BusinessRuleException("CONCEPTO_INVALIDO", "Indica el nombre y una clasificación válida.");
        Nombre = nombre.Trim(); ClasificacionFlujo = clasificacion; Activo = activo;
    }
}

/// <summary>Clasificación de flujo de efectivo del concepto (§5 DDL: 1=Operación 2=Inversión 3=Financiamiento 4=Traspaso interno: no es ingreso ni egreso del flujo).</summary>
public enum ClasificacionFlujo : short
{
    Operacion = 1,
    Inversion = 2,
    Financiamiento = 3,
    TraspasoInterno = 4,
}
