using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;

namespace Millet.DatosMaestros.Application.Articulos;

/// <summary>
/// Cabecera completa de un artículo del catálogo cross-empresa
/// <c>compartido.articulos</c>. Forma <b>única</b> compartida por los dos
/// endpoints de detalle por id:
/// <list type="bullet">
///   <item><c>GET /api/v1/datos-maestros/articulos/{id}</c> (F-Admin-PR4.5,
///         el que consume el form de edición del front).</item>
///   <item><c>GET /api/v1/catalogos/articulos/{id}</c> (legacy F7-PR1, B.5).</item>
/// </list>
///
/// <para>
/// Se unifica para eliminar el drift que causó el defecto de
/// <see cref="UnidadMedidaId"/>: el detalle de <c>/datos-maestros</c> tenía
/// su propia copia del record sin el FK (ADR-0046 Etapa 1b), así que el
/// front lo leía como <c>null</c> y mostraba la unidad legacy aunque el
/// artículo tuviera unidad asignada. Un solo record evita que se vuelvan a
/// desincronizar. Espejo manual en el front:
/// <c>frontend/src/modules/datos-maestros/api/types.ts</c> (<c>ArticuloDetalle</c>).
/// </para>
/// </summary>
public sealed record ArticuloDetalle(
    Guid Id,
    string Clave,
    string? ClaveLegacy,
    string Nombre,
    string? DescripcionLarga,
    string UnidadMedidaDefault,
    Guid? UnidadMedidaId,
    Naturaleza Naturaleza,
    string? Categoria,
    Guid? CategoriaId,
    decimal? PrecioReferenciaMonto,
    string? PrecioReferenciaMoneda,
    EstatusCatalogo Estatus);
