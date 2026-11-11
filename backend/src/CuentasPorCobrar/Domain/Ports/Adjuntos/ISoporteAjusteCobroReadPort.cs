namespace Millet.CuentasPorCobrar.Domain.Ports.Adjuntos;

/// <summary>
/// M-240: ¿la propuesta de aplicación tiene soporte vigente de su ajuste (estado de cuenta o aviso del banco)? Lo responde el servicio
/// genérico de adjuntos (G1.2, Compartido); CxC no lee sus tablas.
/// </summary>
public interface ISoporteAjusteCobroReadPort
{
    Task<bool> TieneSoporteAsync(Guid propuestaId, CancellationToken ct);
}

/// <summary>Tipo de entidad y tipo de documento del soporte en el servicio genérico de adjuntos.</summary>
public static class SoporteAjusteCobro
{
    public const string TipoEntidad = "propuesta_aplicacion_cxc";
    public const string CodigoDocumento = "soporte_comision";
    public static readonly Guid TipoDocumentoId = Guid.Parse("00000011-0009-0000-0000-000000000001");
}
