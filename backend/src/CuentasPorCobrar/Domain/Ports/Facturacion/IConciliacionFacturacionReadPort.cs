namespace Millet.CuentasPorCobrar.Domain.Ports.Facturacion;

/// <summary>
/// C7.7: lo que Facturación registró en un día contable y que debe verse en la
/// cartera. Son los mismos datos que viajan en los eventos que alimentan la
/// proyección (factura timbrada, NC, REP, cobro de mostrador y cancelaciones),
/// leídos de Facturación para comprobar que cada evento llegó. CxC no lee el
/// esquema <c>facturacion</c>; lo implementa un adaptador público de Facturación.
/// </summary>
public interface IConciliacionFacturacionReadPort
{
    /// <summary>
    /// Comprobantes timbrados en <paramref name="dia"/> (hora de Millet) y
    /// comprobantes cancelados ese día, con su estado actual. Respeta el filtro
    /// por empresa del contexto (en bypass devuelve todas las empresas).
    /// </summary>
    Task<ComprobantesDelDia> ConsultarDiaAsync(DateOnly dia, CancellationToken cancellationToken);
}

public sealed record ComprobantesDelDia(
    IReadOnlyList<FacturaFacturacion> Facturas,
    IReadOnlyList<AplicacionFacturacion> Aplicaciones)
{
    public int Total => Facturas.Count + Aplicaciones.Select(a => a.OrigenId).Distinct().Count();
}

/// <summary>Factura de venta (Ingreso, sin las de anticipo) tal como la ve Facturación.</summary>
public sealed record FacturaFacturacion(
    Guid EmpresaId,
    Guid FacturaVentaId,
    string Folio,
    string? Uuid,
    string ReceptorRfc,
    string ReceptorNombre,
    decimal Total,
    bool Cancelada);

public enum TipoAplicacionFacturacion : short
{
    NotaCredito = 1,
    ReciboPago = 2,
    CobroMostrador = 3,
}

/// <summary>
/// Lo que una NC, un REP o un cobro de mostrador aplica a una factura. Un REP
/// que paga varias facturas da una aplicación por factura.
/// </summary>
public sealed record AplicacionFacturacion(
    Guid EmpresaId,
    TipoAplicacionFacturacion Tipo,
    Guid OrigenId,
    string Folio,
    string? Uuid,
    Guid FacturaVentaId,
    string ReceptorRfc,
    string ReceptorNombre,
    decimal Importe,
    bool Cancelada);
