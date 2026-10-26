namespace Millet.CuentasPorCobrar.Domain.Ports.Facturacion;

/// <summary>
/// Estado del anticipo en la consulta de aplicaciones (F1-CXC-05). Derivado de
/// lo cobrado contra lo aplicado con egreso vigente — mismas reglas que la
/// comparativa F6.2 de Facturación; no es el estado persistido del agregado.
/// </summary>
public static class EstadoAnticipoAplicaciones
{
    public const string Pendiente = "Pendiente";
    public const string Parcial = "Parcial";
    public const string Aplicado = "Aplicado";
    public const string Cancelado = "Cancelado";

    public static readonly IReadOnlyList<string> Todos = [Pendiente, Parcial, Aplicado, Cancelado];
}

/// <summary>Tipo de movimiento de una aplicación del anticipo.</summary>
public static class TipoAplicacionAnticipo
{
    /// <summary>Factura final + egreso tipo 07 timbrado (o en cancelación pendiente): resta saldo.</summary>
    public const string Aplicacion = "Aplicación";

    /// <summary>Vinculación a factura final sin egreso timbrado: compromete, no resta.</summary>
    public const string VinculacionSinEgreso = "Vinculación sin egreso";

    /// <summary>El egreso 07 se canceló: se muestra, no resta.</summary>
    public const string EgresoCancelado = "Egreso cancelado";
}

/// <summary>
/// Aplicación de un anticipo a una factura final (relación CFDI 07 vía la nota
/// de crédito de amortización).
/// </summary>
public sealed record AplicacionAnticipoDto(
    string Tipo,
    string? FacturaFinalFolio,
    string? FacturaFinalUuid,
    string? EgresoFolio,
    string? EgresoUuid,
    DateTimeOffset? FechaAplicacion,
    string? EstadoEgreso,
    decimal Importe);

/// <summary>
/// Anticipo del cliente con sus aplicaciones a factura final (F1-CXC-05).
/// <c>Saldo = Cobrado − Aplicado</c>; <c>Comprometido</c> es informativo.
/// <c>SaldoSistema</c> es el saldo persistido del agregado; cuando difiere del
/// calculado viaja la <c>Observacion</c> «Saldo en sistema …».
/// </summary>
public sealed record AnticipoConAplicacionesDto(
    Guid AnticipoId,
    Guid FacturaAnticipoId,
    Guid ClienteId,
    string ClienteNombre,
    string Folio,
    string? Uuid,
    DateTimeOffset? FechaTimbrado,
    Guid? SucursalId,
    string? SucursalNombre,
    string Moneda,
    string Estado,
    decimal Cobrado,
    decimal Aplicado,
    decimal Comprometido,
    decimal Saldo,
    decimal SaldoSistema,
    string? PedidoOrigenRef,
    string? Observacion,
    IReadOnlyList<AplicacionAnticipoDto> Aplicaciones);

/// <summary>
/// Puerto de lectura de anticipos con sus aplicaciones a factura final
/// (F1-CXC-05). El adapter vive en Facturación
/// (<c>Infrastructure/PublicAdapters</c>) — CxC nunca lee el esquema
/// <c>facturacion</c>. Separado de <see cref="IFacturacionAnticiposReadPort"/>
/// para no tocar el contrato que consume el estado de cuenta (C7.8).
/// </summary>
public interface IFacturacionAplicacionesAnticipoReadPort
{
    /// <summary>
    /// Anticipos timbrados (incluye cancelados) con sus aplicaciones.
    /// <paramref name="clienteId"/> <c>null</c> = todos los clientes con anticipos.
    /// </summary>
    Task<IReadOnlyList<AnticipoConAplicacionesDto>> ListarAsync(
        Guid? clienteId, CancellationToken cancellationToken);
}
