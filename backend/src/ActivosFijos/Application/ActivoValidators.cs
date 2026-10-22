using FluentValidation;
using Millet.ActivosFijos.Domain;
namespace Millet.ActivosFijos.Application;
public sealed class DatosActivoValidator : AbstractValidator<DatosActivo>
{
    public DatosActivoValidator()
    {
        RuleFor(x=>x.Descripcion).NotEmpty().MaximumLength(500);RuleFor(x=>x.ClaseId).NotEmpty();
        RuleFor(x=>x.Moneda).NotEmpty().Matches("^[A-Z]{3}$");RuleFor(x=>x.TipoCambio).GreaterThan(0);
        RuleFor(x=>x.ValorMonedaOriginal).GreaterThanOrEqualTo(0);RuleFor(x=>x.Custodia).NotNull();
        RuleFor(x=>x.FechaAdquisicion).NotEmpty();RuleFor(x=>x.DocumentoSoporte).MaximumLength(500);
        RuleFor(x=>x.Pedimento).MaximumLength(100);RuleFor(x=>x.NumeroSerie).MaximumLength(100);
        RuleFor(x=>x.Marca).MaximumLength(100);RuleFor(x=>x.Modelo).MaximumLength(100);
    }
}
public sealed class CrearActivoValidator : AbstractValidator<CrearActivoCommand>
{
    public CrearActivoValidator() { RuleFor(x=>x.Datos).NotNull().SetValidator(new DatosActivoValidator());RuleFor(x=>x.Datos.DocumentoSoporte).NotEmpty().When(x=>x.Datos!=null); }
}
public sealed class MutarActivoValidator : AbstractValidator<MutarActivoCommand>
{
    public MutarActivoValidator()
    {
        RuleFor(x=>x.Id).NotEmpty();RuleFor(x=>x.Version).GreaterThan(0);
        RuleFor(x=>x.Datos!).SetValidator(new DatosActivoValidator()).When(x=>x.Datos!=null);
        RuleFor(x=>x.Ajuste!.Fecha).NotEmpty().When(x=>x.Ajuste!=null);
        RuleFor(x=>x.Ajuste!.Descripcion).NotEmpty().MaximumLength(500).When(x=>x.Ajuste!=null);
    }
}
public sealed class GuardarClaseValidator : AbstractValidator<GuardarClaseCommand>
{
    public GuardarClaseValidator() {RuleFor(x=>x.Datos).NotNull();RuleFor(x=>x.Datos.Clave).NotEmpty().MaximumLength(20).When(x=>x.Datos!=null);RuleFor(x=>x.Datos.Nombre).NotEmpty().MaximumLength(200).When(x=>x.Datos!=null);}
}
