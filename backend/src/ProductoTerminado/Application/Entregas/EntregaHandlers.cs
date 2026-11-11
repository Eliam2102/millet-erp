using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Application.Salidas;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.Application.Entregas;

// Permiso y alcance: los mismos que el resto de la hoja de salida (excepciones.gestionar para confirmar; piezas.ver para ver la evidencia).
internal static class EntregaImagen
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];

    /// <summary>Decodifica una imagen en base64 (con o sin prefijo data URL) y valida su tipo real por los primeros bytes y su tamaño.</summary>
    public static byte[]? Decodificar(string? texto, bool permiteJpeg, int maximo, string que)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var coma = texto.IndexOf(',');
        var base64 = texto.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && coma > 0 ? texto[(coma + 1)..] : texto;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { throw new BusinessRuleException("PT_ENTREGA_IMAGEN_INVALIDA", $"La {que} no es una imagen válida."); }
        if (bytes.Length > maximo) throw new BusinessRuleException("PT_ENTREGA_IMAGEN_GRANDE", $"La {que} pesa demasiado (máximo {maximo / 1000} KB).");
        var esPng = bytes.AsSpan().StartsWith(Png);
        if (!esPng && !(permiteJpeg && bytes.AsSpan().StartsWith(Jpeg))) throw new BusinessRuleException("PT_ENTREGA_IMAGEN_INVALIDA", $"La {que} no es una imagen válida.");
        return bytes;
    }

    public static string? ADataUrl(byte[]? bytes) =>
        bytes is null ? null : $"data:{(bytes.AsSpan().StartsWith(Png) ? "image/png" : "image/jpeg")};base64,{Convert.ToBase64String(bytes)}";
}

