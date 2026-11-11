using FluentValidation;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.Autorizaciones;

// ============================================================================
// CXC-PR4: CRUD acotado de AutorizacionCredito (override consumible, calco
// de AutorizacionAperturaCaja). El supervisor es SIEMPRE el usuario actual
// (permiso liberacion.override en el endpoint) — nunca se acepta por body.
// ============================================================================

public sealed record AutorizacionCreditoResponse(
    Guid Id,
    Guid SupervisorUsuarioId,
    Guid BeneficiarioUsuarioId,
    string Motivo,
    string ClienteOPedidoRef,
    DateTimeOffset FechaAutorizacion,
    DateTimeOffset VigenteHasta,
    EstadoAutorizacionCredito Estado,
    Guid? DecisionLiberacionId,
    int Version,
    CondicionExcepcionCredito? Condicion = null,
    decimal? MontoAutorizado = null,
    string Moneda = "MXN",
    int FirmasRequeridas = 1,
    bool AutorizadaPorDireccion = false,
    Guid? SegundaFirmaPorId = null,
    DateTimeOffset? SegundaFirmaEn = null);

/// <summary>M-239: vigencia de las excepciones de crédito; antes eran 24 h fijas.</summary>
public sealed class ExcepcionCreditoOptions
{
    public const string SectionName = "CuentasPorCobrar:ExcepcionCredito";

    /// <summary>Vigencia que se propone al solicitar una excepción.</summary>
    public int VigenciaPorOmisionHoras { get; set; } = 24;

    /// <summary>Vigencia máxima que se acepta al solicitar.</summary>
    public int VigenciaMaximaHoras { get; set; } = 24;
}

internal static class AutorizacionCreditoMapper
{
    public static AutorizacionCreditoResponse ToResponse(AutorizacionCredito a) =>
        new(a.Id, a.SupervisorUsuarioId, a.BeneficiarioUsuarioId, a.Motivo,
            a.ClienteOPedidoRef, a.FechaAutorizacion, a.VigenteHasta,
            a.Estado, a.DecisionLiberacionId, a.Version, a.Condicion, a.MontoAutorizado, a.Moneda,
            a.FirmasRequeridas, a.AutorizadaPorDireccion, a.SegundaFirmaPorId, a.SegundaFirmaEn);
}

// --------------------------------------------------- Crear

/// <param name="Condicion">M-239: condición excepcional que se autoriza.</param>
/// <param name="MontoAutorizado">M-239: monto que cubre la excepción; limita lo que se libera y define las firmas.</param>
/// <param name="VigenciaHoras">M-239: null = la vigencia por omisión de la configuración.</param>
public sealed record CrearAutorizacionCreditoCommand(
    Guid BeneficiarioUsuarioId,
    string Motivo,
    string ClienteOPedidoRef,
    CondicionExcepcionCredito Condicion,
    decimal MontoAutorizado,
    string Moneda = "MXN",
    int? VigenciaHoras = null) : IRequest<AutorizacionCreditoResponse>;

public sealed class CrearAutorizacionCreditoValidator : AbstractValidator<CrearAutorizacionCreditoCommand>
{
    public CrearAutorizacionCreditoValidator()
    {
        RuleFor(c => c.BeneficiarioUsuarioId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(254);
        RuleFor(c => c.ClienteOPedidoRef).NotEmpty().MaximumLength(80);
        RuleFor(c => c.Condicion).IsInEnum();
        RuleFor(c => c.MontoAutorizado).GreaterThan(0);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.VigenciaHoras).GreaterThanOrEqualTo(1).When(c => c.VigenciaHoras is not null);
    }
}

