using FluentValidation;
namespace Millet.ActivosFijos.Application;
public sealed class RegistrarDocumentoActivoValidator : AbstractValidator<RegistrarDocumentoActivo>
{
    public RegistrarDocumentoActivoValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Version).GreaterThan(0); RuleFor(x => x.Datos).NotNull(); RuleFor(x => x.Datos.Tipo).IsInEnum().When(x => x.Datos != null); }
}
public sealed class RegistrarMantenimientoActivoValidator : AbstractValidator<RegistrarMantenimientoActivo>
{
    public RegistrarMantenimientoActivoValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Version).GreaterThan(0); RuleFor(x => x.Datos).NotNull(); RuleFor(x => x.Datos.Tipo).IsInEnum().When(x => x.Datos != null); }
}
public sealed class AbrirConteoActivoValidator : AbstractValidator<AbrirConteoActivo>
{
    public AbrirConteoActivoValidator() { RuleFor(x => x.SucursalId).NotEmpty(); RuleFor(x => x.Ubicacion).MaximumLength(500); }
}
public sealed class MutarConteoActivoValidator : AbstractValidator<MutarConteoActivo>
{
    public MutarConteoActivoValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Version).GreaterThan(0); RuleFor(x => x.Captura).NotNull().When(x => x.Accion == "capturar"); }
}
