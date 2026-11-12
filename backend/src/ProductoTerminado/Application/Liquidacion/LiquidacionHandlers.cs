using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Application.Salidas;
using Millet.ProductoTerminado.Domain;
using Microsoft.Extensions.Options;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.ProductoTerminado.Infrastructure.Workers;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.Application.Liquidacion;

// Permisos reutilizados (sin permiso nuevo): ver = piezas.ver; liquidar y cerrar el día = excepciones.gestionar, con su bypass por sucursal (igual que las hojas de salida).
internal static class LiquidacionMapeo
{
    private static string NombreEstado(LiquidacionRutaPt? l) => l?.Estado.ToString() ?? "Pendiente";

    public static async Task<LiquidacionDto> ADtoAsync(ProductoTerminadoDbContext db, IPedidosPtReadPort pedidos, IFacturacionSalidaPort facturacion, Guid empresaId,
        SalidaPt s, LiquidacionRutaPt? liq, CancellationToken ct)
    {
        var albaranes = s.Albaranes.Where(a => a.Piezas.Count > 0).OrderBy(a => a.Secuencia).ToList();
        // M-177: el chofer cobra la factura del albarán (FAC-08, con IVA y neta de NC), no el total del pedido sin IVA.
        var facturas = await facturacion.FacturasDeAlbaranesAsync([.. albaranes.Select(a => a.Folio)], ct);
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
            var factura = facturas.GetValueOrDefault(a.Folio);
            var esperado = !contra ? 0m : factura?.PorCobrar ?? (await pedidos.ObtenerPorNumeroAsync(empresaId, pedido.NumeroPedido, ct))?.Total ?? 0m;
            var l = liq?.Albaranes.FirstOrDefault(x => x.AlbaranPtId == a.Id);
            var cliente = clientes.GetValueOrDefault(pedido.PedidoFacturableId)?.ClienteNombre ?? "";
            var e = entregas.GetValueOrDefault(a.Id);
            var tpv = e?.CobroForma == "TPV";
            var efectivo = e?.CobroForma == "Efectivo" ? e.CobroMonto ?? 0m : 0m;
            var voucher = tpv ? e!.CobroMonto ?? 0m : 0m;
            lineas.Add(l is null
                ? new LiquidacionLineaDto(a.Id, a.Folio, pedido.NumeroPedido, cliente, contra, esperado, e is not null, efectivo, voucher, tpv ? e!.CobroReferencia : null, esperado - efectivo - voucher, false, avisados.Contains(a.Id),
                    FacturaFolio: factura?.Folio, CobradoEnCaja: factura?.CobroCajaId is not null)
                : new LiquidacionLineaDto(a.Id, a.Folio, pedido.NumeroPedido, cliente, contra, l.Esperado, l.Firmado, l.Efectivo, l.Voucher, l.VoucherReferencia, l.Diferencia, l.Conciliado, avisados.Contains(a.Id),
                    l.TipoTarjeta, l.TarjetaUltimos4, factura?.Folio, l.CobroCajaId is not null || factura?.CobroCajaId is not null));
        }
        var cobrado = lineas.Sum(x => x.Efectivo + x.Voucher);
        return new LiquidacionDto(s.Id, s.Folio, s.Destino, NombreEstado(liq), liq?.LiquidadaAt, lineas,
            liq?.Imprevistos.Select(i => new ImprevistoInput(i.Descripcion, i.Importe)).ToList() ?? [], lineas.Sum(x => x.Esperado), cobrado, lineas.Sum(x => x.Diferencia));
    }

    public static IQueryable<SalidaPt> Salida(ProductoTerminadoDbContext db) => SalidaMapeo.Completa(db);

    public static IQueryable<LiquidacionRutaPt> Completa(ProductoTerminadoDbContext db) => db.Liquidaciones.Include(x => x.Albaranes).Include(x => x.Imprevistos);

    /// <summary>Regla de salida 5: alerta (12 horas hábiles) y límite de liquidación de cada hoja, en la zona horaria de su sucursal.</summary>
    public static async Task<Dictionary<Guid, (DateTimeOffset Alerta, DateTimeOffset Limite)>> PlazosAsync(IReadOnlyCollection<SalidaPt> salidas,
        IPtCalendarioSucursalesPort calendario, CierreContraEntregaOptions opciones, CancellationToken ct)
    {
        if (salidas.Count == 0) return [];
        var sucursales = await calendario.SucursalesAsync([.. salidas.Select(s => s.SucursalId).Distinct()], ct);
        var festivos = await calendario.FestivosAsync(ct);
        return salidas.ToDictionary(s => s.Id, s =>
        {
            var sucursal = sucursales.GetValueOrDefault(s.SucursalId);
            var zona = TimeZoneInfo.FindSystemTimeZoneById(sucursal?.ZonaHoraria ?? Millet.Administracion.Domain.Sucursal.ZonaHorariaDefault);
            return (ReglasSalidaPt.AlertaLiquidacion(s.EmitidaAt!.Value, zona, festivos),
                ReglasSalidaPt.LimiteLiquidacion(s.EmitidaAt!.Value, zona, opciones.DiasLiquidacion(sucursal?.Clave), festivos));
        });
    }
}

