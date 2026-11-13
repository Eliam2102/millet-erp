using FluentValidation;

namespace Millet.ProductoTerminado.Application.Liquidacion;

public sealed class LiquidarRutaPtValidator : AbstractValidator<LiquidarRutaPtCommand>
{
    public LiquidarRutaPtValidator()
    {
        RuleFor(x => x.SalidaId).NotEmpty();
        RuleFor(x => x.Albaranes).NotEmpty().Must(a => a.Count <= 50).WithMessage("Máximo 50 albaranes por hoja.");
        RuleForEach(x => x.Albaranes).ChildRules(a =>
        {
            a.RuleFor(x => x.Efectivo).InclusiveBetween(0m, 99_999_999m);
            a.RuleFor(x => x.Voucher).InclusiveBetween(0m, 99_999_999m);
            a.RuleFor(x => x.VoucherReferencia).MaximumLength(100);
            a.RuleFor(x => x.TipoTarjeta).Must(t => t is null || t.Trim() is "Credito" or "Debito").WithMessage("El tipo de tarjeta es Credito o Debito.");
            a.RuleFor(x => x.TarjetaUltimos4).MaximumLength(4);
        });
        RuleFor(x => x.Imprevistos).Must(i => i.Count <= 20).WithMessage("Máximo 20 imprevistos por hoja.");
        RuleForEach(x => x.Imprevistos).ChildRules(i =>
        {
            i.RuleFor(x => x.Descripcion).NotEmpty().MaximumLength(300);
            i.RuleFor(x => x.Importe).InclusiveBetween(0m, 99_999_999m);
        });
    }
}

public sealed class ListarLiquidacionesValidator : AbstractValidator<ListarLiquidacionesQuery>
{
    public ListarLiquidacionesValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Estado).Must(e => e is null or "Pendiente" or "ConDiferencia" or "Cerrada").WithMessage("Estado inválido.");
    }
}
