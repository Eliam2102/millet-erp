using FluentValidation;
using Millet.ProductoTerminado.Domain;

namespace Millet.ProductoTerminado.Application.Traspasos;

public sealed class CrearTraspasoValidator : AbstractValidator<CrearTraspasoCommand>
{
    public CrearTraspasoValidator()
    {
        RuleFor(x => x.SucursalOrigenId).NotEmpty();
        RuleFor(x => x.SucursalDestinoId).NotEmpty().NotEqual(x => x.SucursalOrigenId).WithMessage("El origen y el destino deben ser distintos.");
        RuleFor(x => x.Etiquetas).NotEmpty().Must(e => e.Count <= 500).WithMessage("Máximo 500 piezas por traspaso.");
        RuleForEach(x => x.Etiquetas).NotEmpty().MaximumLength(100);
    }
}

public sealed class ListarTraspasosValidator : AbstractValidator<ListarTraspasosQuery>
{
    public ListarTraspasosValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Estado).Must(e => e is null || Enum.TryParse<EstadoTraspaso>(e, out _)).WithMessage("Estado inválido.");
    }
}
