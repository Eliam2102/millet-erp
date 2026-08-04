using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.DatosMaestros.Domain;

/// <summary>
/// Cliente del catálogo cross-empresa <c>compartido.clientes</c> (ADR-0048
/// D6). Master único de clientes del ERP; primer consumidor: Facturación
/// (datos fiscales del receptor CFDI vía <c>IClientesReadPort</c>). Los
/// clientes <b>nacen desde A+W</b> por auto-provisión al ingestar un pedido
/// (<see cref="OrigenMaster.Aw"/> + <see cref="ReferenciaExterna"/> =
/// <c>numero_cliente</c>); también se capturan a mano en
/// <c>/admin/datos-maestros/clientes</c>.
///
/// <para>
/// Regla clave (levantamiento Facturación §5.1): datos fiscales incompletos
/// (RFC/régimen/CP en <c>null</c>) <b>no bloquean el alta</b> — bloquean el
/// timbrado. Por eso todos los atributos fiscales son nullable y la
/// validación dura vive en la emisión del CFDI, no aquí.
/// </para>
/// </summary>
public sealed class Cliente : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = string.Empty;

    /// <summary>
    /// Llave natural del cliente en el sistema origen (p.ej.
    /// <c>numero_cliente</c>/<c>KU_KUNDEN.ID</c> de A+W). UNIQUE cuando no es
    /// null — es la correlación que usa la auto-provisión (upsert idempotente).
    /// </summary>
    public string? ReferenciaExterna { get; private set; }

    public string RazonSocial { get; private set; } = string.Empty;
    public string? Rfc { get; private set; }

    /// <summary>Código SAT c_RegimenFiscal (3 dígitos), p.ej. "601".</summary>
    public string? RegimenFiscal { get; private set; }

    /// <summary>CP del domicilio fiscal (5 dígitos) — receptor CFDI 4.0.</summary>
    public string? CodigoPostalFiscal { get; private set; }

    public string? UsoCfdiDefault { get; private set; }
    public string? FormaPagoDefault { get; private set; }
    public string? MetodoPagoDefault { get; private set; }
    public string MonedaDefault { get; private set; } = "MXN";

    /// <summary>Público en general (XAXX010101000) — mostrador sin datos.</summary>
    public bool EsGenerico { get; private set; }

    // ── Receptor extranjero (Comercio Exterior / CCE) ───────────────────
    // Datos del receptor cuando el cliente es de exportación. Prellenan el
    // encabezado del CCE (cero captura a mano). Fuente primaria = A+W
    // (vw_erp_cliente ya manda país+domicilio; NumRegIdTrib es gap A+W); si
    // A+W no lo trae, el operador los captura en Datos Maestros. Nullable:
    // solo aplican a clientes de exportación.

    /// <summary>Tax ID / registro de identificación tributaria extranjero (NumRegIdTrib).</summary>
    public string? NumRegIdTrib { get; private set; }

    /// <summary>País de residencia fiscal del receptor (ISO 3166-1 alfa-3, p.ej. USA).</summary>
    public string? PaisResidencia { get; private set; }

    public string? DomicilioExtranjeroCalle { get; private set; }
    public string? DomicilioExtranjeroEstado { get; private set; }
    public string? DomicilioExtranjeroCodigoPostal { get; private set; }

    public OrigenMaster Origen { get; private set; } = OrigenMaster.Manual;
    public string? Email { get; private set; }
    public string? Telefono { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private Cliente() { }

    public Cliente(
        Guid id,
        string clave,
        string razonSocial,
        OrigenMaster origen = OrigenMaster.Manual,
        EstatusCatalogo estatus = EstatusCatalogo.Activo,
        string? referenciaExterna = null,
        string? rfc = null,
        string? regimenFiscal = null,
        string? codigoPostalFiscal = null,
        string? usoCfdiDefault = null,
        string? formaPagoDefault = null,
        string? metodoPagoDefault = null,
        string monedaDefault = "MXN",
        bool esGenerico = false,
        string? email = null,
        string? telefono = null,
        string? numRegIdTrib = null,
        string? paisResidencia = null,
        string? domicilioExtranjeroCalle = null,
        string? domicilioExtranjeroEstado = null,
        string? domicilioExtranjeroCodigoPostal = null) : base(id)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("CLIENTE_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 20 caracteres.");
        if (string.IsNullOrWhiteSpace(razonSocial) || razonSocial.Length > 254)
            throw new BusinessRuleException("CLIENTE_RAZON_SOCIAL_INVALIDA",
                "La razón social es requerida y no puede exceder 254 caracteres.");
        ValidarFiscales(rfc, regimenFiscal, codigoPostalFiscal, usoCfdiDefault,
            formaPagoDefault, metodoPagoDefault, monedaDefault);
        ValidarReceptorExtranjero(numRegIdTrib, paisResidencia,
            domicilioExtranjeroCalle, domicilioExtranjeroEstado, domicilioExtranjeroCodigoPostal);
        if (referenciaExterna is { Length: > 50 })
            throw new BusinessRuleException("CLIENTE_REFERENCIA_INVALIDA",
                "La referencia externa no puede exceder 50 caracteres.");

        Clave = clave;
        ReferenciaExterna = referenciaExterna;
        RazonSocial = razonSocial;
        Rfc = rfc;
        RegimenFiscal = regimenFiscal;
        CodigoPostalFiscal = codigoPostalFiscal;
        UsoCfdiDefault = usoCfdiDefault;
        FormaPagoDefault = formaPagoDefault;
        MetodoPagoDefault = metodoPagoDefault;
        MonedaDefault = monedaDefault;
        EsGenerico = esGenerico;
        Origen = origen;
        Email = email;
        Telefono = telefono;
        NumRegIdTrib = numRegIdTrib;
        PaisResidencia = paisResidencia;
        DomicilioExtranjeroCalle = domicilioExtranjeroCalle;
        DomicilioExtranjeroEstado = domicilioExtranjeroEstado;
        DomicilioExtranjeroCodigoPostal = domicilioExtranjeroCodigoPostal;
        Estatus = estatus;
    }

    /// <summary>
    /// PATCH parcial sobre los campos editables (mismo contrato que
    /// <see cref="Proveedor.ActualizarDatos"/>): <c>null</c> = no tocar;
    /// <c>limpiarX = true</c> = setear nullable a null. Inmutables: `Id`,
    /// `Clave`, `ReferenciaExterna` (correlación con el origen) y `Origen`.
    /// </summary>
    public void ActualizarDatos(
        string? razonSocial = null,
        string? rfc = null,
        string? regimenFiscal = null,
        string? codigoPostalFiscal = null,
        string? usoCfdiDefault = null,
        string? formaPagoDefault = null,
        string? metodoPagoDefault = null,
        string? monedaDefault = null,
        bool? esGenerico = null,
        string? email = null,
        string? telefono = null,
        bool limpiarRfc = false,
        bool limpiarRegimenFiscal = false,
        bool limpiarCodigoPostalFiscal = false,
        bool limpiarUsoCfdiDefault = false,
        bool limpiarFormaPagoDefault = false,
        bool limpiarMetodoPagoDefault = false,
        bool limpiarEmail = false,
        bool limpiarTelefono = false)
    {
        if (razonSocial is not null)
        {
            if (razonSocial.Length is 0 or > 254)
                throw new BusinessRuleException("CLIENTE_RAZON_SOCIAL_INVALIDA",
                    "La razón social es requerida y no puede exceder 254 caracteres.");
            RazonSocial = razonSocial;
        }

        ValidarFiscales(
            rfc, regimenFiscal, codigoPostalFiscal, usoCfdiDefault,
            formaPagoDefault, metodoPagoDefault, monedaDefault ?? MonedaDefault);

        if (rfc is not null) Rfc = rfc;
        else if (limpiarRfc) Rfc = null;

        if (regimenFiscal is not null) RegimenFiscal = regimenFiscal;
        else if (limpiarRegimenFiscal) RegimenFiscal = null;

        if (codigoPostalFiscal is not null) CodigoPostalFiscal = codigoPostalFiscal;
        else if (limpiarCodigoPostalFiscal) CodigoPostalFiscal = null;

        if (usoCfdiDefault is not null) UsoCfdiDefault = usoCfdiDefault;
        else if (limpiarUsoCfdiDefault) UsoCfdiDefault = null;

        if (formaPagoDefault is not null) FormaPagoDefault = formaPagoDefault;
        else if (limpiarFormaPagoDefault) FormaPagoDefault = null;

        if (metodoPagoDefault is not null) MetodoPagoDefault = metodoPagoDefault;
        else if (limpiarMetodoPagoDefault) MetodoPagoDefault = null;

        if (monedaDefault is not null) MonedaDefault = monedaDefault;

        if (esGenerico is bool g) EsGenerico = g;

        if (email is not null) Email = email;
        else if (limpiarEmail) Email = null;

        if (telefono is not null) Telefono = telefono;
        else if (limpiarTelefono) Telefono = null;
    }

    /// <summary>
    /// Completa/corrige los datos del receptor extranjero (CCE). <c>null</c> =
    /// no tocar; <c>limpiarX</c> = borrar. Separado de <see cref="ActualizarDatos"/>
    /// (datos fiscales MX) — mismo patrón que <c>ProductoAw.AsignarDatosAduana</c>.
    /// </summary>
    public void AsignarDatosReceptorExtranjero(
        string? numRegIdTrib = null,
        string? paisResidencia = null,
        string? domicilioExtranjeroCalle = null,
        string? domicilioExtranjeroEstado = null,
        string? domicilioExtranjeroCodigoPostal = null,
        bool limpiarNumRegIdTrib = false,
        bool limpiarPaisResidencia = false,
        bool limpiarDomicilioExtranjeroCalle = false,
        bool limpiarDomicilioExtranjeroEstado = false,
        bool limpiarDomicilioExtranjeroCodigoPostal = false)
    {
        ValidarReceptorExtranjero(numRegIdTrib, paisResidencia,
            domicilioExtranjeroCalle, domicilioExtranjeroEstado, domicilioExtranjeroCodigoPostal);

        if (numRegIdTrib is not null) NumRegIdTrib = numRegIdTrib;
        else if (limpiarNumRegIdTrib) NumRegIdTrib = null;

        if (paisResidencia is not null) PaisResidencia = paisResidencia;
        else if (limpiarPaisResidencia) PaisResidencia = null;

        if (domicilioExtranjeroCalle is not null) DomicilioExtranjeroCalle = domicilioExtranjeroCalle;
        else if (limpiarDomicilioExtranjeroCalle) DomicilioExtranjeroCalle = null;

        if (domicilioExtranjeroEstado is not null) DomicilioExtranjeroEstado = domicilioExtranjeroEstado;
        else if (limpiarDomicilioExtranjeroEstado) DomicilioExtranjeroEstado = null;

        if (domicilioExtranjeroCodigoPostal is not null) DomicilioExtranjeroCodigoPostal = domicilioExtranjeroCodigoPostal;
        else if (limpiarDomicilioExtranjeroCodigoPostal) DomicilioExtranjeroCodigoPostal = null;
    }

    /// <summary>
    /// ¿El cliente puede ser receptor de un CFDI nominal? (RFC + régimen +
    /// CP fiscal presentes). La emisión valida esto antes de timbrar.
    /// </summary>
    public bool DatosFiscalesCompletos =>
        !string.IsNullOrWhiteSpace(Rfc)
        && !string.IsNullOrWhiteSpace(RegimenFiscal)
        && !string.IsNullOrWhiteSpace(CodigoPostalFiscal);

    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;

    private static void ValidarFiscales(
        string? rfc, string? regimenFiscal, string? codigoPostalFiscal,
        string? usoCfdiDefault, string? formaPagoDefault, string? metodoPagoDefault,
        string? monedaDefault)
    {
        if (rfc is { Length: not (12 or 13) })
            throw new BusinessRuleException("CLIENTE_RFC_INVALIDO",
                "El RFC debe tener 12 (moral) o 13 (física) caracteres.");
        if (regimenFiscal is { Length: not 3 })
            throw new BusinessRuleException("CLIENTE_REGIMEN_INVALIDO",
                "El régimen fiscal debe ser código SAT de 3 dígitos.");
        if (codigoPostalFiscal is { Length: not 5 })
            throw new BusinessRuleException("CLIENTE_CP_FISCAL_INVALIDO",
                "El CP fiscal debe tener 5 dígitos.");
        if (usoCfdiDefault is { Length: > 4 })
            throw new BusinessRuleException("CLIENTE_USO_CFDI_INVALIDO",
                "El uso CFDI debe ser clave SAT de hasta 4 caracteres.");
        if (formaPagoDefault is { Length: not 2 })
            throw new BusinessRuleException("CLIENTE_FORMA_PAGO_INVALIDA",
                "La forma de pago debe ser clave SAT de 2 dígitos.");
        if (metodoPagoDefault is not (null or "PUE" or "PPD"))
            throw new BusinessRuleException("CLIENTE_METODO_PAGO_INVALIDO",
                "El método de pago debe ser PUE o PPD.");
        if (monedaDefault is not { Length: 3 })
            throw new BusinessRuleException("CLIENTE_MONEDA_INVALIDA",
                "La moneda default debe ser código ISO 4217 (3 letras).");
    }

    private static void ValidarReceptorExtranjero(
        string? numRegIdTrib, string? paisResidencia,
        string? calle, string? estado, string? codigoPostal)
    {
        if (numRegIdTrib is { Length: 0 or > 40 })
            throw new BusinessRuleException("CLIENTE_NUM_REG_ID_TRIB_INVALIDO",
                "El registro de identificación tributaria extranjero no puede exceder 40 caracteres.");
        if (paisResidencia is not null
            && !System.Text.RegularExpressions.Regex.IsMatch(paisResidencia, "^[A-Za-z]{3}$"))
            throw new BusinessRuleException("CLIENTE_PAIS_RESIDENCIA_INVALIDO",
                "El país de residencia debe ser clave SAT c_Pais (ISO 3166-1 alfa-3, 3 letras).");
        if (calle is { Length: > 200 })
            throw new BusinessRuleException("CLIENTE_DOMICILIO_EXT_INVALIDO",
                "La calle del domicilio extranjero no puede exceder 200 caracteres.");
        if (estado is { Length: > 100 })
            throw new BusinessRuleException("CLIENTE_DOMICILIO_EXT_INVALIDO",
                "El estado del domicilio extranjero no puede exceder 100 caracteres.");
        if (codigoPostal is { Length: > 12 })
            throw new BusinessRuleException("CLIENTE_DOMICILIO_EXT_INVALIDO",
                "El código postal del domicilio extranjero no puede exceder 12 caracteres.");
    }
}
