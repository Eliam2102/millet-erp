namespace Millet.Facturacion.Domain.CartaPorte;

/// <summary>
/// Mercancía transportada en una <see cref="CartaPorte"/> (§4.6 levantamiento).
/// Entidad hija. Peso y cantidad son los datos mínimos del complemento Carta
/// Porte 3.1 por bien transportado.
/// </summary>
public sealed class CartaPorteMercancia
{
    public Guid Id { get; private set; }
    public Guid CartaPorteId { get; private set; }

    public string Descripcion { get; private set; } = string.Empty;

    /// <summary>Clave SAT del bien transportado (<c>c_ClaveProdServCP</c>).</summary>
    public string BienesTransp { get; private set; } = string.Empty;

    public string ClaveUnidad { get; private set; } = string.Empty;
    public decimal Cantidad { get; private set; }
    public decimal PesoEnKg { get; private set; }
    public bool MaterialPeligroso { get; private set; }

    private CartaPorteMercancia() { }

    internal CartaPorteMercancia(
        Guid id, Guid cartaPorteId, string descripcion, string bienesTransp, string claveUnidad,
        decimal cantidad, decimal pesoEnKg, bool materialPeligroso)
    {
        Id = id;
        CartaPorteId = cartaPorteId;
        Descripcion = descripcion;
        BienesTransp = bienesTransp;
        ClaveUnidad = claveUnidad;
        Cantidad = cantidad;
        PesoEnKg = pesoEnKg;
        MaterialPeligroso = materialPeligroso;
    }
}
