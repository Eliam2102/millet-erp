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
    public string RazonSocial { get; private set; } = string.Empty;
    public string? NombreComercial { get; private set; }
    public string Rfc { get; private set; } = string.Empty;
    public TipoPersonaProveedor TipoPersona { get; private set; }
    public short? CondicionesPagoDias { get; private set; }
    public Guid? MonedaPreferidaId { get; private set; }
    public string? Email { get; private set; }
    public string? Telefono { get; private set; }
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    // ----- Datos bancarios para pago (TES-PR3, T-G1) -----
    // Tesorería los resuelve vía IProveedorBancoReadPort para ejecutar la
    // transferencia; el evento de pasivo de CxP no los trae
    // (PLATFORM-TODO(PayloadEnriquecido)). PII: la CLABE se enmascara en
    // logs (enricher Serilog) y en DTOs salvo permiso explícito.
    /// <summary>Banco del proveedor (texto libre, catálogo pendiente).</summary>
    public string? Banco { get; private set; }

    /// <summary>CLABE interbancaria (18 dígitos) para transferencias.</summary>
    public string? Clabe { get; private set; }

    /// <summary>Nombre del beneficiario de la transferencia si difiere de la razón social.</summary>
    public string? Beneficiario { get; private set; }

    private Proveedor() { }

    public Proveedor(
        Guid id,
        string clave,
        string razonSocial,
        string rfc,
        TipoPersonaProveedor tipoPersona,
        EstatusCatalogo estatus = EstatusCatalogo.Activo,
        string? claveLegacy = null,
        string? nombreComercial = null,
        short? condicionesPagoDias = null,
        Guid? monedaPreferidaId = null,
        string? email = null,
        string? telefono = null) : base(id)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.Length > 20)
            throw new BusinessRuleException("PROVEEDOR_CLAVE_INVALIDA",
                "La clave es requerida y no puede exceder 20 caracteres.");
        if (string.IsNullOrWhiteSpace(razonSocial) || razonSocial.Length > 254)
            throw new BusinessRuleException("PROVEEDOR_RAZON_SOCIAL_INVALIDA",
                "La razón social es requerida y no puede exceder 254 caracteres.");
        if (string.IsNullOrWhiteSpace(rfc) || rfc.Length is < 12 or > 13)
            throw new BusinessRuleException("PROVEEDOR_RFC_INVALIDO",
                "El RFC debe tener 12 (moral) o 13 (física) caracteres.");
        if (condicionesPagoDias is < 0 or > 365)
            throw new BusinessRuleException("PROVEEDOR_CONDICIONES_PAGO_INVALIDAS",
                "Las condiciones de pago deben estar entre 0 y 365 días.");

        Clave = clave;
        ClaveLegacy = claveLegacy;
        RazonSocial = razonSocial;
        NombreComercial = nombreComercial;
        Rfc = rfc;
        TipoPersona = tipoPersona;
        CondicionesPagoDias = condicionesPagoDias;
        MonedaPreferidaId = monedaPreferidaId;
        Email = email;
        Telefono = telefono;
        Estatus = estatus;
    }

    /// <summary>
    /// PATCH parcial sobre los campos editables del proveedor (B.5;
    /// datos bancarios desde TES-PR3). Convención: parámetro <c>null</c>
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
        string? banco = null,
        string? clabe = null,
        string? beneficiario = null,
        bool limpiarNombreComercial = false,
        bool limpiarCondicionesPago = false,
        bool limpiarMonedaPreferida = false,
        bool limpiarEmail = false,
        bool limpiarTelefono = false,
        bool limpiarBanco = false,
        bool limpiarClabe = false,
        bool limpiarBeneficiario = false)
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
            if (rfc.Length is < 12 or > 13)
                throw new BusinessRuleException("PROVEEDOR_RFC_INVALIDO",
                    "El RFC debe tener 12 (moral) o 13 (física) caracteres.");
            Rfc = rfc;
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

        if (banco is not null)
        {
            if (banco.Length is 0 or > 120)
                throw new BusinessRuleException("PROVEEDOR_BANCO_INVALIDO",
                    "El banco no puede estar vacío ni exceder 120 caracteres.");
            Banco = banco;
        }
        else if (limpiarBanco) Banco = null;

        if (clabe is not null)
        {
            var c = clabe.Trim();
            if (c.Length != 18 || !c.All(char.IsAsciiDigit))
                throw new BusinessRuleException("PROVEEDOR_CLABE_INVALIDA",
                    "La CLABE debe tener 18 dígitos.");
            Clabe = c;
        }
        else if (limpiarClabe) Clabe = null;

        if (beneficiario is not null)
        {
            if (beneficiario.Length is 0 or > 254)
                throw new BusinessRuleException("PROVEEDOR_BENEFICIARIO_INVALIDO",
                    "El beneficiario no puede estar vacío ni exceder 254 caracteres.");
            Beneficiario = beneficiario;
        }
        else if (limpiarBeneficiario) Beneficiario = null;

        if (tipoPersona is TipoPersonaProveedor tp) TipoPersona = tp;
    }

    /// <summary>
    /// Cambia el estatus del proveedor (B.5). El endpoint público
    /// solo expone Activo/Inactivo; <c>EnRevision</c> queda para
    /// flujos internos (cutover SAP, compliance) que invocan este
    /// método directamente.
    /// </summary>
    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;
}