public sealed class LiquidarRutaPtHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, IFacturacionSalidaPort facturacion, ICurrentEmpresaContext empresa, IClock clock,
    IIntegrationEventPublisher eventos)
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
        var esperados = (await LiquidacionMapeo.ADtoAsync(db, pedidosPort, facturacion, empresaId, s, null, cancellationToken)).Albaranes.ToDictionary(l => l.AlbaranId, l => l.Esperado);

        var liq = previa ?? new LiquidacionRutaPt(empresaId, s.Id);
        if (previa is null) db.Liquidaciones.Add(liq);
        else db.RemoveRange(previa.Imprevistos);
        liq.Registrar(c.Albaranes.Select(a => (albaranes[a.AlbaranId], esperados[a.AlbaranId], a.Firmado, a.Efectivo, a.Voucher, a.VoucherReferencia,
                string.IsNullOrWhiteSpace(a.TipoTarjeta) ? null : a.TipoTarjeta.Trim(), a.TarjetaUltimos4)),
            c.Imprevistos.Select(i => (i.Descripcion, i.Importe)), clock.UtcNow, acceso.Actor.ToString());
        if (liq.Estado == EstadoLiquidacion.Cerrada)
        {
            await RegistrarCobrosEnCajaAsync(liq, s, albaranes, cancellationToken);
            foreach (var alb in albaranes.Values) alb.MarcarLiquidado(clock.UtcNow);
            var ahora = clock.UtcNow;
            var ids = albaranes.Values.Select(a => a.PedidoPtId).Distinct().ToList();
            foreach (var pedido in await db.PedidosPt.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken))
                if (pedido.Confirmar72(ahora))
                    await eventos.PublishAsync(new PedidoEnRutaEvent(empresaId, ahora, pedido.Id, pedido.NumeroPedido, s.Folio,
                        albaranes.Values.First(a => a.PedidoPtId == pedido.Id).Folio, s.CartaPorteFolio), cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return await LiquidacionMapeo.ADtoAsync(db, pedidosPort, facturacion, empresaId, s, liq, cancellationToken);
    }

    /// <summary>
    /// M-177: lo cobrado contra entrega entra a la caja de la sucursal de la hoja (sesión abierta de quien liquida), un cobro por albarán sobre su
    /// factura. Va antes de guardar: si la caja lo rechaza (sin sesión, albarán sin factura, voucher incompleto) no se guarda nada y la liquidación
    /// no se cierra. Un albarán que ya tiene cobro de caja no se vuelve a enviar.
    /// </summary>
    private async Task RegistrarCobrosEnCajaAsync(LiquidacionRutaPt liq, SalidaPt s, Dictionary<Guid, AlbaranPt> albaranes, CancellationToken ct)
    {
        var pendientes = liq.Albaranes.Where(l => l.Efectivo + l.Voucher > 0m && l.CobroCajaId is null).ToList();
        if (pendientes.Count == 0) return;
        var incompleto = pendientes.FirstOrDefault(l => l.Voucher > 0m && (l.TipoTarjeta is null || l.TarjetaUltimos4 is null));
        if (incompleto is not null)
            throw new BusinessRuleException("PT_LIQUIDACION_VOUCHER_TARJETA",
                $"El voucher del albarán {albaranes[incompleto.AlbaranPtId].Folio} lleva el tipo de tarjeta (crédito o débito) y los últimos 4 dígitos para registrarlo en caja.");

        var registrados = await facturacion.RegistrarCobrosRutaAsync(s.SucursalId, [.. pendientes.Select(l => new CobroRutaAlbaran(
            albaranes[l.AlbaranPtId].Folio, l.Efectivo, l.Voucher, l.VoucherReferencia, l.TipoTarjeta, l.TarjetaUltimos4))], ct);
        var porFolio = registrados.ToDictionary(r => r.AlbaranFolio, r => r.CobroCajaId);
        foreach (var l in pendientes)
            if (porFolio.TryGetValue(albaranes[l.AlbaranPtId].Folio, out var cobro)) l.RegistrarCobroCaja(cobro);
    }
}

