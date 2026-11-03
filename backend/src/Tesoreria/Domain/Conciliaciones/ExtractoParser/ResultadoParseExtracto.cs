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
