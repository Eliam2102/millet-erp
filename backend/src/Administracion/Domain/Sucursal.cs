using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Administracion.Domain;

/// <summary>
/// Sucursal del catálogo <c>compartido.sucursales</c> (B.1). Representa
/// una unidad geográfica/operativa (CDMX, Monterrey, Querétaro, etc.).
/// Las RQs, OCs y movimientos de almacén referencian sucursales por
/// <see cref="BaseEntity.Id"/>.
///
/// <para>
/// F1-ADM-01: aislamiento multiempresa real vía <see cref="EmpresaId"/> +
/// <see cref="IPerteneceAEmpresa"/> — el global query filter de
/// <c>BaseDbContext</c> aplica automáticamente. <see cref="Clave"/> pasa
/// de única global a única por empresa; <see cref="ClaveAw"/> se mantiene
/// única global porque identifica la sucursal en el sistema A+W externo
/// (compartido entre empresas).
/// </para>
/// </summary>
public sealed class Sucursal : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; } // public set requerido por IPerteneceAEmpresa

    public string Clave { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;

    /// <summary>Clasificación operativa (F1-ADM-01, placeholder — ver <see cref="TipoSucursal"/>).</summary>
    public TipoSucursal Tipo { get; private set; } = TipoSucursal.Sucursal;

    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    /// <summary>
    /// Clave con la que A+W refiere esta sucursal en sus pedidos
    /// (columna <c>numero_sucursal</c> de <c>vw_erp_pedido_cabecera</c>,
    /// p.ej. "CONKAL"). La ingesta de pedidos (ADR-0048, flujo 2) resuelve
    /// la sucursal destino por este campo, case-insensitive. Única global
    /// (identifica la sucursal en A+W, sistema compartido entre empresas).
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

    // --- Domicilio operativo estructurado (F1-ADM-01) ---
    public string Calle { get; private set; } = string.Empty;
    public string NumeroExterior { get; private set; } = string.Empty;
    public string? NumeroInterior { get; private set; }
    public string Colonia { get; private set; } = string.Empty;
    public string Ciudad { get; private set; } = string.Empty;
    public string Municipio { get; private set; } = string.Empty;
    public string Estado { get; private set; } = string.Empty;
    public string CodigoPostal { get; private set; } = string.Empty;
    public string Pais { get; private set; } = string.Empty;

    /// <summary>
    /// Nombre del responsable de la sucursal, texto libre. Decisión de
    /// Fase 0: SIN FK a <see cref="Empleado"/> (evita dependencia
    /// circular Empleado ↔ Sucursal). <c>null</c> = sin capturar.
    /// </summary>
    public string? Responsable { get; private set; }

    /// <summary>Notas/ubicación adicional en texto libre. <c>null</c> = sin capturar.</summary>
    public string? InformacionUbicacion { get; private set; }

    private Sucursal() { }

    public Sucursal(
        Guid id,
        Guid empresaId,
        string clave,
        string nombre,
        TipoSucursal tipo,
        string calle,
        string numeroExterior,
        string colonia,
        string ciudad,
        string municipio,
        string estado,
        string codigoPostal,
        string pais,
        string? numeroInterior = null,
        string? responsable = null,
        string? informacionUbicacion = null,
        EstatusCatalogo estatus = EstatusCatalogo.Activo,
        string? claveAw = null,
        string? zonaHoraria = null) : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("SUCURSAL_ID_INVALIDO", "El id es obligatorio.");
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("SUCURSAL_EMPRESA_INVALIDA", "La empresa es obligatoria.");
        ValidarClave(clave);
        ValidarNombre(nombre);
        ValidarClaveAw(claveAw);
        ValidarCalle(calle);
        ValidarNumeroExterior(numeroExterior);
        ValidarNumeroInterior(numeroInterior);
        ValidarColonia(colonia);
        ValidarCiudad(ciudad);
        ValidarMunicipio(municipio);
        ValidarEstado(estado);
        ValidarCodigoPostal(codigoPostal);
        ValidarPais(pais);
        ValidarResponsable(responsable);
        var zona = zonaHoraria ?? ZonaHorariaDefault;
        ValidarZonaHoraria(zona);

        EmpresaId = empresaId;
        Clave = clave;
        Nombre = nombre;
        Tipo = tipo;
        Estatus = estatus;
        ClaveAw = Normalizar(claveAw);
        Calle = calle;
        NumeroExterior = numeroExterior;
        NumeroInterior = numeroInterior;
        Colonia = colonia;
        Ciudad = ciudad;
        Municipio = municipio;
        Estado = estado;
        CodigoPostal = codigoPostal;
        Pais = pais;
        Responsable = Normalizar(responsable);
        InformacionUbicacion = Normalizar(informacionUbicacion);
        ZonaHoraria = zona.Trim();
    }

    /// <summary>
    /// PATCH parcial sobre los campos editables de la sucursal
    /// (F-Admin-PR2.1). Convención: parámetro <c>null</c> = no tocar;
    /// <paramref name="limpiarClaveAw"/> desasocia la sucursal de A+W.
    /// Inmutables: <see cref="Clave"/> (business key) y
    /// <see cref="EmpresaId"/>.
    /// </summary>
    public void ActualizarDatos(
        string? nombre = null,
        TipoSucursal? tipo = null,
        string? claveAw = null,
        bool limpiarClaveAw = false,
        string? zonaHoraria = null,
        string? calle = null,
        string? numeroExterior = null,
        string? numeroInterior = null,
        bool limpiarNumeroInterior = false,
        string? colonia = null,
        string? ciudad = null,
        string? municipio = null,
        string? estado = null,
        string? codigoPostal = null,
        string? pais = null,
        string? responsable = null,
        bool limpiarResponsable = false,
        string? informacionUbicacion = null,
        bool limpiarInformacionUbicacion = false)
    {
        if (nombre is not null)
        {
            ValidarNombre(nombre);
            Nombre = nombre;
        }

        if (tipo is not null) Tipo = tipo.Value;

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

        if (calle is not null) { ValidarCalle(calle); Calle = calle; }
        if (numeroExterior is not null) { ValidarNumeroExterior(numeroExterior); NumeroExterior = numeroExterior; }
        if (limpiarNumeroInterior) NumeroInterior = null;
        else if (numeroInterior is not null) { ValidarNumeroInterior(numeroInterior); NumeroInterior = numeroInterior; }
        if (colonia is not null) { ValidarColonia(colonia); Colonia = colonia; }
        if (ciudad is not null) { ValidarCiudad(ciudad); Ciudad = ciudad; }
        if (municipio is not null) { ValidarMunicipio(municipio); Municipio = municipio; }
        if (estado is not null) { ValidarEstado(estado); Estado = estado; }
        if (codigoPostal is not null) { ValidarCodigoPostal(codigoPostal); CodigoPostal = codigoPostal; }
        if (pais is not null) { ValidarPais(pais); Pais = pais; }

        if (limpiarResponsable) Responsable = null;
        else if (responsable is not null) { ValidarResponsable(responsable); Responsable = Normalizar(responsable); }

        if (limpiarInformacionUbicacion) InformacionUbicacion = null;
        else if (informacionUbicacion is not null) InformacionUbicacion = Normalizar(informacionUbicacion);
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

    private static void ValidarCalle(string calle)
    {
        if (string.IsNullOrWhiteSpace(calle) || calle.Length > 254)
            throw new BusinessRuleException("SUCURSAL_CALLE_INVALIDA",
                "La calle es requerida y no puede exceder 254 caracteres.");
    }

    private static void ValidarNumeroExterior(string numeroExterior)
    {
        if (string.IsNullOrWhiteSpace(numeroExterior) || numeroExterior.Length > 20)
            throw new BusinessRuleException("SUCURSAL_NUMERO_EXTERIOR_INVALIDO",
                "El número exterior es requerido y no puede exceder 20 caracteres.");
    }

    private static void ValidarNumeroInterior(string? numeroInterior)
    {
        if (numeroInterior is { Length: > 20 })
            throw new BusinessRuleException("SUCURSAL_NUMERO_INTERIOR_INVALIDO",
                "El número interior no puede exceder 20 caracteres.");
    }

    private static void ValidarColonia(string colonia)
    {
        if (string.IsNullOrWhiteSpace(colonia) || colonia.Length > 254)
            throw new BusinessRuleException("SUCURSAL_COLONIA_INVALIDA",
                "La colonia es requerida y no puede exceder 254 caracteres.");
    }

    private static void ValidarCiudad(string ciudad)
    {
        if (string.IsNullOrWhiteSpace(ciudad) || ciudad.Length > 100)
            throw new BusinessRuleException("SUCURSAL_CIUDAD_INVALIDA",
                "La ciudad es requerida y no puede exceder 100 caracteres.");
    }

    private static void ValidarMunicipio(string municipio)
    {
        if (string.IsNullOrWhiteSpace(municipio) || municipio.Length > 100)
            throw new BusinessRuleException("SUCURSAL_MUNICIPIO_INVALIDO",
                "El municipio es requerido y no puede exceder 100 caracteres.");
    }

    private static void ValidarEstado(string estado)
    {
        if (string.IsNullOrWhiteSpace(estado) || estado.Length > 100)
            throw new BusinessRuleException("SUCURSAL_ESTADO_INVALIDO",
                "El estado es requerido y no puede exceder 100 caracteres.");
    }

    private static void ValidarCodigoPostal(string codigoPostal)
    {
        if (codigoPostal.Length != 5 || !codigoPostal.All(char.IsAsciiDigit))
            throw new BusinessRuleException("SUCURSAL_CODIGO_POSTAL_INVALIDO",
                "El código postal debe ser de 5 dígitos (catálogo c_CodigoPostal del SAT).");
    }

    private static void ValidarPais(string pais)
    {
        if (string.IsNullOrWhiteSpace(pais) || pais.Length > 100)
            throw new BusinessRuleException("SUCURSAL_PAIS_INVALIDO",
                "El país es requerido y no puede exceder 100 caracteres.");
    }

    private static void ValidarResponsable(string? responsable)
    {
        if (responsable is { Length: > 254 })
            throw new BusinessRuleException("SUCURSAL_RESPONSABLE_INVALIDO",
                "El nombre del responsable no puede exceder 254 caracteres.");
    }

    private static string? Normalizar(string? claveAw) => claveAw?.Trim();
}
