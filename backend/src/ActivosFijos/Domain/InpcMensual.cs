using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Domain;
public sealed class InpcMensual : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public int Anio { get; private set; }
    public int Mes { get; private set; }
    public decimal Valor { get; private set; }
    private InpcMensual() { }
    public InpcMensual(Guid empresa, int anio, int mes, decimal valor) : base(Guid.CreateVersion7())
    {
        if (anio is < 1 or > 9998 || mes is < 1 or > 12 || valor <= 0 || decimal.Round(valor, 6) != valor || valor >= 1000000000000m)
            throw new BusinessRuleException("AF_INPC_INVALIDO", "Capture año, mes y un INPC positivo con hasta seis decimales.");
        EmpresaId = empresa; Anio = anio; Mes = mes; Valor = valor;
    }
}
