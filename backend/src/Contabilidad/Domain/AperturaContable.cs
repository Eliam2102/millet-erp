using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Contabilidad.Domain;

/// <summary>
/// M-166 · marca de la apertura contable de la empresa: el lote de la balanza del sistema anterior que la generó, su huella
/// (SHA-256 del archivo) y la fecha de corte. Una por empresa: un segundo archivo distinto se rechaza y el ajuste va por el
/// ajuste único (M-300). El mismo archivo (misma huella y corte) regresa la póliza ya creada.
/// </summary>
public sealed class AperturaContable : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int HuellaLargo = 64;

    public Guid EmpresaId { get; set; }
    public Guid LoteId { get; private set; }
    public string Huella { get; private set; } = string.Empty;
    public DateOnly FechaCorte { get; private set; }
    public Guid PolizaId { get; private set; }
    public string Folio { get; private set; } = string.Empty;
    public Guid? SubidoPorId { get; private set; }
    public Guid? AutorizadoPorId { get; private set; }
    public string AplicadoPor { get; private set; } = string.Empty;
    public DateTimeOffset AplicadoEn { get; private set; }

    private AperturaContable() { }

    public AperturaContable(Guid id, Guid loteId, string huella, DateOnly fechaCorte, Guid polizaId, string folio,
        Guid? subidoPorId, Guid? autorizadoPorId, string aplicadoPor, DateTimeOffset aplicadoEn) : base(id)
    {
        var h = NormalizarHuella(huella);
        LoteId = loteId;
        Huella = h;
        FechaCorte = fechaCorte;
        PolizaId = polizaId;
        Folio = folio;
        SubidoPorId = subidoPorId;
        AutorizadoPorId = autorizadoPorId;
        AplicadoPor = aplicadoPor.Length <= 256 ? aplicadoPor : aplicadoPor[..256];
        AplicadoEn = aplicadoEn;
    }

    /// <summary>SHA-256 en hexadecimal (64 caracteres), en minúsculas.</summary>
    public static string NormalizarHuella(string? huella)
    {
        var h = huella?.Trim().ToLowerInvariant() ?? string.Empty;
        if (h.Length != HuellaLargo || !h.All(Uri.IsHexDigit))
            throw new BusinessRuleException("AP_HUELLA_INVALIDA", "La huella del archivo debe ser un SHA-256 en hexadecimal (64 caracteres).");
        return h;
    }

    public bool EsElMismoArchivo(string huella, DateOnly fechaCorte) => Huella == NormalizarHuella(huella) && FechaCorte == fechaCorte;
}
