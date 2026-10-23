using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;

/// <summary>
/// Puerto de lectura del master de proveedores (vive en DatosMaestros).
/// CxP lo consume al capturar facturas, NCs y anticipos para resolver
/// RFC, tolerancia y flags operativos (§6.1 del 01-diseno).
///
/// <para>
/// La <see cref="ProveedorDto.Tolerancia"/> es el snapshot que la captura
/// persiste en <c>FacturaProveedor</c> — si el master cambia después, el
/// histórico se mantiene estable (§3.bis.3 del 01-diseno).
/// </para>
/// </summary>
public interface IProveedorReadPort
{
    Task<ProveedorDto?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken);

    Task<ProveedorDto?> ObtenerPorRfcAsync(string rfc, CancellationToken cancellationToken);

    /// <summary>
    /// Resuelve id → razón social en batch para enriquecer etiquetas de
    /// listados (mismo molde que Compras, ADR-0042). Ids no encontrados
    /// simplemente no aparecen en el diccionario.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resuelve id → RFC en batch para las bandejas. La implementación por defecto
    /// recorre <see cref="ObtenerAsync"/>; el adapter productivo la sobreescribe con una sola consulta.
    /// </summary>
    async Task<IReadOnlyDictionary<Guid, string>> ObtenerRfcsPorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds,
        CancellationToken cancellationToken)
    {
        var resultado = new Dictionary<Guid, string>();
        foreach (var id in proveedorIds)
        {
            var p = await ObtenerAsync(id, cancellationToken);
            if (p is not null) resultado[id] = p.Rfc;
        }
        return resultado;
    }
}

public sealed record ProveedorDto(
    Guid Id,
    string Rfc,
    string RazonSocial,
    ToleranciaProveedorDto? Tolerancia,
    bool EnRevision,
    bool Activo,
    // 86e3pdmee: retención esperada del proveedor (concepto P8 o SIN_RETENCION); null = por revisar.
    string? ConceptoRetencion = null);

public sealed record ToleranciaProveedorDto(
    ToleranciaTipo Tipo,
    decimal Valor);
