using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.RevisarRetenciones;

/// <summary>
/// Fiscal (<c>cuentas_por_pagar.retenciones.administrar</c>) confirma que revisó
/// la alerta de retenciones de una factura; sin esto no se puede autorizar para pago.
/// </summary>
public sealed record MarcarRetencionesRevisadasCommand(Guid Id, int VersionEsperada, string Motivo)
    : IRequest<MarcarRetencionesRevisadasResponse>;

public sealed record MarcarRetencionesRevisadasResponse(Guid Id, int Version);

public sealed class MarcarRetencionesRevisadasValidator : AbstractValidator<MarcarRetencionesRevisadasCommand>
{
    public MarcarRetencionesRevisadasValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Motivo).NotEmpty().Must(m => m.Trim().Length is >= 5 and <= 500)
            .WithMessage("El motivo debe tener entre 5 y 500 caracteres.");
    }
}

public sealed class MarcarRetencionesRevisadasHandler
    : IRequestHandler<MarcarRetencionesRevisadasCommand, MarcarRetencionesRevisadasResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;
    private readonly IAuditLogWriter _audit;
    private readonly ICurrentEmpresaContext _empresa;

    public MarcarRetencionesRevisadasHandler(
        CuentasPorPagarDbContext db, ICurrentUserContext currentUser, IClock clock,
        IAuditLogWriter audit, ICurrentEmpresaContext empresa)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _audit = audit; _empresa = empresa;
    }

    public async Task<MarcarRetencionesRevisadasResponse> Handle(
        MarcarRetencionesRevisadasCommand command, CancellationToken cancellationToken)
    {
        var factura = await _db.FacturasProveedor.FirstOrDefaultAsync(f => f.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("FACTURA_NO_ENCONTRADA", $"No se encontró la factura con id '{command.Id}'.");
        if (factura.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Domain.FacturaProveedor.FacturaProveedor), factura.Id);

        factura.MarcarRetencionesRevisadas(command.Motivo, _currentUser.UserId, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.RegistrarAsync(
            operacion: "factura.retenciones-revisadas",
            modulo: "CuentasPorPagar", entidad: "FacturaProveedor",
            entidadId: factura.Id, aggregateRootId: factura.Id,
            actorNombre: _currentUser.UserName ?? "Usuario", actorTipo: "usuario", actorEmail: _currentUser.Email,
            entidadEtiqueta: $"Factura {factura.FolioProveedor ?? factura.Id.ToString()}",
            resumen: "Fiscal revisó la alerta de retenciones.",
            usuarioId: _currentUser.UserId, empresaId: _empresa.Current,
            cambios: JsonSerializer.Serialize(new { motivo = command.Motivo.Trim() }),
            cancellationToken: cancellationToken);
        return new MarcarRetencionesRevisadasResponse(factura.Id, factura.Version);
    }
}
