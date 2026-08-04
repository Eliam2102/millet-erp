using FluentValidation;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Integraciones.Fiscal.Application.CatalogosSat;

public sealed class BuscarCatalogoSatValidator : AbstractValidator<BuscarCatalogoSatQuery>
{
    public BuscarCatalogoSatValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, 50);

        RuleFor(q => q.Buscar)
            .MaximumLength(100);

        // ObjetoImp es el único catálogo que se puede pedir sin texto
        // (8 entradas); los demás requieren al menos 1 carácter — con
        // 1–3 el handler hace lookup exacto por código.
        RuleFor(q => q.Buscar)
            .NotEmpty()
            .When(q => q.Catalogo != CatalogoSat.ObjetoImp)
            .WithMessage("El texto de búsqueda es requerido.");
    }
}
