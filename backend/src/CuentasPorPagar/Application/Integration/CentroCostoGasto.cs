using Millet.Contabilidad.Application.PublicPorts;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Integration;

/// <summary>
/// H1 paso 2 (86e3z5w25): valida el área o equipo capturado en un gasto de caja chica, viáticos o tarjeta contra la sucursal con la
/// que se contabiliza (la de la comprobación de caja chica; la del empleado en viáticos y tarjeta). Que la cuenta lo exija no se
/// revisa aquí: sin el dato la póliza queda en Rechazos (C-03), como pidió Contabilidad.
/// </summary>
internal static class CentroCostoGasto
{
    public const string CodigoInvalido = "GASTO_CENTRO_COSTO_INVALIDO";

    public static async Task<Guid?> ValidarAsync(
        IDimensionesGastoPort? dimensiones, Guid empresaId, Guid? sucursalId, Guid? centroCostoId, CancellationToken ct)
    {
        if (centroCostoId is not { } id || id == Guid.Empty) return null;
        if (dimensiones is null || sucursalId is not { } sucursal || sucursal == Guid.Empty) return id;
        if (await dimensiones.ValidarCentroAsync(empresaId, sucursal, id, ct) is { } error)
            throw new BusinessRuleException(CodigoInvalido, error);
        return id;
    }
}
