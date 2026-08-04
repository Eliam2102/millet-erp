using FluentValidation;

namespace Millet.Compras.Application.RegistrarRecepcion;

public sealed class RegistrarRecepcionValidator : AbstractValidator<RegistrarRecepcionCommand>
{
    public RegistrarRecepcionValidator()
    {
        RuleFor(c => c.RequisicionId).NotEmpty().WithErrorCode("REQUISICION_REQUERIDA");
        RuleFor(c => c.LineaRequisicionId).NotEmpty().WithErrorCode("LINEA_REQUERIDA");
        RuleFor(c => c.CantidadRecibida).GreaterThan(0m).WithErrorCode("RECEPCION_CANTIDAD_INVALIDA");
    }
}
