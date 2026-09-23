using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Millet.Identidad.Application.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Infrastructure.Adapters;

/// <summary>Busca cuentas existentes en el tenant; nunca crea cuentas por inferencia.</summary>
public sealed class GraphEntraIdResolver : IEntraIdResolverPort, IDisposable
{
    private readonly GraphServiceClient _graph;

    public GraphEntraIdResolver(IOptions<EntraDirectoryOptions> options)
    {
        var config = options.Value;
        if (!config.IsConfigured)
            throw new InvalidOperationException("EntraDirectory no está configurado.");
        _graph = new GraphServiceClient(
            new ClientSecretCredential(config.TenantId, config.ClientId, config.ClientSecret),
            ["https://graph.microsoft.com/.default"]);
    }

    public async Task<EntraIdResolution?> ResolverPorEmailAsync(string email, CancellationToken ct)
    {
        var escaped = email.Trim().Replace("'", "''", StringComparison.Ordinal);
        var result = await _graph.Users.GetAsync(req =>
        {
            req.QueryParameters.Filter = $"mail eq '{escaped}' or userPrincipalName eq '{escaped}'";
            req.QueryParameters.Select = ["id", "displayName", "mail", "userPrincipalName", "accountEnabled"];
            req.QueryParameters.Top = 2;
        }, ct);

        var accounts = result?.Value ?? [];
        if (accounts.Count > 1)
            throw new ConflictException("ENTRA_CUENTA_AMBIGUA", "Hay varias cuentas de Microsoft que coinciden con este correo.");
        var account = accounts.SingleOrDefault();
        if (account is not null && account.AccountEnabled != true)
            throw new BusinessRuleException("ENTRA_CUENTA_INACTIVA", "No se pudo confirmar que la cuenta Microsoft esté habilitada.");
        return account?.Id is { Length: > 0 } oid
            ? new EntraIdResolution(oid, account.DisplayName ?? email)
            : null;
    }

    public void Dispose() => _graph.Dispose();
}
