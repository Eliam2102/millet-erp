using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Viaticos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Viaticos.Transiciones;

// ============================================================================
// F7-PR3: transiciones del ciclo de SolicitudViaticos.
// Solicitada → AutorizadaPorJefe → (RequiereDireccionFinanzas → AutorizadaCompleta) →
// Anticipada → ComprobacionCapturada → Liquidada + Rechazada terminal.
// ============================================================================

public sealed record TransicionViaticosResponse(Guid Id, EstadoSolicitudViaticos Estado, int Version);

/// <summary>
/// GI-PR1 (doc 12 §D3-ida): publica el pasivo interno del préstamo de
/// viáticos hacia Tesorería cuando la solicitud queda autorizada
/// (AutorizadaPorJefe si no excede política; AutorizadaCompleta si DF
/// firmó). Idempotencia del consumidor por OrigenId = solicitud.
/// </summary>
internal static class PrestamoViaticos
{
    public static Task PublicarPasivoAsync(
        IIntegrationEventPublisher events,
        SolicitudViaticos s,
        DateTimeOffset ahora,
        CancellationToken cancellationToken)
        => events.PublishAsync(new PasivoAutorizadoParaPagoIntegrationEvent(
            EmpresaId: s.EmpresaId,
            OcurridoEn: ahora,
            FacturaProveedorId: Guid.Empty,
            ProveedorId: Guid.Empty,
            OrdenCompraId: null,
            MontoTotal: s.MontoSolicitado,
            SaldoPendiente: s.MontoSolicitado,
            Moneda: s.Moneda,
            TipoCambio: null,
            FechaVencimiento: s.FechaSalida,
            UuidCfdi: null,
            FolioProveedor: null,
            MetodoPago: null,
            TipoBeneficiario: PasivoAutorizadoParaPagoIntegrationEvent.BeneficiarioEmpleado,
            BeneficiarioId: s.EmpleadoId,
            OrigenTipo: PasivoAutorizadoParaPagoIntegrationEvent.OrigenPrestamoViaticos,
            OrigenId: s.Id), cancellationToken);
}

// ------------------------------------------------------ AutorizarPorJefe

public sealed record AutorizarPorJefeViaticosCommand(Guid Id, int VersionEsperada)
    : IRequest<TransicionViaticosResponse>;

public sealed class AutorizarPorJefeViaticosValidator : AbstractValidator<AutorizarPorJefeViaticosCommand>
{
    public AutorizarPorJefeViaticosValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

/// <summary>
/// Firma N1 del jefe directo. Desde ADM-FE-PR1/#630 el
/// <c>JefeDirectoId</c> de la solicitud es un <b>Empleado del catálogo</b>
/// (no un usuario de Identidad), así que el usuario autenticado se
/// resuelve a su empleado vía <c>Empleado.UsuarioId</c> (correlación D5,
/// doc 10 de Administración) antes de comparar.
/// </summary>
public sealed class AutorizarPorJefeViaticosHandler
    : IRequestHandler<AutorizarPorJefeViaticosCommand, TransicionViaticosResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IEmpleadoReadPort _empleados;
    private readonly IIntegrationEventPublisher _events;
    private readonly IClock _clock;

    public AutorizarPorJefeViaticosHandler(
        CuentasPorPagarDbContext db,
        ICurrentUserContext currentUser,
        IEmpleadoReadPort empleados,
        IIntegrationEventPublisher events,
        IClock clock)
    {
        _db = db; _currentUser = currentUser; _empleados = empleados; _events = events; _clock = clock;
    }

