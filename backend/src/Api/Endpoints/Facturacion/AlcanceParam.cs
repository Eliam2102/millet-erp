using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Parseo del query param <c>?alcance=</c> de las bandejas bajo Capa A de
/// Cajas (CAJAS-PR2, 12-cajas.md §9). Único valor soportado:
/// <c>sin-asignar</c> (bucket `[Decisión 12-B]`, solo
/// <c>facturacion.caja.leer-todas</c> — el 403 lo produce el handler).
/// </summary>
internal static class AlcanceParam
{
    private const string SinAsignar = "sin-asignar";

    /// <summary>True si se pidió el bucket "Sin asignar"; 400 si el valor no se reconoce.</summary>
    internal static bool SoloSinAsignar(string? alcance) => alcance switch
    {
        null or "" => false,
        SinAsignar => true,
        _ => throw new ValidationException(
        [
            new ValidationError("alcance", "ALCANCE_INVALIDO", $"Valor de alcance no soportado: '{alcance}'. Único valor válido: '{SinAsignar}'."),
        ]),
    };
}
