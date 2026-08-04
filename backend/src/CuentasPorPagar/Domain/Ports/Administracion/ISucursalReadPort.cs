namespace Millet.CuentasPorPagar.Domain.Ports.Administracion;

/// <summary>
/// Puerto de lectura del catálogo de sucursales (vive en
/// Administración/DatosMaestros). CxP lo consume al capturar facturas
/// (sucursal del gasto), comprobaciones de gastos y notas de cargo
/// (§6.1 del 01-diseno).
///
/// <para>
/// F0-PR1 introduce el contrato + stub <c>NoOpSucursalReadPort</c>.
/// Implementación real cuando el módulo Administración exponga el
/// catálogo enriquecido cross-módulo.
/// </para>
/// </summary>
public interface ISucursalReadPort
{
    Task<SucursalDto?> ObtenerAsync(Guid sucursalId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SucursalDto>> ListarPorEmpresaAsync(Guid empresaId, CancellationToken cancellationToken);
}

public sealed record SucursalDto(Guid Id, Guid EmpresaId, string Codigo, string Nombre, bool Activa);
