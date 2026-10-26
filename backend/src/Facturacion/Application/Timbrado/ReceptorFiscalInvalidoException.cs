using Millet.Facturacion.Domain.Comprobantes;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Timbrado;

public sealed class ReceptorFiscalInvalidoException(
    IReadOnlyList<CampoFiscalInvalido> campos, Guid? clienteId)
    : BusinessRuleException("RECEPTOR_FISCAL_INVALIDO",
        "Corrige los datos fiscales del receptor: " + string.Join("; ", campos.Select(c => c.Motivo)))
{
    public IReadOnlyList<CampoFiscalInvalido> Campos { get; } = campos;
    public Guid? ClienteId { get; } = clienteId;
    public string? EnlaceCliente => ClienteId is { } id ? $"/admin/datos-maestros/clientes/{id}" : null;
}
