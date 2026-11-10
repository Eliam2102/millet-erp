using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Tambores;

/// <summary>
/// Línea de producción de una sucursal a la que el almacén surte tambores o cubetas (M-172, AC-030): p. ej. Conkal ·
/// Insulado 1 e Insulado 2, independientes entre sí. Tabla <c>almacen.lineas_produccion</c>; clave única por sucursal.
/// </summary>
public sealed class LineaProduccion : BaseEntity, IAuditable
{
    public Guid SucursalId { get; private set; }
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    /// <summary>Proceso de la línea (p. ej. INSULADO).</summary>
    public string Proceso { get; private set; } = string.Empty;
    public bool Activa { get; private set; } = true;

    private LineaProduccion() { }

    public LineaProduccion(Guid id, Guid sucursalId, string clave, string nombre, string proceso) : base(id)
    {
        if (sucursalId == Guid.Empty)
            throw new BusinessRuleException("LINEA_PRODUCCION_SUCURSAL_INVALIDA", "La línea debe pertenecer a una sucursal.");
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("LINEA_PRODUCCION_CLAVE_INVALIDA", "La clave es requerida y no puede exceder 20 caracteres.");
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 100)
            throw new BusinessRuleException("LINEA_PRODUCCION_NOMBRE_INVALIDO", "El nombre es requerido y no puede exceder 100 caracteres.");
        if (string.IsNullOrWhiteSpace(proceso) || proceso.Length > 50)
            throw new BusinessRuleException("LINEA_PRODUCCION_PROCESO_INVALIDO", "El proceso es requerido y no puede exceder 50 caracteres.");
        SucursalId = sucursalId; Clave = clave; Nombre = nombre; Proceso = proceso;
    }
}
