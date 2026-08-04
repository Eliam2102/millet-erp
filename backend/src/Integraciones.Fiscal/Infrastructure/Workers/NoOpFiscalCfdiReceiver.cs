using Microsoft.Extensions.Logging;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Integraciones.Fiscal.Infrastructure.Workers;

/// <summary>
/// Implementación default de <see cref="IFiscalCfdiReceiver"/> que solo
/// loggea. Sirve cuando ningún otro módulo registró un receiver real
/// (tests aislados o ambientes sin CxP).
///
/// <para>
/// El wiring DI registra esta implementación con <c>TryAddScoped</c>: si
/// CxP ya registró su <c>FiscalCfdiReceiverAdapter</c>, este NoOp queda
/// fuera del contenedor automáticamente.
/// </para>
/// </summary>
public sealed class NoOpFiscalCfdiReceiver : IFiscalCfdiReceiver
{
    private readonly ILogger<NoOpFiscalCfdiReceiver> _logger;

    public NoOpFiscalCfdiReceiver(ILogger<NoOpFiscalCfdiReceiver> logger)
    {
        _logger = logger;
    }

    public Task IngresarCfdiAsync(CfdiCosechadoPayload payload, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[NoOpFiscalCfdiReceiver] empresa={Empresa} rfc={Rfc} uuid={Uuid} emisor={Emisor} total={Total} tipo={Tipo} estatus={Estatus} xmlBytes={XmlBytes}",
            payload.EmpresaId, payload.RfcReceptorMillet, payload.Uuid,
            payload.RfcEmisor, payload.Total, payload.TipoComprobante, payload.EstatusSat,
            payload.XmlBytes.Length);
        return Task.CompletedTask;
    }
}
