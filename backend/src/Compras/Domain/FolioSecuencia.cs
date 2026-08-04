namespace Millet.Compras.Domain;

/// <summary>
/// Tabla de secuencias de folios por <c>(empresa_id, sucursal_id, anio)</c>
/// del diseño §10.1. La generación atómica del siguiente folio se hace
/// en F1-PR2 vía <c>UPDATE ... RETURNING siguiente</c> dentro de la
/// transacción del comando <c>CrearRequisicion</c>; esta entidad solo
/// modela la fila persistida.
///
/// PK compuesta. No hereda de <c>BaseEntity</c> (no es agregado de
/// dominio, es estructura de soporte).
///
/// PLATFORM-TODO(&lt;FolioSecuenciaDeprecate&gt;): F-Admin-PR6.1 introdujo
/// el sistema cross-módulo <c>compartido.series</c> +
/// <c>compartido.secuencias_folio</c>. Cuando RQ migre a consumir
/// <c>ReservarFolioCommand</c> (futuro F-Admin-PR6.x), eliminar esta
/// entidad y la tabla <c>compras.folio_secuencias</c> en una migración
/// aditiva. Hoy se mantiene activa para preservar el formato canónico
/// <c>MID2026-NNNNNN</c> que los tests RQ esperan.
/// </summary>
public sealed class FolioSecuencia
{
    public Guid EmpresaId { get; private set; }
    public Guid SucursalId { get; private set; }
    public short Anio { get; private set; }
    public int Siguiente { get; private set; }

    /// <summary>Constructor para EF Core.</summary>
    private FolioSecuencia() { }

    public FolioSecuencia(Guid empresaId, Guid sucursalId, short anio, int siguiente)
    {
        EmpresaId = empresaId;
        SucursalId = sucursalId;
        Anio = anio;
        Siguiente = siguiente;
    }
}
