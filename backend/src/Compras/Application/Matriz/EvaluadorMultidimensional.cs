using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Infrastructure;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Application.Matriz;

/// <summary>
/// Implementación productiva de <see cref="IRequiereNivelEvaluator"/>
/// (F9-PR2). Reemplaza al <c>EvaluadorMontoVsUmbral</c> v0 con las
/// 3 capas del §3.bis.2 del diseño:
///
/// <list type="number">
///   <item>
///     <b>Capa 3 — Naturaleza fuerza N2:</b>
///     <c>Critico</c> o <c>Riesgo</c> en cualquier línea → N1+N2 directo.
///   </item>
///   <item>
///     <b>Capa 2 — Monto vs umbral del departamento:</b>
///     <c>monto_total &gt; umbral</c> vigente del depto → N1+N2.
///     Sin fila vigente en <c>compras.umbrales_aprobacion_departamento</c>
///     → fail-open (capa no aplica, las otras siguen funcionando).
///   </item>
///   <item>
///     <b>Capa 1 — Solo N1:</b> default si las anteriores no disparan.
///   </item>
///
/// </list>
///
/// <para>
/// Evalúa las 3 capas en orden y short-circuit en la primera que dispara
/// N2: una sola query a Postgres por capa (naturaleza + umbral).
/// </para>
/// </summary>
public sealed class EvaluadorMultidimensional : IRequiereNivelEvaluator
{
    private readonly ComprasDbContext _db;
    private readonly NaturalezaResolverService _naturaleza;

    public EvaluadorMultidimensional(
        ComprasDbContext db,
        NaturalezaResolverService naturaleza)
    {
        _db = db;
        _naturaleza = naturaleza;
    }

    public async Task<RequiereNivel> EvaluarAsync(
        Requisicion requisicion,
        CancellationToken cancellationToken = default)
    {
        // Capa 3: naturaleza más restrictiva.
        var naturaleza = await _naturaleza.ResolverMasRestrictivaAsync(requisicion, cancellationToken);
        if (naturaleza == Naturaleza.Critico || naturaleza == Naturaleza.Riesgo)
        {
            return RequiereNivel.N1YN2;
        }

        // Capa 2: monto vs umbral del depto. Mismo lookup que v0.
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var umbral = await _db.UmbralesAprobacionDepartamento
            .AsNoTracking()
            .Where(u => u.EmpresaId == requisicion.EmpresaId
                        && u.DepartamentoId == requisicion.DepartamentoId
                        && u.VigenteDesde <= hoy
                        && (u.VigenteHasta == null || u.VigenteHasta >= hoy))
            .OrderByDescending(u => u.VigenteDesde)
            .Select(u => (decimal?)u.UmbralMonto)
            .FirstOrDefaultAsync(cancellationToken);

        if (umbral is decimal u)
        {
            // Asume todas las líneas en la misma moneda (validación
            // cross-moneda fuera de scope; la incompatibilidad la
            // detecta el aggregate al sumar Money).
            var montoTotal = requisicion.Lineas.Sum(l => l.Cantidad * l.PrecioEstimado.Amount);
            if (montoTotal > u)
            {
                return RequiereNivel.N1YN2;
            }
        }

        // Capa 1: solo N1.
        return RequiereNivel.SoloN1;
    }
}
