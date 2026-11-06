namespace Millet.Contabilidad.Application.ConciliacionAuxiliares;

// F1-CON-09 (86e3jvvm7): puertos de CONSUMIDOR con los que Contabilidad lee el saldo de cada auxiliar al corte. Los implementa el
// módulo dueño del dato (CxP, CxC, Tesorería) en su Infrastructure/PublicAdapters; ninguno escribe ni lee tablas de otro módulo.

/// <summary>Factura de proveedor con saldo al corte. <c>SaldoMxn</c> = saldo × TC del documento (1 si es MXN).</summary>
public sealed record DocumentoProveedorAlCorte(
    Guid FacturaId, Guid ProveedorId, Guid SucursalId, string? Folio, string? Uuid, string Moneda, decimal Saldo, decimal SaldoMxn);

/// <summary>Auxiliar de proveedores al corte. <c>HistorialPorConfirmar</c> = facturas cuyo historial de aplicaciones no se puede reconstruir.</summary>
public sealed record AuxiliarProveedoresAlCorte(IReadOnlyList<DocumentoProveedorAlCorte> Documentos, int HistorialPorConfirmar);

/// <summary>Auxiliar de proveedores (CxP): facturas con saldo al corte, reconstruido con el historial de aplicaciones.</summary>
public interface IAuxiliarProveedoresReadPort
{
    Task<AuxiliarProveedoresAlCorte> LeerAsync(DateOnly corte, Guid? sucursalId, CancellationToken ct);
}

/// <summary>Total de cartera en una moneda al corte (en la moneda del documento).</summary>
public sealed record SaldoCarteraMoneda(string Moneda, decimal Saldo, int Documentos);

/// <summary>
/// Cartera de clientes (CxC) al corte: solo totales por moneda. El detalle documento por documento de cobros contra pólizas es de
/// <i>CxC › Conciliación con pólizas</i> y no se repite aquí.
/// </summary>
public interface ISaldoCarteraAlCorteReadPort
{
    Task<IReadOnlyList<SaldoCarteraMoneda>> LeerAsync(DateOnly corte, CancellationToken ct);
}

/// <summary>
/// Saldo en libros de una cuenta bancaria al corte (Tesorería): saldo inicial más movimientos con fecha valor ≤ corte.
/// <c>SaldoLibros</c> = null si el corte es anterior al corte del saldo inicial (no hay libros a esa fecha).
/// </summary>
public sealed record SaldoBancoAlCorte(
    Guid CuentaBancariaId, string Banco, string NumeroCuenta, string Moneda, string? CuentaContableRef, decimal? SaldoLibros);

public interface ISaldosBancariosAlCorteReadPort
{
    Task<IReadOnlyList<SaldoBancoAlCorte>> LeerAsync(DateOnly corte, CancellationToken ct);
}
