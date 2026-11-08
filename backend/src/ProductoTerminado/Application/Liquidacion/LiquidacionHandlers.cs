using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Application.Salidas;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.Application.Liquidacion;

// Permisos reutilizados (sin permiso nuevo): ver = piezas.ver; liquidar y cerrar el día = excepciones.gestionar, con su bypass por sucursal (igual que las hojas de salida).
internal static class LiquidacionMapeo
{
    private static string NombreEstado(LiquidacionRutaPt? l) => l?.Estado.ToString() ?? "Pendiente";

    public static async Task<LiquidacionDto> ADtoAsync(ProductoTerminadoDbContext db, IPedidosPtReadPort pedidos, Guid empresaId, SalidaPt s, LiquidacionRutaPt? liq, CancellationToken ct)
    {
        var albaranes = s.Albaranes.Where(a => a.Piezas.Count > 0).OrderBy(a => a.Secuencia).ToList();
        var pedidoIds = albaranes.Select(a => a.PedidoPtId).ToList();
        var pedidosPt = await db.PedidosPt.AsNoTracking().Where(p => pedidoIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var clientes = await pedidos.ObtenerParaMonitorAsync(empresaId, pedidosPt.Values.Select(p => p.PedidoFacturableId).ToList(), ct);
        var albaranIds = albaranes.Select(a => a.Id).ToList();
        var avisados = (await db.AvisosContraEntrega.AsNoTracking().Where(x => albaranIds.Contains(x.AlbaranPtId)).Select(x => x.AlbaranPtId).ToListAsync(ct)).ToHashSet();

        // PT5.6: lo que el chofer ya capturó al entregar (firma o foto, cobro) llega precargado; Caja lo confirma o lo corrige.
        var entregas = await db.Entregas.AsNoTracking().Where(x => albaranIds.Contains(x.AlbaranPtId)).Select(x => new { x.AlbaranPtId, x.CobroMonto, x.CobroForma, x.CobroReferencia }).ToDictionaryAsync(x => x.AlbaranPtId, ct);

        var lineas = new List<LiquidacionLineaDto>();
        foreach (var a in albaranes)
        {
            var pedido = pedidosPt[a.PedidoPtId];
            var contra = LiquidacionRutaPt.EsContraEntrega(pedido.CondicionPagoOrigen);
            var esperado = contra ? (await pedidos.ObtenerPorNumeroAsync(empresaId, pedido.NumeroPedido, ct))?.Total ?? 0m : 0m;
            var l = liq?.Albaranes.FirstOrDefault(x => x.AlbaranPtId == a.Id);
            var cliente = clientes.GetValueOrDefault(pedido.PedidoFacturableId)?.ClienteNombre ?? "";
            var e = entregas.GetValueOrDefault(a.Id);
            var tpv = e?.CobroForma == "TPV";
            var efectivo = e?.CobroForma == "Efectivo" ? e.CobroMonto ?? 0m : 0m;
            var voucher = tpv ? e!.CobroMonto ?? 0m : 0m;
            lineas.Add(l is null
                ? new LiquidacionLineaDto(a.Id, a.Folio, pedido.NumeroPedido, cliente, contra, esperado, e is not null, efectivo, voucher, tpv ? e!.CobroReferencia : null, esperado - efectivo - voucher, false, avisados.Contains(a.Id))
                : new LiquidacionLineaDto(a.Id, a.Folio, pedido.NumeroPedido, cliente, contra, l.Esperado, l.Firmado, l.Efectivo, l.Voucher, l.VoucherReferencia, l.Diferencia, l.Conciliado, avisados.Contains(a.Id)));
        }
        var cobrado = lineas.Sum(x => x.Efectivo + x.Voucher);
        return new LiquidacionDto(s.Id, s.Folio, s.Destino, NombreEstado(liq), liq?.LiquidadaAt, lineas,
            liq?.Imprevistos.Select(i => new ImprevistoInput(i.Descripcion, i.Importe)).ToList() ?? [], lineas.Sum(x => x.Esperado), cobrado, lineas.Sum(x => x.Diferencia));
    }

    public static IQueryable<SalidaPt> Salida(ProductoTerminadoDbContext db) => SalidaMapeo.Completa(db);

    public static IQueryable<LiquidacionRutaPt> Completa(ProductoTerminadoDbContext db) => db.Liquidaciones.Include(x => x.Albaranes).Include(x => x.Imprevistos);
}

public sealed class LiquidarRutaPtHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<LiquidarRutaPtCommand, LiquidacionDto>
{
    public async Task<LiquidacionDto> Handle(LiquidarRutaPtCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        var s = await LiquidacionMapeo.Salida(db).FirstOrDefaultAsync(x => x.Id == c.SalidaId, cancellationToken)
            ?? throw new EntityNotFoundException("PT_SALIDA_NO_ENCONTRADA", "No existe la hoja de salida.");
        await acceso.VerificarSucursalAsync(s.SucursalId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        if (s.Estado != EstadoSalida.Emitida)
            throw new BusinessRuleException("PT_LIQUIDACION_HOJA_NO_EMITIDA", "Solo se liquida una hoja de salida ya emitida.");
        var empresaId = empresa.Current ?? throw new UnauthorizedAccessException();

        var albaranes = s.Albaranes.Where(a => a.Piezas.Count > 0).ToDictionary(a => a.Id);
        var capturados = c.Albaranes.Select(a => a.AlbaranId).ToList();
        if (capturados.Distinct().Count() != capturados.Count || capturados.Count != albaranes.Count || capturados.Any(id => !albaranes.ContainsKey(id)))
            throw new BusinessRuleException("PT_LIQUIDACION_ALBARANES", "La liquidación lleva exactamente los albaranes de la hoja, cada uno una vez.");

        var previa = await LiquidacionMapeo.Completa(db).FirstOrDefaultAsync(x => x.SalidaPtId == s.Id, cancellationToken);
        // Lo esperado se recalcula siempre con el pedido de hoy.
        var esperados = (await LiquidacionMapeo.ADtoAsync(db, pedidosPort, empresaId, s, null, cancellationToken)).Albaranes.ToDictionary(l => l.AlbaranId, l => l.Esperado);

        var liq = previa ?? new LiquidacionRutaPt(empresaId, s.Id);
        if (previa is null) db.Liquidaciones.Add(liq);
        else db.RemoveRange(previa.Imprevistos);
        liq.Registrar(c.Albaranes.Select(a => (albaranes[a.AlbaranId], esperados[a.AlbaranId], a.Firmado, a.Efectivo, a.Voucher, a.VoucherReferencia)),
            c.Imprevistos.Select(i => (i.Descripcion, i.Importe)), clock.UtcNow, acceso.Actor.ToString());
        if (liq.Estado == EstadoLiquidacion.Cerrada)
            foreach (var alb in albaranes.Values) alb.MarcarLiquidado(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await LiquidacionMapeo.ADtoAsync(db, pedidosPort, empresaId, s, liq, cancellationToken);
    }
}

public sealed class ObtenerLiquidacionHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, ICurrentEmpresaContext empresa)
    : IRequestHandler<ObtenerLiquidacionQuery, LiquidacionDto>
{
    public async Task<LiquidacionDto> Handle(ObtenerLiquidacionQuery q, CancellationToken cancellationToken)
    {
        var s = await LiquidacionMapeo.Salida(db).AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.SalidaId, cancellationToken)
            ?? throw new EntityNotFoundException("PT_SALIDA_NO_ENCONTRADA", "No existe la hoja de salida.");
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoPiezasVer, cancellationToken);
        var visibles = await acceso.SucursalesVisiblesAsync(PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales, cancellationToken);
        if (visibles is not null && !visibles.Contains(s.SucursalId))
            throw new ForbiddenException("SUCURSAL_NO_ASOCIADA", "No tiene acceso a esta hoja de salida.");
        var liq = await LiquidacionMapeo.Completa(db).AsNoTracking().FirstOrDefaultAsync(x => x.SalidaPtId == s.Id, cancellationToken);
        return await LiquidacionMapeo.ADtoAsync(db, pedidosPort, empresa.Current ?? throw new UnauthorizedAccessException(), s, liq, cancellationToken);
    }
}

