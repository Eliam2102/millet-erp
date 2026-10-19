using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.RespuestaProveedor;

public sealed record CompromisoEntregaInput(Guid LineaOrdenCompraId, decimal Cantidad, DateOnly FechaPrometida);

public sealed record RegistrarRespuestaProveedorCommand(Guid OrdenCompraId, TipoRespuestaProveedorOc Tipo,
    DateOnly FechaRespuesta, string Evidencia, string? ContactoProveedor, string? Motivo,
    IReadOnlyList<CompromisoEntregaInput> Compromisos) : IRequest<RespuestaProveedorOcResponse>;

public sealed class RegistrarRespuestaProveedorValidator : AbstractValidator<RegistrarRespuestaProveedorCommand>
{
    public RegistrarRespuestaProveedorValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty();
        RuleFor(c => c.Tipo).IsInEnum();
        RuleFor(c => c.FechaRespuesta).NotEmpty();
        RuleFor(c => c.Evidencia).NotEmpty()
            .WithMessage("Indica la evidencia de la respuesta (correo, folio o documento del proveedor).")
            .MaximumLength(300);
        RuleFor(c => c.ContactoProveedor).MaximumLength(120);
        RuleFor(c => c.Motivo).MaximumLength(500);
        RuleFor(c => c.Compromisos).NotNull().Must(c => c is null || c.Count <= 200)
            .WithMessage("Una respuesta admite máximo 200 fechas prometidas.");

        When(c => c.Tipo == TipoRespuestaProveedorOc.Rechazo, () =>
        {
            RuleFor(c => c.Motivo).NotEmpty().WithMessage("Indica el motivo del rechazo del proveedor.");
            RuleFor(c => c.Compromisos).Empty().WithMessage("Un rechazo no lleva fechas prometidas.");
        });

        When(c => c.Tipo == TipoRespuestaProveedorOc.Confirmacion, () =>
        {
            RuleFor(c => c.Compromisos).NotEmpty().WithMessage("Captura al menos una fecha prometida.");
        });

        RuleForEach(c => c.Compromisos).ChildRules(x =>
        {
            x.RuleFor(i => i.LineaOrdenCompraId).NotEmpty();
            x.RuleFor(i => i.Cantidad).GreaterThan(0).WithMessage("La cantidad prometida debe ser mayor a cero.");
            x.RuleFor(i => i.FechaPrometida).NotEmpty();
        });
    }
}

public sealed class RegistrarRespuestaProveedorHandler(ComprasDbContext db, ICurrentUserContext currentUser,
    IClock clock) : IRequestHandler<RegistrarRespuestaProveedorCommand, RespuestaProveedorOcResponse>
{
    public async Task<RespuestaProveedorOcResponse> Handle(RegistrarRespuestaProveedorCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        // Las respuestas se incluyen: el dominio numera con su conteo.
        var oc = await db.OrdenesCompra.Include(o => o.Lineas).Include(o => o.RespuestasProveedor)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", "No se encontró la orden de compra.");
        var ahora = clock.UtcNow;
        var hoy = FechaContable.HoyLocal(ahora);
        var respuesta = oc.RegistrarRespuestaProveedor(command.Tipo, command.FechaRespuesta, hoy, command.Evidencia,
            command.ContactoProveedor, command.Motivo,
            command.Compromisos.Select(c => new CompromisoEntregaPropuesto(c.LineaOrdenCompraId, c.Cantidad, c.FechaPrometida)).ToList(),
            userId, ahora);
        await db.SaveChangesAsync(cancellationToken);
        return RespuestaProveedorMapper.ToResponse(respuesta, vigente: true);
    }
}
