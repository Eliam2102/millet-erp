using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application.Ports;

/// <summary>
/// Puerto de CONSUMIDOR (F1-CON-02, D9): Contabilidad lee los centros de costo de ADM-08 sin acoplarse al esquema
/// <c>centros_costo</c>. Lo implementa el dueño del dato (<c>CentrosCosto.Infrastructure.PublicAdapters</c>) y se cablea
/// en <c>Program.cs</c>. Incluye inactivos al resolver por id ("ver ≠ elegir", ADR-0050).
/// </summary>
public interface ICentroCostoContabilidadPort
{
    /// <summary>Nodos de cualquier nivel por id (batch). Los ids que no existen no aparecen.</summary>
    Task<IReadOnlyDictionary<Guid, CentroCostoNodo>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>
    /// Búsqueda para selectores. <paramref name="dentroDeDim2"/> restringe a esos CeCo (Dim2 = ellos mismos; Dim3 = sus hijos;
    /// Dim1 = sus padres); null = sin restricción. Sin inactivos salvo que se pidan.
    /// </summary>
    Task<IReadOnlyList<CentroCostoNodo>> BuscarAsync(
        DimensionContable nivel, string? q, IReadOnlyCollection<Guid>? dentroDeDim2, bool incluirInactivos, int limite, CancellationToken ct);
}

/// <summary>
/// Nodo del árbol de centros. <see cref="ActivoEnCadena"/> = el nodo y todos sus ancestros están activos.
/// <see cref="Dim1Id"/>/<see cref="Dim2Id"/> son los ancestros (o el propio nodo en su nivel).
/// </summary>
public sealed record CentroCostoNodo(
    Guid Id, DimensionContable Nivel, string Clave, string Nombre, bool Activo, bool ActivoEnCadena,
    Guid Dim1Id, Guid? Dim2Id, string Dim1Clave, string? Dim2Clave);

/// <summary>
/// Puerto de CONSUMIDOR para sucursales (ADR-0051): catálogo de la empresa actual y pertenencia usuario ↔ sucursal.
/// Lo implementa Compartido (dueño de <c>compartido.sucursales</c>) delegando la pertenencia en <c>IUsuarioSucursalReadPort</c>.
/// </summary>
public interface ISucursalContabilidadPort
{
    Task<IReadOnlyList<SucursalContable>> ListarAsync(CancellationToken ct);
    Task<bool> UsuarioAsociadoAsync(Guid usuarioId, Guid sucursalId, CancellationToken ct);
}

public sealed record SucursalContable(Guid Id, string Clave, string Nombre, bool Activa);
