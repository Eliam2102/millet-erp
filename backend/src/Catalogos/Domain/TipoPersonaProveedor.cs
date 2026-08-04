namespace Millet.Catalogos.Domain;

/// <summary>
/// Tipo fiscal del proveedor (México). Se deriva del RFC: 12 caracteres
/// = persona moral; 13 caracteres = persona física.
/// </summary>
public enum TipoPersonaProveedor : short
{
    Moral = 0,
    Fisica = 1,
}
