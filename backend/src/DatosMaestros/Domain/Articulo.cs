using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.DatosMaestros.Domain;

/// <summary>
/// Artículo del catálogo cross-empresa <c>compartido.articulos</c>
/// (F7-PR1). No-producción: lo consume Compras para captura de líneas
/// de requisición; en fases posteriores Almacén no-prod, Contabilidad,
/// Activos Fijos. Los artículos de producción (vidrio crudo, etc.)
/// los maneja A+W externamente.
///
/// <para>
/// La <see cref="Naturaleza"/> es atributo del artículo desde la
/// perspectiva de Compras (alimenta la matriz de aprobación A1).
/// Default <c>Estandar</c>; el cliente reclasifica post-go-live.
/// </para>
/// </summary>
public sealed class Articulo : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = string.Empty;
    public string? ClaveLegacy { get; private set; }
    public string Nombre { get; private set; } = string.Empty;
    public string? DescripcionLarga { get; private set; }
    public string UnidadMedidaDefault { get; private set; } = string.Empty;

    /// <summary>
    /// FK al catálogo <c>compartido.unidades_medida</c> (ADR-0046 Etapa 1b).
    /// <b>Nullable</b> durante la transición: artículos legacy (empaque
    /// variable o sin mapeo claro) quedan <c>NULL</c> y siguen operando con
    /// <see cref="UnidadMedidaDefault"/>. Cuando está seteado,
    /// <see cref="UnidadMedidaDefault"/> = código de la unidad (el snapshot
    /// que copian las líneas de RQ/OC/movimiento). Se asigna vía
    /// <see cref="AsignarUnidadMedida"/>.
    /// </summary>
    public Guid? UnidadMedidaId { get; private set; }

    public Naturaleza Naturaleza { get; private set; } = Naturaleza.Estandar;
    public string? Categoria { get; private set; }

    /// <summary>
    /// FK al catálogo <c>compartido.categorias_articulo</c> (patrón ADR-0046,
    /// PR2). <b>Nullable</b> durante la transición: los artículos no
    /// reconciliados (PR3) quedan <c>NULL</c> y siguen operando con el string
    /// legacy <see cref="Categoria"/>. Cuando está seteado,
    /// <see cref="Categoria"/> = nombre de la categoría. Se asigna vía
    /// <see cref="AsignarCategoria"/> (espejo de <see cref="AsignarUnidadMedida"/>).
    /// </summary>
    public Guid? CategoriaId { get; private set; }

    public decimal? PrecioReferenciaMonto { get; private set; }
    public string? PrecioReferenciaMoneda { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private Articulo() { }

    public Articulo(
        Guid id,
        string clave,
        string nombre,
        string unidadMedidaDefault,
        Naturaleza naturaleza = Naturaleza.Estandar,
        EstatusCatalogo estatus = EstatusCatalogo.Activo,
        string? claveLegacy = null,
        string? descripcionLarga = null,
        string? categoria = null,
        decimal? precioReferenciaMonto = null,
        string? precioReferenciaMoneda = null,
        Guid? unidadMedidaId = null,
        Guid? categoriaId = null) : base(id)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("ARTICULO_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 20 caracteres.");
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 254)
            throw new BusinessRuleException("ARTICULO_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 254 caracteres.");
        if (string.IsNullOrWhiteSpace(unidadMedidaDefault) || unidadMedidaDefault.Length > 20)
            throw new BusinessRuleException("ARTICULO_UNIDAD_MEDIDA_INVALIDA",
                "La unidad de medida es requerida y no puede exceder 20 caracteres.");
        if (precioReferenciaMonto < 0)
            throw new BusinessRuleException("ARTICULO_PRECIO_NEGATIVO",
                "El precio de referencia no puede ser negativo.");
        if (precioReferenciaMonto.HasValue && string.IsNullOrWhiteSpace(precioReferenciaMoneda))
            throw new BusinessRuleException("ARTICULO_PRECIO_SIN_MONEDA",
                "Si se provee precio, la moneda es requerida (ISO 4217).");
        if (precioReferenciaMoneda is { Length: not 3 })
            throw new BusinessRuleException("ARTICULO_MONEDA_INVALIDA",
                "La moneda debe ser código ISO 4217 (3 letras).");

        Clave = clave;
        ClaveLegacy = claveLegacy;
        Nombre = nombre;
        DescripcionLarga = descripcionLarga;
        UnidadMedidaDefault = unidadMedidaDefault;
        Naturaleza = naturaleza;
        Categoria = categoria;
        PrecioReferenciaMonto = precioReferenciaMonto;
        PrecioReferenciaMoneda = precioReferenciaMoneda;
        Estatus = estatus;
        UnidadMedidaId = unidadMedidaId;
        CategoriaId = categoriaId;
    }

    /// <summary>
    /// Asigna la unidad de medida del catálogo (ADR-0046 Etapa 1b): setea el
    /// FK <see cref="UnidadMedidaId"/> y sincroniza
    /// <see cref="UnidadMedidaDefault"/> con el código de la unidad, para que
    /// las líneas que snapshotean el string sigan funcionando sin cambios.
    /// </summary>
    public void AsignarUnidadMedida(Guid unidadMedidaId, string codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Length > 20)
            throw new BusinessRuleException("ARTICULO_UNIDAD_MEDIDA_INVALIDA",
                "El código de la unidad es requerido y no puede exceder 20 caracteres.");
        UnidadMedidaId = unidadMedidaId;
        UnidadMedidaDefault = codigo;
    }

    /// <summary>
    /// Asigna la categoría del catálogo (ADR-0046, PR2): setea el FK
    /// <see cref="CategoriaId"/> y sincroniza el string legacy
    /// <see cref="Categoria"/> con el nombre de la categoría. Espejo exacto de
    /// <see cref="AsignarUnidadMedida"/>.
    /// </summary>
    public void AsignarCategoria(Guid categoriaId, string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 100)
            throw new BusinessRuleException("ARTICULO_CATEGORIA_INVALIDA",
                "El nombre de la categoría es requerido y no puede exceder 100 caracteres.");
        CategoriaId = categoriaId;
        Categoria = nombre;
    }

    /// <summary>
    /// Deselecciona la categoría: FK y string legacy a <c>null</c> (cuando el
    /// usuario limpia el selector).
    /// </summary>
    public void LimpiarCategoria()
    {
        CategoriaId = null;
        Categoria = null;
    }

    /// <summary>
    /// PATCH parcial sobre los campos editables del artículo (B.5).
    /// Convención: parámetro <c>null</c> = no tocar; flag
    /// <c>limpiarX</c> = setear nullable a null. El cambio individual
    /// de <see cref="Naturaleza"/> es complementario al endpoint bulk
    /// de F9-PR1 (reclasificar-naturaleza).
    /// </summary>
    public void ActualizarDatos(
        string? nombre = null,
        string? descripcionLarga = null,
        string? unidadMedidaDefault = null,
        Naturaleza? naturaleza = null,
        string? categoria = null,
        decimal? precioReferenciaMonto = null,
        string? precioReferenciaMoneda = null,
        bool limpiarDescripcionLarga = false,
        bool limpiarCategoria = false,
        bool limpiarPrecioReferencia = false)
    {
        if (nombre is not null)
        {
            if (nombre.Length is 0 or > 254)
                throw new BusinessRuleException("ARTICULO_NOMBRE_INVALIDO",
                    "El nombre es requerido y no puede exceder 254 caracteres.");
            Nombre = nombre;
        }
        if (unidadMedidaDefault is not null)
        {
            if (unidadMedidaDefault.Length is 0 or > 20)
                throw new BusinessRuleException("ARTICULO_UNIDAD_MEDIDA_INVALIDA",
                    "La unidad de medida es requerida y no puede exceder 20 caracteres.");
            UnidadMedidaDefault = unidadMedidaDefault;
        }
        if (naturaleza is Naturaleza n) Naturaleza = n;

        if (descripcionLarga is not null) DescripcionLarga = descripcionLarga;
        else if (limpiarDescripcionLarga) DescripcionLarga = null;

        if (categoria is not null) Categoria = categoria;
        else if (limpiarCategoria) Categoria = null;

        // Precio: invariantes del constructor — monto y moneda van juntos.
        if (limpiarPrecioReferencia)
        {
            PrecioReferenciaMonto = null;
            PrecioReferenciaMoneda = null;
        }
        else if (precioReferenciaMonto.HasValue || precioReferenciaMoneda is not null)
        {
            var nuevoMonto = precioReferenciaMonto ?? PrecioReferenciaMonto;
            var nuevaMoneda = precioReferenciaMoneda ?? PrecioReferenciaMoneda;

            if (nuevoMonto < 0)
                throw new BusinessRuleException("ARTICULO_PRECIO_NEGATIVO",
                    "El precio de referencia no puede ser negativo.");
            if (nuevoMonto.HasValue && string.IsNullOrWhiteSpace(nuevaMoneda))
                throw new BusinessRuleException("ARTICULO_PRECIO_SIN_MONEDA",
                    "Si se provee precio, la moneda es requerida (ISO 4217).");
            if (nuevaMoneda is { Length: not 3 })
                throw new BusinessRuleException("ARTICULO_MONEDA_INVALIDA",
                    "La moneda debe ser código ISO 4217 (3 letras).");

            PrecioReferenciaMonto = nuevoMonto;
            PrecioReferenciaMoneda = nuevaMoneda;
        }
    }

    /// <summary>
    /// Cambia el estatus del artículo (B.5). Endpoint público expone
    /// Activo/Inactivo; <c>EnRevision</c> queda para flujos internos.
    /// </summary>
    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;
}
