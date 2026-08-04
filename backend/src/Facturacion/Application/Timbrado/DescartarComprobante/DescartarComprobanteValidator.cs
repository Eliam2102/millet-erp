using FluentValidation;

namespace Millet.Facturacion.Application.Timbrado.DescartarComprobante;

public sealed class DescartarComprobanteValidator : AbstractValidator<DescartarComprobanteCommand>
{
    public DescartarComprobanteValidator()
    {
        RuleFor(c => c.ComprobanteId).NotEqual(Guid.Empty);
    }
}
