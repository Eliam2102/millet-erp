using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Administracion.Domain;

/// <summary>
/// Sucursal del catálogo cross-empresa <c>compartido.sucursales</c>
/// (B.1). Representa una unidad geográfica/operativa (CDMX, Monterrey,
/// Querétaro, etc.). Las RQs, OCs y movimientos de almacén referencian
/// sucursales por <see cref="BaseEntity.Id"/>.
///
/// <para>
/// Sin <c>EmpresaId</c> en MVP — una sola empresa cliente al lanzar.
/// Cuando sea multi-empresa, se agrega columna + filter en migration
/// aditiva. Mismo patrón que <see cref="DatosMaestros.Domain.Proveedor"/>
/// y <see cref="DatosMaestros.Domain.Articulo"/>.
/// </para>
/// </summary>
public sealed class Sucursal : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    /// <summary>
    /// Clave con la que A+W refiere esta sucursal en sus pedidos
    /// (columna <c>numero_sucursal</c> de <c>vw_erp_pedido_cabecera</c>,
    /// p.ej. "CONKAL"). La ingesta de pedidos (ADR-0048, flujo 2) resuelve
    /// la sucursal destino por este campo, case-insensitive.
    /// <c>null</c> = la sucursal no recibe pedidos de A+W.
    /// </summary>
    public string? ClaveAw { get; private set; }

    /// <summary>
    /// Zona horaria IANA de la sucursal (12-cajas.md `[Decisión 12-8]`,
    /// CAJAS-PR3). Millet opera en dos zonas: Yucatán
    /// (<c>America/Merida</c>, default) y Cancún (<c>America/Cancun</c>).
    /// Define el <b>día de operación</b> de las sesiones de caja; los
    /// timestamps siguen en UTC.
    /// </summary>
    public string ZonaHoraria { get; private set; } = ZonaHorariaDefault;

    public const string ZonaHorariaDefault = "America/Merida";

    private Sucursal() { }

    public Sucursal(
        Guid id,
        string clave,
        string nombre,
        EstatusCatalogo estatus = EstatusCatalogo.Activo,
        string? claveAw = null) : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("SUCURSAL_ID_INVALIDO", "El id es obligatorio.");
        ValidarClave(clave);
        ValidarNombre(nombre);
        ValidarClaveAw(claveAw);

        Clave = clave;
        Nombre = nombre;
        Estatus = estatus;
        ClaveAw = Normalizar(claveAw);
    }

    /// <summary>
    /// PATCH parcial sobre los campos editables de la sucursal
    /// (F-Admin-PR2.1). Convención: parámetro <c>null</c> = no tocar;
    /// <paramref name="limpiarClaveAw"/> desasocia la sucursal de A+W.
    /// Inmutable: <see cref="Clave"/> (business key).
    /// </summary>
    public void ActualizarDatos(
        string? nombre = null,
        string? claveAw = null,
        bool limpiarClaveAw = false,
        string? zonaHoraria = null)
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

        if (zonaHoraria is not null)
        {
            ValidarZonaHoraria(zonaHoraria);
            ZonaHoraria = zonaHoraria.Trim();
        }
    }

    /// <summary>
    /// Reactiva una sucursal (<see cref="EstatusCatalogo.Activo"/>).
    /// Idempotente.
    /// </summary>
    public void Activar() => Estatus = EstatusCatalogo.Activo;

    /// <summary>
    /// Desactiva la sucursal (<see cref="EstatusCatalogo.Inactivo"/>).
    /// La validación cross-entity (no desactivar si tiene almacenes
    /// activos, B.1) vive en el handler de F-Admin-PR2.3 porque
    /// requiere consultar otra tabla.
    /// </summary>
    public void Desactivar() => Estatus = EstatusCatalogo.Inactivo;

    /// <summary>
    /// Mueve a estado <see cref="EstatusCatalogo.EnRevision"/> para
    /// flujos internos (cutover, compliance) donde aplique. El endpoint
    /// público solo expone Activar/Desactivar.
    /// </summary>
    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;

    private static void ValidarClave(string clave)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("SUCURSAL_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 20 caracteres.");
    }

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("SUCURSAL_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 254 caracteres.");
    }

    private static void ValidarClaveAw(string? claveAw)
    {
        if (claveAw is not null
            && (string.IsNullOrWhiteSpace(claveAw) || claveAw.Trim().Length > 40))
            throw new BusinessRuleException("SUCURSAL_CLAVE_AW_INVALIDA",
                "La clave A+W no puede ser vacía ni exceder 40 caracteres.");
    }

    private static void ValidarZonaHoraria(string zonaHoraria)
    {
        var valor = zonaHoraria.Trim();
        if (string.IsNullOrWhiteSpace(valor) || valor.Length > 64)
            throw new BusinessRuleException("SUCURSAL_ZONA_HORARIA_INVALIDA",
                "La zona horaria es requerida y no puede exceder 64 caracteres.");
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(valor);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new BusinessRuleException("SUCURSAL_ZONA_HORARIA_INVALIDA",
                $"'{valor}' no es una zona horaria IANA reconocida (p.ej. America/Merida, America/Cancun).");
        }
    }

    private static string? Normalizar(string? claveAw) => claveAw?.Trim();
}
