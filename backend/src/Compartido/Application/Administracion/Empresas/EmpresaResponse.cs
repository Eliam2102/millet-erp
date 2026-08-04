namespace Millet.Administracion.Application.Empresas;

/// <summary>
/// DTO de respuesta para Empresa (F-Admin-PR2.3). Mismo shape en list,
/// detail y mutaciones — el caller siempre recibe el agregado completo
/// para que el FE pueda refrescar caches sin segundo fetch.
/// </summary>
public sealed record EmpresaResponse(
    Guid Id,
    string Rfc,
    string RazonSocial,
    string? NombreComercial,
    string RegimenFiscal,
    decimal? TasaIvaDefault,
    string? CodigoPostal,
    bool Activa,
    int Version);
