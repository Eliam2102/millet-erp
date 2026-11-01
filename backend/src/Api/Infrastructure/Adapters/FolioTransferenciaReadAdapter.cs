using MediatR;
using Millet.Facturacion.Domain.Ports;
using Millet.Tesoreria.Application.PublicPorts;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// F6.6: Caja (Facturación) lee los folios de transferencia (dueño: Tesorería) por
/// el puerto público, mismo patrón que <see cref="CuentaOrdenanteMovimientoReadAdapter"/>.
/// </summary>
public sealed class FolioTransferenciaReadAdapter(ISender sender) : IFolioTransferenciaReadPort
{
    public async Task<IReadOnlyList<FolioTransferenciaCajaDto>> ConsultarAsync(
        IReadOnlyCollection<string> folios, CancellationToken cancellationToken) =>
        Convertir(await sender.Send(new ConsultarFoliosTransferenciaQuery(folios), cancellationToken));

    public async Task<IReadOnlyList<FolioTransferenciaCajaDto>> VigentesDelClienteAsync(
        Guid clienteId, CancellationToken cancellationToken) =>
        Convertir(await sender.Send(new FoliosTransferenciaVigentesClienteQuery(clienteId), cancellationToken));

    private static List<FolioTransferenciaCajaDto> Convertir(IReadOnlyList<FolioTransferenciaPublicoDto> folios) =>
        folios.Select(f => new FolioTransferenciaCajaDto(f.Id, f.Folio, f.ClienteId, f.Importe, f.Estado, f.CobroMostradorId, f.Pedido)).ToList();
}
