using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;

namespace Millet.ProductoTerminado.Application.Traspasos;

// Permisos reutilizados (sin permiso nuevo): ver = piezas.ver; operar (preparar, autorizar, recibir, clasificar y aclarar) = excepciones.gestionar, ambos con su bypass por sucursal.
internal static class TraspasoMapeo
{
    public static TraspasoDto ADto(TraspasoPt t, IReadOnlyDictionary<Guid, (string Etiqueta, string Pedido)>? piezas = null) => new(
        t.Id, t.Folio, t.SucursalOrigenId, t.SucursalDestinoId, t.Estado.ToString(), t.SalidaAt, t.RecibidoAt, t.Piezas.Count, t.Piezas.Count(p => p.EnTransito),
        piezas is null ? [] : t.Piezas.Select(p => new TraspasoPiezaDto(p.PiezaId, piezas[p.PiezaId].Etiqueta, piezas[p.PiezaId].Pedido, p.Recibida,
            p.Clasificacion?.ToString(), p.MotivoDiferencia, p.EvidenciaDiferencia, p.Resolucion?.ToString())).ToList(),
        t.PreparadoPorId, t.AutorizadoPorId, t.AutorizadoAt, t.Foranea, t.Destino, t.CartaPorteFolio);

    public static async Task<IReadOnlyDictionary<Guid, (string Etiqueta, string Pedido)>> PiezasAsync(ProductoTerminadoDbContext db, TraspasoPt t, CancellationToken ct)
    {
        var ids = t.Piezas.Select(p => p.PiezaId).ToList();
        var filas = await (from p in db.Piezas.AsNoTracking() join o in db.PedidosPt.AsNoTracking() on p.PedidoPtId equals o.Id where ids.Contains(p.Id) select new { p.Id, p.Etiqueta, o.NumeroPedido }).ToListAsync(ct);
        return filas.ToDictionary(x => x.Id, x => (x.Etiqueta, x.NumeroPedido));
    }

    /// <summary>Piezas agrupadas por pedido y posición, para que Contabilidad las valúe.</summary>
    public static async Task<IReadOnlyList<PedidoPiezasTraspaso>> PedidosDeAsync(ProductoTerminadoDbContext db, IReadOnlyCollection<PiezaPt> piezas, CancellationToken ct)
    {
        var ids = piezas.Select(p => p.PedidoPtId).Distinct().ToList();
        var numeros = await db.PedidosPt.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.NumeroPedido, ct);
        return piezas.GroupBy(p => p.PedidoPtId).Select(g => new PedidoPiezasTraspaso(g.Key, numeros[g.Key],
            g.GroupBy(p => p.Posicion).OrderBy(x => x.Key).Select(x => new PosicionEntregada(x.Key, x.Count())).ToList())).ToList();
    }

    public static async Task<TraspasoPt> CargarAsync(ProductoTerminadoDbContext db, Guid id, CancellationToken ct) =>
        await db.Traspasos.Include(x => x.Piezas).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("PT_TRASPASO_NO_ENCONTRADO", "No existe el traspaso.");
}

