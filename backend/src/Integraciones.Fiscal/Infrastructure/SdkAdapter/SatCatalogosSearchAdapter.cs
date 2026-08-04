using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Fiscal.Domain.Exceptions;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;

/// <summary>
/// Implementación de <see cref="ISatCatalogosSearchPort"/> con el SDK
/// oficial FiscalAPI (<c>sdk.Catalogs</c>). Puerto separado de
/// <see cref="Domain.Ports.IFiscalApiSdkClient"/> — aquel está
/// organizado por etapas del flujo de descarga masiva; los catálogos
/// son una preocupación transversal de captura fiscal (FAC-DET-PR1).
///
/// <para>
/// Toda falla (SDK deshabilitado, empresa sin ConfiguracionPac, error
/// HTTP del PAC, catálogo renombrado) se traduce a
/// <see cref="CatalogoSatNoDisponibleException"/> → HTTP 503. Para el
/// operador el contrato es binario: el catálogo responde o se captura
/// manual; el detalle queda en el log.
/// </para>
/// </summary>
public sealed class SatCatalogosSearchAdapter : ISatCatalogosSearchPort
{
    private readonly IFiscalApiSdkClientFactory _factory;
    private readonly IOptionsMonitor<FiscalApiSdkAdapterOptions> _options;
    private readonly ILogger<SatCatalogosSearchAdapter> _logger;

    public SatCatalogosSearchAdapter(
        IFiscalApiSdkClientFactory factory,
        IOptionsMonitor<FiscalApiSdkAdapterOptions> options,
        ILogger<SatCatalogosSearchAdapter> logger)
    {
        _factory = factory;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CatalogoSatItem>> BuscarAsync(
        Guid empresaId,
        CatalogoSat catalogo,
        string searchText,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var nombre = NombreRemoto(catalogo, _options.CurrentValue);
        try
        {
            var sdk = await _factory.GetClientAsync(empresaId, cancellationToken);
            var resp = await sdk.Catalogs.SearchCatalogAsync(
                nombre, searchText, pageNumber: 1, pageSize: pageSize);
            if (!resp.Succeeded)
                throw new CatalogoSatNoDisponibleException(
                    $"búsqueda en '{nombre}' falló (HTTP {resp.HttpStatusCode}): {resp.Message ?? "(sin mensaje)"}");

            return resp.Data.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Id))
                .Select(i => new CatalogoSatItem(i.Id, i.Description ?? string.Empty))
                .ToList();
        }
        catch (Exception ex) when (ex is not CatalogoSatNoDisponibleException
                                       and not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "[SatCatalogosSearch] catálogo={Catalogo} buscar='{Texto}': PAC no disponible.",
                nombre, searchText);
            throw new CatalogoSatNoDisponibleException(ex.Message, ex);
        }
    }

    public async Task<CatalogoSatItem?> ObtenerPorCodigoAsync(
        Guid empresaId,
        CatalogoSat catalogo,
        string codigo,
        CancellationToken cancellationToken)
    {
        var nombre = NombreRemoto(catalogo, _options.CurrentValue);
        try
        {
            var sdk = await _factory.GetClientAsync(empresaId, cancellationToken);
            var resp = await sdk.Catalogs.GetRecordByIdAsync(nombre, codigo);

            // Código inexistente = null (no es indisponibilidad): el PAC
            // responde no-exitoso para claves que no están en el catálogo.
            if (!resp.Succeeded)
            {
                _logger.LogDebug(
                    "[SatCatalogosSearch] catálogo={Catalogo} código='{Codigo}' sin match (HTTP {Status}).",
                    nombre, codigo, resp.HttpStatusCode);
                return null;
            }

            return string.IsNullOrWhiteSpace(resp.Data?.Id)
                ? null
                : new CatalogoSatItem(resp.Data.Id, resp.Data.Description ?? string.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "[SatCatalogosSearch] catálogo={Catalogo} código='{Codigo}': PAC no disponible.",
                nombre, codigo);
            throw new CatalogoSatNoDisponibleException(ex.Message, ex);
        }
    }

    internal static string NombreRemoto(CatalogoSat catalogo, FiscalApiSdkAdapterOptions opts) =>
        catalogo switch
        {
            CatalogoSat.ClaveProdServ       => opts.CatalogoClaveProdServ,
            CatalogoSat.ClaveUnidad         => opts.CatalogoClaveUnidad,
            CatalogoSat.ObjetoImp           => opts.CatalogoObjetoImp,
            CatalogoSat.FraccionArancelaria => opts.CatalogoFraccionArancelaria,
            CatalogoSat.UnidadAduana        => opts.CatalogoUnidadAduana,
            CatalogoSat.Pais                => opts.CatalogoPais,
            CatalogoSat.ClavePedimento     => opts.CatalogoClavePedimento,
            _ => throw new ArgumentOutOfRangeException(nameof(catalogo)),
        };
}
