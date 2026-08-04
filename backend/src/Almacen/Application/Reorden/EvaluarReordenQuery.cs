using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Ports.Externos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;

namespace Millet.Almacen.Application.Reorden;

// ============================================================================
// Cálculo del faltante del motor de reorden (ADR-0047 PR5.B). Read query:
// faltante = max(0, objetivo − existencia física − vivo de origen sistema), por
// cada configuración activa. El cálculo vive en Almacén (dueño de config + saldos)
// y cruza a Compras SOLO por IComprasPedidoVivoReadPort para "lo vivo de sistema".
//
// Es una consulta pura (sin efectos). El worker (PR5.D) la consumirá y filtrará
// AutoRequisicion && Faltante > 0 para generar borradores.
// ============================================================================

public sealed record EvaluarReordenQuery(
    Guid? ArticuloId = null,
    Guid? EntidadId = null) : IRequest<IReadOnlyList<FaltanteReorden>>;

public sealed record FaltanteReorden(
    Guid ConfiguracionId,
    Guid ArticuloId,
    NivelReorden Nivel,
    Guid EntidadId,
    ObjetivoReposicion Objetivo,
    decimal ObjetivoCantidad,
    decimal ExistenciaFisica,
    decimal Vivo,
    decimal Faltante,
    bool AutoRequisicion);

public sealed class EvaluarReordenHandler
    : IRequestHandler<EvaluarReordenQuery, IReadOnlyList<FaltanteReorden>>
{
    private readonly AlmacenDbContext _db;
    private readonly IAlmacenSaldoQueryPort _saldos;
    private readonly IComprasPedidoVivoReadPort _pedidoVivo;

    public EvaluarReordenHandler(
        AlmacenDbContext db,
        IAlmacenSaldoQueryPort saldos,
        IComprasPedidoVivoReadPort pedidoVivo)
    {
        _db = db;
        _saldos = saldos;
        _pedidoVivo = pedidoVivo;
    }

    public async Task<IReadOnlyList<FaltanteReorden>> Handle(
        EvaluarReordenQuery request, CancellationToken cancellationToken)
    {
        var query = _db.ConfiguracionesReorden.AsNoTracking()
            .Where(c => c.Estatus == EstatusCatalogo.Activo);
        if (request.ArticuloId is Guid aid) query = query.Where(c => c.ArticuloId == aid);
        if (request.EntidadId is Guid eid) query = query.Where(c => c.EntidadId == eid);

        var configs = await query.ToListAsync(cancellationToken);
        if (configs.Count == 0) return [];

        // Almacenes de cada sucursal N1 (dato local de Almacén) — para expandir el
        // rollup y el "vivo" a nivel sucursal.
        var sucursalesN1 = configs
            .Where(c => c.Nivel == NivelReorden.Sucursal)
            .Select(c => c.EntidadId).Distinct().ToList();
        var almacenesPorSucursal = sucursalesN1.Count == 0
            ? new Dictionary<Guid, List<Guid>>()
            : (await _db.Almacenes.AsNoTracking()
                .Where(a => sucursalesN1.Contains(a.SucursalId))
                .Select(a => new { a.SucursalId, a.Id })
                .ToListAsync(cancellationToken))
                .GroupBy(a => a.SucursalId)
                .ToDictionary(g => g.Key, g => g.Select(a => a.Id).ToList());

        // Batch de pares (articulo, almacén) para el puerto de "vivo de sistema".
        var pares = new HashSet<PedidoVivoClave>();
        foreach (var c in configs)
            foreach (var alm in AlmacenesEnScope(c, almacenesPorSucursal))
                pares.Add(new PedidoVivoClave(c.ArticuloId, alm));

        var vivo = pares.Count > 0
            ? await _pedidoVivo.ObtenerVivoDeSistemaAsync(pares.ToList(), cancellationToken)
            : new Dictionary<PedidoVivoClave, decimal>();

        // NOTA: la existencia se resuelve por config (N+1 sobre el puerto de saldos).
        // El motor (5.D) corre por ciclo con un universo acotado de configs; si crece,
        // batchear ConsultarDisponibilidad* es la optimización natural.
        var resultado = new List<FaltanteReorden>(configs.Count);
        foreach (var c in configs)
        {
            var objetivo = c.ResolverObjetivo();

            decimal existencia;
            decimal vivoTotal;
            if (c.Nivel == NivelReorden.Almacen)
            {
                var disp = await _saldos.ConsultarDisponibilidadPorAlmacenAsync(
                    c.EntidadId, c.ArticuloId, cancellationToken);
                existencia = disp.Cantidad;
                vivoTotal = vivo.GetValueOrDefault(new PedidoVivoClave(c.ArticuloId, c.EntidadId));
            }
            else
            {
                var disp = await _saldos.ConsultarDisponibilidadPorSucursalAsync(
                    c.EntidadId, c.ArticuloId, cancellationToken);
                existencia = disp.Cantidad;
                vivoTotal = AlmacenesEnScope(c, almacenesPorSucursal)
                    .Sum(alm => vivo.GetValueOrDefault(new PedidoVivoClave(c.ArticuloId, alm)));
            }

            var faltante = Math.Max(0m, objetivo - existencia - vivoTotal);
            resultado.Add(new FaltanteReorden(
                c.Id, c.ArticuloId, c.Nivel, c.EntidadId, c.Objetivo,
                objetivo, existencia, vivoTotal, faltante, c.AutoRequisicion));
        }

        return resultado;
    }

    private static List<Guid> AlmacenesEnScope(
        ConfiguracionReorden config, IReadOnlyDictionary<Guid, List<Guid>> almacenesPorSucursal)
        => config.Nivel == NivelReorden.Almacen
            ? [config.EntidadId]
            : almacenesPorSucursal.GetValueOrDefault(config.EntidadId) ?? [];
}