public sealed class ListarLiquidacionesHandler(ProductoTerminadoDbContext db, PtAcceso acceso) : IRequestHandler<ListarLiquidacionesQuery, LiquidacionesPagina>
{
    public async Task<LiquidacionesPagina> Handle(ListarLiquidacionesQuery q, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoPiezasVer, cancellationToken);
        const string todas = PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales;
        var visibles = await acceso.SucursalesVisiblesAsync(todas, cancellationToken);
        if (q.SucursalId is { } s) await acceso.VerificarSucursalAsync(s, todas, cancellationToken);

        var consulta = SalidaMapeo.Completa(db).AsNoTracking().Where(x => x.Estado == EstadoSalida.Emitida);
        if (visibles is not null) consulta = consulta.Where(x => visibles.Contains(x.SucursalId));
        if (q.SucursalId is { } sid) consulta = consulta.Where(x => x.SucursalId == sid);
        var salidas = await consulta.OrderByDescending(x => x.EmitidaAt).ToListAsync(cancellationToken);

        var ids = salidas.Select(x => x.Id).ToList();
        var liqs = await LiquidacionMapeo.Completa(db).AsNoTracking().Where(x => ids.Contains(x.SalidaPtId)).ToDictionaryAsync(x => x.SalidaPtId, cancellationToken);
        var pedidoIds = salidas.SelectMany(x => x.Albaranes.Select(a => a.PedidoPtId)).Distinct().ToList();
        var condiciones = await db.PedidosPt.AsNoTracking().Where(p => pedidoIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.CondicionPagoOrigen, cancellationToken);

