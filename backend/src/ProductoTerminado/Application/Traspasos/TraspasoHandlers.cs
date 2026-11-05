using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.Application.Traspasos;

// Permisos reutilizados (sin permiso nuevo): ver = piezas.ver; operar salida y recepción = excepciones.gestionar, ambos con su bypass por sucursal.
internal static class TraspasoMapeo
{
    public static TraspasoDto ADto(TraspasoPt t, IReadOnlyDictionary<Guid, (string Etiqueta, string Pedido)>? piezas = null) => new(
        t.Id, t.SucursalOrigenId, t.SucursalDestinoId, t.Estado.ToString(), t.SalidaAt, t.RecibidoAt, t.Piezas.Count, t.Piezas.Count(p => !p.Recibida),
        piezas is null ? [] : t.Piezas.Select(p => new TraspasoPiezaDto(p.PiezaId, piezas[p.PiezaId].Etiqueta, piezas[p.PiezaId].Pedido, p.Recibida)).ToList());

    public static async Task<IReadOnlyDictionary<Guid, (string Etiqueta, string Pedido)>> PiezasAsync(ProductoTerminadoDbContext db, TraspasoPt t, CancellationToken ct)
    {
        var ids = t.Piezas.Select(p => p.PiezaId).ToList();
        var filas = await (from p in db.Piezas.AsNoTracking() join o in db.PedidosPt.AsNoTracking() on p.PedidoPtId equals o.Id where ids.Contains(p.Id) select new { p.Id, p.Etiqueta, o.NumeroPedido }).ToListAsync(ct);
        return filas.ToDictionary(x => x.Id, x => (x.Etiqueta, x.NumeroPedido));
    }
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

        var ahora = clock.UtcNow;
        var traspaso = TraspasoPt.Crear(empresaId, c.SucursalOrigenId, c.SucursalDestinoId, piezas.Select(p => p.Id).ToList(), ahora);
        foreach (var p in piezas)
        {
            db.Movimientos.Add(MovimientoPieza.TrasladoSalida(p, c.SucursalDestinoId, traspaso.Id, ahora, acceso.Actor.ToString()));
            p.SalirATransito();
        }
        db.Traspasos.Add(traspaso);
        await db.SaveChangesAsync(cancellationToken);
        return TraspasoMapeo.ADto(traspaso, piezas.ToDictionary(p => p.Id, p => (p.Etiqueta, "")));
    }
}

public sealed class RecibirTraspasoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IClock clock) : IRequestHandler<RecibirTraspasoCommand, TraspasoDto>
{
    public async Task<TraspasoDto> Handle(RecibirTraspasoCommand c, CancellationToken cancellationToken)
    {
        var t = await db.Traspasos.Include(x => x.Piezas).FirstOrDefaultAsync(x => x.Id == c.TraspasoId, cancellationToken)
            ?? throw new EntityNotFoundException("PT_TRASPASO_NO_ENCONTRADO", "No existe el traspaso.");
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        await acceso.VerificarSucursalAsync(t.SucursalDestinoId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);

        var ids = c.PiezaIds ?? t.Piezas.Where(p => !p.Recibida).Select(p => p.PiezaId).ToList();
        var ahora = clock.UtcNow;
        t.Recibir(ids, ahora);
        foreach (var p in await db.Piezas.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken))
        {
            p.Recibir(t.SucursalDestinoId);
            db.Movimientos.Add(MovimientoPieza.TrasladoRecepcion(p, t.SucursalOrigenId, t.SucursalDestinoId, t.Id, ahora, acceso.Actor.ToString()));
        }
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
        if (q.Estado is { } e) { var estado = Enum.Parse<EstadoTraspaso>(e); consulta = consulta.Where(t => t.Estado == estado); }

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
