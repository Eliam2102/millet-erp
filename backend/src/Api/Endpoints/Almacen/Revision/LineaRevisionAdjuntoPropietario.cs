using MediatR;
using Millet.Almacen.Application.Revision;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Adjuntos;

namespace Millet.Api.Endpoints.Almacen.Revision;

/// <summary>
/// Propietario de adjuntos de una línea de material en revisión (evidencia del daño, ALM-04).
/// Vive en Api porque Almacén no referencia Compartido (donde está SucursalScopeGuard).
/// </summary>
public sealed class LineaRevisionAdjuntoPropietario(IMediator mediator, DocumentoSucursalScope documentos) : IAdjuntoPropietario
{
    public string TipoEntidad => "linea_revision";
    public string CodigoNoEncontrado => "REVISION_LINEA_NO_ENCONTRADA";
    public string PermisoVer => PermisosCanonicos.AlmacenEntradasLeer;
    public string PermisoSubir => PermisosCanonicos.AlmacenEntradasCapturar;
    public string PermisoBaja => PermisosCanonicos.AlmacenEntradasCapturar;

    public async Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken)
    {
        var i = await mediator.Send(new ObtenerLineaRevisionQuery(entidadId), cancellationToken);
        return i is null ? null : new AdjuntoPropietarioInfo(i.LineaRevisionId, i.EmpresaId, i.SucursalId, true, null, i.Etiqueta);
    }

    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken)
        => VerificarAlcanceAsync(info, AdjuntoOperacion.Ver, cancellationToken);

    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, AdjuntoOperacion operacion, CancellationToken cancellationToken)
        => documentos.VerificarSucursalAsync(info.SucursalId,
            operacion == AdjuntoOperacion.Ver
                ? PermisosCanonicos.AlmacenEntradasLeerTodasSucursales
                : PermisosCanonicos.AlmacenEntradasGestionarTodasSucursales, cancellationToken);
}