    public async Task<TransicionViaticosResponse> Handle(
        AutorizarPorJefeViaticosCommand command, CancellationToken cancellationToken)
    {
        var s = await Cargar(command.Id, command.VersionEsperada, cancellationToken);
        var usuarioId = _currentUser.UserId
            ?? throw new ForbiddenException("USUARIO_NO_AUTENTICADO",
                "Se requiere usuario autenticado para autorizar.");

        var empleadoActual = await _empleados.ObtenerPorUsuarioAsync(usuarioId, cancellationToken)
            ?? throw new ForbiddenException(
                "VIA_USUARIO_SIN_EMPLEADO",
                "Tu usuario no está vinculado a ningún empleado activo del catálogo. " +
                "Pide al administrador asignar tu usuario en Administración → Empleados " +
                "(campo \"Usuario del sistema\") para poder firmar como jefe directo.");

        var ahora = _clock.UtcNow;
        s.AutorizarPorJefe(empleadoActual.Id, ahora);

        // GI-PR1 (doc 12 §D3-ida): dentro de política, la firma del jefe
        // completa la autorización — se emite el pasivo interno del
        // préstamo para la bandeja de Tesorería. Si excede (RequiereDF),
        // lo emite la firma de DF.
        if (s.Estado == EstadoSolicitudViaticos.AutorizadaPorJefe)
        {
            await PrestamoViaticos.PublicarPasivoAsync(_events, s, ahora, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new TransicionViaticosResponse(s.Id, s.Estado, s.Version);
    }

    private async Task<SolicitudViaticos> Cargar(Guid id, int versionEsperada, CancellationToken ct)
    {
        var s = await _db.SolicitudesViaticos.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("VIA_NO_ENCONTRADA",
                $"No se encontró la solicitud de viáticos '{id}'.");
        if (s.Version != versionEsperada)
            throw new ConcurrencyException(nameof(SolicitudViaticos), s.Id);
        return s;
    }
}

// ---------------------------------------------------- AutorizarPorDireccionFinanzas

public sealed record AutorizarPorDfViaticosCommand(Guid Id, int VersionEsperada)
    : IRequest<TransicionViaticosResponse>;

public sealed class AutorizarPorDfViaticosValidator : AbstractValidator<AutorizarPorDfViaticosCommand>
{
    public AutorizarPorDfViaticosValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

public sealed class AutorizarPorDfViaticosHandler
    : IRequestHandler<AutorizarPorDfViaticosCommand, TransicionViaticosResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IEmpleadoReadPort _empleados;
    private readonly IIntegrationEventPublisher _events;
    private readonly IClock _clock;

    public AutorizarPorDfViaticosHandler(
        CuentasPorPagarDbContext db,
        ICurrentUserContext currentUser,
        IEmpleadoReadPort empleados,
        IIntegrationEventPublisher events,
        IClock clock)
    {
        _db = db; _currentUser = currentUser; _empleados = empleados; _events = events; _clock = clock;
    }

    public async Task<TransicionViaticosResponse> Handle(
        AutorizarPorDfViaticosCommand command, CancellationToken cancellationToken)
    {
        var s = await _db.SolicitudesViaticos
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("VIA_NO_ENCONTRADA",
                $"No se encontró la solicitud '{command.Id}'.");
        if (s.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(SolicitudViaticos), s.Id);

        var dfId = _currentUser.UserId
            ?? throw new ForbiddenException("USUARIO_NO_AUTENTICADO",
                "Se requiere usuario autenticado para autorizar DF.");

        // Si el firmante DF está vinculado a un empleado, se registra su
        // empleado — así el candado "DF ≠ jefe que firmó N1" del agregado
        // compara ids del mismo catálogo. DF sin empleado vinculado firma
        // con su usuario (no requiere estar en el catálogo).
        var empleadoDf = await _empleados.ObtenerPorUsuarioAsync(dfId, cancellationToken);

        var ahora = _clock.UtcNow;
        s.AutorizarPorDireccionFinanzas(empleadoDf?.Id ?? dfId, ahora);

        // GI-PR1 (doc 12 §D3-ida): autorización completa — pasivo interno
        // del préstamo hacia Tesorería.
        await PrestamoViaticos.PublicarPasivoAsync(_events, s, ahora, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return new TransicionViaticosResponse(s.Id, s.Estado, s.Version);
    }
}

// ---------------------------------------------------- MarcarAnticipoPagado

public sealed record MarcarAnticipoPagadoViaticosCommand(Guid Id, int VersionEsperada)
    : IRequest<TransicionViaticosResponse>;

public sealed class MarcarAnticipoPagadoViaticosValidator
    : AbstractValidator<MarcarAnticipoPagadoViaticosCommand>
{
    public MarcarAnticipoPagadoViaticosValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

/// <summary>
/// Fallback MANUAL (Q2, doc 12): marca que el préstamo al empleado fue
/// pagado en efectivo/ventanilla sin pasar por Tesorería. La vía normal
/// es automática desde GI-PR3: Tesorería paga el pasivo interno y
/// <c>tesoreria.pago-prestamo-viaticos.aplicado.v1</c> transiciona la
/// solicitud a Anticipada (cerró PLATFORM-TODO
/// &lt;TesoreriaPagoViaticosEvent&gt;).
/// </summary>
public sealed class MarcarAnticipoPagadoViaticosHandler
    : IRequestHandler<MarcarAnticipoPagadoViaticosCommand, TransicionViaticosResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;

    public MarcarAnticipoPagadoViaticosHandler(CuentasPorPagarDbContext db, IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task<TransicionViaticosResponse> Handle(
        MarcarAnticipoPagadoViaticosCommand command, CancellationToken cancellationToken)
    {
        var s = await _db.SolicitudesViaticos
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("VIA_NO_ENCONTRADA",
                $"No se encontró la solicitud '{command.Id}'.");
        if (s.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(SolicitudViaticos), s.Id);

        s.MarcarAnticipoPagado(_clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return new TransicionViaticosResponse(s.Id, s.Estado, s.Version);
    }
}

// ---------------------------------------------------- RechazarSolicitud

public sealed record RechazarSolicitudViaticosCommand(Guid Id, int VersionEsperada, string Motivo)
    : IRequest<TransicionViaticosResponse>;

public sealed class RechazarSolicitudViaticosValidator
    : AbstractValidator<RechazarSolicitudViaticosCommand>
{
    public RechazarSolicitudViaticosValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(1000);
    }
}

public sealed class RechazarSolicitudViaticosHandler
    : IRequestHandler<RechazarSolicitudViaticosCommand, TransicionViaticosResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public RechazarSolicitudViaticosHandler(
        CuentasPorPagarDbContext db, ICurrentUserContext currentUser, IClock clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<TransicionViaticosResponse> Handle(
        RechazarSolicitudViaticosCommand command, CancellationToken cancellationToken)
    {
        var s = await _db.SolicitudesViaticos
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("VIA_NO_ENCONTRADA",
                $"No se encontró la solicitud '{command.Id}'.");
        if (s.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(SolicitudViaticos), s.Id);

        var usuarioId = _currentUser.UserId
            ?? throw new ForbiddenException("USUARIO_NO_AUTENTICADO",
                "Se requiere usuario autenticado para rechazar.");
        s.Rechazar(usuarioId, command.Motivo, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return new TransicionViaticosResponse(s.Id, s.Estado, s.Version);
    }
}