public sealed class CrearTraspasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, ICurrentEmpresaContext empresa, IClock clock) : IRequestHandler<CrearTraspasoCommand, TraspasoDto>
{
    public async Task<TraspasoDto> Handle(CrearTraspasoCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        await acceso.VerificarSucursalAsync(c.SucursalOrigenId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        var empresaId = empresa.Current ?? throw new UnauthorizedAccessException();

        var etiquetas = c.Etiquetas.Select(e => e.Trim()).ToList();
        var piezas = await db.Piezas.Where(p => etiquetas.Contains(p.Etiqueta)).ToListAsync(cancellationToken);
        var faltan = etiquetas.Except(piezas.Select(p => p.Etiqueta)).ToList();
        if (faltan.Count > 0 || piezas.Count != etiquetas.Count)
            throw new BusinessRuleException("PT_TRASPASO_PIEZAS_INVALIDAS", $"Etiquetas inexistentes o repetidas: {string.Join(", ", faltan)}");
        if (piezas.Any(p => p.SucursalId != c.SucursalOrigenId))
            throw new BusinessRuleException("PT_TRASPASO_PIEZA_OTRA_SUCURSAL", "Todas las piezas deben estar en la sucursal de origen.");
        var noEnPt = piezas.Where(p => p.Estado != EstadoPieza.EnPT).Select(p => p.Etiqueta).ToList();
        if (noEnPt.Count > 0) throw new BusinessRuleException("PT_PIEZA_NO_EN_PT", $"Piezas que no están en PT: {string.Join(", ", noEnPt)}");
        // Las piezas no se mueven al preparar: una pieza no puede estar en dos traspasos por autorizar.
        var ids = piezas.Select(p => p.Id).ToList();
        if (await db.Traspasos.AnyAsync(t => t.Estado == EstadoTraspaso.Preparado && t.Piezas.Any(tp => ids.Contains(tp.PiezaId)), cancellationToken))
            throw new BusinessRuleException("PT_TRASPASO_PIEZA_YA_PREPARADA", "Alguna pieza ya está en otro traspaso por autorizar.");

        var ahora = clock.UtcNow;
        var traspaso = TraspasoPt.Crear(empresaId, c.SucursalOrigenId, c.SucursalDestinoId, ids, acceso.Actor, ahora, c.Foranea, c.Transporte,
            // El folio lleva el día local, igual que la hoja de salida.
            HoraAw.FechaLocal(ahora));
        db.Traspasos.Add(traspaso);
        await db.SaveChangesAsync(cancellationToken);
        return TraspasoMapeo.ADto(traspaso, await TraspasoMapeo.PiezasAsync(db, traspaso, cancellationToken));
    }
}

public sealed class AutorizarTraspasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IFacturacionSalidaPort facturacion, IAvisoTraspasoPort aviso, IIntegrationEventPublisher eventos,
    ICurrentEmpresaContext empresa, IClock clock, ILogger<AutorizarTraspasoHandler> logger) : IRequestHandler<AutorizarTraspasoCommand, TraspasoDto>
{
    // ponytail: origen fijo hasta recibir de Logística la Carta Porte real (D15), igual que la hoja de salida.
    private const string Origen = "Planta Millet";

    public async Task<TraspasoDto> Handle(AutorizarTraspasoCommand c, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        var t = await TraspasoMapeo.CargarAsync(db, c.TraspasoId, cancellationToken);
        await acceso.VerificarSucursalAsync(t.SucursalOrigenId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        var empresaId = empresa.Current ?? throw new UnauthorizedAccessException();
        t.ExigirAutorizable(acceso.Actor);

        var ids = t.Piezas.Select(p => p.PiezaId).ToList();
        var piezas = await db.Piezas.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        var ahora = clock.UtcNow;

        // La Carta Porte primero (su falla más común es de configuración fiscal y no debe dejar piezas en tránsito sin documento); queda guardada para que un reintento no emita otra.
        if (t.Foranea && t.CartaPorteId is null)
        {
            var pedidoIds = piezas.Select(p => p.PedidoPtId).Distinct().ToList();
            var pedidos = await db.PedidosPt.Where(p => pedidoIds.Contains(p.Id)).ToListAsync(cancellationToken);
            var cp = await facturacion.EmitirCartaPorteAsync(new SolicitudCartaPorte(t.SucursalOrigenId, Origen, t.OrigenCodigoPostal!, t.OrigenEstado!, t.Destino!, t.DestinoCodigoPostal!, t.DestinoEstado!, t.DistanciaKm!.Value,
                t.VehiculoId!.Value, t.OperadorId!.Value, pedidos.Count == 1 ? pedidos[0].PedidoFacturableId : null, ahora, [new MercanciaSalida($"Vidrio terminado, {piezas.Count} piezas")], t.PesoKg!.Value), cancellationToken);
            t.AsignarCartaPorte(cp.Id, cp.Folio);
            await db.SaveChangesAsync(cancellationToken);
        }

        t.Autorizar(acceso.Actor, ahora);
        foreach (var p in piezas)
        {
            db.Movimientos.Add(MovimientoPieza.TrasladoSalida(p, t.SucursalDestinoId, t.Id, ahora, acceso.Actor.ToString()));
            p.SalirATransito();
        }
        await eventos.PublishAsync(new TraspasoSalidaAutorizadaEvent(empresaId, ahora, t.Id, t.Folio, t.SucursalOrigenId, t.SucursalDestinoId, await TraspasoMapeo.PedidosDeAsync(db, piezas, cancellationToken)), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        // El aviso es posterior y no condiciona la autorización: si el correo falla, el traspaso ya salió y queda en el log.
        try { await aviso.AvisarPorRecibirAsync(new AvisoTraspasoPorRecibir(empresaId, t.Id, t.Folio, t.SucursalDestinoId, piezas.Count), cancellationToken); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "[Traspasos] No se pudo avisar a la sucursal destino del traspaso {Folio}.", t.Folio);
        }
        return TraspasoMapeo.ADto(t, await TraspasoMapeo.PiezasAsync(db, t, cancellationToken));
    }
}

public sealed class RecibirTraspasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IIntegrationEventPublisher eventos, ICurrentEmpresaContext empresa, IClock clock) : IRequestHandler<RecibirTraspasoCommand, TraspasoDto>
{
    public async Task<TraspasoDto> Handle(RecibirTraspasoCommand c, CancellationToken cancellationToken)
    {
        var t = await TraspasoMapeo.CargarAsync(db, c.TraspasoId, cancellationToken);
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        await acceso.VerificarSucursalAsync(t.SucursalDestinoId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);

        var ids = c.PiezaIds ?? t.Piezas.Where(p => p.EnTransito).Select(p => p.PiezaId).ToList();
        var ahora = clock.UtcNow;
        t.Recibir(ids, ahora);
        var piezas = await db.Piezas.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        foreach (var p in piezas)
        {
            p.Recibir(t.SucursalDestinoId);
            db.Movimientos.Add(MovimientoPieza.TrasladoRecepcion(p, t.SucursalOrigenId, t.SucursalDestinoId, t.Id, ahora, acceso.Actor.ToString()));
        }
        await eventos.PublishAsync(new TraspasoEntradaRegistradaEvent(empresa.Current ?? throw new UnauthorizedAccessException(), ahora, t.Id, t.Folio, "Recepcion", t.SucursalDestinoId,
            await TraspasoMapeo.PedidosDeAsync(db, piezas, cancellationToken)), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return TraspasoMapeo.ADto(t, await TraspasoMapeo.PiezasAsync(db, t, cancellationToken));
    }
}

public sealed class ClasificarDiferenciasTraspasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso) : IRequestHandler<ClasificarDiferenciasTraspasoCommand, TraspasoDto>
{
    public async Task<TraspasoDto> Handle(ClasificarDiferenciasTraspasoCommand c, CancellationToken cancellationToken)
    {
        var t = await TraspasoMapeo.CargarAsync(db, c.TraspasoId, cancellationToken);
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        await acceso.VerificarSucursalAsync(t.SucursalDestinoId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        t.Clasificar(c.Piezas.Select(p => (p.PiezaId, Enum.Parse<ClasificacionDiferencia>(p.Clasificacion), p.Motivo, p.Evidencia)).ToList());
        await db.SaveChangesAsync(cancellationToken);
        return TraspasoMapeo.ADto(t, await TraspasoMapeo.PiezasAsync(db, t, cancellationToken));
    }
}

public sealed class AclararDiferenciaTraspasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IIntegrationEventPublisher eventos, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<AclararDiferenciaTraspasoCommand, TraspasoDto>
{
    public async Task<TraspasoDto> Handle(AclararDiferenciaTraspasoCommand c, CancellationToken cancellationToken)
    {
        var t = await TraspasoMapeo.CargarAsync(db, c.TraspasoId, cancellationToken);
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        await acceso.VerificarSucursalAsync(t.SucursalDestinoId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);
        var ahora = clock.UtcNow;
        var resolucion = Enum.Parse<ResolucionDiferencia>(c.Resolucion);
        t.Aclarar(c.PiezaId, resolucion, ahora);

        var pieza = await db.Piezas.FirstAsync(p => p.Id == c.PiezaId, cancellationToken);
        string resultado;
        Guid sucursalEntra;
        if (resolucion == ResolucionDiferencia.RegresadaAlOrigen)
        {
            pieza.RegresarAlOrigen();
            db.Movimientos.Add(MovimientoPieza.TrasladoRegreso(pieza, t.SucursalOrigenId, t.SucursalDestinoId, t.Id, ahora, acceso.Actor.ToString()));
            (resultado, sucursalEntra) = ("RegresoOrigen", t.SucursalOrigenId);
        }
        else
        {
            pieza.DarPorPerdida(t.SucursalDestinoId);
            db.Movimientos.Add(MovimientoPieza.TrasladoPerdida(pieza, t.SucursalOrigenId, t.SucursalDestinoId, t.Id, ahora, acceso.Actor.ToString()));
            (resultado, sucursalEntra) = ("Perdida", t.SucursalDestinoId);
        }
        // El puente se salda en la sucursal donde la pieza queda; así siempre termina en cero.
        await eventos.PublishAsync(new TraspasoEntradaRegistradaEvent(empresa.Current ?? throw new UnauthorizedAccessException(), ahora, t.Id, t.Folio, resultado, sucursalEntra,
            await TraspasoMapeo.PedidosDeAsync(db, [pieza], cancellationToken)), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return TraspasoMapeo.ADto(t, await TraspasoMapeo.PiezasAsync(db, t, cancellationToken));
    }
}

public sealed class ListarTraspasosHandler(ProductoTerminadoDbContext db, PtAcceso acceso) : IRequestHandler<ListarTraspasosQuery, TraspasosPagina>
{
    public async Task<TraspasosPagina> Handle(ListarTraspasosQuery q, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoPiezasVer, cancellationToken);
        const string todas = PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales;
        var visibles = await acceso.SucursalesVisiblesAsync(todas, cancellationToken);
        if (q.SucursalId is { } s) await acceso.VerificarSucursalAsync(s, todas, cancellationToken);

