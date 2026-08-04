namespace Millet.CuentasPorPagar.Domain.NotaCargo;

/// <summary>
/// Estados de <see cref="NotaCargo"/> (§4.6 del 00-levantamiento).
/// Flujo: <c>Borrador → Autorizada → Aplicada → Formalizada</c> (la
/// formalización ocurre cuando el proveedor emite la NC fiscal que
/// cierra la nota de cargo).
/// </summary>
public enum EstadoNotaCargo
{
    Borrador     = 1,
    Autorizada   = 2,
    Aplicada     = 3,
    Formalizada  = 4,
    Cancelada    = 5,
}
