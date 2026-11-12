using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Sustituciones;

/// <summary>
/// Qué sigue según el motivo SAT de cancelación. 01: se emite la nueva con relación 04 y se cancela la original.
/// 02: primero se cancela y después se emite la nueva SIN relación. 03: solo se cancela. 04: factura global
/// (público en general) → se cancela y se emite la nominativa SIN relación.
/// </summary>
public sealed record GuiaMotivoCancelacion(
    string Motivo, bool CancelaPrimero, bool EmiteNueva, bool LlevaRelacion04, IReadOnlyList<string> Pasos)
{
    public static GuiaMotivoCancelacion Para(string motivo) => motivo switch
    {
        "01" => new("01", false, true, true,
            ["Emite la factura nueva con relación 04 (sustitución).", "Al timbrarla se pide cancelar la original con motivo 01."]),
        "02" => new("02", true, true, false,
            ["Cancela la original con motivo 02 (errores sin relación).", "Cuando esté cancelada, emite la factura nueva sin relación."]),
        "03" => new("03", true, false, false,
            ["Cancela con motivo 03 (no se llevó a cabo la operación).", "No se emite factura nueva."]),
        "04" => new("04", true, true, false,
            ["Cancela la factura global con motivo 04 (operación nominativa).", "Cuando esté cancelada, emite la factura nominativa sin relación."]),
        _ => throw new BusinessRuleException("CANCELACION_MOTIVO_INVALIDO", "El motivo SAT debe ser 01, 02, 03 o 04."),
    };
}
