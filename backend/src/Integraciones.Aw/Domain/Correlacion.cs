using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Aw.Domain;

/// <summary>
/// Correlación exitosa entre una <see cref="EntidadExterna"/> del ERP y
/// su contraparte en A+W (<c>pool_auftrag</c> para cotizaciones).
/// Relación 1:1 con la entidad raíz — solo se crea desde
/// <see cref="EntidadExterna.MarcarCorrelacionado"/>.
///
/// <para>
/// El <see cref="AwRecordSnapshot"/> es un JSON con la fila completa de
/// A+W al momento del match — útil para debug y auditoría sin tener que
/// volver a A+W. Opcional.
/// </para>
/// </summary>
public sealed class Correlacion : BaseEntity, IAuditable
{
    public Guid EntidadExternaId { get; private set; }
    public long AwDocId { get; private set; }
    public string? AwDocIdSecondary { get; private set; }
    public int PollingCycleNumber { get; private set; }
    public int? PollingQueryDurationMs { get; private set; }
    public DateTimeOffset CorrelatedAt { get; private set; }
    public string? AwRecordSnapshot { get; private set; }

    private Correlacion() { } // EF Core

    internal Correlacion(
        Guid id,
        Guid entidadExternaId,
        long awDocId,
        string? awDocIdSecondary,
        int pollingCycleNumber,
        int? pollingQueryDurationMs,
        DateTimeOffset correlatedAt,
        string? awRecordSnapshot) : base(id)
    {
        EntidadExternaId = entidadExternaId;
        AwDocId = awDocId;
        AwDocIdSecondary = awDocIdSecondary;
        PollingCycleNumber = pollingCycleNumber;
        PollingQueryDurationMs = pollingQueryDurationMs;
        CorrelatedAt = correlatedAt;
        AwRecordSnapshot = awRecordSnapshot;
    }
}
