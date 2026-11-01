namespace Millet.Facturacion.Domain.Ports;

/// <summary>Folio de transferencia visto desde Caja (F6.6). <c>Estado</c>: Vigente, Usado o Cancelado.</summary>
public sealed record FolioTransferenciaCajaDto(
    Guid Id, string Folio, Guid ClienteId, decimal Importe, string Estado, Guid? CobroMostradorId, string? Pedido)
{
    public bool Vigente => Estado == "Vigente";
}

/// <summary>
/// F6.6 (R16): folios de confirmación de transferencia que emite Ingresos en
/// Tesorería. Caja los lee para validar el cobro con forma 03 (vigente, del
/// cliente y por el importe) y para ofrecer los vigentes del cliente. El
/// adaptador vive en <c>Millet.Api</c> y consulta el puerto público de Tesorería.
/// </summary>
public interface IFolioTransferenciaReadPort
{
    Task<IReadOnlyList<FolioTransferenciaCajaDto>> ConsultarAsync(IReadOnlyCollection<string> folios, CancellationToken cancellationToken);

    Task<IReadOnlyList<FolioTransferenciaCajaDto>> VigentesDelClienteAsync(Guid clienteId, CancellationToken cancellationToken);
}
