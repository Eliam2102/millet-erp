namespace Millet.CuentasPorPagar.Domain.FacturaProveedor;

public enum TipoMovimientoPasivo { Pago = 1, Anticipo = 2, NotaCredito = 3, NotaCargo = 4 }

/// <summary>Aplicación fechada e inmutable; un reverso de pago lleva monto negativo.</summary>
public sealed class MovimientoPasivo
{
    public Guid Id { get; private set; }
    public Guid FacturaProveedorId { get; private set; }
    public Guid? DocumentoId { get; private set; }
    public TipoMovimientoPasivo Tipo { get; private set; }
    public DateOnly Fecha { get; private set; }
    public decimal Monto { get; private set; }
    private MovimientoPasivo() { }
    public MovimientoPasivo(Guid facturaId, Guid? documentoId, TipoMovimientoPasivo tipo, DateOnly fecha, decimal monto)
    {
        Id = Guid.CreateVersion7(); FacturaProveedorId = facturaId; DocumentoId = documentoId;
        Tipo = tipo; Fecha = fecha; Monto = monto;
    }
}
