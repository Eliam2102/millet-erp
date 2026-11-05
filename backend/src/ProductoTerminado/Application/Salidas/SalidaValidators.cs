using FluentValidation;
using Millet.ProductoTerminado.Domain;

namespace Millet.ProductoTerminado.Application.Salidas;

public sealed class CrearHojaSalidaValidator : AbstractValidator<CrearHojaSalidaCommand>
{
    public CrearHojaSalidaValidator()
    {
        RuleFor(x => x.SucursalId).NotEmpty();
        RuleFor(x => x.Destino).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OrigenCodigoPostal).Matches(@"^\d{5}$").When(x => !string.IsNullOrEmpty(x.OrigenCodigoPostal)).WithMessage("El código postal de origen son 5 dígitos.");
        RuleFor(x => x.DestinoCodigoPostal).Matches(@"^\d{5}$").When(x => !string.IsNullOrEmpty(x.DestinoCodigoPostal)).WithMessage("El código postal de destino son 5 dígitos.");
        RuleFor(x => x.OrigenEstado).Length(3).When(x => !string.IsNullOrEmpty(x.OrigenEstado)).WithMessage("El estado de origen es la clave SAT de 3 letras (p. ej. YUC).");
        RuleFor(x => x.DestinoEstado).Length(3).When(x => !string.IsNullOrEmpty(x.DestinoEstado)).WithMessage("El estado de destino es la clave SAT de 3 letras (p. ej. ROO).");
        RuleFor(x => x.Pedidos).NotEmpty().Must(p => p.Count <= 50).WithMessage("Máximo 50 pedidos por hoja de salida.");
        RuleForEach(x => x.Pedidos).ChildRules(p =>
        {
            p.RuleFor(x => x.NumeroPedido).NotEmpty().MaximumLength(50);
            p.RuleFor(x => x.Etiquetas).NotEmpty().Must(e => e.Count <= 500).WithMessage("Máximo 500 piezas por pedido.");
            p.RuleForEach(x => x.Etiquetas).NotEmpty().MaximumLength(100);
        });
    }
}

public sealed class ListarHojasSalidaValidator : AbstractValidator<ListarHojasSalidaQuery>
{
    public ListarHojasSalidaValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Estado).Must(e => e is null || Enum.TryParse<EstadoSalida>(e, out _)).WithMessage("Estado inválido.");
    }
}