        // Un traspaso lo ve quien tiene acceso a su origen o a su destino.
        var consulta = db.Traspasos.AsNoTracking().Include(x => x.Piezas).AsQueryable();
        if (visibles is not null) consulta = consulta.Where(t => visibles.Contains(t.SucursalOrigenId) || visibles.Contains(t.SucursalDestinoId));
        if (q.SucursalId is { } sid) consulta = consulta.Where(t => t.SucursalOrigenId == sid || t.SucursalDestinoId == sid);
        if (q.DestinoId is { } did) consulta = consulta.Where(t => t.SucursalDestinoId == did);
        if (q.Estado is { } e) { var estados = e.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Enum.Parse<EstadoTraspaso>).ToList(); consulta = consulta.Where(t => estados.Contains(t.Estado)); }

        var total = await consulta.CountAsync(cancellationToken);
        var items = await consulta.OrderByDescending(t => t.SalidaAt).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(cancellationToken);
        return new TraspasosPagina(items.Select(t => TraspasoMapeo.ADto(t)).ToList(), total);
    }
}

public sealed class ObtenerTraspasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso) : IRequestHandler<ObtenerTraspasoQuery, TraspasoDto>
{
    public async Task<TraspasoDto> Handle(ObtenerTraspasoQuery q, CancellationToken cancellationToken)
    {
        var t = await db.Traspasos.AsNoTracking().Include(x => x.Piezas).FirstOrDefaultAsync(x => x.Id == q.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PT_TRASPASO_NO_ENCONTRADO", "No existe el traspaso.");
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoPiezasVer, cancellationToken);
        const string todas = PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales;
        var visibles = await acceso.SucursalesVisiblesAsync(todas, cancellationToken);
        if (visibles is not null && !visibles.Contains(t.SucursalOrigenId) && !visibles.Contains(t.SucursalDestinoId))
            throw new ForbiddenException("SUCURSAL_NO_ASOCIADA", "No tiene acceso a este traspaso.");
        return TraspasoMapeo.ADto(t, await TraspasoMapeo.PiezasAsync(db, t, cancellationToken));
    }
}
