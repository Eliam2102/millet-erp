using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Cobranza;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.Cobranza;

// M-241 (CXC-GESTION): ficha de cobranza por factura vencida con responsable, siguiente revisión, escalamiento y corrección con rastro.

public sealed record FichaCobranzaEventoResponse(
    Guid Id, short Tipo, string TipoNombre, DateTimeOffset Fecha, string? UsuarioNombre, string Nota,
    short? Canal, short? Resultado, decimal? MontoComprometido, DateOnly? FechaComprometida,
    DateOnly? RevisionAntes, DateOnly? RevisionDespues, string? ResponsableAntes, string? ResponsableDespues,
    Guid? CorrigeEventoId, string? NotaAnterior, string? MotivoCorreccion)
{
    public static FichaCobranzaEventoResponse De(FichaCobranzaEvento e) => new(
        e.Id, (short)e.Tipo, e.Tipo.ToString(), e.Fecha, e.UsuarioNombre, e.Nota, (short?)e.Canal, (short?)e.Resultado,
        e.MontoComprometido, e.FechaComprometida, e.RevisionAntes, e.RevisionDespues,
        e.ResponsableAntesNombre, e.ResponsableDespuesNombre, e.CorrigeEventoId, e.NotaAnterior, e.MotivoCorreccion);
}

public sealed record FichaCobranzaResponse(
    Guid Id, Guid FacturaCarteraId, Guid ClienteId, Guid? ResponsableId, string? ResponsableNombre,
    DateOnly? SiguienteRevision, bool Escalada, FichaCobranzaEventoResponse? UltimoEvento)
{
    public static FichaCobranzaResponse De(FichaCobranza f, FichaCobranzaEvento? ultimo) => new(
        f.Id, f.FacturaCarteraId, f.ClienteId, f.ResponsableId, f.ResponsableNombre, f.SiguienteRevision, f.Escalada,
        ultimo is null ? null : FichaCobranzaEventoResponse.De(ultimo));
}

internal static class FichasCobranzaComun
{
    public static Guid ExigirUsuario(ICurrentUserContext usuario, string accion) =>
        usuario.UserId ?? throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", $"No se pudo identificar a quien {accion}.");

    public static async Task<FacturaCartera> FacturaGestionableAsync(CuentasPorCobrarDbContext db, Guid facturaId, CancellationToken ct)
    {
        var factura = await db.FacturasCartera.AsNoTracking().FirstOrDefaultAsync(f => f.Id == facturaId, ct)
            ?? throw new EntityNotFoundException("FC_NO_ENCONTRADA", "No se encontró la factura en cartera.");
        if (factura.ClienteId is null)
            throw new BusinessRuleException("FCO_FACTURA_SIN_CLIENTE", "La factura no tiene cliente asociado.");
        if (factura.Estado is not (EstadoFacturaCartera.Abierta or EstadoFacturaCartera.Parcial))
            throw new BusinessRuleException("FCO_FACTURA_SIN_SALDO", "Solo se gestionan facturas con saldo pendiente.");
        return factura;
    }

    public static Task<FichaCobranza?> FichaDeAsync(CuentasPorCobrarDbContext db, Guid facturaId, CancellationToken ct) =>
        db.FichasCobranza.FirstOrDefaultAsync(f => f.FacturaCarteraId == facturaId, ct);
}

// ── Asignar responsable (abre la ficha si no existe) ────────────────────────

public sealed record AsignarResponsableFichaCommand(
    Guid FacturaCarteraId, Guid ResponsableId, string? ResponsableNombre, DateOnly? SiguienteRevision, string? Motivo)
    : IRequest<FichaCobranzaResponse>;

public sealed class AsignarResponsableFichaValidator : AbstractValidator<AsignarResponsableFichaCommand>
{
    public AsignarResponsableFichaValidator()
    {
        RuleFor(c => c.FacturaCarteraId).NotEmpty();
        RuleFor(c => c.ResponsableId).NotEmpty();
        RuleFor(c => c.ResponsableNombre).MaximumLength(200);
        RuleFor(c => c.Motivo).MaximumLength(400);
    }
}

