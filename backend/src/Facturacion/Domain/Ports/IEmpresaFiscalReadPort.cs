namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Puerto de lectura de los datos fiscales del emisor (dueño:
/// <c>Administracion</c> — <c>compartido.empresas</c> / <c>compartido.sucursales</c>).
/// Facturación resuelve RFC, razón social y régimen fiscal del emisor para
/// prellenar la emisión de CFDI (FAC-UX-PR1, cierra
/// PLATFORM-TODO(&lt;EmisorDefaults&gt;) del frontend). Solo lectura — la
/// administración de empresas/sucursales vive en el módulo Administración.
/// </summary>
public interface IEmpresaFiscalReadPort
{
    /// <summary>Resuelve los datos fiscales de la empresa emisora.</summary>
    Task<EmpresaFiscalLectura?> ObtenerAsync(Guid empresaId, CancellationToken cancellationToken);

    /// <summary>
    /// Id de la única sucursal activa, o <c>null</c> si hay cero o más de una
    /// (en ese caso el usuario elige en el formulario). MVP opera con sucursal
    /// única (levantamiento Facturación).
    /// </summary>
    Task<Guid?> ObtenerSucursalUnicaActivaAsync(CancellationToken cancellationToken);
}

/// <summary>Snapshot de los datos fiscales del emisor al momento de la consulta.</summary>
/// <param name="TasaIvaDefault">
/// Tasa de IVA default configurada en la empresa (fracción, FAC-DET-PR2).
/// Fallback para captura manual cuando el artículo no aporta tasa;
/// <c>null</c> = sin default configurado.
/// </param>
/// <param name="CodigoPostal">
/// CP fiscal de la empresa = <c>LugarExpedicion</c> del CFDI 4.0 (F12-PR1).
/// <c>null</c> = sin capturar — la emisión falla con
/// <c>EMISOR_SIN_LUGAR_EXPEDICION</c>.
/// </param>
public sealed record EmpresaFiscalLectura(
    Guid EmpresaId,
    string Rfc,
    string RazonSocial,
    string RegimenFiscal,
    decimal? TasaIvaDefault,
    string? CodigoPostal);
