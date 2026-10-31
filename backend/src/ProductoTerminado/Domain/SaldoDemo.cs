using Millet.SharedKernel.Domain;

namespace Millet.ProductoTerminado.Domain;

/// <summary>Saldo de prueba por pedido, solo canal Demo. PLATFORM-TODO(&lt;SaldoPorPedidoFacturacion&gt;): se retira con el puerto de Uziel.</summary>
public sealed class SaldoDemo : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public string NumeroPedido { get; private set; } = "";
    public decimal Saldo { get; private set; }

    private SaldoDemo() { }

    public SaldoDemo(Guid empresa, string numeroPedido, decimal saldo) : base(Guid.CreateVersion7())
    {
        EmpresaId = empresa;
        NumeroPedido = numeroPedido.Trim();
        Saldo = saldo;
    }

    public void Poner(decimal saldo) => Saldo = saldo;
}
