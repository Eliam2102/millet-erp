using Millet.ComercioExterior.Application.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Application;

/// <summary>Tipo de cambio aplicado al gasto del expediente, su fuente y el sugerido para comparar (AC-011, M-187).</summary>
public sealed record TipoCambioGastoResuelto(decimal TipoCambio, string Fuente, decimal? Sugerido);

public static class TipoCambioGastoResolver
{
    public const string ErrorFaltante = "CE_TC_BANXICO_FALTANTE";

    /// <summary>
    /// Fuente Banxico (o sin fuente): usa la fila del catálogo de la fecha; si no es de Banxico, la fuente queda
    /// «Catálogo Manual» o «Catálogo DOF» para mostrar su origen. Sin fila, conserva CE_TC_BANXICO_FALTANTE.
    /// Fuente capturada (Negociado, CFDI, Pedimento): usa el valor del usuario o del CFDI y nunca falla por falta de
    /// catálogo; el sugerido sólo se consulta para comparar y puede quedar vacío.
    /// </summary>
    public static async Task<TipoCambioGastoResuelto> ResolverAsync(ITipoCambioGastoReadPort catalogo, string moneda, DateOnly fecha,
        string? fuente, decimal? tipoCambioUsado, decimal? tipoCambioCfdi, CancellationToken ct)
    {
        if (moneda == "MXN") return new(1, "MXN", 1);
        if (fuente is null or "Banxico")
        {
            var fila = await catalogo.ObtenerSugeridoAsync(fecha, ct) ?? throw Faltante();
            return new(fila.Valor, fila.Origen == "Banxico" ? "Banxico" : $"Catálogo {fila.Origen}", fila.Valor);
        }
        if (fuente.StartsWith("Catálogo", StringComparison.Ordinal))
            throw new BusinessRuleException("CE_TIPO_CAMBIO_FUENTE_INVALIDA", "Elija la fuente Banxico, Negociado, CFDI o Pedimento.");
        var tc = fuente == "CFDI" && tipoCambioCfdi is decimal delCfdi ? delCfdi
            : tipoCambioUsado ?? throw new BusinessRuleException("CE_TIPO_CAMBIO_REQUERIDO", "Capture el tipo de cambio usado.");
        var sugerido = await catalogo.ObtenerSugeridoAsync(fecha, ct);
        return new(tc, fuente, sugerido?.Valor);
    }

    /// <summary>Tipo de cambio obligatorio de la fecha (Banxico o, en su defecto, la fila del catálogo).</summary>
    public static async Task<decimal> ObligatorioAsync(ITipoCambioGastoReadPort catalogo, DateOnly fecha, CancellationToken ct) =>
        (await catalogo.ObtenerSugeridoAsync(fecha, ct) ?? throw Faltante()).Valor;

    private static BusinessRuleException Faltante() =>
        new(ErrorFaltante, "Registre el tipo de cambio Banxico de la fecha de la factura en el catálogo de monedas.");
}
