using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.CuentasPorPagar.Domain.FacturaProveedor;
/// <summary>Proyección del pago y de su cobertura fiscal; PagoId es la aplicación de Tesorería.</summary>
public sealed class PagoProveedorLocal : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid FacturaProveedorId { get; private set; }
    public Guid PagoId { get; private set; }
    public DateOnly FechaPago { get; private set; }
    public decimal Importe { get; private set; }
    public decimal CubiertoRepp { get; private set; }
    public bool Revertido { get; private set; }
    private PagoProveedorLocal() { }
    public PagoProveedorLocal(Guid empresaId, Guid facturaId, Guid pagoId, decimal importe, DateOnly fecha) : base(Guid.CreateVersion7())
    { EmpresaId = empresaId; FacturaProveedorId = facturaId; PagoId = pagoId; Importe = importe; FechaPago = fecha; }
    public void Confirmar(decimal importe, DateOnly fecha) { Importe = importe; FechaPago = fecha; }
    public void Cubrir(decimal importe)
    {
        if (importe <= 0 || (Importe > 0 && importe + CubiertoRepp > Importe))
            throw new BusinessRuleException("REPP_IMPORTE_EXCEDE_PAGO", "El importe del REPP debe ser positivo y no exceder el pago pendiente de complementar.");
        CubiertoRepp += importe;
    }
    public void SincronizarCobertura(decimal cubierto, bool revertido)
    {
        if (cubierto < 0 || cubierto > Importe) throw new BusinessRuleException("REPP_COBERTURA_INVALIDA", "La cobertura fiscal no puede exceder el pago.");
        CubiertoRepp = cubierto; Revertido = revertido;
    }
    public void Revertir() => Revertido = true;
}
