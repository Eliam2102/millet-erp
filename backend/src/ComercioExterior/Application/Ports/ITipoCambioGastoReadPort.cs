namespace Millet.ComercioExterior.Application.Ports;

/// <summary>Fila USD del catálogo de monedas para una fecha. <c>Origen</c>: Banxico, DOF o Manual.</summary>
public sealed record TipoCambioCatalogo(decimal Valor, string Origen);

public interface ITipoCambioGastoReadPort
{
    /// <summary>
    /// Tipo de cambio USD de la fecha: prefiere la fila de origen Banxico y, si no hay, acepta la fila
    /// de la fecha con origen Manual o DOF (el índice único moneda-fecha sólo permite una). Null si no hay fila.
    /// </summary>
    Task<TipoCambioCatalogo?> ObtenerSugeridoAsync(DateOnly fecha, CancellationToken ct);
}
