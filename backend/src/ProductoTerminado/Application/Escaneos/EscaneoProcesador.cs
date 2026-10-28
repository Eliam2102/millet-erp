using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.Application.Escaneos;

/// <summary>
/// Decide el resultado de un escaneo (alta de pieza o fila de bandeja). Muta el escaneo y las entidades
/// rastreadas, publica el evento al outbox, pero nunca llama a <c>SaveChanges</c>: lo hace quien lo invoca.
/// Todo camino termina llamando a un <c>Marcar*</c> (el default del escaneo es Excepcion).
/// </summary>
public sealed class EscaneoProcesador(ProductoTerminadoDbContext db, IPedidosPtReadPort pedidos, IPtSucursalPorClaveAwPort sucursales, IIntegrationEventPublisher eventos, IClock clock)
{
    public async Task ProcesarAsync(EscaneoPt e, int maxIntentos, CancellationToken ct)
    {
        // (1) identidad
        if (!int.TryParse(e.NumeroPiezaTexto, out var numeroPieza) || numeroPieza <= 0 || e.Posicion is null || string.IsNullOrWhiteSpace(e.Etiqueta))
        {
            e.MarcarExcepcion(MotivosPt.IdentidadIncompleta);
            return;
        }
        var posicion = e.Posicion.Value;

        // (2) sucursal
        if (e.SucursalId is null && !string.IsNullOrWhiteSpace(e.ClaveSucursalAw))
        {
            var id = await sucursales.ResolverAsync(e.ClaveSucursalAw.Trim(), ct);
            if (id is not null) e.AsignarSucursal(id.Value);
        }
        if (e.SucursalId is null)
        {
            e.MarcarExcepcion(MotivosPt.SucursalSinClaveAw);
            return;
        }
        var sucursalId = e.SucursalId.Value;

        // (3) pedido
        var origen = await pedidos.ObtenerPorNumeroAsync(e.EmpresaId, e.NumeroPedido, ct);
        if (origen is null)
        {
            e.MarcarPospuesto(MotivosPt.PedidoInexistente);
            e.Intentar(maxIntentos);
            return;
        }
        // P-11: pedido cancelado

        // (4) PedidoPt local
        var pedido = await db.PedidosPt.Include(x => x.Posiciones)
            .FirstOrDefaultAsync(x => x.EmpresaId == e.EmpresaId && x.NumeroPedido == e.NumeroPedido, ct);
        var lineas = origen.Lineas.Select(l => (l.Posicion, l.ProductoId, l.Descripcion, l.Cantidad, l.ClaveUnidadSat)).ToList();
        if (pedido is null)
        {
            pedido = PedidoPt.Crear(e.EmpresaId, origen.PedidoFacturableId, origen.NumeroPedido, origen.SucursalId, origen.CondicionPagoOrigen, origen.VersionOrigen);
            pedido.ActualizarPosiciones(origen.VersionOrigen, lineas);
            db.PedidosPt.Add(pedido);
        }
        // P-11: refresco por versión de origen

        // (5) posición
        var pos = pedido.Posiciones.FirstOrDefault(p => p.Posicion == posicion);
        if (pos is null)
        {
            e.MarcarExcepcion(MotivosPt.PosicionInexistente);
            return;
        }
        // P-11: cantidad no entera

        // (6) exceso
        if (numeroPieza > pos.PiezasEsperadas)
        {
            e.MarcarExcepcion(MotivosPt.PiezaExcedePosicion);
            return;
        }
        // P-11: reposición y ranura duplicada

        // (7) alta
        var ahora = clock.UtcNow;
        var pieza = PiezaPt.Alta(e.EmpresaId, pedido, posicion, numeroPieza, e.Etiqueta, sucursalId, e.RegistradoUtc, e.Id);
        db.Piezas.Add(pieza);
        db.Movimientos.Add(MovimientoPieza.Alta(pieza, e.Id.ToString(), ahora));
        e.MarcarAlta(pieza.Id);
        await eventos.PublishAsync(new PiezaIngresadaEvent(e.EmpresaId, ahora, pieza.Id, pedido.Id, pedido.NumeroPedido, posicion, numeroPieza, pos.ProductoId, sucursalId, pieza.EscaneadaAt), ct);
    }
}
