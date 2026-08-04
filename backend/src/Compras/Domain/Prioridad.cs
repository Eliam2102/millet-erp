namespace Millet.Compras.Domain;

/// <summary>
/// Prioridad de la requisición (§10.2.1). Persistido como <c>smallint</c>
/// con CHECK <c>prioridad BETWEEN 0 AND 2</c>.
/// </summary>
public enum Prioridad : short
{
    Baja = 0,
    Normal = 1,
    Alta = 2,
}
