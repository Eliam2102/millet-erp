using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Catalogos.Domain;

/// <summary>
/// Categoría (grupo) de artículo del catálogo cross-empresa
/// <c>compartido.categorias_articulo</c>. Reemplaza el string libre
/// <c>varchar(100)</c> que hoy vive en <c>Articulo.Categoria</c>, siguiendo
/// el patrón de <see cref="UnidadMedida"/> (ADR-0046): catálogo administrable
/// + FK nullable en Articulo (la FK <c>categoria_id</c> llega en PR2).
///
/// <para>Catálogo simple (molde <see cref="UsoPrincipal"/>): <see cref="Nombre"/>
/// + <see cref="Estatus"/>. <b>No tiene código</b>: el business-key es el propio
/// <see cref="Nombre"/> (UNIQUE sobre su forma normalizada — lower + colapso de
/// espacios), porque los valores legacy ya son nombres y no existe un código
/// externo (la alineación con grupos de SAP/OITB queda pospuesta).</para>
/// </summary>
public sealed class CategoriaArticulo : BaseEntity, IAuditable
{
    public string Nombre { get; private set; } = string.Empty;
    public EstatusCatalogo Estatus { get; private set; } = EstatusCatalogo.Activo;

    private CategoriaArticulo() { }

    public CategoriaArticulo(
        Guid id, string nombre, EstatusCatalogo estatus = EstatusCatalogo.Activo) : base(id)
    {
        ValidarNombre(nombre);
        Nombre = nombre;
        Estatus = estatus;
    }

    /// <summary>PATCH parcial. El único campo editable es el nombre.</summary>
    public void ActualizarDatos(string? nombre = null)
    {
        if (nombre is not null)
        {
            ValidarNombre(nombre);
            Nombre = nombre;
        }
    }

    public void CambiarEstatus(EstatusCatalogo nuevoEstatus) => Estatus = nuevoEstatus;

    private static void ValidarNombre(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre) || nombre.Length > 100)
            throw new BusinessRuleException("CATEGORIA_ARTICULO_NOMBRE_INVALIDO",
                "El nombre es requerido y no puede exceder 100 caracteres.");
    }
}
