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

    /// <summary>
    /// Resuelve id → nombre en batch para las bandejas. Implementación por defecto
    /// vía <see cref="ObtenerAsync"/>; el adapter productivo la sobreescribe con una consulta.
    /// </summary>
    async Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(
        IReadOnlyCollection<Guid> sucursalIds, CancellationToken cancellationToken)
    {
        var resultado = new Dictionary<Guid, string>();
        foreach (var id in sucursalIds)
        {
            var s = await ObtenerAsync(id, cancellationToken);
            if (s is not null) resultado[id] = s.Nombre;
        }
        return resultado;
    }
}

public sealed record SucursalDto(Guid Id, Guid EmpresaId, string Codigo, string Nombre, bool Activa);
