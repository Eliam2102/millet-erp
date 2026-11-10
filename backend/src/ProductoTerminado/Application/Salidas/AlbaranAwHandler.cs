using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.Application.Salidas;

/// <summary>Un albarán de A+W para una posición: folio, fecha y piezas que salieron con él.</summary>
public sealed record AlbaranAwPiezas(int Folio, DateOnly Fecha, int Piezas);

/// <summary>
/// Todos los albaranes de A+W de una posición del pedido (lote de PT = pedido-posición). Lo manda el lector de
/// Integraciones.Aw por cada posición con un albarán nuevo. Devuelve cuántas piezas descontó.
/// </summary>
public sealed record AplicarAlbaranesAwCommand(Guid EmpresaId, string NumeroPedido, int Posicion, IReadOnlyList<AlbaranAwPiezas> Albaranes) : IRequest<int>;

/// <summary>
/// Descuenta del PT las piezas que salieron con albaranes de A+W (decisión 10-nov: el albarán vive en A+W).
/// Idempotente por pedido + posición + albarán (movimiento <c>aw-albaran:{folio}</c>). No duplica lo que ya salió por la
/// hoja de salida del ERP: solo descuenta lo que A+W reporta por encima de las piezas que ya no están en PT
/// (cargadas, embarcadas, entregadas o en revisión). Si aún no hay piezas en PT, descuenta lo que haya y el resto en
/// la siguiente lectura. No cambia el estatus del pedido (71/72 los lleva la hoja de salida).
/// </summary>
public sealed class AplicarAlbaranesAwHandler(ProductoTerminadoDbContext db, IClock clock) : IRequestHandler<AplicarAlbaranesAwCommand, int>
{
    public async Task<int> Handle(AplicarAlbaranesAwCommand c, CancellationToken cancellationToken)
    {
        var numero = c.NumeroPedido.Trim();
        var pedido = await db.PedidosPt.FirstOrDefaultAsync(p => p.EmpresaId == c.EmpresaId && p.NumeroPedido == numero, cancellationToken);
        if (pedido is null) return 0;
        var piezas = await db.Piezas.Where(p => p.PedidoPtId == pedido.Id && p.Posicion == c.Posicion)
            .OrderBy(p => p.NumeroPieza).ToListAsync(cancellationToken);
        if (piezas.Count == 0) return 0;

        var ids = piezas.Select(p => p.Id).ToList();
        var referencias = c.Albaranes.Select(a => MovimientoPieza.ReferenciaAlbaranAw(a.Folio)).ToList();
        var aplicadas = (await db.Movimientos
                .Where(m => ids.Contains(m.PiezaId) && m.Tipo == TipoMovimientoPieza.Salida && referencias.Contains(m.Referencia))
                .Select(m => m.Referencia).ToListAsync(cancellationToken))
            .GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());

        var fuera = piezas.Count(p => p.Estado is EstadoPieza.Cargada or EstadoPieza.Embarcada or EstadoPieza.Entregada or EstadoPieza.EnRevision);
        var porSalir = c.Albaranes.Sum(a => Math.Max(0, a.Piezas)) - fuera;
        var enPt = new Queue<PiezaPt>(piezas.Where(p => p.Estado == EstadoPieza.EnPT));
        var ahora = clock.UtcNow;
        var descontadas = 0;
        foreach (var a in c.Albaranes.OrderBy(a => a.Fecha).ThenBy(a => a.Folio))
        {
            var falta = a.Piezas - aplicadas.GetValueOrDefault(MovimientoPieza.ReferenciaAlbaranAw(a.Folio));
            for (; falta > 0 && porSalir > 0 && enPt.TryDequeue(out var pz); falta--, porSalir--, descontadas++)
            {
                pz.Cargar();
                pz.Embarcar();
                db.Movimientos.Add(MovimientoPieza.SalidaAlbaranAw(pz, EstadoPieza.EnPT, a.Folio, ahora));
            }
        }
        if (descontadas > 0) await db.SaveChangesAsync(cancellationToken);
        return descontadas;
    }
}
