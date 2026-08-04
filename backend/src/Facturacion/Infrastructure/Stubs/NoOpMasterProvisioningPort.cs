using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Stubs;

/// <summary>
/// Fallback DEV de la auto-provisión (deuda &lt;MasterProvisioningAw&gt; CERRADA por
/// ADR-0048 PR4: <c>AwMasterProvisioningAdapter</c> en Integraciones.Aw/Pedidos,
/// activo vía toggle AwIntegracionDb). <b>No</b>
/// auto-crea (devuelve <c>null</c>) — la matriz de ingesta cae a excepción
/// (cliente/artículo no existe) hasta que <c>DatosMaestros</c> implemente la
/// provisión real consumiendo <c>IAwClientesReader</c>/<c>IAwArticulosReader</c>
/// de <c>Integraciones.Aw</c> (§3.bis.6). El contrato + el wireup de la matriz
/// ya quedan listos: cuando el adapter real reemplace este stub, el Alta de A+W
/// creará el cliente/artículo en automático sin tocar más código de Facturación.
/// </summary>
public sealed class NoOpMasterProvisioningPort : IMasterProvisioningPort
{
    private readonly ILogger<NoOpMasterProvisioningPort> _logger;

    public NoOpMasterProvisioningPort(ILogger<NoOpMasterProvisioningPort> logger) => _logger = logger;

    public Task<ClienteFiscalLectura?> EnsureClienteDesdeAwAsync(string clienteRef, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "[NoOpMasterProvisioningPort] EnsureCliente({Ref}) → null (stub fallback dev — adapter real vía toggle AwIntegracionDb, ADR-0048)",
            clienteRef);
        return Task.FromResult<ClienteFiscalLectura?>(null);
    }

    public Task<ProductoFiscalLectura?> EnsureArticuloDesdeAwAsync(string articuloRef, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "[NoOpMasterProvisioningPort] EnsureArticulo({Ref}) → null (stub fallback dev — adapter real vía toggle AwIntegracionDb, ADR-0048)",
            articuloRef);
        return Task.FromResult<ProductoFiscalLectura?>(null);
    }
}
