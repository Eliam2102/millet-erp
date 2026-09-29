using Millet.DatosMaestros.Domain;

namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>Resumen de origen A+W para la bandeja de clientes.</summary>
public sealed record ClienteOrigenAwResumen(string Resultado, DateTime UltimaLecturaUtc);

/// <summary>
/// Detalle de origen A+W. Los campos "sensibles" (candidatos fiscales, crédito
/// de referencia y domicilio de origen) van en null salvo con `origen-ver`.
/// </summary>
public sealed record ClienteOrigenAwDetalle(
    string? CondicionOrigen,
    int? DiasNominalesOrigen,
    string? MonedaOrigen,
    string? MonedaNormalizada,
    string? NombreComercialOrigen,
    int? EstadoOrigenCrudo,
    int? BloqueoOrigenCrudo,
    DateTime UltimaLecturaUtc,
    DateTime UltimaAplicacionUtc,
    string Resultado,
    string? Error,
    string VersionContrato,
    string VersionMapeo,
    int RegistroVersion,
    string? CandidatoFiscalUstId,
    string? CandidatoFiscalSteuernummer,
    decimal? CreditoReferenciaLimite,
    decimal? CreditoReferenciaLimite1,
    double? CreditoReferenciaNet,
    string? DomicilioOrigenCalle,
    string? DomicilioOrigenCiudad,
    string? DomicilioOrigenCp,
    string? DomicilioOrigenProvincia,
    string? DomicilioOrigenPais);

public static class ClienteOrigenAwProyeccion
{
    public static ClienteOrigenAwResumen Resumen(ClienteSincronizacionAw s)
        => new(s.Resultado.ToString(), s.LeidoEnUtc);

    public static ClienteOrigenAwDetalle Detalle(ClienteSincronizacionAw s, bool verSensibles)
        => new(
            s.CondicionCodigoOrigen, s.DiasNominalesOrigen, s.MonedaCodigoOrigen,
            s.MonedaNormalizada, s.NombreComercialOrigen, s.EstadoOrigenCrudo,
            s.BloqueoOrigenCrudo, s.LeidoEnUtc, s.AplicadoEnUtc, s.Resultado.ToString(),
            s.Error, s.VersionContrato, s.VersionMapeo, s.Version,
            verSensibles ? s.CandidatoFiscalUstId : null,
            verSensibles ? s.CandidatoFiscalSteuernummer : null,
            verSensibles ? s.CreditoReferenciaLimite : null,
            verSensibles ? s.CreditoReferenciaLimite1 : null,
            verSensibles ? s.CreditoReferenciaNet : null,
            verSensibles ? s.DomicilioOrigenCalle : null,
            verSensibles ? s.DomicilioOrigenCiudad : null,
            verSensibles ? s.DomicilioOrigenCp : null,
            verSensibles ? s.DomicilioOrigenProvincia : null,
            verSensibles ? s.DomicilioOrigenPais : null);
}
