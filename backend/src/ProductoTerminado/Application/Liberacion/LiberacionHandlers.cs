using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.Application.Liberacion;

public sealed class AplicarDecisionCxcHandler(ProductoTerminadoDbContext db, LiberacionPedido liberacion) : IRequestHandler<AplicarDecisionCxcCommand, bool>
{
    public async Task<bool> Handle(AplicarDecisionCxcCommand c, CancellationToken cancellationToken)
    {
        if (c.Resultado is not ("Liberado" or "LiberadoConOverride")) return false;
        var numero = c.PedidoRef.Trim();
        var pedido = await db.PedidosPt.FirstOrDefaultAsync(x => x.EmpresaId == c.EmpresaId && x.NumeroPedido == numero, cancellationToken);
        if (pedido is not { Liberacion: EstadoLiberacion.Retenido }) return false;
        if (!await liberacion.LiberarAsync(pedido, MotivosPt.DecisionCxc, cancellationToken)) return false;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed class BarrerRetenidosHandler(ProductoTerminadoDbContext db, LiberacionPedido liberacion, ILogger<BarrerRetenidosHandler> logger) : IRequestHandler<BarrerRetenidosCommand, int>
{
    public async Task<int> Handle(BarrerRetenidosCommand c, CancellationToken cancellationToken)
    {
        var retenidos = await db.PedidosPt.Where(x => x.Liberacion == EstadoLiberacion.Retenido).OrderBy(x => x.LiberacionAt).Take(c.Lote).ToListAsync(cancellationToken);
        var liberados = 0;
        foreach (var pedido in retenidos)
        {
            try
            {
                if (!await liberacion.ReevaluarAsync(pedido, cancellationToken)) continue;
                await db.SaveChangesAsync(cancellationToken);
                liberados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Otro proceso lo cambió a la vez, o falló su saldo: se sigue con el siguiente y se reintenta en el próximo barrido.
                logger.LogWarning(ex, "No se pudo reevaluar el pedido retenido {PedidoPtId}", pedido.Id);
                db.ChangeTracker.Clear();
            }
        }
        return liberados;
    }
}

public sealed class PonerSaldoDemoHandler(ProductoTerminadoDbContext db, ICurrentEmpresaContext empresa, LiberacionPedido liberacion) : IRequestHandler<PonerSaldoDemoCommand, bool>
{
    public async Task<bool> Handle(PonerSaldoDemoCommand c, CancellationToken cancellationToken)
    {
        var empresaId = empresa.Current ?? throw new UnauthorizedAccessException();
        var numero = c.NumeroPedido.Trim();
        var fila = await db.SaldosDemo.FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.NumeroPedido == numero, cancellationToken);
        if (fila is null) db.SaldosDemo.Add(new SaldoDemo(empresaId, numero, c.Saldo)); else fila.Poner(c.Saldo);
        await db.SaveChangesAsync(cancellationToken);

        var pedido = await db.PedidosPt.FirstOrDefaultAsync(x => x.EmpresaId == empresaId && x.NumeroPedido == numero, cancellationToken);
        if (pedido is null || !await liberacion.ReevaluarAsync(pedido, cancellationToken)) return false;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
