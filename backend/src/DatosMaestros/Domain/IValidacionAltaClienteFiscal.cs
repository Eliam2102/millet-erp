namespace Millet.DatosMaestros.Domain;
public sealed record AltaClienteFiscal(string Rfc, string RazonSocial, string RegimenFiscal, string CodigoPostalFiscal,
    string UsoCfdi, string? PaisResidencia, string? FormaPago = null, string? MetodoPago = null, string Moneda = "MXN");
public interface IValidacionAltaClienteFiscal
{
    Task ValidarAsync(AltaClienteFiscal datos, CancellationToken ct);
}
