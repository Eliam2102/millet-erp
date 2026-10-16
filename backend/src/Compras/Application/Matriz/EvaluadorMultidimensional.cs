using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Application.Disponibilidad;
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
/// Decide si la requisición necesita nivel 2 de autorización. Desde la
/// minuta Millet del 12-oct la requisición <b>no</b> lleva monto: solo
/// Compras cotiza y el monto contra el umbral N1-N2 se valida en la OC.
///
/// <list type="number">
///   <item>
///     <b>Capa 1 — Naturaleza fuerza N2:</b>
///     <c>Critico</c> o <c>Riesgo</c> en cualquier línea → N1+N2 directo.
///   </item>
///   <item>
///     <b>Capa 2 — Disponibilidad en almacén:</b>
///     alguna línea de artículo de inventario (no servicio) con existencia
///     insuficiente en la sucursal (cubrimiento parcial o nulo) → N1+N2.
///     Solo aplica si <see cref="ComprasSettings.ModoNivel2"/> es
///     <c>SoloDisponibilidad</c> o <c>Ambos</c>.
///     Sin fila de settings para la empresa, el modo es <c>SoloMonto</c>
///     (en la RQ equivale a «sin criterio de disponibilidad»).
///   </item>
///   <item>
///     <b>Capa 3 — Solo N1:</b> default si las anteriores no disparan.
///   </item>
/// </list>
///
/// <para>
/// Evalúa las capas en orden y short-circuit en la primera que dispara N2.
/// </para>
/// </summary>
public sealed class EvaluadorMultidimensional : IRequiereNivelEvaluator
{
    private readonly ComprasDbContext _db;
    private readonly NaturalezaResolverService _naturaleza;
    private readonly IConsultarStockPort _stock;
    private readonly IConversionUnidadPort _conversion;
    private readonly IArticuloReadPort _articulos;

    public EvaluadorMultidimensional(
        ComprasDbContext db,
        NaturalezaResolverService naturaleza,
        IConsultarStockPort stock,
        IConversionUnidadPort conversion,
        IArticuloReadPort articulos)
    {
        _db = db;
        _naturaleza = naturaleza;
        _stock = stock;
        _conversion = conversion;
        _articulos = articulos;
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

        // Capa 2: existencia en almacén por línea de inventario (solo si el modo la incluye).
        var modo = await ObtenerModoNivel2Async(requisicion.EmpresaId, cancellationToken);
        if (modo is ModoNivel2.SoloDisponibilidad or ModoNivel2.Ambos
            && await HayFaltanteDeExistenciaAsync(requisicion, cancellationToken))
        {
            return RequiereNivel.N1YN2;
        }

        // Capa 3: solo N1.
        return RequiereNivel.SoloN1;
    }

    /// <summary>
    /// Modo de nivel 2 de la empresa de la requisición. Sin fila de settings
    /// se asume <see cref="ModoNivel2.SoloMonto"/> (en la RQ: sin criterio de disponibilidad).
    /// </summary>
    private async Task<ModoNivel2> ObtenerModoNivel2Async(Guid empresaId, CancellationToken cancellationToken) =>
        await _db.ComprasSettings
            .AsNoTracking()
            .Where(s => s.EmpresaId == empresaId)
            .Select(s => (ModoNivel2?)s.ModoNivel2)
            .FirstOrDefaultAsync(cancellationToken) ?? ModoNivel2.SoloMonto;

    /// <summary>
    /// Reparto por línea (mismo cálculo que el preview y la bandeja, vía
    /// <see cref="RepartoDisponibilidadService"/>). <c>true</c> si alguna línea
    /// de inventario (no servicio) tiene faltante de existencia. Los servicios
    /// no consultan stock ni cuentan como faltante. Solo lectura: no reserva stock.
    /// </summary>
    private async Task<bool> HayFaltanteDeExistenciaAsync(
        Requisicion requisicion,
        CancellationToken cancellationToken)
    {
        if (requisicion.Lineas.Count == 0)
        {
            return false;
        }

        var articulos = await _articulos.ObtenerPorIdsAsync(
            requisicion.Lineas.Select(l => l.ArticuloId).Distinct().ToArray(),
            cancellationToken);
        var repartos = await RepartoDisponibilidadService.RepartirAsync(
            _stock, _conversion, articulos, requisicion.Clasificacion, requisicion.SucursalId,
            requisicion.Lineas, cancellationToken);

        return repartos.Any(r => !r.EsServicio && r.DeCompra > 0m);
    }
}
