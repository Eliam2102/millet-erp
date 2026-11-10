using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Domain.Liberacion;

/// <summary>
/// M-085 (Crédito y Cobranza, 9-nov): la autorización excepcional de Crédito y Cobranza vale 24 h y cubre hasta 50,000 pesos de
/// exceso sobre el crédito disponible; arriba de eso autoriza Dirección (permiso
/// <c>cuentas_por_cobrar.liberacion.override-direccion</c>). Exceso = monto del pedido − crédito disponible (sin bajar de 0).
/// Supuesto a validar: el tope es en pesos y solo se aplica a pedidos en MXN; los de otra moneda quedan sin tope hasta que
/// Crédito y Cobranza diga cómo convertirlos.
/// </summary>
public static class ExcesoCredito
{
    public const decimal TopeCreditoYCobranza = 50_000m;
    public const string CodigoRequiereDireccion = "CXC_EXCESO_REQUIERE_DIRECCION";

    public static decimal Calcular(decimal disponible, decimal montoPedido) => Math.Max(0m, montoPedido - Math.Max(0m, disponible));

    public static bool RequiereDireccion(string moneda, decimal exceso) =>
        string.Equals(moneda, "MXN", StringComparison.OrdinalIgnoreCase) && exceso > TopeCreditoYCobranza;

    /// <summary>Rechaza la liberación por excepción cuando el exceso pasa el tope y la autorización no es de Dirección.</summary>
    public static void ExigirNivel(string moneda, decimal disponible, decimal montoPedido, bool autorizadaPorDireccion)
    {
        var exceso = Calcular(disponible, montoPedido);
        if (RequiereDireccion(moneda, exceso) && !autorizadaPorDireccion)
            throw new BusinessRuleException(CodigoRequiereDireccion,
                $"El exceso sobre el crédito disponible es de {exceso:N2} {moneda}: Crédito y Cobranza autoriza hasta "
                + $"{TopeCreditoYCobranza:N2}; arriba de eso la autorización debe ser de Dirección.");
    }
}
