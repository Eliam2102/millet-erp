using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Identidad.Domain;
namespace Millet.ActivosFijos.Infrastructure.PublicAdapters;
public sealed class ActivoAdjuntoPropietario(ActivosFijosDbContext db, ActivoServicio servicio) : IAdjuntoPropietario
{
    public const string Entidad = "activo_fijo";
    public string TipoEntidad => Entidad;
    public string CodigoNoEncontrado => "AF_NO_EXISTE";
    public string PermisoVer => PermisosCanonicos.ActivosFijosActivosVer;
    public string PermisoSubir => PermisosCanonicos.ActivosFijosDocumentosGestionar;
    public string PermisoBaja => PermisosCanonicos.ActivosFijosDocumentosGestionar;
    public async Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken) => await db.Activos.AsNoTracking().Where(x => x.Id == entidadId).Select(x => new AdjuntoPropietarioInfo(x.Id, x.EmpresaId, x.SucursalId, true, null, x.Numero)).SingleOrDefaultAsync(cancellationToken);
    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken) => VerificarAlcanceAsync(info, AdjuntoOperacion.Ver, cancellationToken);
    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, AdjuntoOperacion operacion, CancellationToken cancellationToken) => servicio.SucursalAsync(info.SucursalId!.Value, operacion != AdjuntoOperacion.Ver, cancellationToken);
    public async Task AlDarDeBajaAsync(Guid entidadId, Guid adjuntoId, CancellationToken ct)
    {
        if (await db.Documentos.AnyAsync(x => x.ActivoId == entidadId && x.AdjuntoId == adjuntoId, ct))
            throw new BusinessRuleException("AF_DOCUMENTO_CONSERVAR", "El archivo forma parte del historial. Registre un reemplazo para conservar ambas versiones.");
    }
}
