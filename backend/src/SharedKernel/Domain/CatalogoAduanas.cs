namespace Millet.SharedKernel.Domain;

// PLATFORM-TODO(<CatalogoSatComercioExterior>): catálogo SAT completo de aduanas y claves de pedimento
// DEMO: subconjunto local para captura sin PAC. Validar y ampliar contra el catálogo SAT vigente antes de producción.
// shortcut: catálogo acotado de demostración, sustituir por catálogo completo versionado al habilitar operación fiscal.
public static class CatalogoAduanas
{
    public static readonly IReadOnlyDictionary<string, string> Aduanas = new Dictionary<string, string>
    { ["16"] = "Manzanillo", ["43"] = "Veracruz", ["19"] = "Ciudad del Carmen", ["80"] = "Colombia", ["81"] = "Altamira" };
}