public sealed class CrearAutorizacionCreditoHandler
    : IRequestHandler<CrearAutorizacionCreditoCommand, AutorizacionCreditoResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;
    private readonly IBeneficiarioCreditoReadPort _beneficiarios;
    private readonly ICurrentUserPermissions? _permisos;
    private readonly ExcepcionCreditoOptions _opciones;

    public CrearAutorizacionCreditoHandler(
        CuentasPorCobrarDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IClock clock, IBeneficiarioCreditoReadPort beneficiarios,
        ICurrentUserPermissions? permisos = null,
        Microsoft.Extensions.Options.IOptions<ExcepcionCreditoOptions>? opciones = null)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser; _clock = clock; _beneficiarios = beneficiarios;
        _permisos = permisos;
        _opciones = opciones?.Value ?? new ExcepcionCreditoOptions();
    }

    public async Task<AutorizacionCreditoResponse> Handle(
        CrearAutorizacionCreditoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid supervisorId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al supervisor.");

        if (!await _beneficiarios.PuedeDecidirAsync(command.BeneficiarioUsuarioId, empresaId, cancellationToken))
            throw new BusinessRuleException("AC_BENEFICIARIO_SIN_PERMISO", "El beneficiario debe tener permiso para decidir liberaciones en Crédito y Cobranza.");

        // M-239: las firmas salen de la tabla por monto; la vigencia, de configuración (ya no 24 h fijas).
        var reglas = await _db.ReglasFirmaExcepcionCredito.AsNoTracking().ToListAsync(cancellationToken);
        var firmas = ReglaFirmaExcepcionCredito.FirmasPara(reglas, command.Moneda, command.MontoAutorizado);
        var vigenciaHoras = command.VigenciaHoras ?? _opciones.VigenciaPorOmisionHoras;

        var autorizacion = AutorizacionCredito.CrearExcepcional(
            empresaId: empresaId,
            supervisorUsuarioId: supervisorId,
            beneficiarioUsuarioId: command.BeneficiarioUsuarioId,
            motivo: command.Motivo,
            clienteOPedidoRef: command.ClienteOPedidoRef,
            ahora: _clock.UtcNow,
            vigencia: TimeSpan.FromHours(vigenciaHoras),
            vigenciaMaxima: TimeSpan.FromHours(_opciones.VigenciaMaximaHoras),
            condicion: command.Condicion,
            montoAutorizado: command.MontoAutorizado,
            moneda: command.Moneda,
            firmasRequeridas: firmas,
            // M-085/M-239: si la otorga Dirección, ocupa ambos niveles y queda firmada completa.
            autorizadaPorDireccion: _permisos is not null
                && await _permisos.TieneAsync(Millet.Identidad.Domain.PermisosCanonicos.CuentasPorCobrarLiberacionOverrideDireccion, cancellationToken));

        _db.AutorizacionesCredito.Add(autorizacion);
        await _db.SaveChangesAsync(cancellationToken);
        return AutorizacionCreditoMapper.ToResponse(autorizacion);
    }
}

// --------------------------------------------------- Firmar (M-239)

/// <summary>M-239: la segunda firma (Dirección) de una excepción pendiente por su monto.</summary>
public sealed record FirmarAutorizacionCreditoCommand(Guid Id, int VersionEsperada) : IRequest<AutorizacionCreditoResponse>;

