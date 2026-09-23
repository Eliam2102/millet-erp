using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.Almacen;

/// <summary>
/// Proyección local de <c>almacen.oc_recepcion.registrada.v1</c>
/// (F5-PR1). CxP mantiene una copia compacta para que los handlers
/// resuelvan "¿esta OC tiene recepción?" sin query cross-DbContext.
///
/// <para>
/// Una fila por <c>RecepcionId</c>; unicidad enforced por índice.
/// Variante A: <c>FacturaPendiente = false</c>. Variante B:
/// <c>FacturaPendiente = true</c> hasta que CxP capture la factura y
/// el listener de Almacén la marque conciliada — esa transición vive
/// en F6+ cuando exista comando de actualización.
/// </para>
/// </summary>
public sealed class RecepcionOcLocal : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid RecepcionId { get; private set; }
    public string FolioRecepcion { get; private set; } = default!;
    public Guid OrdenCompraId { get; private set; }
    // Almacén-por-línea 6b-3: la propiedad SubAlmacenId se eliminó por completo
    // (cerró el retiro de tres fases: relajar nullable → dejar de mapear → dropear).
    // La columna sub_almacen_id ya no existe en la BD ni en el espejo.
    public DateOnly FechaMovimiento { get; private set; }

    public bool FacturaPendiente { get; private set; }
    public Guid? CfdiRecibidoId { get; private set; }
    public string? Observaciones { get; private set; }

    /// <summary>
    /// JSON serializado de las líneas (id linea recepción, FK línea OC,
    /// artículo, cantidad, costo). Se hidrata bajo demanda en queries
    /// que necesiten el detalle.
    /// </summary>
    public string LineasJson { get; private set; } = "[]";

    public DateTimeOffset OcurridoEn { get; private set; }
    public DateTimeOffset ProyectadoEn { get; private set; }

    private RecepcionOcLocal() { }

    public RecepcionOcLocal(
        Guid empresaId,
        Guid recepcionId,
        string folioRecepcion,
        Guid ordenCompraId,
        DateOnly fechaMovimiento,
        bool facturaPendiente,
        Guid? cfdiRecibidoId,
        string? observaciones,
        string lineasJson,
        DateTimeOffset ocurridoEn,
        DateTimeOffset proyectadoEn) : base(Guid.CreateVersion7())
    {
        if (recepcionId == Guid.Empty)
            throw new BusinessRuleException("RECEPCION_ID_VACIO", "RecepcionId es obligatorio.");
        if (ordenCompraId == Guid.Empty)
            throw new BusinessRuleException("RECEPCION_OC_VACIA", "OrdenCompraId es obligatorio.");

        EmpresaId = empresaId;
        RecepcionId = recepcionId;
        FolioRecepcion = folioRecepcion;
        OrdenCompraId = ordenCompraId;
        FechaMovimiento = fechaMovimiento;
        FacturaPendiente = facturaPendiente;
        CfdiRecibidoId = cfdiRecibidoId;
        Observaciones = observaciones;
        LineasJson = lineasJson ?? "[]";
        OcurridoEn = ocurridoEn;
        ProyectadoEn = proyectadoEn;
    }
}
