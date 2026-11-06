namespace Millet.Contabilidad.Application.PublicPorts;

/// <summary>
/// CXC-06 (86e3qddef): Contabilidad pregunta a CxC (servidor del dato) el ajuste documentado y autorizado de un cobro corto, para
/// cargarlo a su cuenta en la póliza del pago de cliente (#12). CxC lo implementa; Contabilidad no lee tablas de CxC.
/// Corre sin usuario (motor contable): la empresa viaja en la llamada.
/// </summary>
public interface IAjusteCobroContablePort
{
    /// <summary>El ajuste autorizado de la propuesta; null si la propuesta no existe o no tiene ajuste autorizado.</summary>
    Task<AjusteCobroContable?> ObtenerAsync(Guid empresaId, Guid propuestaId, CancellationToken ct);
}

/// <summary>Tipo del ajuste que define la cuenta por rol de Contabilidad.</summary>
public enum TipoAjusteCobroContable
{
    /// <summary>Rol <c>COMISIONES_BANCARIAS</c>.</summary>
    ComisionBancaria = 1,

    /// <summary>Rol <c>DIFERENCIAS_EN_COBRO</c>.</summary>
    DiferenciaMenor = 2,
}

/// <summary>
/// <paramref name="Importe"/> positivo, en la moneda del cobro. <paramref name="AutorizadoPor"/> nulo solo en propuestas anteriores a
/// CXC-06 (la migración las marca autorizadas para no detener los pagos que Tesorería ya tenía en su bandeja).
/// </summary>
public sealed record AjusteCobroContable(TipoAjusteCobroContable Tipo, decimal Importe, string Motivo, Guid? AutorizadoPor, DateTimeOffset AutorizadoEn);
