using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application.Entregas;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.Application.Salidas;

// Permisos reutilizados (sin permiso nuevo): ver = piezas.ver; operar = excepciones.gestionar, ambos con su bypass por sucursal (igual que traspasos).
internal static class SalidaMapeo
{
    public static async Task<HojaSalidaDto> ADtoAsync(ProductoTerminadoDbContext db, IPedidosPtReadPort pedidos, Guid empresaId, SalidaPt s, bool detalle, bool yaEmitida, CancellationToken ct)
    {
        var total = s.Albaranes.Sum(a => a.Piezas.Count);
        var entregas = s.Estado == EstadoSalida.Emitida ? await db.Entregas.AsNoTracking().Where(e => e.SalidaPtId == s.Id).ToListAsync(ct) : [];
        var pendientes = s.Estado == EstadoSalida.Emitida ? s.Albaranes.Count(a => a.Piezas.Count > 0 && !a.Cancelado) - entregas.Count : 0;
        var entregaIds = entregas.Select(e => e.Id).ToList();
        var versiones = await db.EntregaVersiones.AsNoTracking().Where(v => entregaIds.Contains(v.EntregaPtId))
            .Select(v => new { v.EntregaPtId, v.Numero, v.Resultado, v.Receptor, v.Entregadas, v.Rechazadas, v.MotivoRechazo, v.EntregadaAt, v.MotivoCorreccion, v.CorregidaAt, v.CorregidaPor }).ToListAsync(ct);
        var cabecera = new HojaSalidaDto(s.Id, s.Folio, s.SucursalId, s.Destino, s.Foranea, s.VehiculoId, s.OperadorId, s.Estado.ToString(), s.CreadaAt, s.EmitidaAt, s.CartaPorteFolio, s.Albaranes.Count, total, [], yaEmitida, pendientes);
        if (!detalle) return cabecera;

        var pedidoIds = s.Albaranes.Select(a => a.PedidoPtId).ToList();
        var pedidosPt = await db.PedidosPt.AsNoTracking().Include(p => p.Posiciones).Where(p => pedidoIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var origen = await pedidos.ObtenerParaMonitorAsync(empresaId, pedidosPt.Values.Select(p => p.PedidoFacturableId).ToList(), ct);
        var piezaIds = s.Albaranes.SelectMany(a => a.Piezas.Select(p => p.PiezaId)).ToList();
        var piezas = await db.Piezas.AsNoTracking().Where(p => piezaIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var sucursalEnPt = s.SucursalId;
        var enPt = await db.Piezas.AsNoTracking().Where(p => pedidoIds.Contains(p.PedidoPtId) && p.Estado == EstadoPieza.EnPT && p.SucursalId == sucursalEnPt).ToListAsync(ct);
        var rotas = await RotasAsync(db, enPt.Select(p => p.Id).ToList(), ct);

        var albaranes = s.Albaranes.Where(a => s.Estado != EstadoSalida.Emitida || a.Piezas.Count > 0).OrderBy(a => a.Secuencia).Select(a =>
        {
            var pedido = pedidosPt[a.PedidoPtId];
            var mias = a.Piezas.Select(p => piezas[p.PiezaId]).OrderBy(p => p.Posicion).ThenBy(p => p.NumeroPieza).ToList();
            var cliente = origen.GetValueOrDefault(pedido.PedidoFacturableId);
            var delPedido = enPt.Where(p => p.PedidoPtId == a.PedidoPtId).OrderBy(p => p.Posicion).ThenBy(p => p.NumeroPieza).ToList();
            return new AlbaranDto(a.Id, a.Folio, a.Secuencia, pedido.NumeroPedido, cliente?.ClienteNombre ?? "", cliente?.Destino, a.EmitidoAt, mias.Count,
                mias.GroupBy(p => p.Posicion).Select(g => new AlbaranLineaDto(g.Key, pedido.Posiciones.FirstOrDefault(x => x.Posicion == g.Key)?.Descripcion, g.Count())).ToList(),
                mias.Select(p => p.Etiqueta).ToList(), pedido.Posiciones.Sum(x => x.PiezasEsperadas),
                s.Estado == EstadoSalida.Borrador ? delPedido.Where(p => !rotas.Contains(p.Id)).Select(p => p.Etiqueta).ToList() : [],
                delPedido.Where(p => rotas.Contains(p.Id)).Select(p => p.Etiqueta).ToList(),
                entregas.FirstOrDefault(e => e.AlbaranPtId == a.Id) is { } en
                    ? new EntregaDto(en.Id, en.Resultado.ToString(), en.Receptor, en.EntregadaAt, en.Entregadas, en.Rechazadas, en.MotivoRechazo, en.Firma is not null, en.Foto is not null,
                        mias.Where(p => p.Estado == EstadoPieza.EnRevision).Select(p => p.Etiqueta).ToList(),
                        en.CobroMonto is { } monto ? new CobroEntregaDto(monto, en.CobroForma ?? "", en.CobroReferencia, en.CobroConciliadoAt) : null,
                        versiones.Where(v => v.EntregaPtId == en.Id).OrderBy(v => v.Numero).Select(v => new EntregaVersionDto(v.Numero, v.Resultado.ToString(), v.Receptor,
                            v.Entregadas, v.Rechazadas, v.MotivoRechazo, v.EntregadaAt, v.MotivoCorreccion, v.CorregidaAt, v.CorregidaPor)).ToList())
                    : null,
                pedido.Estado115At is not null ? "115" : pedido.EntregadoAt is not null ? "Entregado" : pedido.EnRutaAt is not null ? "71" : null,
                a.Cancelado, a.CanceladoAt, a.MotivoCancelacion,
                a.CancelacionSolicitadaAt is null && !a.Cancelado ? null : new CancelacionAlbaranDto(a.CancelacionPendiente, a.CancelacionSolicitadaAt, a.CancelacionSolicitadaPorId,
                    a.MotivoSolicitudCancelacion, a.CancelacionRechazadaAt, a.CancelacionRechazadaPorId, a.MotivoRechazoCancelacion, a.CanceladoPorId));
        }).ToList();
        return cabecera with { Albaranes = albaranes };
    }

    /// <summary>Piezas cuyo último movimiento es la rotura en carga: siguen en PT pero no se cargan hasta su reposición.</summary>
    public static async Task<HashSet<Guid>> RotasAsync(ProductoTerminadoDbContext db, IReadOnlyCollection<Guid> piezaIds, CancellationToken ct)
    {
        var movs = await db.Movimientos.AsNoTracking().Where(m => piezaIds.Contains(m.PiezaId)).OrderByDescending(m => m.OcurridoAt).ThenByDescending(m => m.Id)
            .Select(m => new { m.PiezaId, m.Tipo }).ToListAsync(ct);
        return movs.GroupBy(m => m.PiezaId).Where(g => g.First().Tipo == TipoMovimientoPieza.RotaEnCarga).Select(g => g.Key).ToHashSet();
    }

    public static IQueryable<SalidaPt> Completa(ProductoTerminadoDbContext db) => db.Salidas.Include(x => x.Albaranes).ThenInclude(a => a.Piezas);
}

/// <summary>
/// Reglas de salida 1, 3 y 4 (Millet, 9-nov) sobre lo cargado hoy: pedidos sin factura válida (timbrada del día o anterior, o en
/// PendientePedimento si es exportación) y pedidos que salen incompletos. Cada pedido va como «pedido:piezas cargadas», que es el alcance
/// que una autorización cubre.
/// </summary>
internal static class ReglasHoja
{
    private static readonly EstadoPieza[] YaSalieron = [EstadoPieza.Embarcada, EstadoPieza.Entregada, EstadoPieza.EnRevision];

    public static async Task<(IReadOnlyList<string> SinFactura, IReadOnlyList<string> Parciales)> EvaluarAsync(ProductoTerminadoDbContext db, IFacturacionSalidaPort facturacion,
        SalidaPt s, DateOnly hoy, CancellationToken ct)
    {
        var conPiezas = s.Albaranes.Where(a => a.Piezas.Count > 0 && !a.Cancelado).OrderBy(a => a.Secuencia).ToList();
        if (conPiezas.Count == 0) return ([], []);
        var pedidoIds = conPiezas.Select(a => a.PedidoPtId).ToList();
        var pedidos = await db.PedidosPt.AsNoTracking().Include(p => p.Posiciones).Where(p => pedidoIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var cargadasIds = conPiezas.SelectMany(a => a.Piezas.Select(p => p.PiezaId)).ToList();
        var posicionDe = await db.Piezas.AsNoTracking().Where(p => cargadasIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Posicion, ct);
        var salieron = await db.Piezas.AsNoTracking().Where(p => pedidoIds.Contains(p.PedidoPtId) && YaSalieron.Contains(p.Estado))
            .GroupBy(p => new { p.PedidoPtId, p.Posicion }).Select(g => new { g.Key.PedidoPtId, g.Key.Posicion, Piezas = g.Count() }).ToListAsync(ct);
        var facturas = await facturacion.FacturasDePedidosAsync([.. pedidos.Values.Select(p => p.PedidoFacturableId)], ct);
        // M-245: lo que sale dentro de una salida parcial ya autorizada en el pedido no vuelve a pedir autorización en la hoja.
        var autorizadas = (await db.SolicitudesParciales.AsNoTracking().Where(x => pedidoIds.Contains(x.PedidoPtId) && x.Estado == EstadoSolicitudPt.Autorizada)
            .Select(x => new { x.PedidoPtId, x.PiezaIds }).ToListAsync(ct)).GroupBy(x => x.PedidoPtId).ToDictionary(g => g.Key, g => g.SelectMany(x => x.PiezaIds).ToHashSet());

        List<string> sinFactura = [], parciales = [];
        foreach (var a in conPiezas)
        {
            var pedido = pedidos[a.PedidoPtId];
            var clave = $"{pedido.NumeroPedido}:{a.Piezas.Count}";
            if (!ReglasSalidaPt.TieneFactura(facturas.Where(f => f.PedidoFacturableId == pedido.PedidoFacturableId)
                    .Select(f => (f.PendientePedimento, f.Exportacion, f.FechaTimbrado is { } t ? FechaContable.HoyLocal(t) : (DateOnly?)null)), hoy))
                sinFactura.Add(clave);
            if (!(autorizadas.TryGetValue(pedido.Id, out var ok) && a.Piezas.All(p => ok.Contains(p.PiezaId))) && ReglasSalidaPt.EsParcial(pedido.Posiciones.Select(x => (x.PiezasEsperadas, a.Piezas.Count(p => posicionDe[p.PiezaId] == x.Posicion),
                    salieron.Where(y => y.PedidoPtId == pedido.Id && y.Posicion == x.Posicion).Sum(y => y.Piezas)))))
                parciales.Add(clave);
        }
        return (sinFactura, parciales);
    }

    /// <summary>Lo requerido que la autorización no cubre (si la carga cambió después de autorizar, ya no lo cubre).</summary>
    public static List<string> SinCubrir(IReadOnlyList<string> requeridos, AutorizacionSalidaPt? autorizacion) =>
        requeridos.Where(r => autorizacion?.Alcance.Contains(r) != true).ToList();

    public static string Pedidos(IEnumerable<string> alcance) => string.Join(", ", alcance.Select(x => x[..x.LastIndexOf(':')]));

    public static AutorizacionSalidaDto? Dto(AutorizacionSalidaPt? a) => a is null ? null : new(a.Actor, a.Fecha, a.Nivel, a.Motivo, a.Suplencia, a.Alcance);

    public static async Task<HojaSalidaDto> ConReglasAsync(ProductoTerminadoDbContext db, IFacturacionSalidaPort facturacion, SalidaPt s, HojaSalidaDto dto, DateOnly hoy, CancellationToken ct)
    {
        if (s.Estado != EstadoSalida.Borrador) return dto;
        var (sinFactura, parciales) = await EvaluarAsync(db, facturacion, s, hoy, ct);
        var excepcion = s.LeerAutorizacion(false);
        var parcial = s.LeerAutorizacion(true);
        return dto with { Reglas = new ReglasHojaDto(sinFactura, parciales, Dto(excepcion), Dto(parcial),
            sinFactura.Count > 0 && SinCubrir(sinFactura, excepcion).Count == 0, parciales.Count > 0 && SinCubrir(parciales, parcial).Count == 0) };
    }
}

public sealed class CrearHojaSalidaHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<CrearHojaSalidaCommand, HojaSalidaDto>
{
    public async Task<HojaSalidaDto> Handle(CrearHojaSalidaCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        await acceso.VerificarSucursalAsync(c.SucursalId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        var empresaId = empresa.Current ?? throw new UnauthorizedAccessException();

        var numeros = c.Pedidos.Select(p => p.NumeroPedido.Trim()).ToList();
        if (numeros.Distinct().Count() != numeros.Count)
            throw new BusinessRuleException("PT_SALIDA_PEDIDO_REPETIDO", "Cada pedido va una sola vez en la hoja; sus piezas van en un solo albarán.");
        var etiquetas = c.Pedidos.SelectMany(p => p.Etiquetas.Select(e => e.Trim())).ToList();
        if (etiquetas.Distinct().Count() != etiquetas.Count)
            throw new BusinessRuleException("PT_SALIDA_PIEZAS_INVALIDAS", "Hay etiquetas repetidas en la hoja.");

        var pedidos = await db.PedidosPt.Where(p => numeros.Contains(p.NumeroPedido)).ToDictionaryAsync(p => p.NumeroPedido, cancellationToken);
        var sinPedido = numeros.Except(pedidos.Keys).ToList();
        if (sinPedido.Count > 0) throw new EntityNotFoundException("PT_PEDIDO_NO_ENCONTRADO", $"Pedidos sin piezas en PT: {string.Join(", ", sinPedido)}");
        var noLiberados = pedidos.Values.Where(p => p.Liberacion != EstadoLiberacion.Liberado).Select(p => p.NumeroPedido).ToList();
        if (noLiberados.Count > 0)
            throw new BusinessRuleException("PT_PEDIDO_NO_LIBERADO", $"Solo se cargan pedidos liberados por caja (70): con saldo pendiente no se carga sin liberación. Sin liberar: {string.Join(", ", noLiberados)}");

        var piezas = await db.Piezas.Where(p => etiquetas.Contains(p.Etiqueta)).ToDictionaryAsync(p => p.Etiqueta, cancellationToken);
        var faltan = etiquetas.Except(piezas.Keys).ToList();
        if (faltan.Count > 0) throw new BusinessRuleException("PT_SALIDA_PIEZAS_INVALIDAS", $"Etiquetas inexistentes: {string.Join(", ", faltan)}");
        if (piezas.Values.Any(p => p.SucursalId != c.SucursalId))
            throw new BusinessRuleException("PT_SALIDA_PIEZA_OTRA_SUCURSAL", "Todas las piezas deben estar en la sucursal de la salida.");
        var ids = piezas.Values.Select(p => p.Id).ToList();
        var yaEnHoja = await (from ap in db.AlbaranPiezas join a in db.Albaranes on ap.AlbaranPtId equals a.Id join s in db.Salidas on a.SalidaPtId equals s.Id
                              where ids.Contains(ap.PiezaId) && s.Estado != EstadoSalida.Cancelada && a.CanceladoAt == null select ap.PiezaId).AnyAsync(cancellationToken);
        if (yaEnHoja) throw new BusinessRuleException("PT_SALIDA_PIEZA_YA_EN_HOJA", "Alguna pieza ya está en otra hoja de salida.");

        var ahora = clock.UtcNow;
        var salida = SalidaPt.Crear(empresaId, c.SucursalId, c.Destino, c.Foranea, c.VehiculoId, c.OperadorId, c.DistanciaKm, c.PesoKg, c.OrigenCodigoPostal, c.OrigenEstado, c.DestinoCodigoPostal, c.DestinoEstado, ahora,
            // M11: el folio lleva el día local (a las 23:34 del 8-nov no puede decir 20261109).
            HoraAw.FechaLocal(ahora), acceso.Actor);
        foreach (var p in c.Pedidos)
        {
            var pedido = pedidos[p.NumeroPedido.Trim()];
            var delPedido = p.Etiquetas.Select(e => piezas[e.Trim()]).ToList();
            if (delPedido.Any(x => x.PedidoPtId != pedido.Id))
                throw new BusinessRuleException("PT_SALIDA_PIEZA_OTRO_PEDIDO", $"Alguna pieza no pertenece al pedido {pedido.NumeroPedido}.");
            // M-245: de un pedido incompleto solo salen las piezas de su salida parcial autorizada.
            if (await SalidaParcial.PermitidasAsync(db, pedido, cancellationToken) is { } permitidas && delPedido.Any(x => !permitidas.Contains(x.Id)))
                throw new BusinessRuleException("PT_PIEZA_NO_AUTORIZADA", $"El pedido {pedido.NumeroPedido} está incompleto: solo salen las piezas de su salida parcial autorizada.");
            var albaran = salida.AgregarAlbaran(pedido, delPedido.Select(x => x.Id).ToList());
            foreach (var pz in delPedido)
            {
                var anterior = pz.Estado;
                pz.Cargar();
                if (anterior == EstadoPieza.EnPT) db.Movimientos.Add(MovimientoPieza.Paso(pz, TipoMovimientoPieza.Carga, anterior, albaran.Id, ahora, acceso.Actor.ToString()));
            }
        }
        db.Salidas.Add(salida);
        await db.SaveChangesAsync(cancellationToken);
        return await SalidaMapeo.ADtoAsync(db, pedidosPort, empresaId, salida, true, false, cancellationToken);
    }
}

public sealed class EmitirHojaSalidaHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, IFacturacionSalidaPort facturacion,
    IIntegrationEventPublisher eventos, ICurrentEmpresaContext empresa, IClock clock) : IRequestHandler<EmitirHojaSalidaCommand, HojaSalidaDto>
{
    // ponytail: origen fijo hasta recibir de Logística la Carta Porte real (D15); luego sale de la sucursal.
    private const string Origen = "Planta Millet";

    public async Task<HojaSalidaDto> Handle(EmitirHojaSalidaCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        var s = await SalidaMapeo.Completa(db).FirstOrDefaultAsync(x => x.Id == c.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PT_SALIDA_NO_ENCONTRADA", "No existe la hoja de salida.");
        await acceso.VerificarSucursalAsync(s.SucursalId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        var empresaId = empresa.Current ?? throw new UnauthorizedAccessException();

        // Segunda emisión: nada que descontar, registrar ni emitir.
        if (s.Estado == EstadoSalida.Emitida) return await SalidaMapeo.ADtoAsync(db, pedidosPort, empresaId, s, true, true, cancellationToken);
        if (s.Estado == EstadoSalida.Cancelada) throw new BusinessRuleException("PT_SALIDA_CANCELADA", "La hoja de salida está cancelada.");

        // PT5.4: un pedido sin nada cargado (todo roto o pendiente) no lleva albarán ni pasa a 71.
        var conPiezas = s.Albaranes.Where(a => a.Piezas.Count > 0).ToList();
        if (conPiezas.Count == 0) throw new BusinessRuleException("PT_SALIDA_SIN_PIEZAS", "La hoja no tiene piezas cargadas: no hay nada que emitir.");
        var piezaIds = conPiezas.SelectMany(a => a.Piezas.Select(p => p.PiezaId)).ToList();
        var piezas = await db.Piezas.Where(p => piezaIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var descargadas = piezas.Values.Where(p => p.Estado != EstadoPieza.Cargada).Select(p => p.Etiqueta).ToList();
        if (descargadas.Count > 0) throw new BusinessRuleException("PT_PIEZA_NO_CARGADA", $"Piezas que ya no están cargadas: {string.Join(", ", descargadas)}");
        var pedidoIds = conPiezas.Select(a => a.PedidoPtId).ToList();
        var pedidos = await db.PedidosPt.Where(p => pedidoIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        var ahora = clock.UtcNow;
        // Reglas 1-4 (Millet, 9-nov): sin factura válida o incompleto, solo con la autorización vigente de su nivel.
        var (sinFactura, parciales) = await ReglasHoja.EvaluarAsync(db, facturacion, s, FechaContable.HoyLocal(ahora), cancellationToken);
        if (ReglasHoja.SinCubrir(sinFactura, s.LeerAutorizacion(false)) is { Count: > 0 } faltaFactura)
            throw new BusinessRuleException("PT_SALIDA_SIN_FACTURA",
                $"Pedidos sin factura timbrada del día o anterior: {ReglasHoja.Pedidos(faltaFactura)}. Factúrelos o pida la excepción a Dirección de Administración (en su ausencia, Planeación o Contraloría).");
        if (ReglasHoja.SinCubrir(parciales, s.LeerAutorizacion(true)) is { Count: > 0 } faltaParcial)
            throw new BusinessRuleException("PT_SALIDA_PARCIAL_SIN_AUTORIZAR",
                $"Pedidos que salen incompletos: {ReglasHoja.Pedidos(faltaParcial)}. La salida parcial la autoriza Crédito y Cobranza (en su ausencia, Gerencia de Ventas).");

        // La Carta Porte primero (su falla más común es de configuración fiscal y no debe dejar una salida facturable sin embarcar); queda guardada para que un reintento no emita otra.
        if (s.Foranea && s.CartaPorteId is null)
        {
            var cp = await facturacion.EmitirCartaPorteAsync(new SolicitudCartaPorte(s.SucursalId, Origen, s.OrigenCodigoPostal!, s.OrigenEstado!, s.Destino, s.DestinoCodigoPostal!, s.DestinoEstado!, s.DistanciaKm!.Value,
                s.VehiculoId!.Value, s.OperadorId!.Value, conPiezas.Count == 1 ? pedidos[conPiezas[0].PedidoPtId].PedidoFacturableId : null, ahora,
                [new MercanciaSalida($"Vidrio terminado, {piezaIds.Count} piezas")], s.PesoKg!.Value), cancellationToken);
            s.AsignarCartaPorte(cp.Id, cp.Folio);
            await db.SaveChangesAsync(cancellationToken);
        }

        // Facturación valida que no exceda lo pedido y es idempotente por folio de albarán, así reintentar tras un fallo no duplica.
        foreach (var a in conPiezas.OrderBy(a => a.Secuencia))
            await facturacion.RegistrarSalidaAsync(pedidos[a.PedidoPtId].PedidoFacturableId, a.Folio,
                a.Piezas.Select(p => piezas[p.PiezaId]).GroupBy(p => p.Posicion).OrderBy(g => g.Key).Select(g => (g.Key, (decimal)g.Count())).ToList(), cancellationToken);

        // El 72 no se revierte en A+W: viaja solo por un albarán que ya no se puede cancelar, con la misma regla que el candado de
        // CancelarAlbaranSalida (factura timbrada ligada a la salida del albarán, no solo al pedido).
        var facturados = await facturacion.FacturasDeAlbaranesAsync(conPiezas.Select(a => a.Folio).ToList(), cancellationToken);
        foreach (var a in conPiezas)
        {
            foreach (var pa in a.Piezas)
            {
                var pz = piezas[pa.PiezaId];
                pz.Embarcar();
                db.Movimientos.Add(MovimientoPieza.Paso(pz, TipoMovimientoPieza.Salida, EstadoPieza.Cargada, a.Id, ahora, acceso.Actor.ToString()));
            }
            var pedido = pedidos[a.PedidoPtId];
            pedido.MarcarEnRuta(ahora);
            if (facturados.ContainsKey(a.Folio) && pedido.Confirmar72(ahora))
                await eventos.PublishAsync(new PedidoEnRutaEvent(empresaId, ahora, pedido.Id, pedido.NumeroPedido, s.Folio, a.Folio, s.CartaPorteFolio), cancellationToken);
        }
        s.Emitir(ahora);
        await db.SaveChangesAsync(cancellationToken);
        return await SalidaMapeo.ADtoAsync(db, pedidosPort, empresaId, s, true, false, cancellationToken);
    }
}

public sealed class CancelarHojaSalidaHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<CancelarHojaSalidaCommand, HojaSalidaDto>
{
    public async Task<HojaSalidaDto> Handle(CancelarHojaSalidaCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        var s = await SalidaMapeo.Completa(db).FirstOrDefaultAsync(x => x.Id == c.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PT_SALIDA_NO_ENCONTRADA", "No existe la hoja de salida.");
        await acceso.VerificarSucursalAsync(s.SucursalId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        s.Cancelar();

        var ids = s.Albaranes.SelectMany(a => a.Piezas.Select(p => p.PiezaId)).ToList();
        var piezas = await db.Piezas.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var ahora = clock.UtcNow;
        foreach (var a in s.Albaranes)
            foreach (var pa in a.Piezas)
            {
                var pz = piezas[pa.PiezaId];
                pz.RevertirCarga();
                db.Movimientos.Add(MovimientoPieza.Paso(pz, TipoMovimientoPieza.CargaRevertida, EstadoPieza.Cargada, a.Id, ahora, acceso.Actor.ToString()));
            }
        await db.SaveChangesAsync(cancellationToken);
        return await SalidaMapeo.ADtoAsync(db, pedidosPort, empresa.Current ?? throw new UnauthorizedAccessException(), s, true, false, cancellationToken);
    }
}

public sealed class CancelarAlbaranSalidaHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, IFacturacionSalidaPort facturacion,
    ICurrentEmpresaContext empresa, IClock clock) : IRequestHandler<CancelarAlbaranSalidaCommand, HojaSalidaDto>
{
    /// <summary>En carga (hoja en Borrador) lo cancela quien opera la hoja. Emitido: <see cref="AutorizarCancelacionAlbaranHandler"/> (M-247).</summary>
    public async Task<HojaSalidaDto> Handle(CancelarAlbaranSalidaCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        var s = await SalidaMapeo.Completa(db).FirstOrDefaultAsync(x => x.Id == c.HojaId, cancellationToken)
            ?? throw new EntityNotFoundException("PT_SALIDA_NO_ENCONTRADA", "No existe la hoja de salida.");
        await acceso.VerificarSucursalAsync(s.SucursalId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        await CancelacionAlbaran.EjecutarAsync(db, facturacion, s, c.AlbaranId, c.Motivo, acceso.Actor, clock.UtcNow, cancellationToken);
        return await SalidaMapeo.ADtoAsync(db, pedidosPort, empresa.Current ?? throw new UnauthorizedAccessException(), s, true, false, cancellationToken);
    }
}

/// <summary>
/// M-247 · cancelación de un albarán por etapa. En carga: libera la carga. Emitido: sin entrega, sin liquidación, sin factura timbrada, sin 72
/// enviado y con la Carta Porte ya cancelada; las piezas regresan a PT con su movimiento y Facturación descarta la salida facturable.
/// Entregado o liquidado: no se cancela (corrección de entrega o caso de devolución).
/// </summary>
internal static class CancelacionAlbaran
{
    public static async Task EjecutarAsync(ProductoTerminadoDbContext db, IFacturacionSalidaPort facturacion, SalidaPt s, Guid albaranId, string motivo, Guid actor,
        DateTimeOffset ahora, CancellationToken ct)
    {
        var albaran = s.Albaranes.FirstOrDefault(a => a.Id == albaranId)
            ?? throw new BusinessRuleException("PT_ALBARAN_NO_PERTENECE_A_SALIDA", "El albarán no pertenece a esta hoja de salida.");
        if (albaran.Cancelado) throw new BusinessRuleException("PT_ALBARAN_YA_CANCELADO", "El albarán ya está cancelado.");
        if (s.Estado == EstadoSalida.Cancelada) throw new BusinessRuleException("PT_SALIDA_CANCELADA", "La hoja de salida está cancelada.");
        if (albaran.Entregado || await db.Entregas.AnyAsync(e => e.AlbaranPtId == albaran.Id, ct))
            throw new BusinessRuleException("PT_ALBARAN_YA_ENTREGADO", "El albarán ya fue entregado en ruta; no se puede cancelar.");
        if (albaran.Liquidado || await db.Liquidaciones.AnyAsync(l => l.SalidaPtId == s.Id && l.Estado == EstadoLiquidacion.Cerrada && l.Albaranes.Any(a => a.AlbaranPtId == albaran.Id), ct))
            throw new BusinessRuleException("PT_ALBARAN_YA_LIQUIDADO", "El albarán ya fue liquidado en caja; no se puede cancelar.");

        var pedido = await db.PedidosPt.FirstAsync(p => p.Id == albaran.PedidoPtId, ct);
        if (s.Estado == EstadoSalida.Emitida)
        {
            // El 72 no se revierte en A+W (TI, 11-nov): con él enviado el albarán ya no es cancelable.
            if (pedido.Estado72At is not null)
                throw new BusinessRuleException("PT_ALBARAN_CON_72", "El pedido ya se informó a A+W en 72 (no se revierte): el albarán no se cancela; use corrección de entrega o caso de devolución.");
            if (s.CartaPorteId is { } cp && await facturacion.CartaPorteVigenteAsync(cp, ct))
                throw new BusinessRuleException("PT_SALIDA_CON_CARTA_PORTE", $"La hoja lleva la Carta Porte {s.CartaPorteFolio} vigente: cancélela primero en Facturación.");
        }
        // Valida separación de funciones y solicitud antes de tocar Facturación.
        s.CancelarAlbaran(albaran.Id, motivo, actor, ahora);
        if (s.Estado != EstadoSalida.Borrador && albaran.EmitidoAt is not null)
        {
            var rFacturacion = await facturacion.CancelarSalidaAsync(albaran.Folio, motivo, ct);
            if (rFacturacion.EnRevision)
                throw new BusinessRuleException("PT_ALBARAN_YA_FACTURADO", "El albarán ya cuenta con factura timbrada ante el SAT; debe cancelarse primero el CFDI.");
        }

        var ids = albaran.Piezas.Select(p => p.PiezaId).ToList();
        foreach (var pz in await db.Piezas.Where(p => ids.Contains(p.Id)).ToListAsync(ct))
        {
            var anterior = pz.Estado;
            pz.RevertirSalida();
            db.Movimientos.Add(MovimientoPieza.SalidaCancelada(pz, anterior, albaran.Id, motivo, ahora, actor.ToString()));
        }

        // Si estaba en ruta (71) y ya no tiene otros albaranes emitidos vigentes, regresa a 70.
        if (pedido.EnRutaAt is not null)
        {
            var otros = await (from a in db.Albaranes join sh in db.Salidas on a.SalidaPtId equals sh.Id
                               where a.PedidoPtId == pedido.Id && a.Id != albaran.Id && a.CanceladoAt == null && a.EmitidoAt != null && sh.Estado != EstadoSalida.Cancelada
                               select a.Id).AnyAsync(ct);
            if (!otros) pedido.RegresarALiberado();
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>M-247: pide cancelar un albarán emitido (lo hace quien opera la hoja).</summary>
public sealed record SolicitarCancelacionAlbaranCommand(Guid HojaId, Guid AlbaranId, string Motivo) : IRequest<HojaSalidaDto>;

/// <summary>M-247: autoriza (ejecuta) o rechaza la cancelación pedida. Otra persona, con <c>producto_terminado.salida.cancelar-emitida</c>.</summary>
public sealed record ResolverCancelacionAlbaranCommand(Guid HojaId, Guid AlbaranId, bool Autorizar, string Motivo) : IRequest<HojaSalidaDto>;

public sealed class SolicitarCancelacionAlbaranHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<SolicitarCancelacionAlbaranCommand, HojaSalidaDto>
{
    public async Task<HojaSalidaDto> Handle(SolicitarCancelacionAlbaranCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        var s = await SalidaMapeo.Completa(db).FirstOrDefaultAsync(x => x.Id == c.HojaId, cancellationToken)
            ?? throw new EntityNotFoundException("PT_SALIDA_NO_ENCONTRADA", "No existe la hoja de salida.");
        await acceso.VerificarSucursalAsync(s.SucursalId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        if (await db.Entregas.AnyAsync(e => e.AlbaranPtId == c.AlbaranId, cancellationToken))
            throw new BusinessRuleException("PT_ALBARAN_YA_ENTREGADO", "El albarán ya fue entregado: se corrige la entrega o se registra la devolución, no se cancela.");
        s.SolicitarCancelacionAlbaran(c.AlbaranId, c.Motivo, acceso.Actor, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return await SalidaMapeo.ADtoAsync(db, pedidosPort, empresa.Current ?? throw new UnauthorizedAccessException(), s, true, false, cancellationToken);
    }
}

public sealed class ResolverCancelacionAlbaranHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, IFacturacionSalidaPort facturacion,
    ICurrentEmpresaContext empresa, IClock clock) : IRequestHandler<ResolverCancelacionAlbaranCommand, HojaSalidaDto>
{
    public async Task<HojaSalidaDto> Handle(ResolverCancelacionAlbaranCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoSalidaCancelarEmitida, cancellationToken);
        var s = await SalidaMapeo.Completa(db).FirstOrDefaultAsync(x => x.Id == c.HojaId, cancellationToken)
            ?? throw new EntityNotFoundException("PT_SALIDA_NO_ENCONTRADA", "No existe la hoja de salida.");
        await acceso.VerificarSucursalAsync(s.SucursalId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        if (c.Autorizar) await CancelacionAlbaran.EjecutarAsync(db, facturacion, s, c.AlbaranId, c.Motivo, acceso.Actor, clock.UtcNow, cancellationToken);
        else
        {
            s.RechazarCancelacionAlbaran(c.AlbaranId, c.Motivo, acceso.Actor, clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }
        return await SalidaMapeo.ADtoAsync(db, pedidosPort, empresa.Current ?? throw new UnauthorizedAccessException(), s, true, false, cancellationToken);
    }
}

public sealed class ListarHojasSalidaHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, ICurrentEmpresaContext empresa)
    : IRequestHandler<ListarHojasSalidaQuery, HojasSalidaPagina>
{
    public async Task<HojasSalidaPagina> Handle(ListarHojasSalidaQuery q, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoPiezasVer, cancellationToken);
        const string todas = PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales;
        var visibles = await acceso.SucursalesVisiblesAsync(todas, cancellationToken);
        if (q.SucursalId is { } s) await acceso.VerificarSucursalAsync(s, todas, cancellationToken);

        var consulta = SalidaMapeo.Completa(db).AsNoTracking();
        if (visibles is not null) consulta = consulta.Where(x => visibles.Contains(x.SucursalId));
        if (q.SucursalId is { } sid) consulta = consulta.Where(x => x.SucursalId == sid);
        if (q.Estado is { } e) { var estado = Enum.Parse<EstadoSalida>(e); consulta = consulta.Where(x => x.Estado == estado); }

        var total = await consulta.CountAsync(cancellationToken);
        var items = await consulta.OrderByDescending(x => x.CreadaAt).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(cancellationToken);
        var empresaId = empresa.Current ?? throw new UnauthorizedAccessException();
        var dtos = new List<HojaSalidaDto>();
        foreach (var x in items) dtos.Add(await SalidaMapeo.ADtoAsync(db, pedidosPort, empresaId, x, false, false, cancellationToken));
        return new HojasSalidaPagina(dtos, total);
    }
}

public sealed class ObtenerHojaSalidaHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, IFacturacionSalidaPort facturacion, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<ObtenerHojaSalidaQuery, HojaSalidaDto>
{
    public async Task<HojaSalidaDto> Handle(ObtenerHojaSalidaQuery q, CancellationToken cancellationToken)
    {
        var s = await SalidaMapeo.Completa(db).AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PT_SALIDA_NO_ENCONTRADA", "No existe la hoja de salida.");
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoPiezasVer, cancellationToken);
        const string todas = PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales;
        var visibles = await acceso.SucursalesVisiblesAsync(todas, cancellationToken);
        if (visibles is not null && !visibles.Contains(s.SucursalId))
            throw new ForbiddenException("SUCURSAL_NO_ASOCIADA", "No tiene acceso a esta hoja de salida.");
        var dto = await SalidaMapeo.ADtoAsync(db, pedidosPort, empresa.Current ?? throw new UnauthorizedAccessException(), s, true, false, cancellationToken);
        return await ReglasHoja.ConReglasAsync(db, facturacion, s, dto, FechaContable.HoyLocal(clock.UtcNow), cancellationToken);
    }
}

public sealed class AutorizarHojaSalidaHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, IFacturacionSalidaPort facturacion,
    ICurrentEmpresaContext empresa, IClock clock) : IRequestHandler<AutorizarHojaSalidaCommand, HojaSalidaDto>
{
    public async Task<HojaSalidaDto> Handle(AutorizarHojaSalidaCommand c, CancellationToken cancellationToken)
    {
        var regla = ReglasSalidaPt.Nivel(c.Nivel) ?? throw new BusinessRuleException("PT_NIVEL_AUTORIZACION_INVALIDO", "Seleccione un nivel de autorización válido.");
        await acceso.ExigirPermisoAsync(Permiso(c.Nivel), cancellationToken);
        var s = await SalidaMapeo.Completa(db).FirstOrDefaultAsync(x => x.Id == c.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PT_SALIDA_NO_ENCONTRADA", "No existe la hoja de salida.");
        await acceso.VerificarSucursalAsync(s.SucursalId, PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales, cancellationToken);
        // Suplencia (igual que CE): el nivel que no es el primero dice por qué no autorizó el anterior.
        var suplencia = regla.Suplencia ? ReglasSalidaPt.MotivoValido(c.Suplencia) : null;

        var ahora = clock.UtcNow;
        var hoy = FechaContable.HoyLocal(ahora);
        var (sinFactura, parciales) = await ReglasHoja.EvaluarAsync(db, facturacion, s, hoy, cancellationToken);
        var alcance = regla.Parcial ? parciales : sinFactura;
        if (alcance.Count == 0)
            throw new BusinessRuleException("PT_SALIDA_AUTORIZACION_INNECESARIA",
                regla.Parcial ? "Ningún pedido de la hoja sale incompleto." : "Todos los pedidos de la hoja tienen factura válida.");

        s.Autorizar(regla.Parcial, new AutorizacionSalidaPt(acceso.Actor, ahora, c.Nivel, c.Motivo, suplencia, alcance), await PreparadoresAsync(s, cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        var dto = await SalidaMapeo.ADtoAsync(db, pedidosPort, empresa.Current ?? throw new UnauthorizedAccessException(), s, true, false, cancellationToken);
        return await ReglasHoja.ConReglasAsync(db, facturacion, s, dto, hoy, cancellationToken);
    }

    /// <summary>Quien armó la hoja y quien cargó cualquiera de sus piezas (movimiento de carga con referencia al albarán).</summary>
    private async Task<IReadOnlyCollection<Guid>> PreparadoresAsync(SalidaPt s, CancellationToken ct)
    {
        var piezaIds = s.Albaranes.SelectMany(a => a.Piezas.Select(p => p.PiezaId)).ToList();
        var albaranes = s.Albaranes.Select(a => a.Id.ToString()).ToList();
        var cargas = await db.Movimientos.AsNoTracking().Where(m => piezaIds.Contains(m.PiezaId) && m.Tipo == TipoMovimientoPieza.Carga)
            .Select(m => new { m.Referencia, m.Usuario }).ToListAsync(ct);
        var preparadores = cargas.Where(m => albaranes.Any(a => m.Referencia == a || m.Referencia.StartsWith(a + "/", StringComparison.Ordinal)))
            .Select(m => Guid.TryParse(m.Usuario, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToHashSet();
        if (s.CreadaPorId is { } creador) preparadores.Add(creador);
        return preparadores;
    }

    private static string Permiso(string nivel) => nivel switch
    {
        "administracion" => PermisosCanonicos.ProductoTerminadoSalidaAprobarAdministracion,
        "planeacion" => PermisosCanonicos.ProductoTerminadoSalidaAprobarPlaneacion,
        "contraloria" => PermisosCanonicos.ProductoTerminadoSalidaAprobarContraloria,
        "credito" => PermisosCanonicos.ProductoTerminadoSalidaAprobarCredito,
        _ => PermisosCanonicos.ProductoTerminadoSalidaAprobarVentas,
    };
}
