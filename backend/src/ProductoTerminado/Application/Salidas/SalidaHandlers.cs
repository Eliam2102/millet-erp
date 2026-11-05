using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
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
        var cabecera = new HojaSalidaDto(s.Id, s.Folio, s.SucursalId, s.Destino, s.Foranea, s.VehiculoId, s.OperadorId, s.Estado.ToString(), s.CreadaAt, s.EmitidaAt, s.CartaPorteFolio, s.Albaranes.Count, total, [], yaEmitida);
        if (!detalle) return cabecera;

        var pedidoIds = s.Albaranes.Select(a => a.PedidoPtId).ToList();
        var pedidosPt = await db.PedidosPt.AsNoTracking().Include(p => p.Posiciones).Where(p => pedidoIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var origen = await pedidos.ObtenerParaMonitorAsync(empresaId, pedidosPt.Values.Select(p => p.PedidoFacturableId).ToList(), ct);
        var piezaIds = s.Albaranes.SelectMany(a => a.Piezas.Select(p => p.PiezaId)).ToList();
        var piezas = await db.Piezas.AsNoTracking().Where(p => piezaIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var albaranes = s.Albaranes.OrderBy(a => a.Secuencia).Select(a =>
        {
            var pedido = pedidosPt[a.PedidoPtId];
            var mias = a.Piezas.Select(p => piezas[p.PiezaId]).OrderBy(p => p.Posicion).ThenBy(p => p.NumeroPieza).ToList();
            var cliente = origen.GetValueOrDefault(pedido.PedidoFacturableId);
            return new AlbaranDto(a.Id, a.Folio, a.Secuencia, pedido.NumeroPedido, cliente?.ClienteNombre ?? "", cliente?.Destino, a.EmitidoAt, mias.Count,
                mias.GroupBy(p => p.Posicion).Select(g => new AlbaranLineaDto(g.Key, pedido.Posiciones.FirstOrDefault(x => x.Posicion == g.Key)?.Descripcion, g.Count())).ToList(),
                mias.Select(p => p.Etiqueta).ToList());
        }).ToList();
        return cabecera with { Albaranes = albaranes };
    }

    public static IQueryable<SalidaPt> Completa(ProductoTerminadoDbContext db) => db.Salidas.Include(x => x.Albaranes).ThenInclude(a => a.Piezas);
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
            throw new BusinessRuleException("PT_PEDIDO_NO_LIBERADO", $"Solo salen pedidos liberados (70). No liberados: {string.Join(", ", noLiberados)}");

        var piezas = await db.Piezas.Where(p => etiquetas.Contains(p.Etiqueta)).ToDictionaryAsync(p => p.Etiqueta, cancellationToken);
        var faltan = etiquetas.Except(piezas.Keys).ToList();
        if (faltan.Count > 0) throw new BusinessRuleException("PT_SALIDA_PIEZAS_INVALIDAS", $"Etiquetas inexistentes: {string.Join(", ", faltan)}");
        if (piezas.Values.Any(p => p.SucursalId != c.SucursalId))
            throw new BusinessRuleException("PT_SALIDA_PIEZA_OTRA_SUCURSAL", "Todas las piezas deben estar en la sucursal de la salida.");
        var ids = piezas.Values.Select(p => p.Id).ToList();
        var yaEnHoja = await (from ap in db.AlbaranPiezas join a in db.Albaranes on ap.AlbaranPtId equals a.Id join s in db.Salidas on a.SalidaPtId equals s.Id
                              where ids.Contains(ap.PiezaId) && s.Estado != EstadoSalida.Cancelada select ap.PiezaId).AnyAsync(cancellationToken);
        if (yaEnHoja) throw new BusinessRuleException("PT_SALIDA_PIEZA_YA_EN_HOJA", "Alguna pieza ya está en otra hoja de salida.");

        var ahora = clock.UtcNow;
        var salida = SalidaPt.Crear(empresaId, c.SucursalId, c.Destino, c.Foranea, c.VehiculoId, c.OperadorId, c.DistanciaKm, c.PesoKg, c.OrigenCodigoPostal, c.OrigenEstado, c.DestinoCodigoPostal, c.DestinoEstado, ahora);
        foreach (var p in c.Pedidos)
        {
            var pedido = pedidos[p.NumeroPedido.Trim()];
            var delPedido = p.Etiquetas.Select(e => piezas[e.Trim()]).ToList();
            if (delPedido.Any(x => x.PedidoPtId != pedido.Id))
                throw new BusinessRuleException("PT_SALIDA_PIEZA_OTRO_PEDIDO", $"Alguna pieza no pertenece al pedido {pedido.NumeroPedido}.");
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

        var piezaIds = s.Albaranes.SelectMany(a => a.Piezas.Select(p => p.PiezaId)).ToList();
        var piezas = await db.Piezas.Where(p => piezaIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var descargadas = piezas.Values.Where(p => p.Estado != EstadoPieza.Cargada).Select(p => p.Etiqueta).ToList();
        if (descargadas.Count > 0) throw new BusinessRuleException("PT_PIEZA_NO_CARGADA", $"Piezas que ya no están cargadas: {string.Join(", ", descargadas)}");
        var pedidoIds = s.Albaranes.Select(a => a.PedidoPtId).ToList();
        var pedidos = await db.PedidosPt.Where(p => pedidoIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        var ahora = clock.UtcNow;
        // La Carta Porte primero (su falla más común es de configuración fiscal y no debe dejar una salida facturable sin embarcar); queda guardada para que un reintento no emita otra.
        if (s.Foranea && s.CartaPorteId is null)
        {
            var cp = await facturacion.EmitirCartaPorteAsync(new SolicitudCartaPorte(s.SucursalId, Origen, s.OrigenCodigoPostal!, s.OrigenEstado!, s.Destino, s.DestinoCodigoPostal!, s.DestinoEstado!, s.DistanciaKm!.Value,
                s.VehiculoId!.Value, s.OperadorId!.Value, s.Albaranes.Count == 1 ? pedidos[s.Albaranes.First().PedidoPtId].PedidoFacturableId : null, ahora,
                [new MercanciaSalida($"Vidrio terminado, {piezaIds.Count} piezas")], s.PesoKg!.Value), cancellationToken);
            s.AsignarCartaPorte(cp.Id, cp.Folio);
            await db.SaveChangesAsync(cancellationToken);
        }

        // Facturación valida que no exceda lo pedido y es idempotente por folio de albarán, así reintentar tras un fallo no duplica.
        foreach (var a in s.Albaranes.OrderBy(a => a.Secuencia))
            await facturacion.RegistrarSalidaAsync(pedidos[a.PedidoPtId].PedidoFacturableId, a.Folio,
                a.Piezas.Select(p => piezas[p.PiezaId]).GroupBy(p => p.Posicion).OrderBy(g => g.Key).Select(g => (g.Key, (decimal)g.Count())).ToList(), cancellationToken);

        foreach (var a in s.Albaranes)
        {
            foreach (var pa in a.Piezas)
            {
                var pz = piezas[pa.PiezaId];
                pz.Embarcar();
                db.Movimientos.Add(MovimientoPieza.Paso(pz, TipoMovimientoPieza.Salida, EstadoPieza.Cargada, a.Id, ahora, acceso.Actor.ToString()));
            }
            var pedido = pedidos[a.PedidoPtId];
            if (pedido.MarcarEnRuta(ahora))
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

public sealed class ObtenerHojaSalidaHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidosPort, ICurrentEmpresaContext empresa)
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
        return await SalidaMapeo.ADtoAsync(db, pedidosPort, empresa.Current ?? throw new UnauthorizedAccessException(), s, true, false, cancellationToken);
    }
}
