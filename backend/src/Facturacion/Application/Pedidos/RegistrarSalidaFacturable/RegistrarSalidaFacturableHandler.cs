using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Pedidos.RegistrarSalidaFacturable;

public sealed class RegistrarSalidaFacturableHandler(FacturacionDbContext db, IClock clock)
    : IRequestHandler<RegistrarSalidaFacturableCommand, SalidaFacturableResponse>
{
    public async Task<SalidaFacturableResponse> Handle(RegistrarSalidaFacturableCommand command, CancellationToken cancellationToken)
    {
        var lineas = command.Lineas.Select(l => (l.Posicion, l.Cantidad)).ToList();
        var referencia = command.Referencia.Trim();

        var existente = await db.SalidasFacturables.Include(s => s.Lineas)
            .FirstOrDefaultAsync(s => s.Origen == command.Origen && s.Referencia == referencia, cancellationToken);
        if (existente is not null)
            return Reenvio(existente, command, lineas);

        var pedido = await CargarPedidoAsync(command, cancellationToken);
        var previas = await db.SalidasFacturables.Include(s => s.Lineas)
            .Where(s => s.PedidoFacturableId == pedido.Id)
            .ToListAsync(cancellationToken);

        var salida = SalidaFacturable.Registrar(pedido, previas, command.Origen, referencia, lineas, clock.UtcNow);
        db.SalidasFacturables.Add(salida);
        await db.SaveChangesAsync(cancellationToken);
        return SalidaFacturableResponse.De(salida, yaRegistrada: false);
    }

    private static SalidaFacturableResponse Reenvio(
        SalidaFacturable existente, RegistrarSalidaFacturableCommand command, IReadOnlyList<(int, decimal)> lineas)
    {
        if (command.PedidoFacturableId is Guid id && id != existente.PedidoFacturableId || !existente.MismasCantidades(lineas))
            throw new BusinessRuleException("SALIDA_REFERENCIA_DISTINTA",
                $"La salida '{existente.Referencia}' ya está registrada como parcialidad {existente.NumeroParcialidad} con otras cantidades; no se cambia.");
        return SalidaFacturableResponse.De(existente, yaRegistrada: true);
    }

    private async Task<PedidoFacturable> CargarPedidoAsync(RegistrarSalidaFacturableCommand command, CancellationToken ct)
    {
        var query = db.PedidosFacturables.Include(p => p.Lineas);
        var pedido = command.PedidoFacturableId is Guid id
            ? await query.FirstOrDefaultAsync(p => p.Id == id, ct)
            : await query.Where(p => p.Origen == OrigenPedido.Aw && p.NumeroPedido == command.NumeroPedidoAw)
                .OrderByDescending(p => p.VersionOrigen).FirstOrDefaultAsync(ct);
        return pedido ?? throw new EntityNotFoundException("PEDIDO_NO_ENCONTRADO",
            $"No existe el pedido {command.PedidoFacturableId?.ToString() ?? command.NumeroPedidoAw}.");
    }
}
