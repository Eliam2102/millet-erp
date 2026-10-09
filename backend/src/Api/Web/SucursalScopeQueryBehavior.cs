using MediatR;
using Millet.Administracion.Application.Abstractions;
using Millet.SharedKernel.Application;

namespace Millet.Api.Web;

public sealed class SucursalScopeQueryBehavior<TRequest, TResponse>(ICurrentUserContext user,
    ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales, DocumentoSucursalScope documentos)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is ISucursalScopedQuery query)
        {
            query.SucursalesPermitidas = await permisos.TieneAsync(query.PermisoTodasSucursales, cancellationToken)
                ? null : user.UserId is Guid id ? await sucursales.ListarIdsAsync(id, cancellationToken) : Array.Empty<Guid>();
            if (query is IDocumentoScopedQuery doc)
                doc.DocumentosPermitidos = query.SucursalesPermitidas is null ? null :
                    (await documentos.Puerto(doc.TipoDocumento).ListarAsync(doc.TipoDocumento, cancellationToken))
                    .Where(x => DocumentoSucursalScope.Permitido(x, query.SucursalesPermitidas))
                    .Select(x => x.Id).ToArray();
        }
        return await next();
    }
}
