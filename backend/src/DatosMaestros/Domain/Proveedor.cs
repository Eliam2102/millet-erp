using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.DatosMaestros.Domain;

/// <summary>
/// Proveedor del catálogo cross-empresa <c>compartido.proveedores</c>
/// (F7-PR1). No-producción: los proveedores que registran requisiciones
/// (Compras), pagos (Cuentas por Pagar), activos (Activos Fijos), etc.
/// Los proveedores de producción (vidrio en bruto, materiales) los
/// maneja A+W externamente.
///
/// <para>
/// El shape inicial es razonable y ampliable: cuando llegue el export
/// de SAP del cliente (F7-PR2), se agregan columnas vía migración
/// aditiva. Lo mínimo cubre lo que necesita Compras para captura de
/// requisiciones (clave, RFC, condiciones de pago, estatus).
/// </para>
/// </summary>
public sealed class Proveedor : BaseEntity, IAuditable
{
    public string Clave { get; private set; } = string.Empty;
    public string? ClaveLegacy { get; private set; }
    public string? OrigenCarga { get; private set; }
    public string RazonSocial { get; private set; } = string.Empty;
    public string? NombreComercial { get; private set; }
    public string Rfc { get; private set; } = string.Empty;
    public TipoPersonaProveedor TipoPersona { get; private set; }
    public short? CondicionesPagoDias { get; private set; }
    public Guid? MonedaPreferidaId { get; private set; }
    public string? Email { get; private set; }
    public string? Telefono { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.EnRevision;

    /// <summary>Diferencia máxima factura contra OC en MXN; null usa el parámetro general.</summary>
    public decimal? ToleranciaFacturaContraOcMxn { get; private set; }

    public void ActualizarToleranciaFacturaContraOc(decimal? monto)
    {
        if (monto < 0)
            throw new BusinessRuleException("PROVEEDOR_TOLERANCIA_NEGATIVA",
                "La tolerancia factura contra OC no puede ser negativa.");
        if (monto > 99999999999999.9999m || (monto.HasValue && decimal.Round(monto.Value, 4) != monto.Value))
            throw new BusinessRuleException("PROVEEDOR_TOLERANCIA_PRECISION",
                "La tolerancia admite hasta 14 enteros y 4 decimales, igual que la foto en la factura.");
        ToleranciaFacturaContraOcMxn = monto;
    }

    // ----- Validación CxP y Expediente (F1-ADM-05 G1.1) -----
    /// <summary>Usuario de CxP o Admin que validó o rechazó al proveedor.</summary>
    public Guid? ValidadoPorId { get; private set; }
    /// <summary>Fecha y hora de la validación o rechazo.</summary>
    public DateTimeOffset? ValidadoEn { get; private set; }
    /// <summary>Motivo obligatorio en caso de rechazo del proveedor.</summary>
    public string? MotivoRechazo { get; private set; }
    /// <summary>Versión de la matriz del expediente con la que se validó (86e3pdmee · ADM-01).</summary>
    public int? ValidadoConMatrizVersion { get; private set; }

    // Datos bancarios: varias cuentas por proveedor en CuentaBancariaProveedor (86e3pdmee · ADM-02).

    // ----- Clasificación fiscal (86e3pdmee · ADM-01) -----
    // Null = «por revisar» (proveedores heredados sin clasificar; no se inventan datos).
    public OrigenProveedor? Origen { get; private set; }
    /// <summary>Código SAT de régimen fiscal (3 dígitos, catálogo compartido); null en extranjeros o por revisar.</summary>
    public string? RegimenFiscalCodigo { get; private set; }
    /// <summary>
    /// Concepto de retención esperado (catálogo de retenciones de CxP, o
    /// <c>SIN_RETENCION</c>). CxP lo toma como predeterminado al capturar y
    /// compara el XML contra él (decisión del owner, 17-oct).
    /// </summary>
    public string? ConceptoRetencion { get; private set; }

    public string? Calle { get; private set; }
    public string? NumeroExterior { get; private set; }
    public string? NumeroInterior { get; private set; }
    public string? Colonia { get; private set; }
    public string? Ciudad { get; private set; }
    public string? Municipio { get; private set; }
    public string? Estado { get; private set; }
    public string? CodigoPostal { get; private set; }
    public string? Pais { get; private set; }
    public string? TaxId { get; private set; }

    /// <summary>
    /// Clave del proveedor en A+W (86e3wyjz3 · ALM-APP-1). La app de Almacenes manda el
    /// proveedor con esta clave y la OC regresa con la misma, porque de ella A+W toma la moneda.
    /// Única entre proveedores cuando existe.
    /// </summary>
    public int? ClaveAw { get; private set; }

    public void AsignarClaveAw(int? claveAw)
    {
        if (claveAw <= 0)
            throw new BusinessRuleException("PROVEEDOR_CLAVE_AW_INVALIDA", "La clave de A+W es un número entero mayor que cero.");
        ClaveAw = claveAw;
    }

    public void ActualizarDomicilioFiscal(string? calle = null,
        string? numeroExterior = null,
        string? numeroInterior = null,
        string? colonia = null,
        string? ciudad = null,
        string? municipio = null,
        string? estado = null,
        string? codigoPostal = null,
        string? pais = null,
        string? taxId = null)
    {
        if (calle is { Length: > 254 }) throw new BusinessRuleException("PROVEEDOR_DOMICILIO_INVALIDO", "El campo calle admite hasta 254 caracteres; corrígelo antes de guardar.");
        if (numeroExterior is { Length: > 20 }) throw new BusinessRuleException("PROVEEDOR_DOMICILIO_INVALIDO", "El campo número exterior admite hasta 20 caracteres; corrígelo antes de guardar.");
        if (numeroInterior is { Length: > 20 }) throw new BusinessRuleException("PROVEEDOR_DOMICILIO_INVALIDO", "El campo número interior admite hasta 20 caracteres; corrígelo antes de guardar.");
        if (colonia is { Length: > 254 }) throw new BusinessRuleException("PROVEEDOR_DOMICILIO_INVALIDO", "El campo colonia admite hasta 254 caracteres; corrígelo antes de guardar.");
        if (ciudad is { Length: > 100 }) throw new BusinessRuleException("PROVEEDOR_DOMICILIO_INVALIDO", "El campo ciudad admite hasta 100 caracteres; corrígelo antes de guardar.");
        if (municipio is { Length: > 100 }) throw new BusinessRuleException("PROVEEDOR_DOMICILIO_INVALIDO", "El campo municipio admite hasta 100 caracteres; corrígelo antes de guardar.");
        if (estado is { Length: > 100 }) throw new BusinessRuleException("PROVEEDOR_DOMICILIO_INVALIDO", "El campo estado admite hasta 100 caracteres; corrígelo antes de guardar.");
        if (codigoPostal is { Length: > 20 }) throw new BusinessRuleException("PROVEEDOR_DOMICILIO_INVALIDO", "El campo código postal admite hasta 20 caracteres; corrígelo antes de guardar.");
        if (pais is { Length: > 3 }) throw new BusinessRuleException("PROVEEDOR_DOMICILIO_INVALIDO", "El campo país admite hasta 3 caracteres; corrígelo antes de guardar.");
        if (taxId is { Length: > 40 }) throw new BusinessRuleException("PROVEEDOR_TAXID_INVALIDO", "El campo Tax ID admite hasta 40 caracteres; corrígelo antes de guardar.");
        if (!string.IsNullOrWhiteSpace(pais) && (pais.Length != 3 || !pais.All(char.IsAsciiLetter)))
            throw new BusinessRuleException("PROVEEDOR_PAIS_INVALIDO", "Escribe el país con su código ISO de tres letras, por ejemplo MEX.");
        if (!string.IsNullOrWhiteSpace(taxId) && Origen != OrigenProveedor.Extranjero)
            throw new BusinessRuleException("PROVEEDOR_TAXID_SOLO_EXTRANJERO", "El Tax ID solo corresponde a proveedores extranjeros; quítalo para guardar.");
        if (calle is not null) Calle = string.IsNullOrWhiteSpace(calle) ? null : calle.Trim();
        if (numeroExterior is not null) NumeroExterior = string.IsNullOrWhiteSpace(numeroExterior) ? null : numeroExterior.Trim();
        if (numeroInterior is not null) NumeroInterior = string.IsNullOrWhiteSpace(numeroInterior) ? null : numeroInterior.Trim();
        if (colonia is not null) Colonia = string.IsNullOrWhiteSpace(colonia) ? null : colonia.Trim();
        if (ciudad is not null) Ciudad = string.IsNullOrWhiteSpace(ciudad) ? null : ciudad.Trim();
        if (municipio is not null) Municipio = string.IsNullOrWhiteSpace(municipio) ? null : municipio.Trim();
        if (estado is not null) Estado = string.IsNullOrWhiteSpace(estado) ? null : estado.Trim();
        if (codigoPostal is not null) CodigoPostal = string.IsNullOrWhiteSpace(codigoPostal) ? null : codigoPostal.Trim();
        if (pais is not null) Pais = string.IsNullOrWhiteSpace(pais) ? (Origen == OrigenProveedor.Nacional ? "MEX" : null) : pais.Trim().ToUpperInvariant();
        if (taxId is not null) TaxId = string.IsNullOrWhiteSpace(taxId) ? null : taxId.Trim();
    }

    public void ActualizarClasificacionFiscal(OrigenProveedor? origen, string? regimenFiscalCodigo, string? conceptoRetencion)
    {
        var regimen = string.IsNullOrWhiteSpace(regimenFiscalCodigo) ? null : regimenFiscalCodigo.Trim();
        if (regimen is not null && (regimen.Length != 3 || !regimen.All(char.IsAsciiDigit)))
            throw new BusinessRuleException("PROVEEDOR_REGIMEN_INVALIDO", "El régimen fiscal es el código SAT de 3 dígitos.");
        if (origen == OrigenProveedor.Nacional && regimen is null)
            throw new BusinessRuleException("PROVEEDOR_REGIMEN_REQUERIDO", "Un proveedor nacional requiere régimen fiscal.");
        var concepto = string.IsNullOrWhiteSpace(conceptoRetencion) ? null : conceptoRetencion.Trim().ToUpperInvariant();
        if (concepto?.Length > 80)
            throw new BusinessRuleException("PROVEEDOR_CONCEPTO_RETENCION_INVALIDO", "El concepto de retención admite hasta 80 caracteres.");
        if (origen != OrigenProveedor.Extranjero) TaxId = null;
        if (origen == OrigenProveedor.Nacional && Pais is null) Pais = "MEX";
        Origen = origen;
        RegimenFiscalCodigo = origen == OrigenProveedor.Extranjero ? null : regimen;
        ConceptoRetencion = concepto;
    }


    /// <summary>
    /// RFC genéricos SAT ("público en general" nacional/extranjero). Se
    /// excluyen de la unicidad de <see cref="Rfc"/>: múltiples proveedores
    /// legítimos comparten estos valores.
    /// </summary>
    public static readonly string[] RfcGenericos = ["XAXX010101000", "XEXX010101000"];

    /// <summary>RFC genérico para proveedores extranjeros, que no tienen RFC (86e3pdmee).</summary>
    public const string RfcGenericoExtranjero = "XEXX010101000";

    public static bool EsRfcGenerico(string rfc) =>
        RfcGenericos.Contains(rfc.Trim().ToUpperInvariant());

    private Proveedor() { }

    public Proveedor(
        Guid id,
        string clave,
        string razonSocial,
        string rfc,
        TipoPersonaProveedor tipoPersona,
        EstatusCatalogo estatus = EstatusCatalogo.EnRevision,
        string? claveLegacy = null,
        string? nombreComercial = null,
        short? condicionesPagoDias = null,
        Guid? monedaPreferidaId = null,
        string? email = null,
        string? telefono = null,
        string? origenCarga = null) : base(id)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("PROVEEDOR_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 20 caracteres.");
        if (string.IsNullOrWhiteSpace(razonSocial) || razonSocial.Length > 254)
            throw new BusinessRuleException("PROVEEDOR_RAZON_SOCIAL_INVALIDA",
                "La razón social es requerida y no puede exceder 254 caracteres.");
        if (string.IsNullOrWhiteSpace(rfc))
            throw new BusinessRuleException("PROVEEDOR_RFC_INVALIDO",
                "El RFC debe tener 12 (moral) o 13 (física) caracteres.");
        var rfcNormalizado = rfc.Trim().ToUpperInvariant();
        if (rfcNormalizado.Length is < 12 or > 13)
            throw new BusinessRuleException("PROVEEDOR_RFC_INVALIDO",
                "El RFC debe tener 12 (moral) o 13 (física) caracteres.");
        if (condicionesPagoDias is < 0 or > 365)
            throw new BusinessRuleException("PROVEEDOR_CONDICIONES_PAGO_INVALIDAS",
                "Las condiciones de pago deben estar entre 0 y 365 días.");

        Clave = clave;
        ClaveLegacy = claveLegacy;
        OrigenCarga = origenCarga;
        RazonSocial = razonSocial;
        NombreComercial = nombreComercial;
        Rfc = rfcNormalizado;
        TipoPersona = tipoPersona;
        CondicionesPagoDias = condicionesPagoDias;
        MonedaPreferidaId = monedaPreferidaId;
        Email = email;
        Telefono = telefono;
        Estatus = estatus;
    }

