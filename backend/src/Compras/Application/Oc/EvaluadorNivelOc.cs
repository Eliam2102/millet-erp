using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Oc;

/// <summary>
/// Matriz de nivel de una OC (minuta Millet 12-oct): decide si la firma N2
/// (Dirección) es necesaria. El monto vive en la OC (<c>TotalAPagar</c> en
/// MXN), no en la requisición.
///
/// <list type="bullet">
///   <item>Monto MXN = <c>TotalAPagar</c>; si la moneda no es MXN se multiplica por <c>TipoCambio</c>.</item>
///   <item>Umbral = fila vigente de <c>compras.umbrales_aprobacion_departamento</c> del departamento de la RQ de origen
///         (primera línea con <c>RequisicionId</c>).</item>
///   <item>Si no hay departamento de origen o no hay fila para él, se usa la fila general de la empresa
///         con <c>DepartamentoId = Guid.Empty</c> (convención: "umbral general de la empresa").</item>
///   <item>Sin monto convertible, sin umbral o <c>monto &gt; UmbralMonto</c> → <c>RequiereNivel2 = true</c> (fail-safe).</item>
///   <item><c>EsMenorCuantia</c> = monto MXN &lt;= <c>UmbralMenorCuantia</c> (solo informativo para la bandeja).</item>
/// </list>
///
/// Requiere que las <see cref="OrdenCompra"/> lleguen con <c>Lineas</c> cargadas
/// (<c>Include</c>), porque <c>TotalAPagar</c> se calcula en memoria.
/// </summary>
public sealed class EvaluadorNivelOc
{
    /// <summary>Clave de la fila general de la empresa (sin departamento).</summary>
    public static readonly Guid UmbralGeneralId = Guid.Empty;

    private readonly ComprasDbContext _db;
    private readonly IClock _clock;

    public EvaluadorNivelOc(ComprasDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>Resultado por OC. <c>MontoMxn</c> null = no convertible (moneda extranjera sin tipo de cambio).</summary>
    public sealed record Decision(decimal? MontoMxn, bool RequiereNivel2, bool EsMenorCuantia);

    /// <summary>Monto en MXN. Null si la moneda no es MXN y no hay tipo de cambio válido.</summary>
    public static decimal? MontoMxn(decimal totalAPagar, string moneda, decimal? tipoCambio) =>
        moneda == OrdenCompra.MonedaDefault
            ? totalAPagar
            : tipoCambio is > 0 ? totalAPagar * tipoCambio.Value : null;

    public async Task<bool> RequiereNivel2Async(OrdenCompra oc, CancellationToken cancellationToken = default)
    {
        var decisiones = await EvaluarAsync([oc], cancellationToken);
        return decisiones[oc.Id].RequiereNivel2;
    }

    /// <summary>Evalúa un lote (una página de la bandeja o una OC) con una consulta por tabla, sin N+1.</summary>
    public async Task<IReadOnlyDictionary<Guid, Decision>> EvaluarAsync(
        IReadOnlyCollection<OrdenCompra> ocs,
        CancellationToken cancellationToken = default)
    {
        var resultado = new Dictionary<Guid, Decision>(ocs.Count);
        if (ocs.Count == 0)
        {
            return resultado;
        }

        var rqPorOc = ocs.ToDictionary(
            o => o.Id,
            o => o.Lineas.FirstOrDefault(l => l.RequisicionId is not null)?.RequisicionId);
        var rqIds = rqPorOc.Values.Where(v => v is not null).Select(v => v!.Value).Distinct().ToArray();
        var deptoPorRq = rqIds.Length == 0
            ? new Dictionary<Guid, Guid>()
            : await _db.Requisiciones.AsNoTracking()
                .Where(r => rqIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.DepartamentoId, cancellationToken);

        // ponytail: carga todas las filas vigentes de las empresas del lote (tabla chica de umbrales);
        // si crece, filtrar por departamento. Gana la VigenteDesde más reciente por (empresa, depto).
        var empresaIds = ocs.Select(o => o.EmpresaId).Distinct().ToArray();
        var hoy = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var filasVigentes = await _db.UmbralesAprobacionDepartamento.AsNoTracking()
            .Where(u => empresaIds.Contains(u.EmpresaId)
                        && u.VigenteDesde <= hoy
                        && (u.VigenteHasta == null || u.VigenteHasta >= hoy))
            .OrderByDescending(u => u.VigenteDesde)
            .ToListAsync(cancellationToken);
        var vigentes = filasVigentes
            .GroupBy(u => (u.EmpresaId, u.DepartamentoId))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var oc in ocs)
        {
            var departamentoId = rqPorOc[oc.Id] is Guid rqId && deptoPorRq.TryGetValue(rqId, out var depto)
                ? (Guid?)depto
                : null;
            var umbral = departamentoId is Guid dep && vigentes.TryGetValue((oc.EmpresaId, dep), out var porDepto)
                ? porDepto
                : vigentes.GetValueOrDefault((oc.EmpresaId, UmbralGeneralId));

            var monto = MontoMxn(oc.CalcularTotales().TotalAPagar, oc.Moneda, oc.TipoCambio);
            var requiereNivel2 = umbral is null || monto is not decimal m || m > umbral.UmbralMonto;
            var esMenorCuantia = monto is decimal mc && umbral?.UmbralMenorCuantia is decimal menor && mc <= menor;

            resultado[oc.Id] = new Decision(monto, requiereNivel2, esMenorCuantia);
        }

        return resultado;
    }
}
