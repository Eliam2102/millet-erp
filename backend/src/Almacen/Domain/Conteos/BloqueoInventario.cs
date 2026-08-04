using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Conteos;

/// <summary>
/// Bloqueo de operaciones durante un conteo anual (F7-PR3, A18).
/// Tabla <c>almacen.bloqueos_inventario</c>. Cuando un
/// <see cref="ConteoInventario"/> tipo <see cref="TipoConteo.Anual"/>
/// pasa a EnCurso, se crea un registro <see cref="BloqueoInventario"/>
/// para cada sub-almacén afectado. Al pasar el conteo a Aplicado o
/// Rechazado, el bloqueo se desactiva.
///
/// <para>
/// La validación cross-cutting vive en <c>RegistrarSalidaConRequisicionHandler</c>
/// y <c>RegistrarSalidaPorValeHandler</c>: si hay bloqueo activo
/// (<c>BloqueaSalidas=true</c>, <c>Activo=true</c>) para el sub-almacén
/// destino, la salida se rechaza con 422 y mensaje claro.
/// </para>
/// </summary>
public sealed class BloqueoInventario : BaseEntity, IAuditable
{
    public Guid ConteoId { get; private set; }
    public Guid SubAlmacenId { get; private set; }
    public bool BloqueaSalidas { get; private set; }
    public bool BloqueaEntradas { get; private set; }
    public bool Activo { get; private set; }
    public DateTimeOffset Desde { get; private set; }
    public DateTimeOffset? Hasta { get; private set; }

    private BloqueoInventario() { }

    public BloqueoInventario(
        Guid id,
        Guid conteoId,
        Guid subAlmacenId,
        bool bloqueaSalidas = true,
        bool bloqueaEntradas = false) : base(id)
    {
        if (conteoId == Guid.Empty)
            throw new BusinessRuleException("BLOQUEO_SIN_CONTEO",
                "El bloqueo requiere conteo.");
        if (subAlmacenId == Guid.Empty)
            throw new BusinessRuleException("BLOQUEO_SIN_SUB_ALMACEN",
                "El bloqueo requiere sub-almacén.");

        ConteoId = conteoId;
        SubAlmacenId = subAlmacenId;
        BloqueaSalidas = bloqueaSalidas;
        BloqueaEntradas = bloqueaEntradas;
        Activo = true;
        Desde = DateTimeOffset.UtcNow;
    }

    public void Liberar()
    {
        if (!Activo) return; // idempotente
        Activo = false;
        Hasta = DateTimeOffset.UtcNow;
    }
}
