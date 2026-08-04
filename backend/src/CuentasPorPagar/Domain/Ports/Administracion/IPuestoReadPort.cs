namespace Millet.CuentasPorPagar.Domain.Ports.Administracion;

/// <summary>
/// Puerto de lectura del catálogo de puestos (<c>compartido.puestos</c>,
/// módulo Administración). CxP lo consume para validar políticas de
/// viáticos (A18 del 01-diseno §3).
///
/// <para>
/// Adapter productivo:
/// <c>Infrastructure.Adapters.PuestoReadPortAdapter</c> (ADM-PR2, doc
/// 10-catalogo-puestos-empleados).
/// </para>
/// </summary>
public interface IPuestoReadPort
{
    Task<PuestoDto?> ObtenerAsync(Guid puestoId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PuestoDto>> ListarAsync(CancellationToken cancellationToken);
}

public sealed record PuestoDto(Guid Id, string Codigo, string Nombre, bool Activo);
