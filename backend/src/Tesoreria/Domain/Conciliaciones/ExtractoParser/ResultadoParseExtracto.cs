using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Tesoreria.Domain.Conciliaciones.ExtractoParser;

public sealed record LineaExtractoParseada(
    int NumeroLinea,
    DateOnly Fecha,
    string Descripcion,
    string? Referencia,
    decimal? Cargo,
    decimal? Abono,
    decimal? Saldo,
    string LineaHash);

public sealed record ResultadoParseExtracto(
    IReadOnlyList<LineaExtractoParseada> Lineas,
    decimal? SaldoInicial,
    decimal? SaldoFinal,
    int TotalLineas);

public static class ExtractoParseo
{
    /// <summary>
    /// Arma el resultado en orden cronológico: el auxiliar BBVA viene del movimiento más reciente al más
    /// antiguo, así que se invierte y se renumera. El saldo final es el del último renglón del periodo.
    /// </summary>
    public static ResultadoParseExtracto Resultado(List<LineaExtractoParseada> lineas)
    {
        if (lineas.Count > 1 && lineas[0].Fecha > lineas[^1].Fecha)
        {
            lineas.Reverse();
            lineas = lineas.Select((l, i) => l with { NumeroLinea = i + 1 }).ToList();
        }

        var conSaldo = lineas.Where(l => l.Saldo.HasValue).ToList();
        return new ResultadoParseExtracto(lineas, conSaldo.FirstOrDefault()?.Saldo, conSaldo.LastOrDefault()?.Saldo, lineas.Count);
    }

    /// <summary>El archivo no corresponde al perfil de la cuenta: se rechaza completo, sin carga parcial.</summary>
    public static BusinessRuleException FormatoNoReconocido(string perfil, string detalle) =>
        new("EXTRACTO_FORMATO_NO_RECONOCIDO", $"Formato no reconocido para el perfil {perfil}: {detalle}");
}
