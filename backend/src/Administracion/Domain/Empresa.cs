using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Administracion.Domain;

/// <summary>
/// Razón social del grupo Millet. Vive en el esquema <c>compartido</c>
/// (catálogo global, multi-tenant lógico). Cada fila representa un
/// tenant lógico al que otras entidades hacen FK vía <c>empresa_id</c>.
/// Ver ADR-0011 y 01-diseno §4.2 del módulo Administración.
/// </summary>
public sealed class Empresa : BaseEntity, IAuditable
{
    public string Rfc { get; private set; } = string.Empty;

    public string RazonSocial { get; private set; } = string.Empty;

    public string? NombreComercial { get; private set; }

    public string RegimenFiscal { get; private set; } = string.Empty;

    /// <summary>
    /// Tasa de IVA default de la empresa (fracción, p.ej. 0.16) para captura
    /// manual en Facturación (FAC-DET-PR2). Precedencia efectiva en pedidos
    /// manuales: IVA del artículo (<c>ProductoAw.TasaIvaTraslado</c>) &gt;
    /// esta tasa. Los pedidos A+W traen la tasa del documento origen y no
    /// usan este default. <c>null</c> = sin default configurado.
    /// </summary>
    public decimal? TasaIvaDefault { get; private set; }

    /// <summary>
    /// Código postal fiscal de la empresa (domicilio fiscal SAT). Es el
    /// <c>LugarExpedicion</c> del CFDI 4.0 (obligatorio para timbrar,
    /// F12-PR1). <c>null</c> = sin capturar — la emisión de CFDI falla con
    /// <c>EMISOR_SIN_LUGAR_EXPEDICION</c> hasta capturarlo.
    /// </summary>
    public string? CodigoPostal { get; private set; }

    public bool Activa { get; private set; } = true;

    private Empresa() { } // EF Core

    public Empresa(Guid id, string rfc, string razonSocial, string regimenFiscal, string? nombreComercial = null)
        : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("EMPRESA_ID_INVALIDO", "El id es obligatorio.");
        ValidarRfc(rfc);
        ValidarRazonSocial(razonSocial);
        ValidarRegimenFiscal(regimenFiscal);
        ValidarNombreComercial(nombreComercial);

        Rfc = rfc;
        RazonSocial = razonSocial;
        RegimenFiscal = regimenFiscal;
        NombreComercial = nombreComercial;
    }

    /// <summary>
    /// PATCH parcial sobre los campos editables de la empresa
    /// (F-Admin-PR2.1). Convención: parámetro <c>null</c> = no tocar;
    /// flag <c>limpiarX = true</c> = setear nullable a null. Inmutables:
    /// <see cref="BaseEntity.Id"/> y <see cref="Rfc"/> (RFC es business
    /// key fiscal — cambios reales requieren alta nueva por SAT).
    /// </summary>
    public void ActualizarDatos(
        string? razonSocial = null,
        string? nombreComercial = null,
        string? regimenFiscal = null,
        bool limpiarNombreComercial = false,
        decimal? tasaIvaDefault = null,
        bool limpiarTasaIvaDefault = false,
        string? codigoPostal = null)
    {
        if (razonSocial is not null)
        {
            ValidarRazonSocial(razonSocial);
            RazonSocial = razonSocial;
        }
        if (regimenFiscal is not null)
        {
            ValidarRegimenFiscal(regimenFiscal);
            RegimenFiscal = regimenFiscal;
        }
        if (nombreComercial is not null)
        {
            ValidarNombreComercial(nombreComercial);
            NombreComercial = nombreComercial;
        }
        else if (limpiarNombreComercial)
        {
            NombreComercial = null;
        }
        if (tasaIvaDefault is not null)
        {
            ValidarTasaIvaDefault(tasaIvaDefault.Value);
            TasaIvaDefault = tasaIvaDefault;
        }
        else if (limpiarTasaIvaDefault)
        {
            TasaIvaDefault = null;
        }
        if (codigoPostal is not null)
        {
            ValidarCodigoPostal(codigoPostal);
            CodigoPostal = codigoPostal;
        }
    }

    /// <summary>
    /// Reactiva una empresa previamente desactivada. Idempotente —
    /// si ya está activa, no-op.
    /// </summary>
    public void Activar() => Activa = true;

    /// <summary>
    /// Desactiva la empresa. La validación cross-entity (no desactivar
    /// si tiene sucursales activas, ADR-0011) vive en el handler
    /// (<c>DesactivarEmpresaCommand</c> de F-Admin-PR2.3) porque
    /// requiere consultar otra tabla. Esta capa se limita a la
    /// transición de estado idempotente.
    /// </summary>
    public void Desactivar() => Activa = false;

    private static void ValidarRfc(string rfc)
    {
        if (string.IsNullOrWhiteSpace(rfc) || rfc.Length is < 12 or > 13)
            throw new BusinessRuleException("EMPRESA_RFC_INVALIDO",
                "El RFC debe tener 12 (moral) o 13 (física) caracteres.");
    }

    private static void ValidarRazonSocial(string razonSocial)
    {
        if (string.IsNullOrWhiteSpace(razonSocial) || razonSocial.Length > 254)
            throw new BusinessRuleException("EMPRESA_RAZON_SOCIAL_INVALIDA",
                "La razón social es requerida y no puede exceder 254 caracteres.");
    }

    private static void ValidarRegimenFiscal(string regimenFiscal)
    {
        if (string.IsNullOrWhiteSpace(regimenFiscal) || regimenFiscal.Length > 10)
            throw new BusinessRuleException("EMPRESA_REGIMEN_FISCAL_INVALIDO",
                "El régimen fiscal es requerido y no puede exceder 10 caracteres.");
    }

    private static void ValidarTasaIvaDefault(decimal tasa)
    {
        if (tasa is < 0 or > 1)
            throw new BusinessRuleException("EMPRESA_TASA_IVA_INVALIDA",
                "La tasa de IVA default debe ser fracción entre 0 y 1 (p.ej. 0.16).");
    }

    private static void ValidarCodigoPostal(string codigoPostal)
    {
        if (codigoPostal.Length != 5 || !codigoPostal.All(char.IsAsciiDigit))
            throw new BusinessRuleException("EMPRESA_CODIGO_POSTAL_INVALIDO",
                "El código postal fiscal debe ser de 5 dígitos (catálogo c_CodigoPostal del SAT).");
    }

    private static void ValidarNombreComercial(string? nombreComercial)
    {
        if (nombreComercial is { Length: > 254 })
            throw new BusinessRuleException("EMPRESA_NOMBRE_COMERCIAL_INVALIDO",
                "El nombre comercial no puede exceder 254 caracteres.");
    }
}