    /// <summary>
    /// PATCH parcial sobre los campos editables del proveedor (B.5). Convención: parámetro <c>null</c>
    /// = no tocar; flag <c>limpiarX = true</c> = setear nullable a null.
    /// Inmutables: <see cref="BaseEntity.Id"/>, <see cref="Clave"/>
    /// (business key) y <see cref="ClaveLegacy"/> (solo cutover SAP).
    /// </summary>
    public void ActualizarDatos(
        string? razonSocial = null,
        string? nombreComercial = null,
        string? rfc = null,
        TipoPersonaProveedor? tipoPersona = null,
        short? condicionesPagoDias = null,
        Guid? monedaPreferidaId = null,
        string? email = null,
        string? telefono = null,
        bool limpiarNombreComercial = false,
        bool limpiarCondicionesPago = false,
        bool limpiarMonedaPreferida = false,
        bool limpiarEmail = false,
        bool limpiarTelefono = false)
    {
        if (razonSocial is not null)
        {
            if (razonSocial.Length is 0 or > 254)
                throw new BusinessRuleException("PROVEEDOR_RAZON_SOCIAL_INVALIDA",
                    "La razón social es requerida y no puede exceder 254 caracteres.");
            RazonSocial = razonSocial;
        }
        if (rfc is not null)
        {
            var rfcNormalizado = rfc.Trim().ToUpperInvariant();
            if (rfcNormalizado.Length is < 12 or > 13)
                throw new BusinessRuleException("PROVEEDOR_RFC_INVALIDO",
                    "El RFC debe tener 12 (moral) o 13 (física) caracteres.");
            Rfc = rfcNormalizado;
        }
        if (condicionesPagoDias is short cpd)
        {
            if (cpd is < 0 or > 365)
                throw new BusinessRuleException("PROVEEDOR_CONDICIONES_PAGO_INVALIDAS",
                    "Las condiciones de pago deben estar entre 0 y 365 días.");
            CondicionesPagoDias = cpd;
        }
        else if (limpiarCondicionesPago) CondicionesPagoDias = null;

        if (nombreComercial is not null) NombreComercial = nombreComercial;
        else if (limpiarNombreComercial) NombreComercial = null;

        if (monedaPreferidaId is Guid m) MonedaPreferidaId = m;
        else if (limpiarMonedaPreferida) MonedaPreferidaId = null;

        if (email is not null) Email = email;
        else if (limpiarEmail) Email = null;

        if (telefono is not null) Telefono = telefono;
        else if (limpiarTelefono) Telefono = null;

        if (tipoPersona is TipoPersonaProveedor tp) TipoPersona = tp;
    }

