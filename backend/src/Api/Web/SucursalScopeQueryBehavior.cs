using MediatR;
using Millet.Administracion.Application.Abstractions;
using Millet.SharedKernel.Application;

namespace Millet.Api.Web;

public sealed class SucursalScopeQueryBehavior<TRequest, TResponse>(ICurrentUserContext user,
    ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales, DocumentoSucursalScope documentos,
    IHttpContextAccessor? http = null)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is ISucursalScopedQuery query)
        {
            query.SucursalesPermitidas = await permisos.TieneAsync(query.PermisoTodasSucursales, cancellationToken)
                ? null : user.UserId is Guid id ? await sucursales.ListarIdsAsync(id, cancellationToken) : Array.Empty<Guid>();
            var seleccion = (query as Millet.SharedKernel.Application.Inicio.ISucursalSeleccionadaQuery)?.SucursalId;
            // Inicio reutiliza los listados existentes para pendientes. Este encabezado aplica
            // la misma selección a sus consultas scoped, sin cambiar los contratos de los módulos.
            var encabezado = http?.HttpContext?.Request.Headers["X-Inicio-Sucursal-Id"].ToString();
            if (seleccion is null && !string.IsNullOrEmpty(encabezado))
                seleccion = Guid.TryParse(encabezado, out var idSeleccionado) && idSeleccionado != Guid.Empty
                    ? idSeleccionado : throw new BadHttpRequestException("La sucursal seleccionada no es válida.");
            if (seleccion is Guid seleccionada)
            {
                await SucursalScopeGuard.VerificarAsync(user.UserId, query.PermisoTodasSucursales, permisos,
                    (id, ct) => sucursales.EstaAsociadoAsync(id, seleccionada, ct), cancellationToken);
                query.SucursalesPermitidas = [seleccionada];
            }
            if (query is IDocumentoScopedQuery doc)
                doc.DocumentosPermitidos = query.SucursalesPermitidas is null ? null :
                    (await documentos.Puerto(doc.TipoDocumento).ListarAsync(doc.TipoDocumento, cancellationToken))
                    .Where(x => DocumentoSucursalScope.Permitido(x, query.SucursalesPermitidas))
                    .Select(x => x.Id).ToArray();
        }
        return await next();
    }
}