public sealed class ConfirmarEntregaHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, IIntegrationEventPublisher eventos, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<ConfirmarEntregaCommand, HojaSalidaDto>
{
    public async Task<HojaSalidaDto> Handle(ConfirmarEntregaCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        var hoja = await SalidaMapeo.Completa(db).FirstOrDefaultAsync(x => x.Id == c.HojaId, cancellationToken)
            ?? throw new EntityNotFoundException("PT_SALIDA_NO_ENCONTRADA", "No existe la hoja de salida.");
        await acceso.VerificarSucursalAsync(hoja.SucursalId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        var empresaId = empresa.Current ?? throw new UnauthorizedAccessException();
        if (hoja.Estado != EstadoSalida.Emitida) throw new BusinessRuleException("PT_ENTREGA_HOJA_NO_EMITIDA", "La hoja de salida aún no se emite: no hay nada que entregar.");
        var albaran = hoja.Albaranes.FirstOrDefault(a => a.Id == c.AlbaranId) ?? throw new EntityNotFoundException("PT_ALBARAN_NO_ENCONTRADO", "El albarán no es de esta hoja.");
        if (albaran.Piezas.Count == 0) throw new BusinessRuleException("PT_ENTREGA_ALBARAN_VACIO", "El albarán no llevó piezas.");
        if (await db.Entregas.AnyAsync(e => e.AlbaranPtId == albaran.Id, cancellationToken))
            throw new BusinessRuleException("PT_ENTREGA_YA_CONFIRMADA", "La entrega de este albarán ya está confirmada.");

        var piezaIds = albaran.Piezas.Select(x => x.PiezaId).ToList();
        var piezas = await db.Piezas.Where(p => piezaIds.Contains(p.Id)).ToListAsync(cancellationToken);
        var rechazadas = (c.Rechazadas ?? []).Select(e => e.Trim()).Distinct().ToList();
        var ajenas = rechazadas.Except(piezas.Select(p => p.Etiqueta)).ToList();
        if (ajenas.Count > 0) throw new BusinessRuleException("PT_ENTREGA_PIEZA_AJENA", $"Piezas que no son de este albarán: {string.Join(", ", ajenas)}");
        if (c.Cobro is not null && rechazadas.Count == piezas.Count)
            throw new BusinessRuleException("PT_ENTREGA_COBRO_SIN_ENTREGA", "No se cobra una entrega rechazada por completo.");

        var firma = EntregaImagen.Decodificar(c.Firma, false, EntregaPt.MaxFirmaBytes, "firma");
        var foto = EntregaImagen.Decodificar(c.Foto, true, EntregaPt.MaxFotoBytes, "foto");
        var ahora = clock.UtcNow;
        var entrega = EntregaPt.Crear(empresaId, hoja.Id, albaran, piezas.Count - rechazadas.Count, rechazadas.Count, c.MotivoRechazo, c.Receptor, firma, foto,
            c.Cobro?.Monto, c.Cobro?.FormaPago, c.Cobro?.Referencia, ahora, acceso.Actor.ToString());
        albaran.MarcarEntregado(ahora);

        foreach (var pz in piezas)
        {
            if (rechazadas.Contains(pz.Etiqueta)) { pz.RechazarEnSitio(); db.Movimientos.Add(MovimientoPieza.Paso(pz, TipoMovimientoPieza.RechazoEnSitio, EstadoPieza.Embarcada, albaran.Id, ahora, acceso.Actor.ToString())); }
            else { pz.Entregar(); db.Movimientos.Add(MovimientoPieza.Paso(pz, TipoMovimientoPieza.Entrega, EstadoPieza.Embarcada, albaran.Id, ahora, acceso.Actor.ToString())); }
        }
        db.Entregas.Add(entrega);

        var pedido = await db.PedidosPt.Include(p => p.Posiciones).FirstAsync(p => p.Id == albaran.PedidoPtId, cancellationToken);
        if (pedido.Confirmar72(ahora))
            await eventos.PublishAsync(new PedidoEnRutaEvent(empresaId, ahora, pedido.Id, pedido.NumeroPedido, hoja.Folio, albaran.Folio, hoja.CartaPorteFolio), cancellationToken);
        var entregadasAntes = await db.Piezas.CountAsync(p => p.PedidoPtId == pedido.Id && p.Estado == EstadoPieza.Entregada, cancellationToken);
        var completo = pedido.Posiciones.Sum(p => p.PiezasEsperadas) is var esperadas && esperadas > 0 && entregadasAntes + entrega.Entregadas >= esperadas;
        var porPosicion = piezas.Where(p => !rechazadas.Contains(p.Etiqueta)).GroupBy(p => p.Posicion).OrderBy(g => g.Key).Select(g => new PosicionEntregada(g.Key, g.Count())).ToList();
        await eventos.PublishAsync(new EntregaConfirmadaEvent(empresaId, ahora, pedido.Id, pedido.NumeroPedido, hoja.Folio, albaran.Folio, entrega.Resultado.ToString(), entrega.Receptor, porPosicion, entrega.Rechazadas, completo), cancellationToken);

        if (completo)
        {
            pedido.MarcarEntregado(ahora);
            // Si la factura ya existe, no hay que esperar su evento; si aún no, lo cierra AplicarFacturaPedido cuando llegue.
            if (pedido.FacturadoAt is null && (await pedidosPort.ObtenerPorNumeroAsync(empresaId, pedido.NumeroPedido, cancellationToken))?.Estado == "Facturado") pedido.MarcarFacturado(ahora);
            if (pedido.Cerrar115(ahora)) await eventos.PublishAsync(new PedidoFacturadoEntregadoEvent(empresaId, ahora, pedido.Id, pedido.NumeroPedido, "115"), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        var s = await SalidaMapeo.Completa(db).AsNoTracking().FirstAsync(x => x.Id == hoja.Id, cancellationToken);
        return await SalidaMapeo.ADtoAsync(db, pedidosPort, empresaId, s, true, false, cancellationToken);
    }
}

public sealed class ObtenerEvidenciaEntregaHandler(ProductoTerminadoDbContext db, PtAcceso acceso) : IRequestHandler<ObtenerEvidenciaEntregaQuery, EntregaEvidenciaDto>
{
    public async Task<EntregaEvidenciaDto> Handle(ObtenerEvidenciaEntregaQuery q, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoPiezasVer, cancellationToken);
        var hoja = await db.Salidas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.HojaId, cancellationToken)
            ?? throw new EntityNotFoundException("PT_SALIDA_NO_ENCONTRADA", "No existe la hoja de salida.");
        await acceso.VerificarSucursalAsync(hoja.SucursalId, PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales, cancellationToken);
        var e = await db.Entregas.AsNoTracking().FirstOrDefaultAsync(x => x.SalidaPtId == q.HojaId && x.AlbaranPtId == q.AlbaranId, cancellationToken)
            ?? throw new EntityNotFoundException("PT_ENTREGA_NO_ENCONTRADA", "El albarán no tiene entrega confirmada.");
        return new EntregaEvidenciaDto(EntregaImagen.ADataUrl(e.Firma), EntregaImagen.ADataUrl(e.Foto));
    }
}

public sealed class AplicarFacturaPedidoHandler(ProductoTerminadoDbContext db, IFacturacionSalidaPort facturacion, IIntegrationEventPublisher eventos, IClock clock)
    : IRequestHandler<AplicarFacturaPedidoCommand, bool>
{
    public async Task<bool> Handle(AplicarFacturaPedidoCommand c, CancellationToken cancellationToken)
    {
        var pedido = await db.PedidosPt.FirstOrDefaultAsync(x => x.EmpresaId == c.EmpresaId && x.PedidoFacturableId == c.PedidoFacturableId, cancellationToken);
        if (pedido is null) return false;
        var ahora = clock.UtcNow;
        var cambio = pedido.MarcarFacturado(ahora);
        var con72 = false;
        // El 72 viaja solo si la factura dejó sin vuelta atrás un albarán emitido y vigente del pedido (candado de CancelarAlbaranSalida).
        if (pedido.Estado72At is null && pedido.EnRutaAt is not null)
        {
            var emitidos = await (from a in db.Albaranes
                                  join s in db.Salidas on a.SalidaPtId equals s.Id
                                  where a.PedidoPtId == pedido.Id && a.CanceladoAt == null && s.Estado == EstadoSalida.Emitida
                                  orderby a.EmitidoAt
                                  select new { a.Folio, Hoja = s.Folio, s.CartaPorteFolio }).ToListAsync(cancellationToken);
            var facturados = emitidos.Count == 0
                ? new Dictionary<string, FacturaAlbaranRuta>()
                : await facturacion.FacturasDeAlbaranesAsync(emitidos.Select(a => a.Folio).ToList(), cancellationToken);
            var bloqueado = emitidos.FirstOrDefault(a => facturados.ContainsKey(a.Folio));
            con72 = bloqueado is not null && pedido.Confirmar72(ahora);
            if (con72)
                await eventos.PublishAsync(new PedidoEnRutaEvent(pedido.EmpresaId, ahora, pedido.Id, pedido.NumeroPedido, bloqueado!.Hoja, bloqueado.Folio, bloqueado.CartaPorteFolio), cancellationToken);
        }
        var cerro = pedido.Cerrar115(ahora);
        if (cerro) await eventos.PublishAsync(new PedidoFacturadoEntregadoEvent(pedido.EmpresaId, ahora, pedido.Id, pedido.NumeroPedido, "115"), cancellationToken);
        if (cambio || con72 || cerro) await db.SaveChangesAsync(cancellationToken);
        return cerro;
    }
}

public sealed class ConciliarCobroEntregaHandler(ProductoTerminadoDbContext db, IPedidosPtReadPort pedidosPort, IClock clock) : IRequestHandler<ConciliarCobroEntregaCommand, bool>
{
    public async Task<bool> Handle(ConciliarCobroEntregaCommand c, CancellationToken cancellationToken)
    {
        var pedidoFacturableId = await pedidosPort.ObtenerPedidoDeFacturaAsync(c.EmpresaId, c.ComprobanteId, cancellationToken);
        if (pedidoFacturableId is null) return false;
        var pedido = await db.PedidosPt.FirstOrDefaultAsync(x => x.EmpresaId == c.EmpresaId && x.PedidoFacturableId == pedidoFacturableId, cancellationToken);
        if (pedido is null) return false;
        var entrega = await db.Entregas.Where(e => e.PedidoPtId == pedido.Id && e.CobroMonto == c.Total && e.CobroConciliadoAt == null).OrderBy(e => e.EntregadaAt).FirstOrDefaultAsync(cancellationToken);
        if (entrega is null || !entrega.ConciliarCobro(c.CobroMostradorId, clock.UtcNow)) return false;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
