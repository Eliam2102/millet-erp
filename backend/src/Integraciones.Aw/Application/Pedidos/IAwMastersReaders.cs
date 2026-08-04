namespace Millet.Integraciones.Aw.Application.Pedidos;

/// <summary>
/// Lectura del master de clientes de A+W (<c>vw_erp_cliente</c> sobre
/// KU_KUNDEN — doc integration/04 §3.4) para la auto-provisión (ADR-0048
/// D6). Dueño del dato crudo: Integraciones.Aw (diseño Facturación §6.1);
/// el alta real la ejecuta DatosMaestros (<c>ProvisionarClienteDesdeAwCommand</c>).
/// </summary>
public interface IAwClientesReader
{
    Task<AwClienteMaster?> LeerClienteAsync(string clienteRef, CancellationToken cancellationToken);
}

/// <summary>
/// Lectura del catálogo de productos de A+W (<c>vw_erp_articulo</c> — doc 04
/// §3.5) para la auto-provisión de <c>ProductoAw</c> (ADR-0048 D5).
/// </summary>
public interface IAwArticulosReader
{
    Task<AwArticuloMaster?> LeerArticuloAsync(string articuloRef, CancellationToken cancellationToken);
}

/// <summary>
/// Fila de <c>vw_erp_cliente</c>. RFC ya viene limpio (solo alfanumérico) y
/// teléfono solo dígitos — reglas heredadas del diseño JSON 2026-05.
/// <c>Cp</c> = KU_KUNDEN.PLZ, validado con datos reales 2026-07-09; el
/// bridge lo usa como prefill del CP fiscal (5 dígitos exactos o null).
/// Régimen fiscal NO existe en A+W: se completa en la UI antes de timbrar.
/// </summary>
public sealed record AwClienteMaster(
    string ClienteRef,
    string RazonSocial,
    string? Rfc,
    string? Calle,
    string? Colonia,
    string? Cp,
    string? Ciudad,
    string? Estado,
    string? Pais,
    string? Telefono,
    // Tax ID extranjero (NumRegIdTrib) para receptor de exportación (CCE).
    // Gap con A+W: la vista aún no lo trae → null; el operador lo captura en
    // Datos Maestros. PLATFORM-TODO(<AwClienteNumRegIdTrib>).
    string? NumRegIdTrib = null);

/// <summary>
/// Fila de <c>vw_erp_articulo</c>. Las claves SAT NO existen en A+W — las
/// asigna el master del ERP (mapeo de unidad + operador). Los datos de aduana
/// (<see cref="FraccionArancelaria"/>, <see cref="PesoUnitarioKg"/>) son parte
/// del contrato a extender con A+W: hoy llegan null (la vista aún no los trae)
/// y el operador los captura en Datos Maestros; cuando A+W agregue las columnas
/// solo se amplía el SELECT del reader (PLATFORM-TODO(&lt;AwArticuloAduana&gt;)).
/// </summary>
public sealed record AwArticuloMaster(
    string ProductoRef,
    string Descripcion,
    string UnidadMedida,
    string? FraccionArancelaria = null,
    decimal? PesoUnitarioKg = null);
