using FluentValidation;

namespace Millet.Compras.Application.Oc.ActualizarReferenciaProveedor;

public sealed class ActualizarReferenciaProveedorValidator
    : AbstractValidator<ActualizarReferenciaProveedorCommand>
{
    public ActualizarReferenciaProveedorValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");
        RuleFor(c => c.ReferenciaProveedor)
            .MaximumLength(60).WithErrorCode("REFERENCIA_DEMASIADO_LARGA")
            .When(c => c.ReferenciaProveedor is not null);
    }
}
