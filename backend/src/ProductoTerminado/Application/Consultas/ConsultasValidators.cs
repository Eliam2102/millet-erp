using FluentValidation;
using Millet.ProductoTerminado.Domain;

namespace Millet.ProductoTerminado.Application.Consultas;

public sealed class ListarPiezasValidator : AbstractValidator<ListarPiezasQuery>
{
    public ListarPiezasValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class ListarSalidasPendientesValidator : AbstractValidator<ListarSalidasPendientesQuery>
{
    public ListarSalidasPendientesValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Alerta).Must(a => a is null or "24h" or "48h").WithMessage("La alerta debe ser 24h o 48h.");
    }
}

public sealed class ListarEscaneosValidator : AbstractValidator<ListarEscaneosQuery>
{
    private static readonly string[] Abiertos = [nameof(ResultadoEscaneo.Pospuesto), nameof(ResultadoEscaneo.Excepcion), nameof(ResultadoEscaneo.Reposicion)];

    public ListarEscaneosValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Resultado).Must(r => r is null || Abiertos.Contains(r))
            .WithMessage("El resultado debe ser Pospuesto, Excepcion o Reposicion.");
    }
}
