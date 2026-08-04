using Millet.Integraciones.Fiscal.Domain;

namespace Millet.Integraciones.Fiscal.Application.RfcsReceptores;

internal static class RfcReceptorMappers
{
    public static RfcReceptorResponse ToResponse(this RfcReceptor r, DateTimeOffset ahora) =>
        new(
            Id: r.Id,
            EmpresaId: r.EmpresaId,
            Rfc: r.Rfc,
            DescargaHabilitada: r.DescargaHabilitada,
            RefreshHabilitada: r.RefreshHabilitada,
            CheckpointDescargaAt: r.CheckpointDescargaAt,
            TieneFiel: r.TieneFielVigente(ahora),
            FielValidFrom: r.FielValidFrom,
            FielValidTo: r.FielValidTo,
            FielSubidaAt: r.FielSubidaAt,
            CreatedAt: r.CreatedAt,
            UpdatedAt: r.UpdatedAt,
            Version: r.Version);
}
