using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Incidencias;

/// <summary>
/// Evidencia de una incidencia (U-4) con el servicio genérico de adjuntos. Ver/subir/baja con los permisos de
/// incidencias; el responsable también puede ver y subir evidencia de las suyas. Una incidencia resuelta o
/// cancelada ya no admite evidencia nueva.
/// </summary>
public sealed class IncidenciaAdjuntoPropietario(CompartidoDbContext db, IncidenciasServicio servicio, ICurrentUserContext usuario) : IAdjuntoPropietario
{
    public const string Tipo = IncidenciasModelo.TipoEntidadAdjunto;

    public string TipoEntidad => Tipo;
    public string CodigoNoEncontrado => "INCIDENCIA_NO_ENCONTRADA";
    public string PermisoVer => IncidenciasPermisos.Ver;
    public string PermisoSubir => IncidenciasPermisos.Registrar;
    public string PermisoBaja => IncidenciasPermisos.Gestionar;

    public async Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken) =>
        await db.Incidencias.AsNoTracking().Where(x => x.Id == entidadId)
            .Select(x => new { x.Id, x.EmpresaId, x.SucursalId, x.Estado, x.Numero, x.DocumentoEtiqueta })
            .FirstOrDefaultAsync(cancellationToken) is { } i
            ? new AdjuntoPropietarioInfo(i.Id, i.EmpresaId, i.SucursalId,
                i.Estado is SharedKernel.Domain.Incidencias.EstadoIncidencia.Abierta or SharedKernel.Domain.Incidencias.EstadoIncidencia.EnAtencion,
                null, $"Incidencia {i.Numero} · {i.DocumentoEtiqueta}")
            : null;

    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken) =>
        VerificarAlcanceAsync(info, AdjuntoOperacion.Ver, cancellationToken);

    public async Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, AdjuntoOperacion operacion, CancellationToken cancellationToken)
    {
        var incidencia = await servicio.CargarAsync(info.EntidadId, false, cancellationToken);
        if (operacion != AdjuntoOperacion.Ver && !incidencia.Pendiente)
            throw new BusinessRuleException("INCIDENCIA_CERRADA", "La incidencia ya está cerrada: su evidencia es de solo lectura.");
        if (operacion == AdjuntoOperacion.Baja && usuario.UserId != incidencia.ResponsableUsuarioId)
            await servicio.VerificarSucursalAsync(incidencia.SucursalId, IncidenciasPermisos.GestionarTodasSucursales, cancellationToken);
    }
}