public sealed class FirmarAutorizacionCreditoHandler(CuentasPorCobrarDbContext db, ICurrentUserContext user, IClock clock)
    : IRequestHandler<FirmarAutorizacionCreditoCommand, AutorizacionCreditoResponse>
{
    public async Task<AutorizacionCreditoResponse> Handle(FirmarAutorizacionCreditoCommand command, CancellationToken cancellationToken)
    {
        if (user.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar a quien firma.");
        var a = await db.AutorizacionesCredito.FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("AC_NO_ENCONTRADA", $"No se encontró la autorización '{command.Id}'.");
        if (a.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(AutorizacionCredito), a.Id);

        a.Firmar(usuarioId, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return AutorizacionCreditoMapper.ToResponse(a);
    }
}

// --------------------------------------------------- Configuración y reglas de firmas (M-239)

public sealed record ReglaFirmaExcepcionCreditoDto(decimal UmbralMxn, short FirmasRequeridas);

/// <param name="ReglasPorOmision">true = no hay reglas guardadas y se aplica la de M-085.</param>
public sealed record ConfiguracionExcepcionCreditoResponse(
    int VigenciaPorOmisionHoras, int VigenciaMaximaHoras,
    IReadOnlyList<ReglaFirmaExcepcionCreditoDto> Reglas, bool ReglasPorOmision);

public sealed record ConfiguracionExcepcionCreditoQuery : IRequest<ConfiguracionExcepcionCreditoResponse>;

/// <summary>M-239: reemplaza las reglas de firmas por monto de la empresa (vacío = vuelve a la regla de M-085).</summary>
public sealed record GuardarReglasFirmaExcepcionCreditoCommand(IReadOnlyList<ReglaFirmaExcepcionCreditoDto> Reglas)
    : IRequest<ConfiguracionExcepcionCreditoResponse>;

public sealed class ConfiguracionExcepcionCreditoHandler(
    CuentasPorCobrarDbContext db, ICurrentEmpresaContext empresa,
    Microsoft.Extensions.Options.IOptions<ExcepcionCreditoOptions>? opciones = null)
    : IRequestHandler<ConfiguracionExcepcionCreditoQuery, ConfiguracionExcepcionCreditoResponse>,
      IRequestHandler<GuardarReglasFirmaExcepcionCreditoCommand, ConfiguracionExcepcionCreditoResponse>
{
    private readonly ExcepcionCreditoOptions _opciones = opciones?.Value ?? new ExcepcionCreditoOptions();

    public async Task<ConfiguracionExcepcionCreditoResponse> Handle(ConfiguracionExcepcionCreditoQuery query, CancellationToken cancellationToken) =>
        Armar(await db.ReglasFirmaExcepcionCredito.AsNoTracking().ToListAsync(cancellationToken));

    public async Task<ConfiguracionExcepcionCreditoResponse> Handle(GuardarReglasFirmaExcepcionCreditoCommand command, CancellationToken cancellationToken)
    {
        if (empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");
        var nuevas = command.Reglas.Select(r => ReglaFirmaExcepcionCredito.Crear(empresaId, r.UmbralMxn, r.FirmasRequeridas)).ToList();
        if (nuevas.GroupBy(r => r.UmbralMxn).Any(g => g.Count() > 1))
            throw new BusinessRuleException("AC_REGLA_DUPLICADA", "Hay dos reglas con el mismo umbral.");

        db.ReglasFirmaExcepcionCredito.RemoveRange(await db.ReglasFirmaExcepcionCredito.ToListAsync(cancellationToken));
        db.ReglasFirmaExcepcionCredito.AddRange(nuevas);
        await db.SaveChangesAsync(cancellationToken);
        return Armar(nuevas);
    }

    private ConfiguracionExcepcionCreditoResponse Armar(List<ReglaFirmaExcepcionCredito> reglas) =>
        new(_opciones.VigenciaPorOmisionHoras, _opciones.VigenciaMaximaHoras,
            reglas.Count == 0
                ? [new ReglaFirmaExcepcionCreditoDto(ExcesoCredito.TopeCreditoYCobranza, 2)]
                : reglas.OrderBy(r => r.UmbralMxn).Select(r => new ReglaFirmaExcepcionCreditoDto(r.UmbralMxn, r.FirmasRequeridas)).ToList(),
            reglas.Count == 0);
}

// --------------------------------------------------- Cancelar

public sealed record CancelarAutorizacionCreditoCommand(Guid Id, int VersionEsperada)
    : IRequest<AutorizacionCreditoResponse>;

public sealed class CancelarAutorizacionCreditoHandler
    : IRequestHandler<CancelarAutorizacionCreditoCommand, AutorizacionCreditoResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    public CancelarAutorizacionCreditoHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<AutorizacionCreditoResponse> Handle(
        CancelarAutorizacionCreditoCommand command, CancellationToken cancellationToken)
    {
        var a = await _db.AutorizacionesCredito
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("AC_NO_ENCONTRADA",
                $"No se encontró la autorización '{command.Id}'.");
        if (a.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(AutorizacionCredito), a.Id);

        a.Cancelar();
        await _db.SaveChangesAsync(cancellationToken);
        return AutorizacionCreditoMapper.ToResponse(a);
    }
}

// --------------------------------------------------- Listar

public sealed record ListarAutorizacionesCreditoQuery(
    EstadoAutorizacionCredito? Estado = null,
    Guid? BeneficiarioUsuarioId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<AutorizacionCreditoResponse>>;

public sealed class ListarAutorizacionesCreditoHandler
    : IRequestHandler<ListarAutorizacionesCreditoQuery, PagedResponse<AutorizacionCreditoResponse>>
{
    private readonly CuentasPorCobrarDbContext _db;
    public ListarAutorizacionesCreditoHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<PagedResponse<AutorizacionCreditoResponse>> Handle(
        ListarAutorizacionesCreditoQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.AutorizacionesCredito.AsNoTracking();
        if (query.Estado is EstadoAutorizacionCredito e) q = q.Where(a => a.Estado == e);
        if (query.BeneficiarioUsuarioId is Guid ben) q = q.Where(a => a.BeneficiarioUsuarioId == ben);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(a => a.FechaAutorizacion)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        return new PagedResponse<AutorizacionCreditoResponse>(
            items.Select(AutorizacionCreditoMapper.ToResponse).ToList(),
            offset, limit, total);
    }
}

public sealed record ListarBeneficiariosCreditoQuery : IRequest<IReadOnlyList<BeneficiarioCreditoDto>>;
public sealed class ListarBeneficiariosCreditoHandler(IBeneficiarioCreditoReadPort port, ICurrentEmpresaContext empresa)
    : IRequestHandler<ListarBeneficiariosCreditoQuery, IReadOnlyList<BeneficiarioCreditoDto>>
{
    public Task<IReadOnlyList<BeneficiarioCreditoDto>> Handle(ListarBeneficiariosCreditoQuery query, CancellationToken cancellationToken) =>
        port.ListarAsync(empresa.Current ?? throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "Selecciona una empresa."), cancellationToken);
}
