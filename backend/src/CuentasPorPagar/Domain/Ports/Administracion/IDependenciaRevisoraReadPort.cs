namespace Millet.CuentasPorPagar.Domain.Ports.Administracion;

/// <summary>
/// Puerto de lectura del catálogo de dependencias revisoras (áreas
/// organizacionales que pueden recibir facturas en revisión). El
/// catálogo es **compartido** y vive en Administración / DatosMaestros
/// para reuso por Notificaciones, RH, Obras (A21 del 01-diseno §3).
///
/// <para>
/// F0-PR1 introduce el contrato + stub <c>NoOpDependenciaRevisoraReadPort</c>
/// con un seed local de 4 dependencias típicas. PLATFORM-TODO
/// (&lt;DependenciasRevisorasEnAdmin&gt;): cuando Administración exponga
/// el catálogo cross-módulo, reemplazar el stub por el adapter real.
/// </para>
/// </summary>
public interface IDependenciaRevisoraReadPort
{
    Task<DependenciaRevisoraDto?> ObtenerAsync(Guid dependenciaId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DependenciaRevisoraDto>> ListarAsync(CancellationToken cancellationToken);
}

public sealed record DependenciaRevisoraDto(Guid Id, string Codigo, string Nombre, bool Activa);
