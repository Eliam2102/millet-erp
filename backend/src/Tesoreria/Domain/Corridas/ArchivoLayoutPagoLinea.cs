using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Corridas;

/// <summary>
/// Detalle del pago incluido en un archivo de layout (§4.5, T9.3, 86e3ke5ck).
/// Registra exactamente qué pasivo salió en qué archivo, con qué cuenta,
/// importe, referencia y concepto.
/// </summary>
public sealed class ArchivoLayoutPagoLinea : BaseEntity, IAuditable
{
    public Guid ArchivoLayoutId { get; private set; }
    public Guid FacturaProveedorId { get; private set; }
    public Guid ProveedorId { get; private set; }
    public decimal ImporteNeto { get; private set; }
    public string CuentaDestino { get; private set; } = default!;
    public string Titular { get; private set; } = default!;
    public string Referencia { get; private set; } = default!;
    public string Concepto { get; private set; } = default!;

    private ArchivoLayoutPagoLinea() { }

    public ArchivoLayoutPagoLinea(
        Guid facturaProveedorId,
        Guid proveedorId,
        decimal importeNeto,
        string cuentaDestino,
        string titular,
        string referencia,
        string concepto) : base(Guid.CreateVersion7())
    {
        FacturaProveedorId = facturaProveedorId;
        ProveedorId = proveedorId;
        ImporteNeto = importeNeto;
        CuentaDestino = cuentaDestino?.Trim() ?? string.Empty;
        Titular = titular?.Trim() ?? string.Empty;
        Referencia = referencia?.Trim() ?? string.Empty;
        Concepto = concepto?.Trim() ?? string.Empty;
    }

    internal void AsignarArchivo(Guid archivoId) => ArchivoLayoutId = archivoId;
}
