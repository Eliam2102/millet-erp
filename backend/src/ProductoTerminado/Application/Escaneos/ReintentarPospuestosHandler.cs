using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.Application.Escaneos;

public sealed class ReintentarPospuestosHandler(ProductoTerminadoDbContext db, EscaneoProcesador procesador, IClock clock, ILogger<ReintentarPospuestosHandler> logger)
    : IRequestHandler<ReintentarPospuestosCommand, int>
{
    public async Task<int> Handle(ReintentarPospuestosCommand c, CancellationToken cancellationToken)
    {
        var pendientes = await db.Escaneos
            .Where(x => x.EmpresaId == c.EmpresaId && x.Resultado == ResultadoEscaneo.Pospuesto && x.ResueltoAt == null)
            .OrderBy(x => x.RecibidoAt).Take(c.Lote).ToListAsync(cancellationToken);

        var resueltos = 0;
        foreach (var e in pendientes)
        {
            try
            {
                await procesador.ProcesarAsync(e, c.MaxIntentos, cancellationToken);
                // D13: pasado el plazo sin que aparezca el pedido, la fila se promueve a Excepcion (conserva motivo e intentos).
                if (e.Resultado == ResultadoEscaneo.Pospuesto && clock.UtcNow - e.RecibidoAt >= TimeSpan.FromHours(c.PospuestoHoras))
                    e.MarcarExcepcion(MotivosPt.PedidoInexistente);
                await db.SaveChangesAsync(cancellationToken);
                if (e.Resultado != ResultadoEscaneo.Pospuesto) resueltos++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "No se pudo reintentar el escaneo pospuesto {EscaneoId}", e.Id);
                // Se descarta solo el estado de la fila fallida y sus hijos; las demás filas del lote siguen rastreadas.
                foreach (var entry in db.ChangeTracker.Entries().Where(x => !pendientes.Contains(x.Entity) || ReferenceEquals(x.Entity, e)).ToList())
                    entry.State = EntityState.Detached;
            }
        }
        return resueltos;
    }
}
