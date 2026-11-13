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

    /// <summary>
    /// M-240: la reversa del ajuste autorizado de la propuesta (para la póliza contraria); null si la propuesta no existe o su ajuste no
    /// está revertido.
    /// </summary>
    Task<ReversaAjusteCobroContable?> ObtenerReversaAsync(Guid empresaId, Guid propuestaId, CancellationToken ct);
}

/// <summary>Tipo del ajuste que define la cuenta por rol de Contabilidad.</summary>
public enum TipoAjusteCobroContable
{
    /// <summary>Rol <c>COMISIONES_BANCARIAS</c>.</summary>
    ComisionBancaria = 1,

    /// <summary>Rol <c>DIFERENCIAS_EN_COBRO</c>.</summary>
    DiferenciaMenor = 2,

    /// <summary>M-326: rol <c>REDONDEO</c> (faltante de hasta $0.99 sin documentar).</summary>
    Redondeo = 3,
}

/// <summary>
/// <paramref name="Importe"/> positivo, en la moneda del cobro. <paramref name="AutorizadoPor"/> nulo solo en propuestas anteriores a
/// CXC-06 (la migración las marca autorizadas para no detener los pagos que Tesorería ya tenía en su bandeja).
/// </summary>
public sealed record AjusteCobroContable(TipoAjusteCobroContable Tipo, decimal Importe, string Motivo, Guid? AutorizadoPor, DateTimeOffset AutorizadoEn);

/// <summary>
/// M-240: ajuste revertido. <paramref name="Importe"/> positivo, en la moneda del cobro; <paramref name="MovimientoBancarioId"/> es el
/// documento de la póliza del pago de cliente (#12) que se revierte en la parte del ajuste.
/// </summary>
public sealed record ReversaAjusteCobroContable(TipoAjusteCobroContable Tipo, decimal Importe, string Motivo, Guid MovimientoBancarioId,
    Guid ClienteId, Guid RevertidoPor, DateTimeOffset RevertidoEn);
