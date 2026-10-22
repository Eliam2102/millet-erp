using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
namespace Millet.Almacen.Application.Recepciones;

public sealed record ImportacionRecepcionInput(string? NumeroPedimento = null, string? NumeroBl = null,
    string? NumeroContenedor = null, string? Aduana = null, string? ClavePedimento = null);

// DEMO: subconjunto local para captura sin PAC. Validar y ampliar contra el catálogo SAT vigente antes de producción.
// shortcut: catálogo acotado de demostración, sustituir por catálogo completo versionado al habilitar operación fiscal.
public static class CatalogosImportacionDemo
{
    public static readonly IReadOnlyDictionary<string, string> Aduanas = new Dictionary<string, string>
    { ["16"] = "Manzanillo", ["43"] = "Veracruz", ["19"] = "Ciudad del Carmen", ["80"] = "Colombia", ["81"] = "Altamira" };
    public static readonly IReadOnlyDictionary<string, string> ClavesPedimento = new Dictionary<string, string>
    { ["A1"] = "Importación definitiva", ["IN"] = "Importación temporal para elaboración, transformación o reparación", ["AF"] = "Importación temporal de activo fijo" };
}

internal static class ImportacionRecepcionGuard
{
    public static RecepcionImportacion? Crear(OcLectura oc, ImportacionRecepcionInput? captura, IReadOnlyList<RegistrarRecepcionLineaInput> lineas)
    {
        if (!oc.EsImportacion) return null;
        var pedimento = Valor(captura?.NumeroPedimento, oc.NumeroPedimento);
        var bl = Valor(captura?.NumeroBl, oc.NumeroBl);
        var contenedor = Valor(captura?.NumeroContenedor, oc.NumeroContenedor);
        var faltantes = new List<string>();
        if (pedimento is null) faltantes.Add("número de pedimento");
        if (bl is null) faltantes.Add("BL o guía aérea");
        if (contenedor is null) faltantes.Add("número de contenedor");
        if (faltantes.Count > 0) throw new BusinessRuleException("RECEPCION_IMPORTACION_INCOMPLETA",
            $"Para confirmar la recepción de importación captura: {string.Join(", ", faltantes)}.");
        pedimento = NumeroPedimento.Normalizar(pedimento)!;
        if (bl!.Length > 50 || contenedor!.Length > 40)
            throw new BusinessRuleException("RECEPCION_IMPORTACION_LONGITUD", "El BL admite hasta 50 caracteres y el contenedor hasta 40.");
        var aduana = captura?.Aduana?.Trim();
        var clave = captura?.ClavePedimento?.Trim().ToUpperInvariant();
        if (aduana is null || !CatalogosImportacionDemo.Aduanas.ContainsKey(aduana))
            throw new BusinessRuleException("RECEPCION_ADUANA_INVALIDA", "Selecciona una aduana del catálogo.");
        if (clave is null || !CatalogosImportacionDemo.ClavesPedimento.ContainsKey(clave))
            throw new BusinessRuleException("RECEPCION_CLAVE_PEDIMENTO_INVALIDA", "Selecciona una clave de pedimento del catálogo.");
        var partidas = new List<RecepcionImportacionPartida>();
        foreach (var linea in lineas)
        {
            var origen = oc.Lineas.Single(l => l.LineaId == linea.LineaOcId);
            var fraccion = Valor(linea.FraccionArancelaria, origen.FraccionArancelaria);
            if (fraccion is null || (fraccion.Length != 8 && fraccion.Length != 10) || fraccion.Any(c => c < '0' || c > '9'))
                throw new BusinessRuleException("RECEPCION_FRACCION_INVALIDA", "Captura una fracción arancelaria de 8 o 10 dígitos en cada partida recibida.");
            partidas.Add(new(origen.LineaId, fraccion));
        }
        return new(oc.EmpresaId, oc.Id, pedimento, bl, contenedor, aduana, clave, partidas);
    }
    private static string? Valor(string? captura, string? origen) =>
        string.IsNullOrWhiteSpace(captura) ? (string.IsNullOrWhiteSpace(origen) ? null : origen.Trim()) : captura.Trim();
}
