namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Resultado de una <see cref="AutorizacionOC"/> (diseño §4.10).
/// Persistido como <c>smallint</c> con CHECK <c>resultado IN (1, 2)</c>.
/// Numeración alinea con la columna del DDL §10.1.
/// </summary>
public enum ResultadoAutorizacionOc : short
{
    Autorizado = 1,
    Rechazado = 2,
}