public sealed class AsignarResponsableFichaHandler(CuentasPorCobrarDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<AsignarResponsableFichaCommand, FichaCobranzaResponse>
{
    public async Task<FichaCobranzaResponse> Handle(AsignarResponsableFichaCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = FichasCobranzaComun.ExigirUsuario(usuario, "asigna el responsable");
        var factura = await FichasCobranzaComun.FacturaGestionableAsync(db, request.FacturaCarteraId, cancellationToken);
        var ficha = await FichasCobranzaComun.FichaDeAsync(db, factura.Id, cancellationToken);
        FichaCobranzaEvento evento;
        if (ficha is null)
        {
            (ficha, evento) = FichaCobranza.Abrir(factura.EmpresaId, factura.Id, factura.ClienteId!.Value, request.ResponsableId,
                request.ResponsableNombre, request.SiguienteRevision, usuarioId, usuario.UserName, clock.UtcNow);
            db.FichasCobranza.Add(ficha);
        }
        else
        {
            evento = ficha.Reasignar(request.ResponsableId, request.ResponsableNombre, request.Motivo ?? "", usuarioId, usuario.UserName, clock.UtcNow);
        }
        db.FichaCobranzaEventos.Add(evento);
        await db.SaveChangesAsync(cancellationToken);
        return FichaCobranzaResponse.De(ficha, evento);
    }
}

// ── Registrar gestión / promesa ─────────────────────────────────────────────

public sealed record RegistrarGestionFichaCommand(
    Guid FacturaCarteraId, CanalCobranza Canal, ResultadoCobranza Resultado, string Nota,
    decimal? MontoComprometido, DateOnly? FechaComprometida, DateOnly? SiguienteRevision)
    : IRequest<FichaCobranzaResponse>;

public sealed class RegistrarGestionFichaValidator : AbstractValidator<RegistrarGestionFichaCommand>
{
    public RegistrarGestionFichaValidator()
    {
        RuleFor(c => request.FacturaCarteraId).NotEmpty();
        RuleFor(c => request.Canal).IsInEnum();
        RuleFor(c => request.Resultado).IsInEnum();
        RuleFor(c => request.Nota).NotEmpty().MaximumLength(2000);
    }
}

/// <summary>Si la factura aún no tiene ficha, la abre con quien gestiona como responsable (supuesto a validar).</summary>
public sealed class RegistrarGestionFichaHandler(CuentasPorCobrarDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<RegistrarGestionFichaCommand, FichaCobranzaResponse>
{
    public async Task<FichaCobranzaResponse> Handle(RegistrarGestionFichaCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = FichasCobranzaComun.ExigirUsuario(usuario, "registra la gestión");
        var factura = await FichasCobranzaComun.FacturaGestionableAsync(db, request.FacturaCarteraId, cancellationToken);
        var ficha = await FichasCobranzaComun.FichaDeAsync(db, factura.Id, cancellationToken);
        if (ficha is null)
        {
            var (nueva, apertura) = FichaCobranza.Abrir(factura.EmpresaId, factura.Id, factura.ClienteId!.Value, usuarioId,
                usuario.UserName, null, usuarioId, usuario.UserName, clock.UtcNow);
            ficha = nueva;
            db.FichasCobranza.Add(ficha);
            db.FichaCobranzaEventos.Add(apertura);
        }
        var evento = ficha.RegistrarGestion(request.Canal, request.Resultado, request.Nota, request.MontoComprometido, request.FechaComprometida,
            request.SiguienteRevision, usuarioId, usuario.UserName, clock.UtcNow);
        db.FichaCobranzaEventos.Add(evento);
        await db.SaveChangesAsync(cancellationToken);
        return FichaCobranzaResponse.De(ficha, evento);
    }
}

// ── Reprogramar ─────────────────────────────────────────────────────────────

public sealed record ReprogramarFichaCommand(Guid FacturaCarteraId, DateOnly SiguienteRevision, string Motivo) : IRequest<FichaCobranzaResponse>;

public sealed class ReprogramarFichaValidator : AbstractValidator<ReprogramarFichaCommand>
{
    public ReprogramarFichaValidator()
    {
        RuleFor(c => request.FacturaCarteraId).NotEmpty();
        RuleFor(c => request.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class ReprogramarFichaHandler(CuentasPorCobrarDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<ReprogramarFichaCommand, FichaCobranzaResponse>
{
    public async Task<FichaCobranzaResponse> Handle(ReprogramarFichaCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = FichasCobranzaComun.ExigirUsuario(usuario, "reprograma la revisión");
        var ficha = await FichasCobranzaComun.FichaDeAsync(db, request.FacturaCarteraId, cancellationToken)
            ?? throw new EntityNotFoundException("FCO_NO_ENCONTRADA", "La factura aún no tiene ficha de cobranza.");
        var evento = ficha.Reprogramar(request.SiguienteRevision, request.Motivo, usuarioId, usuario.UserName, clock.UtcNow);
        db.FichaCobranzaEventos.Add(evento);
        await db.SaveChangesAsync(cancellationToken);
        return FichaCobranzaResponse.De(ficha, evento);
    }
}

// ── Escalar a supervisor ────────────────────────────────────────────────────

public sealed record EscalarFichaCommand(Guid FacturaCarteraId, Guid SupervisorId, string? SupervisorNombre, string Motivo)
    : IRequest<FichaCobranzaResponse>;

public sealed class EscalarFichaValidator : AbstractValidator<EscalarFichaCommand>
{
    public EscalarFichaValidator()
    {
        RuleFor(c => request.FacturaCarteraId).NotEmpty();
        RuleFor(c => request.SupervisorId).NotEmpty();
        RuleFor(c => request.SupervisorNombre).MaximumLength(200);
        RuleFor(c => request.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class EscalarFichaHandler(CuentasPorCobrarDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<EscalarFichaCommand, FichaCobranzaResponse>
{
    public async Task<FichaCobranzaResponse> Handle(EscalarFichaCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = FichasCobranzaComun.ExigirUsuario(usuario, "escala la ficha");
        var ficha = await FichasCobranzaComun.FichaDeAsync(db, request.FacturaCarteraId, cancellationToken)
            ?? throw new EntityNotFoundException("FCO_NO_ENCONTRADA", "La factura aún no tiene ficha de cobranza.");
        var evento = ficha.Escalar(request.SupervisorId, request.SupervisorNombre, request.Motivo, usuarioId, usuario.UserName, clock.UtcNow);
        db.FichaCobranzaEventos.Add(evento);
        await db.SaveChangesAsync(cancellationToken);
        return FichaCobranzaResponse.De(ficha, evento);
    }
}

// ── Corregir un evento (rastro; el original no se edita) ────────────────────

public sealed record CorregirEventoFichaCommand(Guid EventoId, string NotaCorregida, string Motivo) : IRequest<FichaCobranzaResponse>;

public sealed class CorregirEventoFichaValidator : AbstractValidator<CorregirEventoFichaCommand>
{
    public CorregirEventoFichaValidator()
    {
        RuleFor(c => request.EventoId).NotEmpty();
        RuleFor(c => request.NotaCorregida).NotEmpty().MaximumLength(2000);
        RuleFor(c => request.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class CorregirEventoFichaHandler(CuentasPorCobrarDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<CorregirEventoFichaCommand, FichaCobranzaResponse>
{
    public async Task<FichaCobranzaResponse> Handle(CorregirEventoFichaCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = FichasCobranzaComun.ExigirUsuario(usuario, "corrige la gestión");
        var original = await db.FichaCobranzaEventos.FirstOrDefaultAsync(e => e.Id == request.EventoId, cancellationToken)
            ?? throw new EntityNotFoundException("FCO_EVENTO_NO_ENCONTRADO", "No se encontró el evento de la ficha.");
        var ficha = await db.FichasCobranza.FirstAsync(f => f.Id == original.FichaCobranzaId, cancellationToken);
        var evento = ficha.Corregir(original, request.NotaCorregida, request.Motivo, usuarioId, usuario.UserName, clock.UtcNow);
        db.FichaCobranzaEventos.Add(evento);
        await db.SaveChangesAsync(cancellationToken);
        return FichaCobranzaResponse.De(ficha, evento);
    }
}

// ── Consultas ───────────────────────────────────────────────────────────────

public sealed record FichaFacturaVencidaDto(
    Guid FacturaCarteraId, Guid ClienteId, string ClienteNombre, string Folio, string Moneda, decimal Saldo,
    DateTimeOffset FechaVencimiento, int DiasVencida, Guid? FichaId, Guid? ResponsableId, string? ResponsableNombre,
    DateOnly? SiguienteRevision, bool Escalada, bool RevisionVencida);

/// <summary>Facturas vencidas con su ficha (o sin ella, = sin responsable). Alcance por sucursal de la factura (ADR-0051).</summary>
public sealed record FichasCobranzaQuery(Guid? ClienteId = null, Guid? ResponsableId = null, bool SoloSinResponsable = false)
    : IRequest<IReadOnlyList<FichaFacturaVencidaDto>>, IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_cobrar.cartera.leer-todas-sucursales";
    public string TipoDocumento => "factura_cartera";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class FichasCobranzaHandler(CuentasPorCobrarDbContext db, IClock clock)
    : IRequestHandler<FichasCobranzaQuery, IReadOnlyList<FichaFacturaVencidaDto>>
{
    public async Task<IReadOnlyList<FichaFacturaVencidaDto>> Handle(FichasCobranzaQuery request, CancellationToken cancellationToken)
    {
        var ahora = clock.UtcNow;
        var hoy = DateOnly.FromDateTime(ahora.UtcDateTime);
        var permitidos = request.DocumentosPermitidos;
        var facturas = db.FacturasCartera.AsNoTracking()
            .Where(f => permitidos == null || permitidos.Contains(f.Id))
            .Where(f => (f.Estado == EstadoFacturaCartera.Abierta || f.Estado == EstadoFacturaCartera.Parcial)
                        && f.FechaVencimiento < ahora && f.ClienteId != null);
        if (request.ClienteId is Guid cli) facturas = facturas.Where(f => f.ClienteId == cli);

        var filas = await (from f in facturas
                           join fi in db.FichasCobranza.AsNoTracking() on f.Id equals fi.FacturaCarteraId into g
                           from fi in g.DefaultIfEmpty()
                           where (request.ResponsableId == null || (fi != null && fi.ResponsableId == request.ResponsableId))
                              && (!request.SoloSinResponsable || fi == null || fi.ResponsableId == null)
                           orderby f.FechaVencimiento
                           select new { f, fi }).Take(500).ToListAsync(cancellationToken);
        return filas.Select(x => new FichaFacturaVencidaDto(
            x.f.Id, x.f.ClienteId!.Value, x.f.ReceptorNombre, x.f.Folio, x.f.Moneda,
            x.f.Total - x.f.MontoPagado - x.f.MontoNc, x.f.FechaVencimiento, (int)(ahora - x.f.FechaVencimiento).TotalDays,
            x.fi?.Id, x.fi?.ResponsableId, x.fi?.ResponsableNombre, x.fi?.SiguienteRevision, x.fi?.Escalada ?? false,
            x.fi?.SiguienteRevision is DateOnly r && r < hoy)).ToList();
    }
}

public sealed record FichaCobranzaDetalleQuery(Guid FacturaCarteraId)
    : IRequest<IReadOnlyList<FichaCobranzaEventoResponse>>, IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_cobrar.cartera.leer-todas-sucursales";
    public string TipoDocumento => "factura_cartera";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class FichaCobranzaDetalleHandler(CuentasPorCobrarDbContext db)
    : IRequestHandler<FichaCobranzaDetalleQuery, IReadOnlyList<FichaCobranzaEventoResponse>>
{
    public async Task<IReadOnlyList<FichaCobranzaEventoResponse>> Handle(FichaCobranzaDetalleQuery request, CancellationToken cancellationToken)
    {
        if (request.DocumentosPermitidos is { } p && !p.Contains(request.FacturaCarteraId)) return [];
        var eventos = await (from e in db.FichaCobranzaEventos.AsNoTracking()
                             join f in db.FichasCobranza.AsNoTracking() on e.FichaCobranzaId equals f.Id
                             where f.FacturaCarteraId == request.FacturaCarteraId
                             orderby e.Fecha descending
                             select e).ToListAsync(cancellationToken);
        return eventos.Select(FichaCobranzaEventoResponse.De).ToList();
    }
}
