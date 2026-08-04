using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Ports.TarjetaCredito;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;

namespace Millet.CuentasPorPagar.Infrastructure.TarjetaCredito;

/// <summary>
/// Implementación del algoritmo de match automático §7 del anexo TC
/// (F7-PR5). El score se compone de 3 componentes (monto, fecha,
/// merchant) y suma 0-100:
///
/// <list type="bullet">
///   <item><b>Monto</b> (50/30/0): diff &lt;$0.50 → 50; diff/total &lt; 2% → 30; lo demás → 0.</item>
///   <item><b>Fecha</b> (25/15/5): mismo día → 25; ±1 día → 15; ±2-3 días → 5.</item>
///   <item><b>Merchant</b> (25/10/0): trigram similarity &gt; 0.80 → 25; &gt; 0.50 → 10; lo demás → 0.</item>
/// </list>
///
/// <para>
/// <b>Decisión</b>: ≥90 → Matched auto; 60-89 → Pendiente (sugerencia);
/// &lt;60 → NoConciliado. Restricción 1:1 (un movimiento solo matchea a
/// una línea: se queda con la de mayor score).
/// </para>
///
/// <para>
/// <b>Líneas omitidas</b>: tipos `Interes`, `Anualidad`, `Comision`
/// según el banco no buscan match — se proponen como movimientos
/// nuevos (F7-PR6 los inserta).
/// </para>
/// </summary>
public sealed class ConciliacionAutomaticaService : IConciliacionAutomaticaService
{
    private const decimal ScoreUmbralAuto = 90m;
    private const decimal ScoreUmbralSugerencia = 60m;
    private const decimal ToleranciaMontoExacto = 0.50m;
    private const decimal ToleranciaMontoPorcentaje = 0.02m;
    private const int VentanaCompraDias = 3;
    private const int VentanaRefundDias = 90;

    private static readonly HashSet<string> TiposBancoQueNoBuscanMatch = new(StringComparer.OrdinalIgnoreCase)
    {
        "Interes", "Anualidad", "Comision",
    };

    private readonly ILogger<ConciliacionAutomaticaService> _logger;

    public ConciliacionAutomaticaService(ILogger<ConciliacionAutomaticaService> logger)
    {
        _logger = logger;
    }

    public Task<ConciliacionResultado> ConciliarAsync(
        EstadoCuentaTc estadoCuenta,
        IReadOnlyList<MovimientoTarjetaCredito> candidatos,
        CancellationToken cancellationToken)
    {
        // Para garantizar 1:1, recolectamos primero todos los pares
        // (línea, movimiento, score), luego asignamos por score descendente
        // sin reusar movimientos ni líneas.
        var pares = new List<(LineaBancoTc Linea, MovimientoTarjetaCredito Mov, decimal Score)>();
        var lineasOmitidas = 0;

        foreach (var linea in estadoCuenta.Lineas)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (linea.TipoSegunBanco is { Length: > 0 } tipo
                && TiposBancoQueNoBuscanMatch.Contains(tipo))
            {
                lineasOmitidas++;
                continue;
            }

            var esRefund = string.Equals(linea.TipoSegunBanco, "Refund", StringComparison.OrdinalIgnoreCase);
            var ventanaDias = esRefund ? VentanaRefundDias : VentanaCompraDias;

            foreach (var mov in candidatos)
            {
                if (mov.TarjetaId != estadoCuenta.TarjetaId) continue;

                // Refunds vs compras positivas; compras normales vs movs no-refund.
                if (esRefund && mov.Tipo == TipoMovimientoTc.Refund) continue;
                if (!esRefund && mov.Tipo == TipoMovimientoTc.Refund) continue;

                var diasDiff = Math.Abs(linea.FechaAplicacion.DayNumber - mov.FechaMovimiento.DayNumber);
                if (diasDiff > ventanaDias) continue;

                var score = CalcularScore(linea, mov);
                if (score < ScoreUmbralSugerencia)
                {
                    // Lo registramos igual para mostrar "mejor score" cuando no hay match.
                    pares.Add((linea, mov, score));
                    continue;
                }

                pares.Add((linea, mov, score));
            }
        }

        // Asignación 1:1 greedy: ordenar por score desc y asignar primero
        // los pares con mayor score.
        var ordenados = pares.OrderByDescending(p => p.Score).ToList();
        var lineasAsignadas = new HashSet<Guid>();
        var movsAsignados = new HashSet<Guid>();

        int matchedAuto = 0, sugerencias = 0;
        decimal totalConciliado = 0m;

