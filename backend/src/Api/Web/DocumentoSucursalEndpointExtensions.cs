namespace Millet.Api.Web;

public static class DocumentoSucursalEndpointExtensions
{
    public static RouteGroupBuilder WithDocumentoSucursalScope(this RouteGroupBuilder group,
        string tipo, string permiso, string parametroId = "id")
    {
        group.AddEndpointFilter(async (context, next) =>
        {
            if (context.HttpContext.Request.RouteValues.TryGetValue(parametroId, out var value)
                && Guid.TryParse(value?.ToString(), out var id))
            {
                var scope = context.HttpContext.RequestServices.GetRequiredService<DocumentoSucursalScope>();
                var accion = HttpMethods.IsGet(context.HttpContext.Request.Method) ? "leer" : "gestionar";
                await scope.VerificarAsync(tipo, id, $"{permiso}.{accion}-todas-sucursales", context.HttpContext.RequestAborted);
            }
            return await next(context);
        });
        return group;
    }
}
