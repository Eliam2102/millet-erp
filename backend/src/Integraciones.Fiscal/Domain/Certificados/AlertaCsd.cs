using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Fiscal.Domain.Certificados;

/// <summary>
/// Aviso de vencimiento enviado para un certificado y un umbral (F6.9, P6.4):
/// 30, 15 y 5 días antes, y el día del vencimiento (0). Uno por certificado y
/// umbral, para no repetir el aviso.
/// </summary>
public sealed class AlertaCsd : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public Guid CertificadoCsdId { get; private set; }
    public int UmbralDias { get; private set; }
    public int DiasRestantes { get; private set; }
    public DateTimeOffset DisparadaEn { get; private set; }

    /// <summary>Correos o roles a los que se dirigió el aviso (texto para consulta).</summary>
    public string Destinatarios { get; private set; } = string.Empty;

    private AlertaCsd() { } // EF Core

    public AlertaCsd(Guid empresaId, Guid certificadoCsdId, int umbralDias, int diasRestantes, DateTimeOffset ahora, string destinatarios)
        : base(Guid.CreateVersion7())
    {
        EmpresaId = empresaId;
        CertificadoCsdId = certificadoCsdId;
        UmbralDias = umbralDias;
        DiasRestantes = diasRestantes;
        DisparadaEn = ahora;
        Destinatarios = destinatarios;
    }
}

/// <summary>
/// Política de avisos de vencimiento (P6.4). Si se cruzan varios umbrales a la
/// vez (p. ej. el worker estuvo apagado), solo se avisa el más urgente; los
/// umbrales ya avisados no se repiten. Supuesto «a validar».
/// </summary>
public static class PoliticaAlertasCsd
{
    public static readonly IReadOnlyList<int> Umbrales = [30, 15, 5, 0];

    /// <summary>Umbral que toca avisar hoy, o <c>null</c> si no hay ninguno pendiente.</summary>
    public static int? UmbralPendiente(int diasParaVencer, IReadOnlyCollection<int> yaAvisados)
    {
        var cruzados = Umbrales.Where(u => diasParaVencer <= u).ToList();
        if (cruzados.Count == 0) return null;
        var masUrgente = cruzados.Min();
        if (yaAvisados.Any(a => a <= masUrgente)) return null;
        return masUrgente;
    }
}