public sealed class ObtenerLiquidacionHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, IFacturacionSalidaPort facturacion, ICurrentEmpresaContext empresa)
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
        return await LiquidacionMapeo.ADtoAsync(db, pedidosPort, facturacion, empresa.Current ?? throw new UnauthorizedAccessException(), s, liq, cancellationToken);
    }
}

public sealed class ListarLiquidacionesHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPtCalendarioSucursalesPort calendario, IOptions<CierreContraEntregaOptions> opciones, IClock clock)
    : IRequestHandler<ListarLiquidacionesQuery, LiquidacionesPagina>
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
        var abiertas = salidas.Where(x => liqs.GetValueOrDefault(x.Id)?.Estado != EstadoLiquidacion.Cerrada).ToList();
        var plazos = await LiquidacionMapeo.PlazosAsync(abiertas, calendario, opciones.Value, cancellationToken);
        var ahora = clock.UtcNow;

        var filas = salidas.Select(x =>
        {
            var conPiezas = x.Albaranes.Where(a => a.Piezas.Count > 0).ToList();
            var liq = liqs.GetValueOrDefault(x.Id);
            var fila = new LiquidacionResumenDto(x.Id, x.Folio, x.SucursalId, x.Destino, x.EmitidaAt!.Value, conPiezas.Count,
                conPiezas.Count(a => LiquidacionRutaPt.EsContraEntrega(condiciones.GetValueOrDefault(a.PedidoPtId))),
                liq?.Estado.ToString() ?? "Pendiente", liq?.Albaranes.Sum(a => a.Diferencia) ?? 0m);
            return plazos.TryGetValue(x.Id, out var p)
                ? fila with { AlertaAt = p.Alerta, LimiteAt = p.Limite, Plazo = ReglasSalidaPt.EstadoPlazo(ahora, p.Alerta, p.Limite) }
                : fila;
        }).Where(f => q.Estado is null || f.Estado == q.Estado).ToList();
        return new LiquidacionesPagina(filas.Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList(), filas.Count);
    }
}

public sealed class CerrarDiaContraEntregaHandler(ProductoTerminadoDbContext db, IPedidosPtReadPort pedidosPort, IIntegrationEventPublisher eventos, IClock clock,
    IPtCalendarioSucursalesPort calendario, IOptions<CierreContraEntregaOptions> opciones)
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
        // Regla 5: el worker solo avisa las hojas que cumplieron 12 horas hábiles sin liquidar (zona de su sucursal); el cierre manual avisa todo.
        if (c.PorAlerta)
        {
            var plazos = await LiquidacionMapeo.PlazosAsync(salidas, calendario, opciones.Value, cancellationToken);
            salidas = salidas.Where(s => ahora >= plazos[s.Id].Alerta).ToList();
        }

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
