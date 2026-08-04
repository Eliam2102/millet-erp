namespace Millet.Identidad.Application.Ports;

/// <summary>
/// Puerto out-port para resolver datos de un usuario de Microsoft Entra ID
/// a partir de su email (F-Admin-PR4.1). Lo consume
/// <c>CrearUsuarioCommand</c> cuando el caller no provee
/// <c>EntraIdObjectId</c> explícito: si la resolución retorna un valor,
/// se usa; si retorna <c>null</c>, el handler genera un placeholder
/// <c>dev-{email}</c> en línea con ADR-0015.
///
/// <para>
/// Implementación productiva (post-MVP): hace una llamada al Microsoft
/// Graph API <c>GET /users?$filter=mail eq '{email}'</c> y mapea el
/// objectId + displayName. Para MVP la stub
/// <c>LocalEntraIdResolverNoOp</c> retorna <c>null</c> siempre — el
/// frontend admin entrega los datos manualmente o el sistema asigna un
/// placeholder de dev.
/// </para>
/// </summary>
public interface IEntraIdResolverPort
{
    Task<EntraIdResolution?> ResolverPorEmailAsync(string email, CancellationToken ct);
}

/// <summary>
/// Resultado positivo de una resolución contra Entra ID: el
/// <see cref="ObjectId"/> es el oid (GUID o string sintético en dev) y
/// el <see cref="NombreCompleto"/> es el <c>displayName</c> del directorio.
/// </summary>
public sealed record EntraIdResolution(string ObjectId, string NombreCompleto);
