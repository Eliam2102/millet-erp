using FluentValidation;
using MediatR;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>
/// Baja lógica con motivo (5-500). Irreversible: una segunda baja es 422 <c>ADJUNTO_YA_DADO_DE_BAJA</c>.
/// El blob se conserva. La auditoría de la baja la escribe el interceptor (quién, cuándo, qué cambió).
/// </summary>
public sealed record DarDeBajaAdjuntoCommand(string TipoEntidad, Guid EntidadId, Guid AdjuntoId, string Motivo)
    : IRequest<AdjuntoResponse>;

public sealed class DarDeBajaAdjuntoValidator : AbstractValidator<DarDeBajaAdjuntoCommand>
{
    public DarDeBajaAdjuntoValidator()
    {
        RuleFor(x => x.TipoEntidad).NotEmpty().MaximumLength(60);
        RuleFor(x => x.EntidadId).NotEmpty();
        RuleFor(x => x.AdjuntoId).NotEmpty();
        RuleFor(x => x.Motivo).NotEmpty()
            .Must(m => m is not null && m.Trim().Length is >= Adjunto.MotivoBajaMin and <= Adjunto.MotivoBajaMax)
            .WithMessage($"El motivo debe tener entre {Adjunto.MotivoBajaMin} y {Adjunto.MotivoBajaMax} caracteres.");
    }
}

public sealed class DarDeBajaAdjuntoHandler : IRequestHandler<DarDeBajaAdjuntoCommand, AdjuntoResponse>
{
    private readonly AdjuntoAcceso _acceso;
    private readonly CompartidoDbContext _db;
    private readonly IClock _clock;

    public DarDeBajaAdjuntoHandler(AdjuntoAcceso acceso, CompartidoDbContext db, IClock clock)
    {
        _acceso = acceso;
        _db = db;
        _clock = clock;
    }

    public async Task<AdjuntoResponse> Handle(DarDeBajaAdjuntoCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = _acceso.UsuarioId;
        var (propietario, _) = await _acceso.AutorizarAsync(request.TipoEntidad, request.EntidadId, AdjuntoOperacion.Baja, cancellationToken);
        var adjunto = await AdjuntoSoporte.CargarAsync(_db, propietario.TipoEntidad, request.EntidadId, request.AdjuntoId, true, cancellationToken);

        // PLATFORM-TODO(<RetencionAdjuntos>): el blob se conserva siempre; falta política de retención/purga de Millet. Ver ADR-0058.
        adjunto.DarDeBaja(request.Motivo, usuarioId, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        var tipo = await AdjuntoSoporte.CargarTipoAsync(_db, adjunto.TipoDocumentoId, cancellationToken);
        return AdjuntoResponse.De(adjunto, tipo, AdjuntoSoporte.Hoy(_clock));
    }
}
