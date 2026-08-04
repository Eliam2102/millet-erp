namespace Millet.Integraciones.Fiscal.Application.RfcsReceptores;

public sealed record RfcReceptorResponse(
    Guid Id,
    Guid EmpresaId,
    string Rfc,
    bool DescargaHabilitada,
    bool RefreshHabilitada,
    DateTimeOffset? CheckpointDescargaAt,
    // PR-10: estado de la FIEL en FiscalAPI.
    bool TieneFiel,
    DateTimeOffset? FielValidFrom,
    DateTimeOffset? FielValidTo,
    DateTimeOffset? FielSubidaAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version);
