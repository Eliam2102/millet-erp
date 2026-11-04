namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Lectura del buzón de CFDI recibidos (dueño: Cuentas por Pagar) para elegir la
/// factura mensual del banco por las comisiones de la terminal (86e3xt2q8). Solo
/// lectura: Facturación no escribe en CxP ni crea cuenta por pagar.
/// </summary>
public interface ICfdiRecibidoBancoReadPort
{
    /// <summary>
    /// CFDI de ingreso por procesar emitidos entre el primer día del mes y el fin
    /// del mes siguiente (el banco suele facturar el mes vencido), opcionalmente de
    /// un RFC emisor. Más recientes primero.
    /// </summary>
    Task<IReadOnlyList<CfdiRecibidoBanco>> ListarParaMesAsync(int anio, int mes, string? rfcEmisor, CancellationToken cancellationToken);

    Task<CfdiRecibidoBanco?> ObtenerAsync(Guid cfdiRecibidoId, CancellationToken cancellationToken);
}

/// <summary>CFDI recibido. <c>Tipo</c> y <c>Estado</c> = <c>ToString()</c> de los enums de CxP.</summary>
public sealed record CfdiRecibidoBanco(
    Guid Id, string Uuid, string RfcEmisor, string? Serie, string? Folio, DateTimeOffset FechaCfdi,
    decimal Subtotal, decimal Iva, decimal Total, string Moneda, string Tipo, string Estado);
