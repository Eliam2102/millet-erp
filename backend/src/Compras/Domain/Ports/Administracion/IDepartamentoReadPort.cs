namespace Millet.Compras.Domain.Ports.Administracion;

/// <summary>
/// Proyección mínima de un departamento del catálogo organizacional
/// (<c>compartido.departamentos</c>) para mostrar en listados/detalle.
/// </summary>
public sealed record DepartamentoNombreLectura(Guid Id, string Clave, string Nombre);

/// <summary>
/// Puerto de lectura de Compras hacia el catálogo organizacional de
/// Compartido. Resuelve, en batch, <c>departamentoId → (clave, nombre)</c>
/// para enriquecer los DTOs de lectura de requisiciones (ADR-0042), sin que
/// el cliente tenga que leer el catálogo completo de departamentos.
///
/// <para>
/// Complementa <see cref="ISucursalDepartamentoReadPort"/> (que resuelve la
/// operabilidad N:M); aquí solo se traen los datos de presentación. Las
/// claves no encontradas no aparecen en el diccionario (fallback al id en
/// el handler).
/// </para>
/// </summary>
public interface IDepartamentoReadPort
{
    Task<IReadOnlyDictionary<Guid, DepartamentoNombreLectura>> ObtenerAsync(
        IReadOnlyCollection<Guid> departamentoIds,
        CancellationToken cancellationToken);
}
