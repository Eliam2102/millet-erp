using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application.Escaneos;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

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

        // Las piezas en tránsito siguen con su sucursal de origen; se muestra a dónde van.
        var transito = items.Where(i => i.Estado == nameof(EstadoPieza.EnTransito)).Select(i => i.Id).ToList();
        if (transito.Count > 0)
        {
            var destinos = await db.Traspasos.AsNoTracking().SelectMany(t => t.Piezas.Where(tp => !tp.Recibida && transito.Contains(tp.PiezaId)).Select(tp => new { tp.PiezaId, t.SucursalDestinoId }))
                .ToDictionaryAsync(x => x.PiezaId, x => x.SucursalDestinoId, cancellationToken);
            items = items.Select(i => destinos.TryGetValue(i.Id, out var d) ? i with { SucursalDestinoId = d } : i).ToList();
        }

        IReadOnlyList<AvancePosicionDto> avance = [];
        EstadoPedidoDto? estadoPedido = null;
        if (!string.IsNullOrEmpty(numero))
        {
            var posiciones = await (from pos in db.Posiciones.AsNoTracking()
                                    join o in db.PedidosPt.AsNoTracking() on pos.PedidoPtId equals o.Id
                                    where o.NumeroPedido == numero
                                    orderby pos.Posicion
                                    select new { pos.Posicion, pos.Descripcion, pos.PiezasEsperadas }).ToListAsync(cancellationToken);
            var est = await db.PedidosPt.AsNoTracking().Where(o => o.NumeroPedido == numero)
                .Select(o => new { o.Liberacion, o.MotivoLiberacion, o.LiberacionAt }).FirstOrDefaultAsync(cancellationToken);
            estadoPedido = est?.Liberacion is { } l ? new EstadoPedidoDto(l.ToString(), est.MotivoLiberacion, est.LiberacionAt) : null;
            var cuentas = await consulta.GroupBy(x => x.p.Posicion).Select(g => new { Posicion = g.Key, N = g.Count(), EnPt = g.Count(x => x.p.Estado == EstadoPieza.EnPT) }).ToListAsync(cancellationToken);
            avance = posiciones.Select(pos =>
            {
                var cuenta = cuentas.FirstOrDefault(c => c.Posicion == pos.Posicion);
                var n = cuenta?.N ?? 0;
                var terminadas = Math.Min(n, pos.PiezasEsperadas);
                var estado = n == 0 ? "SinPiezas" : terminadas >= pos.PiezasEsperadas && pos.PiezasEsperadas > 0 ? "Completo" : "Parcial";
                // TODO(PT5.4-PT5.9): cargadas y entregadas salen de la carga y la entrega; hoy no hay fuente (EstadoPieza solo tiene EnPT).
                const int cargadas = 0, entregadas = 0;
                return new AvancePosicionDto(pos.Posicion, pos.Descripcion, pos.PiezasEsperadas, terminadas, Math.Min(cuenta?.EnPt ?? 0, terminadas) - cargadas - entregadas,
                    cargadas, entregadas, Math.Max(0, pos.PiezasEsperadas - terminadas), estado);
            }).ToList();
        }
        return new PiezasPagina(items, total, avance, estadoPedido);
    }
}

public sealed class ListarSalidasPendientesHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IPedidosPtReadPort pedidos, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<ListarSalidasPendientesQuery, SalidasPendientesPagina>
{
    public async Task<SalidasPendientesPagina> Handle(ListarSalidasPendientesQuery q, CancellationToken cancellationToken)
    {
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoPiezasVer, cancellationToken);
        const string todas = PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales;
        var visibles = await acceso.SucursalesVisiblesAsync(todas, cancellationToken);
        if (q.SucursalId is { } s) await acceso.VerificarSucursalAsync(s, todas, cancellationToken);

        // Igual que Piezas: la sucursal es la física de la pieza. Solo pedidos en 70 (Liberado); los retenidos (69) no salen.
        var piezas = db.Piezas.AsNoTracking().Where(p => p.Estado == EstadoPieza.EnPT);
        if (q.SucursalId is { } sid) piezas = piezas.Where(p => p.SucursalId == sid);
        else if (visibles is not null) piezas = piezas.Where(p => visibles.Contains(p.SucursalId));

        var filas = await (from p in piezas
                           join o in db.PedidosPt.AsNoTracking() on p.PedidoPtId equals o.Id
                           where o.Liberacion == EstadoLiberacion.Liberado && o.LiberacionAt != null
                           group p by new { o.Id, o.PedidoFacturableId, o.NumeroPedido, p.SucursalId, o.LiberacionAt } into g
                           select new { g.Key.Id, g.Key.PedidoFacturableId, g.Key.NumeroPedido, g.Key.SucursalId, At = g.Key.LiberacionAt!.Value, N = g.Count() })
            .ToListAsync(cancellationToken);

        // ponytail: filtro "ya salió" en memoria (trae todos los 70 con piezas en PT); mover a SQL cuando PT5.5 guarde la salida en PT.
        var empresaId = empresa.Current ?? throw new UnauthorizedAccessException();
        var origen = await pedidos.ObtenerParaMonitorAsync(empresaId, filas.Select(f => f.PedidoFacturableId).Distinct().ToArray(), cancellationToken);
        var ahora = clock.UtcNow;
        var buscar = q.Buscar?.Trim();
        var pendientes = filas
            .Where(f => origen.TryGetValue(f.PedidoFacturableId, out var o) && !o.TieneSalida)
            .Select(f =>
            {
                var o = origen[f.PedidoFacturableId];
                var horas = (int)Math.Max(0, Math.Floor((ahora - f.At).TotalHours));
                var alerta = horas >= 48 ? "48h" : horas >= 24 ? "24h" : "ninguna";
                return new SalidaPendienteDto(f.Id, f.NumeroPedido, o.ClienteNombre, o.Destino, f.SucursalId, f.N, f.At, horas, alerta);
            })
            .Where(d => string.IsNullOrEmpty(buscar)
                || d.NumeroPedido.Contains(buscar, StringComparison.OrdinalIgnoreCase)
                || d.Cliente.Contains(buscar, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var lista = q.Alerta is null ? pendientes : pendientes.Where(d => d.Alerta == q.Alerta).ToList();
        var items = lista.OrderBy(d => d.LiberadoAt).ThenBy(d => d.NumeroPedido).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToList();
        return new SalidasPendientesPagina(items, lista.Count, pendientes.Count(d => d.Alerta == "24h"), pendientes.Count(d => d.Alerta == "48h"));
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