    /// <summary>
    /// Cambia el estatus del proveedor (B.5). El endpoint público
    /// solo expone Activo/Inactivo; <c>EnRevision</c> queda para
    /// flujos internos (cutover SAP, compliance) que invocan este
    /// método directamente.
    /// </summary>
    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;

    public void PonerEnRevision(string motivo)
    {
        if (Estatus != EstatusCatalogo.Activo)
            throw new BusinessRuleException("PROVEEDOR_NO_ACTIVO", "Solo puedes poner en revisión a un proveedor activo.");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 5 || motivo.Length > 500)
            throw new BusinessRuleException("PROVEEDOR_MOTIVO_INVALIDO", "Indica el motivo de revisión (de 5 a 500 caracteres).");
        Estatus = EstatusCatalogo.EnRevision;
    }

    /// <summary>
    /// Valida el proveedor en revisión tras comprobar la completitud de su expediente (F1-ADM-05 G1.1).
    /// Pasa de EnRevision a Activo.
    /// </summary>
    public void Validar(Guid validadorId, DateTimeOffset fecha, int? matrizVersion = null)
    {
        if (Estatus != EstatusCatalogo.EnRevision)
        {
            throw new BusinessRuleException(
                "PROVEEDOR_NO_EN_REVISION",
                $"Solo se pueden validar proveedores en estado 'EnRevision'. Estado actual: '{Estatus}'.");
        }

        Estatus = EstatusCatalogo.Activo;
        ValidadoPorId = validadorId;
        ValidadoEn = fecha;
        ValidadoConMatrizVersion = matrizVersion;
        MotivoRechazo = null;
    }

    /// <summary>
    /// Rechaza el proveedor en revisión por inconsistencias documentales o fiscales (F1-ADM-05 G1.1).
    /// Pasa de EnRevision a Inactivo con motivo obligatorio (5 a 500 caracteres).
    /// </summary>
    public void Rechazar(Guid validadorId, string motivo, DateTimeOffset fecha)
    {
        if (Estatus != EstatusCatalogo.EnRevision)
        {
            throw new BusinessRuleException(
                "PROVEEDOR_NO_EN_REVISION",
                $"Solo se pueden rechazar proveedores en estado 'EnRevision'. Estado actual: '{Estatus}'.");
        }

        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 5 || motivo.Trim().Length > 500)
        {
            throw new BusinessRuleException(
                "PROVEEDOR_MOTIVO_RECHAZO_INVALIDO",
                "El motivo de rechazo es requerido y debe tener entre 5 y 500 caracteres.");
        }

        Estatus = EstatusCatalogo.Inactivo;
        ValidadoPorId = validadorId;
        ValidadoEn = fecha;
        MotivoRechazo = motivo.Trim();
    }
}

public enum OrigenProveedor : short
{
    Nacional = 0,
    Extranjero = 1,
}
