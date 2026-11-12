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
        When(x => x.Transporte is not null, () =>
        {
            RuleFor(x => x.Transporte!.Destino).MaximumLength(200);
            RuleFor(x => x.Transporte!.OrigenCodigoPostal).MaximumLength(5);
            RuleFor(x => x.Transporte!.DestinoCodigoPostal).MaximumLength(5);
            RuleFor(x => x.Transporte!.OrigenEstado).MaximumLength(3);
            RuleFor(x => x.Transporte!.DestinoEstado).MaximumLength(3);
        });
    }
}

public sealed class ClasificarDiferenciasTraspasoValidator : AbstractValidator<ClasificarDiferenciasTraspasoCommand>
{
    public ClasificarDiferenciasTraspasoValidator()
    {
        RuleFor(x => x.Piezas).NotEmpty();
        RuleForEach(x => x.Piezas).ChildRules(p =>
        {
            p.RuleFor(x => x.PiezaId).NotEmpty();
            p.RuleFor(x => x.Clasificacion).Must(c => Enum.GetNames<ClasificacionDiferencia>().Contains(c)).WithMessage("Clasificación inválida.");
            p.RuleFor(x => x.Motivo).NotEmpty().WithMessage("Cada pieza con diferencia necesita su motivo.").MaximumLength(300);
            p.RuleFor(x => x.Evidencia).MaximumLength(500);
        });
    }
}

public sealed class AclararDiferenciaTraspasoValidator : AbstractValidator<AclararDiferenciaTraspasoCommand>
{
    public AclararDiferenciaTraspasoValidator()
    {
        RuleFor(x => x.PiezaId).NotEmpty();
        RuleFor(x => x.Resolucion).Must(r => r is nameof(ResolucionDiferencia.RegresadaAlOrigen) or nameof(ResolucionDiferencia.DadaPorPerdida))
            .WithMessage("La aclaración es RegresadaAlOrigen o DadaPorPerdida; la pieza que llega tarde se recibe.");
    }
}

public sealed class ListarTraspasosValidator : AbstractValidator<ListarTraspasosQuery>
{
    public ListarTraspasosValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Estado).Must(e => e is null || e.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(s => Enum.GetNames<EstadoTraspaso>().Contains(s))).WithMessage("Estado inválido.");
    }
}
