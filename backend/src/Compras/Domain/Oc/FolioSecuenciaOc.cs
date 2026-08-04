namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Tabla de secuencias de folios de OC por
/// <c>(empresa_id, sucursal_id, anio)</c> del diseño §10.1. Hereda el
/// patrón de <c>FolioSecuencia</c> de Requisiciones — son tablas
/// separadas para que el contador de OCs sea independiente del de RQs.
///
/// La generación atómica del siguiente folio se hace en F1-PR2 vía
/// <c>UPDATE ... RETURNING siguiente</c> dentro de la transacción del
/// comando <c>CrearOrdenCompraVacia</c>; esta entidad solo modela la
/// fila persistida.
///
/// PK compuesta. No hereda de <c>BaseEntity</c> (no es agregado de
/// dominio, es estructura de soporte).
///
/// PLATFORM-TODO(&lt;FolioSecuenciaDeprecate&gt;): F-Admin-PR6.2 introdujo
/// fallback al nuevo sistema cross-módulo (<c>compartido.series</c> +
/// <c>compartido.secuencias_folio</c>) en
/// <c>CrearOrdenCompraVaciaHandler</c>. Cuando el cutover formal ocurra
/// (ver PLATFORM-TODO &lt;OcFolioMigrateToSeries&gt;), eliminar esta
/// entidad y la tabla <c>compras.folio_secuencias_oc</c> en una
/// migración aditiva.
/// </summary>
public sealed class FolioSecuenciaOc
{
    public Guid EmpresaId { get; private set; }
    public Guid SucursalId { get; private set; }
    public short Anio { get; private set; }
    public long Siguiente { get; private set; }

    /// <summary>Constructor para EF Core.</summary>
    private FolioSecuenciaOc() { }

    public FolioSecuenciaOc(Guid empresaId, Guid sucursalId, short anio, long siguiente)
    {
        EmpresaId = empresaId;
        SucursalId = sucursalId;
        Anio = anio;
        Siguiente = siguiente;
    }
}
