using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.ComprobacionGastos.Transiciones;

// ============================================================================
// F7-PR1: transiciones del ciclo de ComprobacionGastos
// (Borrador → Autorizada → Aplicada) + Rechazo opcional. Comparten archivo
// porque cada comando tiene 4-5 líneas de lógica relevante; aislar cada
// uno por archivo sería ruido (mismo criterio que F6-PR2 NotaCargo).
// ============================================================================

// ------------------------------------------------------------------- Autorizar

public sealed record AutorizarComprobacionGastosCommand(Guid Id, int VersionEsperada)
    : IRequest<TransicionComprobacionResponse>;

public sealed record TransicionComprobacionResponse(Guid Id, EstadoComprobacionGastos Estado, int Version);

public sealed class AutorizarComprobacionGastosValidator : AbstractValidator<AutorizarComprobacionGastosCommand>
{
    public AutorizarComprobacionGastosValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

public sealed class AutorizarComprobacionGastosHandler
    : IRequestHandler<AutorizarComprobacionGastosCommand, TransicionComprobacionResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public AutorizarComprobacionGastosHandler(
        CuentasPorPagarDbContext db, ICurrentUserContext currentUser, IClock clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<TransicionComprobacionResponse> Handle(
        AutorizarComprobacionGastosCommand command, CancellationToken cancellationToken)
    {
        var c = await _db.ComprobacionesGastos
            .Include(x => x.Lineas)
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "COMP_NO_ENCONTRADA",
                $"No se encontró la comprobación '{command.Id}'.");
        if (c.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Domain.ComprobacionGastos.ComprobacionGastos), c.Id);

        var usuarioId = _currentUser.UserId
            ?? throw new ForbiddenException("USUARIO_NO_AUTENTICADO",
                "Se requiere usuario autenticado para autorizar comprobaciones.");
        c.Autorizar(usuarioId, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return new TransicionComprobacionResponse(c.Id, c.Estado, c.Version);
    }
}

// ----------------------------------------------------------------- EnviarRev.

public sealed record EnviarComprobacionARevisionCommand(Guid Id, int VersionEsperada)
    : IRequest<TransicionComprobacionResponse>;

public sealed class EnviarComprobacionARevisionValidator : AbstractValidator<EnviarComprobacionARevisionCommand>
{
    public EnviarComprobacionARevisionValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

public sealed class EnviarComprobacionARevisionHandler
    : IRequestHandler<EnviarComprobacionARevisionCommand, TransicionComprobacionResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;

    public EnviarComprobacionARevisionHandler(CuentasPorPagarDbContext db, IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task<TransicionComprobacionResponse> Handle(
        EnviarComprobacionARevisionCommand command, CancellationToken cancellationToken)
    {
        var c = await _db.ComprobacionesGastos
            .Include(x => x.Lineas)
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "COMP_NO_ENCONTRADA",
                $"No se encontró la comprobación '{command.Id}'.");
        if (c.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Domain.ComprobacionGastos.ComprobacionGastos), c.Id);

        c.EnviarARevision(_clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return new TransicionComprobacionResponse(c.Id, c.Estado, c.Version);
    }
}

// --------------------------------------------------------------------- Aplicar

public sealed record AplicarComprobacionGastosCommand(Guid Id, int VersionEsperada)
    : IRequest<TransicionComprobacionResponse>;

public sealed class AplicarComprobacionGastosValidator : AbstractValidator<AplicarComprobacionGastosCommand>
{
    public AplicarComprobacionGastosValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

/// <summary>
/// Aplica la comprobación y — para caja chica — acumula su monto en el
/// saldo por reponer de la (sucursal, destino); si el saldo alcanza el
/// mínimo configurado, emite la reposición agregada con el pasivo
/// interno hacia Tesorería (doc 12 §D2/Q4, GI-PR1) en la misma TX.
/// </summary>
public sealed class AplicarComprobacionGastosHandler
    : IRequestHandler<AplicarComprobacionGastosCommand, TransicionComprobacionResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly Reposiciones.ReposicionCajaChicaEmisor _emisor;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public AplicarComprobacionGastosHandler(
        CuentasPorPagarDbContext db,
        Reposiciones.ReposicionCajaChicaEmisor emisor,
        ICurrentUserContext currentUser,
        IClock clock)
    {
        _db = db; _emisor = emisor; _currentUser = currentUser; _clock = clock;
    }

    public async Task<TransicionComprobacionResponse> Handle(
        AplicarComprobacionGastosCommand command, CancellationToken cancellationToken)
    {
        var c = await _db.ComprobacionesGastos
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "COMP_NO_ENCONTRADA",
                $"No se encontró la comprobación '{command.Id}'.");
        if (c.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Domain.ComprobacionGastos.ComprobacionGastos), c.Id);

        var usuarioId = _currentUser.UserId
            ?? throw new ForbiddenException("USUARIO_NO_AUTENTICADO",
                "Se requiere usuario autenticado para aplicar comprobaciones.");
        var ahora = _clock.UtcNow;
        c.Aplicar(usuarioId, ahora);

        if (c.Tipo == TipoComprobacionGastos.ReembolsoCajaChica
            && c.DestinoReposicion is DestinoReposicionCaja destino)
        {
            await _emisor.EmitirSiCorrespondeAsync(
                c.EmpresaId, c.SucursalId, destino,
                respetarMinimo: true, usuarioId, ahora, cancellationToken,
                incluirComprobacionId: c.Id);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new TransicionComprobacionResponse(c.Id, c.Estado, c.Version);
    }
}

