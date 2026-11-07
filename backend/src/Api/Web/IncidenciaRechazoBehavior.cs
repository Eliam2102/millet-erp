using MediatR;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.Api.Web;

/// <summary>
/// U-4 (86e3yzemw): cuando un paso se rechaza con <see cref="IncidenciaRechazoException"/>, abre su incidencia y deja
/// que el rechazo siga su curso (422). Corre fuera del handler, así que la transacción del módulo ya se revirtió y la
/// incidencia se guarda sola en Compartido. Si abrirla falla, se registra en el log y el usuario recibe el rechazo
/// original igual (nunca se cambia el error por el de la incidencia).
/// </summary>
public sealed class IncidenciaRechazoBehavior<TRequest, TResponse>(IServiceProvider services, ICurrentUserContext usuario,
    ILogger<IncidenciaRechazoBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (IncidenciaRechazoException rechazo) when (rechazo.Incidencia is null)
        {
            await AbrirAsync(rechazo, cancellationToken);
            throw;
        }
    }

    private async Task AbrirAsync(IncidenciaRechazoException rechazo, CancellationToken cancellationToken)
    {
        var solicitud = rechazo.Solicitud;
        if (solicitud.ResponsableUsuarioId == Guid.Empty)
        {
            if (usuario.UserId is not Guid actual)
                return;
            solicitud = solicitud with { ResponsableUsuarioId = actual };
        }
        if (services.GetService<IIncidenciasPort>() is not { } incidencias)
            return;
        try
        {
            rechazo.Incidencia = await incidencias.AbrirAsync(solicitud, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo abrir la incidencia del rechazo {Code} en el paso {Paso}.", rechazo.Code, solicitud.Paso);
        }
    }
}
