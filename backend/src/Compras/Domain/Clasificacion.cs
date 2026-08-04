namespace Millet.Compras.Domain;

/// <summary>
/// Clasificación funcional de la requisición (§10.2.1). Persistido como
/// <c>smallint</c> con CHECK <c>clasificacion BETWEEN 0 AND 3</c>.
/// </summary>
public enum Clasificacion : short
{
    Servicio = 0,
    OrdenCompra = 1,
    MateriaPrima = 2,
    Pinturas = 3,
}
