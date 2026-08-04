namespace Millet.Catalogos.Domain;

/// <summary>
/// Naturaleza del artículo desde la perspectiva de Compras (atributo
/// del catálogo, ver §3.bis.1 del diseño Compras/Requisiciones).
/// Determina la matriz de aprobación (A1) y futuras políticas de
/// inventario crítico para Almacén.
///
/// <para>
/// Persiste como <c>smallint</c>. Default <c>Estandar</c> hasta que el
/// cliente reclasifique el catálogo (post-go-live, A1).
/// </para>
/// </summary>
public enum Naturaleza : short
{
    Estandar = 0,
    Servicio = 1,
    Critico = 2,
    Riesgo = 3,
}