        foreach (var (linea, mov, score) in ordenados)
        {
            if (lineasAsignadas.Contains(linea.Id)) continue;
            if (movsAsignados.Contains(mov.Id)) continue;

            if (score >= ScoreUmbralAuto)
            {
                linea.MarcarMatched(mov.Id, score);
                lineasAsignadas.Add(linea.Id);
                movsAsignados.Add(mov.Id);
                matchedAuto++;
                totalConciliado += linea.MontoMxn;
            }
            else if (score >= ScoreUmbralSugerencia)
            {
                linea.MarcarSugerencia(mov.Id, score);
                lineasAsignadas.Add(linea.Id);
                movsAsignados.Add(mov.Id);
                sugerencias++;
            }
            // else: < umbral, queda como NoConciliado pero registramos el mejor score abajo.
        }

        // Líneas que quedaron sin asignar: marcar como NoConciliado con el mejor
        // score visto (si lo hubo) para diagnóstico.
        int sinMatch = 0;
        foreach (var linea in estadoCuenta.Lineas)
        {
            if (lineasAsignadas.Contains(linea.Id)) continue;
            if (linea.TipoSegunBanco is { Length: > 0 } tipo
                && TiposBancoQueNoBuscanMatch.Contains(tipo))
            {
                continue;
            }

            var mejorScore = pares
                .Where(p => p.Linea.Id == linea.Id)
                .Select(p => (decimal?)p.Score)
                .DefaultIfEmpty(null)
                .Max();
            linea.MarcarSinSugerencia(mejorScore);
            sinMatch++;
        }

        estadoCuenta.ActualizarTotalConciliado(totalConciliado);

        var resultado = new ConciliacionResultado(
            LineasTotal: estadoCuenta.Lineas.Count,
            LineasMatchedAuto: matchedAuto,
            LineasSugerencia: sugerencias,
            LineasSinMatch: sinMatch,
            LineasOmitidasNoBuscanMatch: lineasOmitidas,
            TotalConciliadoMxn: totalConciliado);

        _logger.LogInformation(
            "[ConciliacionAutomatica] EC={EcId}: total={Total} matched={Matched} sugerencias={Sug} sin-match={Sin} omitidas={Om}",
            estadoCuenta.Id, resultado.LineasTotal, resultado.LineasMatchedAuto,
            resultado.LineasSugerencia, resultado.LineasSinMatch, resultado.LineasOmitidasNoBuscanMatch);

        return Task.FromResult(resultado);
    }

    internal static decimal CalcularScore(LineaBancoTc linea, MovimientoTarjetaCredito mov)
    {
        decimal score = 0m;

        // Componente monto.
        var lineaMonto = Math.Abs(linea.MontoMxn);
        var movMonto = Math.Abs(mov.MontoMxn);
        var diff = Math.Abs(movMonto - lineaMonto);
        if (diff < ToleranciaMontoExacto)
            score += 50m;
        else if (lineaMonto > 0 && diff / lineaMonto < ToleranciaMontoPorcentaje)
            score += 30m;

        // Componente fecha.
        var diasDiff = Math.Abs(linea.FechaAplicacion.DayNumber - mov.FechaMovimiento.DayNumber);
        if (diasDiff == 0) score += 25m;
        else if (diasDiff == 1) score += 15m;
        else if (diasDiff <= 3) score += 5m;

        // Componente merchant (trigram similarity).
        var similarity = TrigramSimilarity(mov.MerchantNormalizado, linea.MerchantNormalizado);
        if (similarity > 0.80) score += 25m;
        else if (similarity > 0.50) score += 10m;

        return Math.Min(score, 100m);
    }

    /// <summary>
    /// Jaccard sobre trigramas (3-letras n-gram) — proxy del
    /// pg_trgm.similarity de Postgres. Para volumen MVP (~100 movs/mes
    /// por TC) la computación en C# es trivial; si crece se migra a
    /// query con gin_trgm_ops sobre <c>merchant_normalizado</c>.
    /// </summary>
    internal static double TrigramSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1.0;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;

        var trigA = Trigramas(a);
        var trigB = Trigramas(b);
        if (trigA.Count == 0 && trigB.Count == 0) return 1.0;
        if (trigA.Count == 0 || trigB.Count == 0) return 0.0;

        var interseccion = trigA.Intersect(trigB).Count();
        var union = trigA.Union(trigB).Count();
        return union == 0 ? 0.0 : (double)interseccion / union;
    }

    private static HashSet<string> Trigramas(string s)
    {
        var prepared = "  " + s.Trim() + " ";
        var result = new HashSet<string>();
        for (var i = 0; i < prepared.Length - 2; i++)
        {
            result.Add(prepared.Substring(i, 3));
        }
        return result;
    }
}
