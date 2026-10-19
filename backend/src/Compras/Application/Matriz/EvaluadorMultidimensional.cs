using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Application.Disponibilidad;
using Millet.Compras.Application.Oc;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Infrastructure;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Application.Matriz;

/// <summary>
/// Implementación productiva de <see cref="IRequiereNivelEvaluator"/>.
/// Decide si la requisición necesita nivel 2 de autorización con el modo y el
/// mínimo del umbral vigente del departamento de la RQ (respaldo: fila general
/// <see cref="Oc.EvaluadorNivelOc.UmbralGeneralId"/>).
///
/// <list type="number">
///   <item><b>Naturaleza:</b> <c>Critico</c> o <c>Riesgo</c> → N1+N2 directo.</item>
///   <item><b>Sin umbral vigente:</b> no se puede decidir, se exige N2 (fail-safe).</item>
///   <item>
///     <b>Monto</b> (modo <c>SoloMonto</c> o <c>Ambos</c>): Σ cantidad × precio estimado
///     &gt; umbral de monto → N1+N2, sin consultar stock.
///   </item>
///   <item>
///     <b>Disponibilidad</b> (modo <c>SoloDisponibilidad</c> o <c>Ambos</c>): si hay faltante
///     de artículos de inventario (servicios excluidos), el importe a comprar
///     (Σ DeCompra × precio estimado de los faltantes) se compara contra
///     <c>MontoMinimoDisponibilidad</c>; sin mínimo o superado → N1+N2.
///   </item>
///   <item><b>Si nada dispara:</b> solo N1.</item>
/// </list>
///
/// <para>Todas las comparaciones son estrictas (<c>&gt;</c>): la igualdad se queda en N1.</para>
/// </summary>
public sealed class EvaluadorMultidimensional : IRequiereNivelEvaluator
{
    private readonly ComprasDbContext _db;
    private readonly NaturalezaResolverService _naturaleza;
    private readonly IConsultarStockPort _stock;
    private readonly IConversionUnidadPort _conversion;
    private readonly IArticuloReadPort _articulos;
    private readonly IClock _clock;

    public EvaluadorMultidimensional(
        ComprasDbContext db,
        NaturalezaResolverService naturaleza,
        IConsultarStockPort stock,
        IConversionUnidadPort conversion,
        IArticuloReadPort articulos,
        IClock clock)
    {
        _db = db;
        _naturaleza = naturaleza;
        _stock = stock;
        _conversion = conversion;
        _articulos = articulos;
        _clock = clock;
    }

    public async Task<RequiereNivel> EvaluarAsync(
        Requisicion requisicion,
        CancellationToken cancellationToken = default)
    {
        // Capa 1: naturaleza más restrictiva.
        var naturaleza = await _naturaleza.ResolverMasRestrictivaAsync(requisicion, cancellationToken);
        if (naturaleza == Naturaleza.Critico || naturaleza == Naturaleza.Riesgo)
        {
            return RequiereNivel.N1YN2;
        }

        // Umbral vigente del departamento; si no hay, el de la fila general.
        var hoy = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var filas = await _db.UmbralesAprobacionDepartamento.AsNoTracking()
            .Where(u => u.EmpresaId == requisicion.EmpresaId
                        && (u.DepartamentoId == requisicion.DepartamentoId
                            || u.DepartamentoId == EvaluadorNivelOc.UmbralGeneralId)
                        && u.VigenteDesde <= hoy
                        && (u.VigenteHasta == null || u.VigenteHasta >= hoy))
            .OrderByDescending(u => u.VigenteDesde)
            .ToListAsync(cancellationToken);
        var umbral = filas.FirstOrDefault(u => u.DepartamentoId == requisicion.DepartamentoId)
                     ?? filas.FirstOrDefault(u => u.DepartamentoId == EvaluadorNivelOc.UmbralGeneralId);

        // Sin umbral vigente no se puede decidir: se exige N2. Ficha C3.1 §7 y plano Compras paso 10.
        if (umbral is null)
        {
            return RequiereNivel.N1YN2;
        }

        var incluyeMonto = umbral.ModoNivel2 is ModoNivel2.SoloMonto or ModoNivel2.Ambos;
        var incluyeDisponibilidad = umbral.ModoNivel2 is ModoNivel2.SoloDisponibilidad or ModoNivel2.Ambos;

        if (incluyeMonto
            && requisicion.Lineas.Sum(l => l.Cantidad * l.PrecioEstimado.Amount) > umbral.UmbralMonto)
        {
            return RequiereNivel.N1YN2;
        }

        if (incluyeDisponibilidad
            && await ImporteFaltanteAsync(requisicion, cancellationToken) is decimal importe
            && (umbral.MontoMinimoDisponibilidad is null || importe > umbral.MontoMinimoDisponibilidad))
        {
            return RequiereNivel.N1YN2;
        }

        return RequiereNivel.SoloN1;
    }

    /// <summary>
    /// Reparto por línea (mismo cálculo que el preview y la bandeja, vía
    /// <see cref="RepartoDisponibilidadService"/>). Devuelve el importe a comprar
    /// (Σ DeCompra × precio estimado) de las líneas de inventario con faltante,
    /// o <c>null</c> si no hay faltante. Los servicios
    /// no consultan stock ni cuentan como faltante. Solo lectura: no reserva stock.
    /// </summary>
    private async Task<decimal?> ImporteFaltanteAsync(
        Requisicion requisicion,
        CancellationToken cancellationToken)
    {
        if (requisicion.Lineas.Count == 0)
        {
            return null;
        }

        var articulos = await _articulos.ObtenerPorIdsAsync(
            requisicion.Lineas.Select(l => l.ArticuloId).Distinct().ToArray(),
            cancellationToken);
        var repartos = await RepartoDisponibilidadService.RepartirAsync(
            _stock, _conversion, articulos, requisicion.Clasificacion, requisicion.SucursalId,
            requisicion.Lineas, cancellationToken);

        var faltantes = repartos.Where(r => !r.EsServicio && r.DeCompra > 0m).ToList();
        return faltantes.Count == 0
            ? null
            : faltantes.Sum(r => r.DeCompra * r.Linea.PrecioEstimado.Amount);
    }
}
