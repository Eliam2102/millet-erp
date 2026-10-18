using FluentValidation;

namespace Millet.Compras.Application.Umbrales;

public sealed class UpsertUmbralValidator : AbstractValidator<UpsertUmbralCommand>
{
    public UpsertUmbralValidator()
    {
        // Guid.Empty es el umbral general de empresa (respaldo cuando el depto no tiene fila).
        RuleFor(c => c.UmbralMonto).GreaterThanOrEqualTo(0m);
        RuleFor(c => c.UmbralMenorCuantia)
            .GreaterThanOrEqualTo(0m)
            .When(c => c.UmbralMenorCuantia.HasValue);
        RuleFor(c => c)
            .Must(c => c.UmbralMenorCuantia is null || c.UmbralMenorCuantia.Value < c.UmbralMonto)
            .WithName(c => nameof(c.UmbralMenorCuantia))
            .WithMessage("El umbral N1 (menor cuantía) debe ser menor que el umbral N2.");
        // Millet opera solo en MXN; el campo se conserva para el contrato.
        RuleFor(c => c.Moneda).Equal("MXN");
    }
}
