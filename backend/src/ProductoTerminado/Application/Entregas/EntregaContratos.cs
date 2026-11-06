using FluentValidation;
using MediatR;

namespace Millet.ProductoTerminado.Application.Entregas;

/// <summary>Cobro contra entrega que hizo el chofer (voucher de TPV, efectivo...); se concilia cuando la caja liquida la ruta.</summary>
public sealed record CobroEntregaDto(decimal Monto, string FormaPago, string? Referencia, DateTimeOffset? ConciliadoAt = null);

/// <summary>
/// Confirmación de entrega de un albarán (PT5.6), desde el celular del chofer: recibe, firma o foto, fecha y hora (del servidor). <c>Rechazadas</c> son las etiquetas que el cliente no aceptó en sitio
/// (entrega parcial, o rechazo total si son todas): pasan a revisión para PT5.8. Firma y foto van como imagen en base64 (PNG la firma; JPEG o PNG la foto).
/// </summary>
public sealed record ConfirmarEntregaCommand(
    Guid HojaId, Guid AlbaranId, string Receptor, IReadOnlyList<string>? Rechazadas, string? MotivoRechazo, string? Firma, string? Foto, CobroEntregaDto? Cobro) : IRequest<Salidas.HojaSalidaDto>;

public sealed record EntregaBody(string Receptor, IReadOnlyList<string>? Rechazadas, string? MotivoRechazo, string? Firma, string? Foto, CobroEntregaDto? Cobro);

public sealed record EntregaDto(
    Guid Id, string Resultado, string Receptor, DateTimeOffset EntregadaAt, int Entregadas, int Rechazadas, string? MotivoRechazo, bool TieneFirma, bool TieneFoto,
    IReadOnlyList<string> EtiquetasRechazadas, CobroEntregaDto? Cobro);

/// <summary>La firma y la foto (data URL) solo se piden al abrir la evidencia de una entrega, no viajan con cada hoja.</summary>
public sealed record EntregaEvidenciaDto(string? Firma, string? Foto);

public sealed record ObtenerEvidenciaEntregaQuery(Guid HojaId, Guid AlbaranId) : IRequest<EntregaEvidenciaDto>;

/// <summary>Facturación timbró una factura del pedido: si ya está entregado, pasa a 115 (aviso a A+W).</summary>
public sealed record AplicarFacturaPedidoCommand(Guid EmpresaId, Guid PedidoFacturableId) : IRequest<bool>;

/// <summary>La caja registró un cobro (liquidación de ruta): si es el voucher que el chofer capturó en la entrega del mismo pedido, queda conciliado.</summary>
public sealed record ConciliarCobroEntregaCommand(Guid EmpresaId, Guid CobroMostradorId, Guid ComprobanteId, decimal Total) : IRequest<bool>;

public sealed class ConfirmarEntregaValidator : AbstractValidator<ConfirmarEntregaCommand>
{
    public static readonly string[] Formas = ["TPV", "Efectivo", "Transferencia", "Cheque"];

    public ConfirmarEntregaValidator()
    {
        RuleFor(x => x.HojaId).NotEmpty();
        RuleFor(x => x.AlbaranId).NotEmpty();
        RuleFor(x => x.Receptor).NotEmpty().MaximumLength(200);
        RuleFor(x => x.MotivoRechazo).MaximumLength(500);
        RuleFor(x => x.Rechazadas).Must(r => r is null || r.Count <= 500).WithMessage("Máximo 500 piezas.");
        RuleForEach(x => x.Rechazadas).NotEmpty().MaximumLength(100);
        When(x => x.Cobro is not null, () =>
        {
            RuleFor(x => x.Cobro!.Monto).GreaterThan(0);
            RuleFor(x => x.Cobro!.FormaPago).Must(f => Formas.Contains(f)).WithMessage("Forma de cobro inválida.");
            RuleFor(x => x.Cobro!.Referencia).MaximumLength(100);
        });
    }
}
