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
    /// Búsqueda para selectores. <paramref name="alcance"/> restringe a los centros de esas ubicaciones (Dim1) o de esos CeCo
    /// (Dim2), unidos; null = sin restricción. Sin inactivos salvo que se pidan.
    /// </summary>
    Task<IReadOnlyList<CentroCostoNodo>> BuscarAsync(
        DimensionContable nivel, string? q, AlcanceCentros? alcance, bool incluirInactivos, int limite, CancellationToken ct);

    /// <summary>
    /// K10.2-d: el centro (de cualquier nivel) está en la asignación de centros del usuario actual (usuario → equipo, ADR-0050/
    /// 0062; el permiso de alcance total la omite). Solo para captura del usuario, no para pólizas automáticas.
    /// </summary>
    Task<bool> EnAlcanceDelUsuarioAsync(Guid centroId, CancellationToken ct);
}

/// <summary>Centros permitidos: todos los de las ubicaciones <see cref="Dim1Ids"/> más los CeCo <see cref="Dim2Ids"/> (corporativos).</summary>
public sealed record AlcanceCentros(IReadOnlyCollection<Guid> Dim1Ids, IReadOnlyCollection<Guid> Dim2Ids);

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

/// <summary>Auxiliares de una partida que se validan contra su catálogo (K10.2: cliente, proveedor y banco).</summary>
public enum TipoAuxiliar { Cliente, Proveedor, Banco }

/// <summary>Lectura mínima de un auxiliar para validar y mostrar: existe, está activo y cómo se llama.</summary>
public sealed record AuxiliarContable(Guid Id, string Clave, string Nombre, bool Activo);

/// <summary>
/// Puerto de CONSUMIDOR para clientes y proveedores (maestros de <c>compartido</c>). Lo implementa Compartido.
/// <paramref name="tipo"/> solo admite <see cref="TipoAuxiliar.Cliente"/> o <see cref="TipoAuxiliar.Proveedor"/>.
/// </summary>
public interface ITerceroContabilidadPort
{
    Task<IReadOnlyDictionary<Guid, AuxiliarContable>> ObtenerAsync(TipoAuxiliar tipo, IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<AuxiliarContable>> BuscarAsync(TipoAuxiliar tipo, string? q, int limite, CancellationToken ct);

    /// <summary>
    /// M-166 (apertura): clientes o proveedores por clave exacta (sin distinguir mayúsculas), con la clave en mayúsculas como llave. Las
    /// claves que no existen no aparecen. Por omisión busca una por una; el adaptador lo resuelve en una consulta.
    /// </summary>
    async Task<IReadOnlyDictionary<string, AuxiliarContable>> ObtenerPorClavesAsync(TipoAuxiliar tipo, IReadOnlyCollection<string> claves, CancellationToken ct)
    {
        var res = new Dictionary<string, AuxiliarContable>(StringComparer.OrdinalIgnoreCase);
        foreach (var clave in claves.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            if ((await BuscarAsync(tipo, clave, 20, ct)).FirstOrDefault(a => string.Equals(a.Clave.Trim(), clave, StringComparison.OrdinalIgnoreCase)) is { } a)
                res[clave.ToUpperInvariant()] = a;
        return res;
    }
}

/// <summary>Puerto de CONSUMIDOR para cuentas bancarias de Millet (dimensión banco). Lo implementa Tesorería.</summary>
public interface ICuentaBancariaContabilidadPort
{
    Task<IReadOnlyDictionary<Guid, AuxiliarContable>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
    Task<IReadOnlyList<AuxiliarContable>> BuscarAsync(string? q, int limite, CancellationToken ct);

    /// <summary>
    /// M-166 (apertura): cuentas bancarias por número exacto, con el número en mayúsculas como llave. Los que no existen no aparecen. Por
    /// omisión busca uno por uno; el adaptador lo resuelve en una consulta.
    /// </summary>
    async Task<IReadOnlyDictionary<string, AuxiliarContable>> ObtenerPorNumerosAsync(IReadOnlyCollection<string> numeros, CancellationToken ct)
    {
        var res = new Dictionary<string, AuxiliarContable>(StringComparer.OrdinalIgnoreCase);
        foreach (var numero in numeros.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            if ((await BuscarAsync(numero, 20, ct)).FirstOrDefault(a => string.Equals(a.Clave.Trim(), numero, StringComparison.OrdinalIgnoreCase)) is { } a)
                res[numero.ToUpperInvariant()] = a;
        return res;
    }
}

/// <summary>
/// Puerto de CONSUMIDOR para artículos (F1-ALM-13): grupo del artículo (grupo SAP, <c>Articulo.Categoria</c>) por id, para el rol
/// <c>INVENTARIO_{grupo}</c>. Lo implementa Compartido. Los ids que no existen o sin grupo no aparecen.
/// </summary>
public interface IArticuloContabilidadPort
{
    Task<IReadOnlyDictionary<Guid, string>> GruposAsync(IReadOnlyCollection<Guid> articuloIds, CancellationToken ct);
}
