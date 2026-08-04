namespace Millet.Catalogos.Domain;

/// <summary>
/// Estatus de una fila de catálogo cross-empresa (proveedores,
/// artículos). <c>EnRevision</c> es estado intermedio para registros
/// nuevos pendientes de validación administrativa.
/// </summary>
public enum EstatusCatalogo : short
{
    Activo = 0,
    Inactivo = 1,
    EnRevision = 2,
}
