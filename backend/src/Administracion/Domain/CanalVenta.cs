using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Administracion.Domain;

/// <summary>
/// Canal de venta del catálogo cross-empresa <c>compartido.canales_venta</c>
/// (FAC-ING-PR2). Eje <b>organizacional</b> de la facturación: de dónde
/// viene la venta (tiendas, centros comerciales, exportación, etc.).
/// Reemplaza al enum hardcodeado <c>Facturacion.Domain.Facturas.CanalVenta</c>
/// — dimensión puramente organizacional, sin lógica fiscal (esa vive en
/// <c>ComportamientoFiscal</c>, que sigue siendo enum).
///
/// <para>
/// PK <c>short</c> asignada por la aplicación (NO identity): las filas seed
/// 1..10 son espejo del enum original y los pedidos/facturas existentes ya
/// persisten esos valores en <c>canal_venta smallint</c>. Por la PK no-GUID
/// la entidad no deriva de <see cref="BaseEntity"/>; la metadata la asigna
/// el interceptor vía <see cref="ITieneMetadata"/>.
/// </para>
/// </summary>
public sealed class CanalVenta : ITieneMetadata, IAuditable
{
    public short Id { get; private set; }

    public string Nombre { get; private set; } = string.Empty;

    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    /// <summary>
    /// GRUPPE con el que A+W refiere este canal en sus pedidos (columna
    /// <c>canal_ventas</c> de <c>vw_erp_pedido_cabecera</c>, p.ej.
    /// "Ventas Cancun", "CC Mérida"). La ingesta de pedidos (ADR-0048,
    /// flujo 2) resuelve el canal por este campo, case-insensitive.
    /// <c>null</c> = el canal no recibe pedidos de A+W.
    /// </summary>
    public string? ClaveAw { get; private set; }

    // --- Metadata (ITieneMetadata; la asigna MetadataSaveChangesInterceptor) ---
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    private CanalVenta() { }

    public CanalVenta(
        short id,
        string nombre,
        string? claveAw = null,
        EstatusCatalogo estatus = EstatusCatalogo.Activo)
    {
        if (id <= 0)
            throw new BusinessRuleException("CANAL_VENTA_ID_INVALIDO", "El id debe ser positivo.");
        ValidarNombre(nombre);
        ValidarClaveAw(claveAw);

        Id = id;
        Nombre = nombre;
        Estatus = estatus;
        ClaveAw = Normalizar(claveAw);
    }

    /// <summary>
    /// PATCH parcial sobre los campos editables del canal. Convención:
    /// parámetro <c>null</c> = no tocar; <paramref name="limpiarClaveAw"/>
    /// desasocia el canal de A+W. Inmutable: <see cref="Id"/> (ya persistido
    /// en pedidos y facturas).
    /// </summary>
    public void ActualizarDatos(
        string? nombre = null,
        string? claveAw = null,
        bool limpiarClaveAw = false)
    {
        if (nombre is not null)
        {
            ValidarNombre(nombre);
            Nombre = nombre;
        }

        if (limpiarClaveAw)
        {
            ClaveAw = null;
        }
        else if (claveAw is not null)
        {
            ValidarClaveAw(claveAw);
            ClaveAw = Normalizar(claveAw);
        }
    }

    /// <summary>Reactiva el canal (<see cref="EstatusCatalogo.Activo"/>). Idempotente.</summary>
    public void Activar() => Estatus = EstatusCatalogo.Activo;

    /// <summary>
    /// Desactiva el canal (<see cref="EstatusCatalogo.Inactivo"/>). Los
    /// pedidos/facturas históricos conservan su <c>canal_venta_id</c>; un
    /// canal inactivo deja de aceptar pedidos nuevos (validators + resolver
    /// de ingesta solo aceptan activos).
    /// </summary>
    public void Desactivar() => Estatus = EstatusCatalogo.Inactivo;

    /// <summary>
    /// Mueve a estado <see cref="EstatusCatalogo.EnRevision"/> para flujos
    /// internos donde aplique. Mismo contrato que <see cref="Sucursal"/>.
    /// </summary>
    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("CANAL_VENTA_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 254 caracteres.");
    }

    private static void ValidarClaveAw(string? claveAw)
    {
        if (claveAw is not null
            && (string.IsNullOrWhiteSpace(claveAw) || claveAw.Trim().Length > 40))
            throw new BusinessRuleException("CANAL_VENTA_CLAVE_AW_INVALIDA",
                "La clave A+W no puede ser vacía ni exceder 40 caracteres.");
    }

    private static string? Normalizar(string? claveAw) => claveAw?.Trim();
}
