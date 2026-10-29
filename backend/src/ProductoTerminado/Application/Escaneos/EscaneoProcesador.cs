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

        // (2) sucursal por clave A+W, si el escaneo la trae (Demo)
        var conClave = !string.IsNullOrWhiteSpace(e.ClaveSucursalAw);
        if (e.SucursalId is null && conClave)
        {
            var id = await sucursales.ResolverAsync(e.ClaveSucursalAw!.Trim(), ct);
            if (id is not null) e.AsignarSucursal(id.Value);
        }
        if (e.SucursalId is null && conClave)
        {
            e.MarcarExcepcion(MotivosPt.SucursalSinClaveAw);
            return;
        }

        // (3) pedido
        var origen = await pedidos.ObtenerPorNumeroAsync(e.EmpresaId, e.NumeroPedido, ct);
        if (origen is null)
        {
            e.MarcarPospuesto(MotivosPt.PedidoInexistente);
            e.Intentar(maxIntentos);
            return;
        }
        // (3a) sin clave en el escaneo (Real: alcimdb no trae sucursal): la del pedido, que ya viene de MILMAIN OR_AVBEREICH (Jorge E08-4)
        if (e.SucursalId is null) e.AsignarSucursal(origen.SucursalId);
        var sucursalId = e.SucursalId!.Value;
        // (3b) pedido cancelado en el origen: no se reciben piezas
        if (string.Equals(origen.Estado, "Cancelado", StringComparison.OrdinalIgnoreCase))
        {
            e.MarcarExcepcion(MotivosPt.PedidoCancelado);
            return;
        }

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
        else if (origen.VersionOrigen is { } v && v > (pedido.VersionOrigen ?? long.MinValue))
        {
            // Refresco por versión de origen: solo agrega/actualiza posiciones, nunca elimina piezas ni baja conteos.
            pedido.ActualizarPosiciones(v, lineas);
        }

        // (5) posición
        var pos = pedido.Posiciones.FirstOrDefault(p => p.Posicion == posicion);
        if (pos is null)
        {
            e.MarcarExcepcion(MotivosPt.PosicionInexistente);
            return;
        }
        // (5b) cantidad no entera: no se pueden derivar piezas esperadas
        if (pos.CantidadNoEntera)
        {
            e.MarcarExcepcion(MotivosPt.CantidadNoEntera);
            return;
        }

        // (6) exceso
        if (numeroPieza > pos.PiezasEsperadas)
        {
            e.MarcarExcepcion(MotivosPt.PiezaExcedePosicion);
            return;
        }
        // (6b) reposición: la etiqueta ya está en PT; no crea pieza, movimiento ni evento (el avance nunca supera lo esperado)
        var etiqueta = e.Etiqueta.Trim();
        if (await db.Piezas.AnyAsync(x => x.EmpresaId == e.EmpresaId && x.Etiqueta == etiqueta, ct))
        {
            e.MarcarReposicion();
            return;
        }
        // (6c) otra etiqueta ya ocupa la misma ranura (pedido, posición, número de pieza)
        if (await db.Piezas.AnyAsync(x => x.EmpresaId == e.EmpresaId && x.PedidoPtId == pedido.Id && x.Posicion == posicion && x.NumeroPieza == numeroPieza, ct))
        {
            e.MarcarExcepcion(MotivosPt.PiezaDuplicada);
            return;
        }

        // (7) alta
        var ahora = clock.UtcNow;
        var pieza = PiezaPt.Alta(e.EmpresaId, pedido, posicion, numeroPieza, e.Etiqueta, sucursalId, e.RegistradoUtc, e.Id);
        db.Piezas.Add(pieza);
        db.Movimientos.Add(MovimientoPieza.Alta(pieza, e.Id.ToString(), ahora));
        e.MarcarAlta(pieza.Id);
        await eventos.PublishAsync(new PiezaIngresadaEvent(e.EmpresaId, ahora, pieza.Id, pedido.Id, pedido.NumeroPedido, posicion, numeroPieza, pos.ProductoId, sucursalId, pieza.EscaneadaAt), ct);
    }
}
