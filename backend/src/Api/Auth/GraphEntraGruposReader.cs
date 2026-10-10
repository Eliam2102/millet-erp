using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Millet.Api.Auth.Options;
using Millet.Identidad.Application.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Auth;

public sealed class GraphEntraGruposReader(IOptions<EntraIdOptions> options) : IEntraGruposReadPort
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];
    public async Task<IReadOnlyList<string>> ListarAsync(string objectId, CancellationToken ct)
    {
        var o = options.Value;
        if (!Guid.TryParse(objectId, out _) || string.IsNullOrWhiteSpace(o.ClientSecret))
            throw new ForbiddenException("ENTRA_GRUPOS_NO_DISPONIBLES",
                "No se pudieron verificar tus grupos de Microsoft. Solicita al administrador configurar la consulta de grupos.");
        try
        {
            using var graph = new GraphServiceClient(new ClientSecretCredential(o.TenantId, o.ClientId, o.ClientSecret),
                Scopes);
            var ids = await graph.Users[objectId].GetMemberGroups.PostAsGetMemberGroupsPostResponseAsync(
                new Microsoft.Graph.Users.Item.GetMemberGroups.GetMemberGroupsPostRequestBody { SecurityEnabledOnly = false },
                cancellationToken: ct);
            return ids?.Value ?? throw new InvalidOperationException("Graph no devolvió la pertenencia.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ForbiddenException("ENTRA_GRUPOS_NO_DISPONIBLES",
                "Microsoft no permitió verificar tus grupos. Solicita al administrador revisar los permisos de Graph e inicia sesión de nuevo.");
        }
    }
}
