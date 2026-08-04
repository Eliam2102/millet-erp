using Millet.CuentasPorPagar.Domain.TarjetaCredito;

namespace Millet.CuentasPorPagar.Domain.Ports.TarjetaCredito;

/// <summary>
/// Puerto que abstrae el parser del archivo del banco (§6 anexo TC,
/// F7-PR5). Strategy por perfil — el parser concreto lee el
/// <see cref="PerfilParserBanco"/> y aplica las reglas (columnas,
/// formato fecha, signo de refund, locale, encoding). Agregar banco =
/// agregar seed; agregar parser solo cuando el formato no es tabular
/// (PDF, JSON exótico).
/// </summary>
public interface IEstadoCuentaTcParserPort
{
    Task<ParseResult> ParseAsync(
        Stream archivoStream,
        PerfilParserBanco perfil,
        CancellationToken cancellationToken);
}

/// <summary>Resultado del parser — líneas + errores + total declarado opcional.</summary>
public sealed record ParseResult(
    IReadOnlyList<LineaBancoParseada> Lineas,
    IReadOnlyList<string> Errores,
    decimal? TotalDeclaradoMxn);

/// <summary>DTO de dominio que el parser devuelve por cada fila de datos válida.</summary>
public sealed record LineaBancoParseada(
    int PosicionArchivo,
    DateOnly FechaAplicacion,
    decimal Monto,
    string Moneda,
    decimal MontoMxn,
    string MerchantRaw,
    string? ReferenciaBanco,
    string? TipoSegunBanco);
