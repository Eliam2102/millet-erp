using System.Text.Json.Serialization;

namespace Millet.Compras.Domain;

/// <summary>
/// Modo de nivel 2 de la requisición, por departamento
/// (<c>UmbralAprobacionDepartamento.ModoNivel2</c>).
/// <see cref="SoloMonto"/>: N2 si el monto de la RQ supera <c>UmbralMonto</c>.
/// <see cref="SoloDisponibilidad"/>: N2 si alguna línea de inventario no se
/// cubre con la existencia y el importe a comprar supera
/// <c>MontoMinimoDisponibilidad</c> (si está definido).
/// <see cref="Ambos"/>: N2 si se cumple cualquiera de las dos.
/// La naturaleza Crítico o Riesgo siempre exige N2. No aplica a la OC.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModoNivel2
{
    SoloMonto = 1,
    SoloDisponibilidad = 2,
    Ambos = 3,
}
