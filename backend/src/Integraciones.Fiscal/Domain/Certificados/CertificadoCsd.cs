using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Fiscal.Domain.Certificados;

/// <summary>
/// Historial de CSD de una empresa (F6.9, R24): alta, sustitución y revocación,
/// con quién y cuándo. Solo datos públicos del certificado (número de serie,
/// RFC, vigencia); el material cifrado sigue en <see cref="ConfiguracionPac"/>.
/// La evidencia (acuse del SAT, solicitud de renovación) se adjunta con el
/// mecanismo genérico de adjuntos (<c>certificado_csd</c>).
///
/// <para>
/// Los nombres de las propiedades evitan «Csd»/«Certificado»: el interceptor de
/// auditoría enmascara esas columnas y aquí sí queremos ver fechas y estado.
/// </para>
/// </summary>
public sealed class CertificadoCsd : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid ConfiguracionPacId { get; private set; }

    /// <summary>Número de certificado del SAT (20 dígitos).</summary>
    public string NumeroSerie { get; private set; } = string.Empty;

    /// <summary>RFC del titular leído del certificado.</summary>
    public string? Rfc { get; private set; }

    public DateTimeOffset VigenteDesde { get; private set; }
    public DateTimeOffset VenceEn { get; private set; }

    public EstadoCertificadoCsd Estado { get; private set; }

    public DateTimeOffset RegistradoEn { get; private set; }
    public string? RegistradoPor { get; private set; }

    /// <summary>Cuándo dejó de usarse (sustitución o revocación) y quién lo hizo.</summary>
    public DateTimeOffset? FinalizadoEn { get; private set; }
    public string? FinalizadoPor { get; private set; }

    /// <summary>Motivo de la revocación (obligatorio al revocar).</summary>
    public string? MotivoRevocacion { get; private set; }

    private CertificadoCsd() { } // EF Core

    private CertificadoCsd(
        Guid id, Guid empresaId, Guid configuracionPacId, string numeroSerie, string? rfc,
        DateTimeOffset vigenteDesde, DateTimeOffset venceEn, DateTimeOffset ahora, string? usuario)
        : base(id)
    {
        EmpresaId = empresaId;
        ConfiguracionPacId = configuracionPacId;
        NumeroSerie = numeroSerie;
        Rfc = rfc;
        VigenteDesde = vigenteDesde;
        VenceEn = venceEn;
        Estado = EstadoCertificadoCsd.Vigente;
        RegistradoEn = ahora;
        RegistradoPor = usuario;
    }

    public static CertificadoCsd Registrar(
        Guid empresaId, Guid configuracionPacId, string numeroSerie, string? rfc,
        DateTimeOffset vigenteDesde, DateTimeOffset venceEn, DateTimeOffset ahora, string? usuario)
    {
        if (string.IsNullOrWhiteSpace(numeroSerie))
            throw new BusinessRuleException("CSD_NUMERO_SERIE_REQUERIDO", "El certificado no trae número de serie.");
        if (venceEn <= vigenteDesde)
            throw new BusinessRuleException("CONFIG_PAC_CSD_VIGENCIA_INVALIDA", "La vigencia del CSD es inválida.");
        return new CertificadoCsd(Guid.CreateVersion7(), empresaId, configuracionPacId, numeroSerie.Trim(), rfc,
            vigenteDesde, venceEn, ahora, usuario);
    }

    /// <summary>Días completos que faltan para el vencimiento (negativo si ya venció).</summary>
    public int DiasParaVencer(DateTimeOffset ahora) => (int)Math.Floor((VenceEn - ahora).TotalDays);

    /// <summary>Vigente → Sustituido: se capturó otro CSD.</summary>
    public void Sustituir(DateTimeOffset ahora, string? usuario)
    {
        if (Estado != EstadoCertificadoCsd.Vigente) return;
        Estado = EstadoCertificadoCsd.Sustituido;
        FinalizadoEn = ahora;
        FinalizadoPor = usuario;
    }

    /// <summary>Vigente → Revocado (con motivo): el ERP deja de timbrar con él aunque no haya vencido.</summary>
    public void Revocar(string motivo, DateTimeOffset ahora, string? usuario)
    {
        if (Estado != EstadoCertificadoCsd.Vigente)
            throw new BusinessRuleException("CSD_NO_VIGENTE", $"Solo se revoca el certificado en uso (estado actual: {Estado}).");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("CSD_MOTIVO_REVOCACION_REQUERIDO", "Indica el motivo de la revocación.");
        Estado = EstadoCertificadoCsd.Revocado;
        MotivoRevocacion = motivo.Trim();
        FinalizadoEn = ahora;
        FinalizadoPor = usuario;
    }
}

/// <summary>Estado del certificado en el historial. Valores fijos por ABI: agregar al final.</summary>
public enum EstadoCertificadoCsd : short
{
    Vigente = 1,
    Sustituido = 2,
    Revocado = 3,
}
