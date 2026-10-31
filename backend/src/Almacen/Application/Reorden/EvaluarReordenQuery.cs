using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Ports.Externos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Application.Reorden;

// ============================================================================
// Cálculo del faltante del motor de reorden (ADR-0047 PR5.B). Read query:
// P7/D6: si existencia física + pedido vivo ≤ punto de reorden, reponer a máximo
// o cantidad fija; en otro caso, cero. Se calcula por
// cada configuración activa. El cálculo vive en Almacén (dueño de config + saldos)
// y cruza a Compras SOLO por IComprasPedidoVivoReadPort para lo pedido vivo de sistema y manual.
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
    bool AutoRequisicion,
    decimal PuntoReorden = 0m,
    decimal Minimo = 0m,
    decimal Maximo = 0m,
    decimal ConsumoDiario = 0m,
    decimal? CoberturaDias = null,
    decimal? MinimoSugerido = null,
    decimal? MaximoSugerido = null);

public sealed class EvaluarReordenHandler
    : IRequestHandler<EvaluarReordenQuery, IReadOnlyList<FaltanteReorden>>
{
    private readonly AlmacenDbContext _db;
    private readonly IAlmacenSaldoQueryPort _saldos;
    private readonly IComprasPedidoVivoReadPort _pedidoVivo;
    private readonly IClock _clock;
    private readonly ReordenConsumoOptions _consumo;

    public EvaluarReordenHandler(
        AlmacenDbContext db,
        IAlmacenSaldoQueryPort saldos,
        IComprasPedidoVivoReadPort pedidoVivo,
        IClock clock,
        IOptions<ReordenConsumoOptions> consumo)
    {
        _db = db;
        _saldos = saldos;
        _pedidoVivo = pedidoVivo;
        _clock = clock;
        _consumo = consumo.Value;
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

        var sucursalPorAlmacen = await _db.Almacenes.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.SucursalId, cancellationToken);
        var clavesManual = configs.Select(c => new PedidoVivoSucursalClave(c.ArticuloId,
            c.Nivel == NivelReorden.Sucursal ? c.EntidadId : sucursalPorAlmacen[c.EntidadId])).Distinct().ToList();
        var manual = await _pedidoVivo.ObtenerVivoManualAsync(clavesManual, cancellationToken);

        // F1-ALM-10: salidas de los últimos DiasHistoria días por (artículo, almacén), agregadas en SQL.
        var articulos = configs.Select(c => c.ArticuloId).Distinct().ToList();
        var desde = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime).AddDays(-_consumo.DiasHistoria);
        var salidas = (await (
            from l in _db.LineasMovimiento.AsNoTracking()
            join m in _db.Movimientos.AsNoTracking() on l.MovimientoId equals m.Id
            join u in _db.Ubicaciones.AsNoTracking() on l.UbicacionId equals (Guid?)u.Id
            join sa in _db.SubAlmacenes.AsNoTracking() on u.SubAlmacenId equals sa.Id
            where m.Estado == EstadoMovimiento.Registrado
               && (m.Tipo == TipoMovimiento.SalidaConsumo || m.Tipo == TipoMovimiento.SalidaPorVale)
               && m.FechaMovimiento >= desde && articulos.Contains(l.ArticuloId)
            group l by new { l.ArticuloId, sa.AlmacenId } into g
            select new { g.Key.ArticuloId, g.Key.AlmacenId, Total = g.Sum(x => x.Cantidad) })
            .ToListAsync(cancellationToken))
            .ToDictionary(x => new PedidoVivoClave(x.ArticuloId, x.AlmacenId), x => x.Total);

        // NOTA: la existencia se resuelve por config (N+1 sobre el puerto de saldos).
        // El motor (5.D) corre por ciclo con un universo acotado de configs; si crece,
        // batchear ConsultarDisponibilidad* es la optimización natural.
        var resultado = new List<FaltanteReorden>(configs.Count);
        foreach (var c in configs)
        {
            var objetivo = c.CantidadFija ?? c.Maximo;

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

            // No asignar una OC manual sin almacén a un destino inventado: para N2
            // se considera la demanda de su sucursal como cobertura conservadora.
            vivoTotal += manual.GetValueOrDefault(new PedidoVivoSucursalClave(c.ArticuloId,
                c.Nivel == NivelReorden.Sucursal ? c.EntidadId : sucursalPorAlmacen[c.EntidadId]));
            var faltante = c.CalcularReposicion(existencia, vivoTotal);
            var consumoDiario = ConsumoReorden.Diario(
                AlmacenesEnScope(c, almacenesPorSucursal).Sum(alm => salidas.GetValueOrDefault(new PedidoVivoClave(c.ArticuloId, alm))),
                _consumo.DiasHistoria);
            var sugerido = ConsumoReorden.Sugerencia(consumoDiario, _consumo.DiasEntrega, _consumo.DiasColchon);
            resultado.Add(new FaltanteReorden(
                c.Id, c.ArticuloId, c.Nivel, c.EntidadId, c.Objetivo,
                objetivo, existencia, vivoTotal, faltante, c.AutoRequisicion,
                c.PuntoReorden, c.Minimo, c.Maximo, consumoDiario,
                ConsumoReorden.Cobertura(existencia, consumoDiario), sugerido?.Minimo, sugerido?.Maximo));
        }

        return resultado;
    }

    private static List<Guid> AlmacenesEnScope(
        ConfiguracionReorden config, IReadOnlyDictionary<Guid, List<Guid>> almacenesPorSucursal)
        => config.Nivel == NivelReorden.Almacen
            ? [config.EntidadId]
            : almacenesPorSucursal.GetValueOrDefault(config.EntidadId) ?? [];
}

// F1-ALM-10: reporte «propuesta de reposición» para la pantalla/exportación. Mismo cálculo del motor,
// recortado a las configuraciones que el usuario puede ver (mismo scope que el listado de reorden).
public sealed record PropuestaReposicionQuery(Guid? ArticuloId = null, Guid? EntidadId = null)
    : IRequest<IReadOnlyList<FaltanteReorden>>, IDocumentoScopedQuery
{
    public string TipoDocumento => "reorden";
    public string PermisoTodasSucursales => "almacen.reorden.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class PropuestaReposicionHandler(IMediator mediator)
    : IRequestHandler<PropuestaReposicionQuery, IReadOnlyList<FaltanteReorden>>
{
    public async Task<IReadOnlyList<FaltanteReorden>> Handle(PropuestaReposicionQuery request, CancellationToken cancellationToken)
    {
        var todos = await mediator.Send(new EvaluarReordenQuery(request.ArticuloId, request.EntidadId), cancellationToken);
        return request.DocumentosPermitidos is { } permitidos ? todos.Where(f => permitidos.Contains(f.ConfiguracionId)).ToList() : todos;
    }
}
