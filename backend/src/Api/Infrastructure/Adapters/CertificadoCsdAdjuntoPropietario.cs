using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// Evidencia de un CSD del historial (F6.9, R24): acuse de renovación,
/// sustitución o revocación. Ver con <c>integraciones.fiscal.leer</c>; subir
/// con <c>administrar</c>. La evidencia no se da de baja: forma parte del
/// historial del certificado.
/// </summary>
public sealed class CertificadoCsdAdjuntoPropietario(IntegracionesFiscalDbContext db) : IAdjuntoPropietario
{
    public const string Entidad = "certificado_csd";
    public string TipoEntidad => Entidad;
    public string CodigoNoEncontrado => "CSD_NO_ENCONTRADO";
    public string PermisoVer => PermisosCanonicos.IntegracionesFiscalLeer;
    public string PermisoSubir => PermisosCanonicos.IntegracionesFiscalAdministrar;
    public string PermisoBaja => PermisosCanonicos.IntegracionesFiscalAdministrar;

    public async Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken) =>
        await db.CertificadosCsd.AsNoTracking()
            .Where(c => c.Id == entidadId)
            .Select(c => new AdjuntoPropietarioInfo(c.Id, c.EmpresaId, null, true, null, "CSD " + c.NumeroSerie, null))
            .SingleOrDefaultAsync(cancellationToken);

    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AlDarDeBajaAsync(Guid entidadId, Guid adjuntoId, CancellationToken ct) =>
        throw new BusinessRuleException("CSD_EVIDENCIA_CONSERVAR",
            "La evidencia forma parte del historial del certificado; sube una nueva en lugar de borrarla.");
}