// --------------------------------------------------------------------- Rechazar

public sealed record RechazarComprobacionGastosCommand(Guid Id, int VersionEsperada, string Motivo)
    : IRequest<TransicionComprobacionResponse>;

public sealed class RechazarComprobacionGastosValidator : AbstractValidator<RechazarComprobacionGastosCommand>
{
    public RechazarComprobacionGastosValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(1000);
    }
}

/// <summary>
/// Rechaza la comprobación y <b>compensa los recursos consumidos</b>
/// (P7-H1):
/// <list type="bullet">
///   <item><b>Caja chica</b>: cancela las <c>FacturaProveedor</c>
///   generadas al crear (quedaban Capturada, vivas y pagables) y regresa
///   los CFDIs vinculados a <c>PorProcesar</c> para poder re-capturarlos
///   en una comprobación corregida. Las líneas se conservan como
///   evidencia de lo que se rechazó.</item>
///   <item><b>Aduanales</b>: elimina las líneas de agrupación para
///   liberar las facturas (índice único <c>ux_linea_comp_factura</c>) —
///   las facturas pre-existen a la comprobación y siguen su ciclo
///   normal; sin esto quedaban ligadas para siempre
///   (<c>COMP_FACTURA_YA_LIGADA</c>).</item>
/// </list>
/// </summary>
public sealed class RechazarComprobacionGastosHandler
    : IRequestHandler<RechazarComprobacionGastosCommand, TransicionComprobacionResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public RechazarComprobacionGastosHandler(
        CuentasPorPagarDbContext db, ICurrentUserContext currentUser, IMediator mediator, IClock clock)
    {
        _db = db; _currentUser = currentUser; _mediator = mediator; _clock = clock;
    }

    public async Task<TransicionComprobacionResponse> Handle(
        RechazarComprobacionGastosCommand command, CancellationToken cancellationToken)
    {
        var c = await _db.ComprobacionesGastos
            .Include(x => x.Lineas)
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "COMP_NO_ENCONTRADA",
                $"No se encontró la comprobación '{command.Id}'.");
        if (c.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Domain.ComprobacionGastos.ComprobacionGastos), c.Id);

        var usuarioId = _currentUser.UserId
            ?? throw new ForbiddenException("USUARIO_NO_AUTENTICADO",
                "Se requiere usuario autenticado para rechazar comprobaciones.");
        var ahora = _clock.UtcNow;
        c.Rechazar(usuarioId, command.Motivo, ahora);

        if (c.Tipo == TipoComprobacionGastos.ReembolsoCajaChica)
        {
            await CompensarCajaChicaAsync(c, usuarioId, command.Motivo, ahora, cancellationToken);
        }
        else if (c.Tipo == TipoComprobacionGastos.GastosAduanales)
        {
            // Libera las facturas agrupadas (hard delete: el índice único
            // sobre FacturaProveedorId no distingue soft-deletes).
            _db.RemoveRange(c.Lineas);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new TransicionComprobacionResponse(c.Id, c.Estado, c.Version);
    }

    private async Task CompensarCajaChicaAsync(
        Domain.ComprobacionGastos.ComprobacionGastos c,
        Guid usuarioId,
        string motivo,
        DateTimeOffset ahora,
        CancellationToken cancellationToken)
    {
        var facturaIds = c.Lineas.Select(l => l.FacturaProveedorId).ToList();
        if (facturaIds.Count == 0) return;

        var facturas = await _db.FacturasProveedor
            .Where(f => facturaIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        var texto = $"Comprobación de caja chica {c.Id} rechazada: {motivo}";
        foreach (var f in facturas)
        {
            // Idempotencia/robustez: solo se compensan las que siguen en
            // captura; una ya cancelada a mano se deja como está.
            if (f.Estado != Domain.FacturaProveedor.EstadoPasivo.Capturada)
                continue;

            f.Cancelar(Domain.FacturaProveedor.MotivoCancelacion.OtroConTexto, texto, usuarioId, ahora);

            // Mismo evento que CancelarFacturaHandler (Contabilidad);
            // ANTES de SaveChanges por el outbox interceptor (ADR-0009).
            await _mediator.Publish(new FacturaProveedorCanceladaDomainEvent(
                EmpresaId: f.EmpresaId,
                FacturaProveedorId: f.Id,
                OrdenCompraId: f.OrdenCompraId,
                Motivo: Domain.FacturaProveedor.MotivoCancelacion.OtroConTexto,
                MotivoTexto: texto,
                OcurridoEn: ahora), cancellationToken);
        }

        var cfdiIds = c.Lineas
            .Where(l => l.CfdiRecibidoId is not null)
            .Select(l => l.CfdiRecibidoId!.Value)
            .Distinct()
            .ToList();
        if (cfdiIds.Count == 0) return;

        var cfdis = await _db.CfdisRecibidos
            .Where(x => cfdiIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        foreach (var linea in c.Lineas)
        {
            if (linea.CfdiRecibidoId is not Guid cfdiId) continue;
            var cfdi = cfdis.FirstOrDefault(x => x.Id == cfdiId);
            if (cfdi is null) continue;

            // Solo si sigue apuntando a la factura de ESTA comprobación —
            // si otro flujo lo tomó, no se toca.
            if (cfdi.Estado == Domain.Cfdi.EstadoCfdiRecibido.ConvertidoEnPasivo
                && cfdi.DocumentoDestinoId == linea.FacturaProveedorId)
            {
                cfdi.RevertirAPorProcesar(linea.FacturaProveedorId);
            }
        }
    }
}
