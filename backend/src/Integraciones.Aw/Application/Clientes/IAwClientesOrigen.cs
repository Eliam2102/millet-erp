namespace Millet.Integraciones.Aw.Application.Clientes;

/// <summary>Coincidencia del catálogo KA_ZAHLBED para un ZAHLBED (BEZ). Nulos != 0.</summary>
public sealed record AwCondicionCoincidencia(int? Numero, int? DiasNominales);

/// <summary>Fila cruda de KU_KUNDEN (columnas de doc 05 §4) + coincidencias de KA_ZAHLBED.</summary>
public sealed record AwClienteOrigenFila(
    int Id,
    int Mandant,
    string? Name1,
    string? Name2,
    string? Name3,
    string? Strasse,
    string? Ort,
    string? Plz,
    string? Provinz,
    string? Land,
    string? UstId,
    string? Steuernummer,
    string? Tlf1,
    string? Tlf2,
    string? Mail,
    string? Zahlbed,
    string? Waehrung,
    decimal? KreditLimit,
    decimal? KreditLimit1,
    double? KreditLimitNet,
    int? KzStatus,
    int KzGesperrt,
    DateOnly? Datum,
    DateTime? TransactionTime,
    IReadOnlyList<AwCondicionCoincidencia> CondicionCoincidencias);

/// <summary><c>SiguienteCursor</c> = último ID leído, o null si ya no hay más páginas.</summary>
public sealed record AwClientesPagina(IReadOnlyList<AwClienteOrigenFila> Filas, string? SiguienteCursor);

/// <summary>
/// Puerto de lectura de clientes A+W (solo lectura). Orden estable por ID numérico;
/// errores de conexión/timeout → <c>AwReaderException</c>.
/// </summary>
public interface IAwClientesOrigen
{
    Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct);

    /// <summary><paramref name="tamano"/> se acota a 1..<see cref="AwClientesOptions.TamanoLoteMaximo"/>.</summary>
    Task<AwClientesPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct);
}
