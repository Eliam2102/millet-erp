using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.ComercioExterior.Application;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.Identidad.Domain;

namespace Millet.ComercioExterior.Infrastructure.PublicAdapters;
public sealed class ExpedienteAdjuntoPropietario(ComercioExteriorDbContext db, ExpedienteServicio servicio) : IAdjuntoPropietario
{
    public string TipoEntidad => CatalogosCeReadAdapter.Entidad;
    public string CodigoNoEncontrado => "CE_EXPEDIENTE_NO_ENCONTRADO";
    public string PermisoVer => PermisosCanonicos.ComercioExteriorExpedientesAdjuntosVer;
    public string PermisoSubir => PermisosCanonicos.ComercioExteriorExpedientesAdjuntosSubir;
    public string PermisoBaja => PermisosCanonicos.ComercioExteriorExpedientesAdjuntosBaja;

    public async Task AlDarDeBajaAsync(Guid entidadId, Guid adjuntoId, CancellationToken ct)
    {
        var expediente = await servicio.CargarAsync(entidadId, true, ct);
        expediente.DarDeBajaAdjunto(adjuntoId);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken) => await db.Expedientes.AsNoTracking().Where(x => x.Id == entidadId).Select(x => new AdjuntoPropietarioInfo(x.Id, x.EmpresaId, x.SucursalId, x.Estado == EstadoExpediente.Abierto, null, x.Folio)).SingleOrDefaultAsync(cancellationToken);
    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken) => VerificarAlcanceAsync(info, AdjuntoOperacion.Ver, cancellationToken);
    public async Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, AdjuntoOperacion operacion, CancellationToken cancellationToken)
    {
        await servicio.SucursalAsync(info.SucursalId!.Value, operacion != AdjuntoOperacion.Ver, cancellationToken);
        if (operacion != AdjuntoOperacion.Ver && !info.PuedeSubir)
            throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException("CE_EXPEDIENTE_NO_ABIERTO", "El expediente está cancelado.");
    }
}
