using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Catalogo;

/// <summary>
/// Ubicación física (Nivel 4) dentro de un <see cref="SubAlmacen"/> (Nivel 3).
/// Tabla <c>almacen.ubicaciones</c>. Es el cuarto nivel de la jerarquía
/// obligatoria Sucursal → Almacén → Sub-almacén → Ubicación (ADR-0047).
///
/// <para>
/// Se gestiona como entidad standalone (DbSet propio + handlers directos +
/// FK <c>sub_almacen_id</c> por id), igual que <see cref="SubAlmacen"/> se
/// gestiona hoy respecto de <see cref="Almacen"/> — no vía métodos del
/// agregado. La coherencia jerárquica la garantizan la FK física a
/// <c>almacen.sub_almacenes</c> y la validación del padre en el handler.
/// </para>
/// <para>
/// Unicidad: <c>(sub_almacen_id, clave)</c>. Una misma clave puede repetirse
/// en distintos sub-almacenes.
/// </para>
/// <para>
/// <b>Alcance PR1 (solo estructura):</b> este nivel NO modela saldos, ni la
/// asignación artículo→ubicación, ni min/máx/reorden. La existencia de
/// inventario baja a este nivel (con rollup a los superiores) en PR2/PR3.
/// </para>
/// </summary>
public sealed class Ubicacion : BaseEntity, IAuditable
{
    public Guid SubAlmacenId { get; private set; }
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    /// <summary>
    /// Ubicación default del sub-almacén (ADR-0047, PR2). El trigger de saldos
    /// enruta a la ubicación con esta bandera para el <c>sub_almacen_id</c> del
    /// movimiento. En PR2 es la ÚNICA temporal (una por sub-almacén, creada por
    /// backfill); forward-compatible con PR7 (captura de bin real) donde sigue
    /// siendo el destino por defecto de movimientos sin bin explícito.
    /// Invariante: a lo sumo una default por sub-almacén (índice único parcial).
    /// </summary>
    public bool EsDefault { get; private set; }

    private Ubicacion() { }

    public Ubicacion(
        Guid id,
        Guid subAlmacenId,
        string clave,
        string nombre,
        EstatusCatalogo estatus = EstatusCatalogo.Activo,
        bool esDefault = false) : base(id)
    {
        if (subAlmacenId == Guid.Empty)
            throw new BusinessRuleException("UBICACION_SUBALMACEN_INVALIDO",
                "La ubicación debe pertenecer a un sub-almacén.");
        ValidarClave(clave);
        ValidarNombre(nombre);

        SubAlmacenId = subAlmacenId;
        Clave = clave;
        Nombre = nombre;
        Estatus = estatus;
        EsDefault = esDefault;
    }

    public void Editar(string clave, string nombre)
    {
        ValidarClave(clave);
        ValidarNombre(nombre);

        Clave = clave;
        Nombre = nombre;
    }

    public void CambiarEstatus(EstatusCatalogo estatus) => Estatus = estatus;

    private static void ValidarClave(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("UBICACION_CLAVE_INVALIDA",
                "La clave de la ubicación es requerida y no puede exceder 20 caracteres.");
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("UBICACION_NOMBRE_INVALIDO",
                "El nombre de la ubicación es requerido y no puede exceder 254 caracteres.");
    }
}
