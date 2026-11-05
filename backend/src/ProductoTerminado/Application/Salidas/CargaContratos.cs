using FluentValidation;
using MediatR;

namespace Millet.ProductoTerminado.Application.Salidas;

// Carga física pieza por pieza (PT5.4) sobre una hoja en Borrador (la ruta armada con sus pedidos). Cada comando devuelve la hoja con su detalle.

/// <summary>Escaneo (o selección) de una pieza: pasa a Cargada y entra al albarán de su pedido. Se rechaza si la pieza no es de la ruta o si su pedido no está liberado en caja.</summary>
public sealed record CargarPiezaHojaCommand(Guid HojaId, string Etiqueta) : IRequest<HojaSalidaDto>;

/// <summary>Saca una pieza del camión: vuelve a EnPT y sale del albarán.</summary>
public sealed record DescargarPiezaHojaCommand(Guid HojaId, string Etiqueta) : IRequest<HojaSalidaDto>;

/// <summary>La pieza se rompió al cargar: no va en el albarán, queda pendiente de reposición y no se vuelve a cargar hasta que A+W la vuelva a terminar.</summary>
public sealed record MarcarPiezaRotaHojaCommand(Guid HojaId, string Etiqueta) : IRequest<HojaSalidaDto>;

public sealed record PiezaHojaBody(string Etiqueta);

public sealed class CargarPiezaHojaValidator : AbstractValidator<CargarPiezaHojaCommand>
{
    public CargarPiezaHojaValidator() => RuleFor(x => x.Etiqueta).NotEmpty().MaximumLength(100);
}

public sealed class DescargarPiezaHojaValidator : AbstractValidator<DescargarPiezaHojaCommand>
{
    public DescargarPiezaHojaValidator() => RuleFor(x => x.Etiqueta).NotEmpty().MaximumLength(100);
}

public sealed class MarcarPiezaRotaHojaValidator : AbstractValidator<MarcarPiezaRotaHojaCommand>
{
    public MarcarPiezaRotaHojaValidator() => RuleFor(x => x.Etiqueta).NotEmpty().MaximumLength(100);
}
