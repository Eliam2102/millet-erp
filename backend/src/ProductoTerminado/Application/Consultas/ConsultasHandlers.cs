using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application.Escaneos;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;

namespace Millet.ProductoTerminado.Application.Consultas;

public sealed class ListarPiezasHandler(ProductoTerminadoDbContext db, PtAcceso acceso) : IRequestHandler<ListarPiezasQuery, PiezasPagina>
{
    public async Task<PiezasPagina> Handle(ListarPiezasQuery q, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoPiezasVer, cancellationToken);
        const string todas = PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales;
        var visibles = await acceso.SucursalesVisiblesAsync(todas, cancellationToken);
        if (q.SucursalId is { } s) await acceso.VerificarSucursalAsync(s, todas, cancellationToken);

        // El alcance es la sucursal física de la pieza (piezas.sucursal_id), nunca la sucursal de venta del pedido.
        var piezas = db.Piezas.AsNoTracking().AsQueryable();
        if (q.SucursalId is { } sid) piezas = piezas.Where(p => p.SucursalId == sid);
        else if (visibles is not null) piezas = piezas.Where(p => visibles.Contains(p.SucursalId));

        var numero = q.Pedido?.Trim();
        var consulta = from p in piezas
                       join o in db.PedidosPt.AsNoTracking() on p.PedidoPtId equals o.Id
                       where string.IsNullOrEmpty(numero) || o.NumeroPedido == numero
                       select new { p, o.NumeroPedido };

        var total = await consulta.CountAsync(cancellationToken);
        var items = await consulta.OrderBy(x => x.NumeroPedido).ThenBy(x => x.p.Posicion).ThenBy(x => x.p.NumeroPieza)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(x => new PiezaDto(x.p.Id, x.p.Etiqueta, x.NumeroPedido, x.p.Posicion, x.p.NumeroPieza, x.p.SucursalId, x.p.Estado.ToString(), x.p.EscaneadaAt))
            .ToListAsync(cancellationToken);

        IReadOnlyList<AvancePosicionDto> avance = [];
        if (!string.IsNullOrEmpty(numero))
        {
            var posiciones = await (from pos in db.Posiciones.AsNoTracking()
                                    join o in db.PedidosPt.AsNoTracking() on pos.PedidoPtId equals o.Id
                                    where o.NumeroPedido == numero
                                    orderby pos.Posicion
                                    select new { pos.Posicion, pos.Descripcion, pos.PiezasEsperadas }).ToListAsync(cancellationToken);
            var cuentas = await consulta.GroupBy(x => x.p.Posicion).Select(g => new { Posicion = g.Key, N = g.Count() }).ToListAsync(cancellationToken);
            avance = posiciones.Select(pos =>
            {
                var n = cuentas.FirstOrDefault(c => c.Posicion == pos.Posicion)?.N ?? 0;
                var terminadas = Math.Min(n, pos.PiezasEsperadas);
                var estado = n == 0 ? "SinPiezas" : terminadas >= pos.PiezasEsperadas && pos.PiezasEsperadas > 0 ? "Completo" : "Parcial";
                return new AvancePosicionDto(pos.Posicion, pos.Descripcion, pos.PiezasEsperadas, terminadas, Math.Max(0, pos.PiezasEsperadas - terminadas), estado);
            }).ToList();
        }
        return new PiezasPagina(items, total, avance);
    }
}

public sealed class ListarEscaneosHandler(ProductoTerminadoDbContext db, PtAcceso acceso) : IRequestHandler<ListarEscaneosQuery, EscaneosPagina>
{
    public async Task<EscaneosPagina> Handle(ListarEscaneosQuery q, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, cancellationToken);
        var visibles = await acceso.SucursalesVisiblesAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, cancellationToken);

        var abiertos = new[] { ResultadoEscaneo.Pospuesto, ResultadoEscaneo.Excepcion, ResultadoEscaneo.Reposicion };
        var filas = db.Escaneos.AsNoTracking().Where(e => e.ResueltoAt == null && abiertos.Contains(e.Resultado));
        if (q.Resultado is not null)
        {
            var r = Enum.Parse<ResultadoEscaneo>(q.Resultado);
            filas = filas.Where(e => e.Resultado == r);
        }
        // Sin sucursal resuelta solo se ve con el bypass corporativo (visibles == null).
        if (visibles is not null) filas = filas.Where(e => e.SucursalId != null && visibles.Contains(e.SucursalId.Value));

        var total = await filas.CountAsync(cancellationToken);
        var items = await filas.OrderByDescending(e => e.RecibidoAt).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(cancellationToken);
        return new EscaneosPagina(items.Select(BandejaGuardas.ADto).ToList(), total);
    }
}
