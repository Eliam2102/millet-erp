using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Application.Incidencias;
using Millet.SharedKernel.Application.Incidencias;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.LiberarRevision;

public sealed record LiberarRevisionFacturaCommand(
    Guid Id,
    int VersionEsperada,
    string AccionTomada) : IRequest<LiberarRevisionFacturaResponse>;

public sealed record LiberarRevisionFacturaResponse(
    Guid Id,
    EstadoPasivo Estado,
    int Version);

public sealed class LiberarRevisionFacturaValidator : AbstractValidator<LiberarRevisionFacturaCommand>
{
    public LiberarRevisionFacturaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
        RuleFor(c => c.AccionTomada).NotEmpty().MaximumLength(400);
    }
}

public sealed class LiberarRevisionFacturaHandler : IRequestHandler<LiberarRevisionFacturaCommand, LiberarRevisionFacturaResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;
    private readonly ICurrentUserPermissions _permisos;

    private readonly IIncidenciasPort? _incidencias;
    private readonly ILogger<LiberarRevisionFacturaHandler>? _logger;

    public LiberarRevisionFacturaHandler(CuentasPorPagarDbContext db, ICurrentUserContext currentUser, IClock clock, ICurrentUserPermissions permisos,
        IIncidenciasPort? incidencias = null, ILogger<LiberarRevisionFacturaHandler>? logger = null)
    {
        _db = db; _currentUser = currentUser; _clock = clock; _permisos = permisos;
        _incidencias = incidencias; _logger = logger;
    }

    public async Task<LiberarRevisionFacturaResponse> Handle(LiberarRevisionFacturaCommand command, CancellationToken cancellationToken)
    {
        var factura = await _db.FacturasProveedor
            .FirstOrDefaultAsync(f => f.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "FACTURA_NO_ENCONTRADA",
                $"No se encontró la factura con id '{command.Id}'.");

        if (factura.Version != command.VersionEsperada)
        {
            throw new ConcurrencyException(nameof(FacturaProveedor), factura.Id);
        }

        var puedeReemplazar = await _permisos.TieneAsync(
            Millet.Identidad.Domain.PermisosCanonicos.CuentasPorPagarBloqueosLiberarEnLugar, cancellationToken);
        factura.LiberarRevision(command.AccionTomada, _currentUser.UserId, _clock.UtcNow, puedeReemplazar);
        await _db.SaveChangesAsync(cancellationToken);

        // U-4 (ALM-costo-06): liberar la revisión retoma el paso; resuelve la incidencia de costo de la factura.
        if (_incidencias is not null)
            await IncidenciasCxp.SinRomperAsync(_logger, "resolver la incidencia de la revisión por costo", () =>
                _incidencias.ResolverPorDocumentoAsync(PasosIncidencia.AlmCostoNetoFactura, factura.Id, IncidenciasCxp.ClaveRevision,
                    $"Se liberó la revisión: {command.AccionTomada.Trim()}", cancellationToken));

        return new LiberarRevisionFacturaResponse(factura.Id, factura.Estado, factura.Version);
    }
}