        var filas = salidas.Select(x =>
        {
            var conPiezas = x.Albaranes.Where(a => a.Piezas.Count > 0).ToList();
            var liq = liqs.GetValueOrDefault(x.Id);
            return new LiquidacionResumenDto(x.Id, x.Folio, x.SucursalId, x.Destino, x.EmitidaAt!.Value, conPiezas.Count,
                conPiezas.Count(a => LiquidacionRutaPt.EsContraEntrega(condiciones.GetValueOrDefault(a.PedidoPtId))),
                liq?.Estado.ToString() ?? "Pendiente", liq?.Albaranes.Sum(a => a.Diferencia) ?? 0m);
        }).Where(f => q.Estado is null || f.Estado == q.Estado).ToList();
        return new LiquidacionesPagina(filas.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList(), filas.Count);
    }
}

public sealed class CerrarDiaContraEntregaHandler(ProductoTerminadoDbContext db, IPedidosPtReadPort pedidosPort, IIntegrationEventPublisher eventos, IClock clock)
    : IRequestHandler<CerrarDiaContraEntregaCommand, int>
{
    public async Task<int> Handle(CerrarDiaContraEntregaCommand c, CancellationToken cancellationToken)
    {
        var ahora = clock.UtcNow;
        var hasta = c.Hasta ?? HoraAw.FechaLocal(ahora);
        var limite = HoraAw.AUtc(hasta.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var salidas = await SalidaMapeo.Completa(db).Where(x => x.Estado == EstadoSalida.Emitida && x.EmitidaAt < limite).ToListAsync(cancellationToken);
        var ids = salidas.Select(x => x.Id).ToList();
        var liqs = await db.Liquidaciones.Include(x => x.Albaranes).Where(x => ids.Contains(x.SalidaPtId)).ToDictionaryAsync(x => x.SalidaPtId, cancellationToken);
        var albaranIds = salidas.SelectMany(x => x.Albaranes.Select(a => a.Id)).ToList();
        var avisados = (await db.AvisosContraEntrega.Where(x => albaranIds.Contains(x.AlbaranPtId)).Select(x => x.AlbaranPtId).ToListAsync(cancellationToken)).ToHashSet();
        var pedidoIds = salidas.SelectMany(x => x.Albaranes.Select(a => a.PedidoPtId)).Distinct().ToList();
        var pedidos = await db.PedidosPt.Where(p => pedidoIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        var nuevos = 0;
        foreach (var s in salidas)
            foreach (var a in s.Albaranes.Where(a => a.Piezas.Count > 0 && !avisados.Contains(a.Id)))
            {
                var pedido = pedidos[a.PedidoPtId];
                if (!LiquidacionRutaPt.EsContraEntrega(pedido.CondicionPagoOrigen)) continue;
                if (liqs.GetValueOrDefault(s.Id)?.Albaranes.FirstOrDefault(l => l.AlbaranPtId == a.Id)?.Conciliado == true) continue;
                var origen = await pedidosPort.ObtenerPorNumeroAsync(s.EmpresaId, pedido.NumeroPedido, cancellationToken);
                if (origen is null) continue;
                await eventos.PublishAsync(new ContraEntregaNoLiquidadaEvent(s.EmpresaId, ahora, pedido.Id, pedido.NumeroPedido, origen.ClienteId, origen.Moneda, origen.Total, s.Folio, s.EmitidaAt!.Value), cancellationToken);
                db.AvisosContraEntrega.Add(new AvisoContraEntregaPt(s.EmpresaId, a.Id, pedido.Id, origen.Total, ahora));
                nuevos++;
            }
        if (nuevos > 0) await db.SaveChangesAsync(cancellationToken);
        return nuevos;
    }
}
