using System.Globalization;
using System.Text.Json;
using Millet.Integraciones.Aw.Application.Clientes;

namespace Millet.Integraciones.Aw.Infrastructure.Clientes;

/// <summary>
/// Fuente en memoria para operar/probar sin A+W. Carga el formato de los fixtures
/// <c>{origen:{KU_KUNDEN:[...],KA_ZAHLBED:[...],KA_BONITAET:[...]}}</c> (KA_BONITAET opcional: régimen fiscal en
/// <c>FREMD_KEY</c>, unido por <c>BONITAET = BEZ</c>). Cursor = último ID, orden numérico.
/// </summary>
public sealed class AwClientesOrigenSimulado : IAwClientesOrigen
{
    private readonly IReadOnlyList<AwClienteOrigenFila> _filas;

    public AwClientesOrigenSimulado(IEnumerable<AwClienteOrigenFila>? filas = null) =>
        _filas = (filas ?? []).OrderBy(f => f.Id).ToList();

    public static AwClientesOrigenSimulado DesdeArchivo(string ruta) => DesdeJson(File.ReadAllText(ruta));

    public static AwClientesOrigenSimulado DesdeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var origen = doc.RootElement.GetProperty("origen");
        var catalogo = origen.TryGetProperty("KA_ZAHLBED", out var cat)
            ? cat.EnumerateArray().Select(e => (Bez: Str(e, "BEZ"), Cond: new AwCondicionCoincidencia(Int(e, "NUMMER"), Int(e, "BRUTTOTAGE")))).ToList()
            : [];
        // BEZ es PK en A+W: la última aparición gana si el fixture la repite.
        var regimenes = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (origen.TryGetProperty("KA_BONITAET", out var bon))
            foreach (var e in bon.EnumerateArray())
                if (Str(e, "BEZ") is { } bez) regimenes[bez] = Str(e, "FREMD_KEY");

        var filas = origen.GetProperty("KU_KUNDEN").EnumerateArray().Select(e =>
        {
            var zahlbed = Str(e, "ZAHLBED");
            var coincidencias = zahlbed is null ? [] : catalogo
                .Where(c => string.Equals(c.Bez?.Trim(), zahlbed.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Cond).ToList();
            return new AwClienteOrigenFila(
                Int(e, "ID") ?? throw new InvalidDataException("KU_KUNDEN.ID nulo."),
                Int(e, "MANDANT") ?? 0,
                Str(e, "NAME1"), Str(e, "NAME2"), Str(e, "NAME3"),
                Str(e, "STRASSE"), Str(e, "ORT"), Str(e, "PLZ"), Str(e, "PROVINZ"), Str(e, "LAND"),
                Str(e, "UST_ID"), Str(e, "STEUERNUMMER"), Str(e, "TLF1"), Str(e, "TLF2"), Str(e, "MAIL"),
                zahlbed, Str(e, "WAEHRUNG"),
                Dec(e, "KREDIT_LIMIT"), Dec(e, "KREDIT_LIMIT1"), (double?)Dec(e, "KREDIT_LIMIT_NET"),
                Int(e, "KZ_STATUS"), Int(e, "KZ_GESPERRT"),
                Str(e, "DATUM") is { } d ? DateOnly.Parse(d, CultureInfo.InvariantCulture) : null,
                Str(e, "TRANSACTION_TIME") is { } t ? DateTime.Parse(t, CultureInfo.InvariantCulture) : null,
                coincidencias,
                Bonitaet: Str(e, "BONITAET"),
                RegimenFiscalOrigen: Str(e, "BONITAET") is { } b && regimenes.TryGetValue(b, out var r) ? r : null);
        });
        return new(filas);
    }

    public Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct) =>
        Task.FromResult(int.TryParse(referencia, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? _filas.FirstOrDefault(f => f.Id == id)
            : null);

    public Task<AwClientesPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
    {
        tamano = Math.Clamp(tamano, 1, AwClientesOptions.TamanoLoteMaximo);
        var desde = 0;
        if (cursor is not null && !int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out desde))
            throw new ArgumentException("Cursor inválido.", nameof(cursor));

        var filas = _filas.Where(f => f.Id > desde).Take(tamano).ToList();
        var siguiente = filas.Count == tamano ? filas[^1].Id.ToString(CultureInfo.InvariantCulture) : null;
        return Task.FromResult(new AwClientesPagina(filas, siguiente));
    }

    private static JsonElement? Get(JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    private static string? Str(JsonElement e, string n) => Get(e, n) is { } v
        ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;

    private static int? Int(JsonElement e, string n) => Str(e, n) is { } s && !string.IsNullOrWhiteSpace(s)
        ? int.Parse(s, CultureInfo.InvariantCulture) : null;

    private static decimal? Dec(JsonElement e, string n) => Str(e, n) is { } s ? decimal.Parse(s, CultureInfo.InvariantCulture) : null;
}
