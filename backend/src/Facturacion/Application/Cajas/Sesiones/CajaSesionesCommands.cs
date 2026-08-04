using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas.Sesiones;

/// <summary>Respuesta común de las mutaciones de sesión (la versión alimenta el ETag).</summary>
public sealed record CajaSesionMutadaResponse(Guid Id, int Version, string Estado, DateOnly DiaOperacion);

// ---- Autorización de apertura de caja ajena ([Decisión 12-1], §5.3) ----

/// <summary>
/// Crea una autorización consumible: "el cajero X puede abrir la caja Y" con
/// vigencia corta y un solo uso. La emite un supervisor desde su propia
/// sesión de trabajo (permiso <c>facturacion.caja.supervisar</c>).
/// </summary>
public sealed record CrearAutorizacionAperturaCommand(
    Guid CajaId,
    Guid CajeroUsuarioId,
    string Motivo) : IRequest<AutorizacionAperturaResponse>;

public sealed record AutorizacionAperturaResponse(
    Guid Id, Guid CajaId, Guid CajeroUsuarioId, DateTimeOffset VigenteHasta);

public sealed class CrearAutorizacionAperturaValidator : AbstractValidator<CrearAutorizacionAperturaCommand>
{
    public CrearAutorizacionAperturaValidator()
    {
        RuleFor(c => c.CajaId).NotEmpty();
        RuleFor(c => c.CajeroUsuarioId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(254);
    }
}

public sealed class CrearAutorizacionAperturaHandler
    : IRequestHandler<CrearAutorizacionAperturaCommand, AutorizacionAperturaResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;
    private readonly CajasOptions _options;

    public CrearAutorizacionAperturaHandler(
        FacturacionDbContext db,
        ICurrentEmpresaContext empresa,
        ICurrentUserContext user,
        IClock clock,
        IOptions<CajasOptions> options)
    {
        _db = db;
        _empresa = empresa;
        _user = user;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<AutorizacionAperturaResponse> Handle(
        CrearAutorizacionAperturaCommand command, CancellationToken cancellationToken)
    {
        var (empresaId, usuarioId) = ContextoRequerido.De(_empresa, _user);

        var caja = await _db.Cajas.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == command.CajaId, cancellationToken)
            ?? throw new EntityNotFoundException("CAJA_NO_ENCONTRADA", $"No existe la caja '{command.CajaId}'.");
        if (caja.Estatus != Millet.Catalogos.Domain.EstatusCatalogo.Activo)
            throw new BusinessRuleException("CAJA_INACTIVA", "No se puede autorizar la apertura de una caja inactiva.");

        var autorizacion = AutorizacionAperturaCaja.Crear(
            empresaId, command.CajaId, command.CajeroUsuarioId, supervisorUsuarioId: usuarioId,
            command.Motivo, _clock.UtcNow, _options.VigenciaAutorizacionApertura);

        _db.AutorizacionesAperturaCaja.Add(autorizacion);
        await _db.SaveChangesAsync(cancellationToken);

        return new AutorizacionAperturaResponse(
            autorizacion.Id, autorizacion.CajaId, autorizacion.CajeroUsuarioId, autorizacion.VigenteHasta);
    }
}

// ---- Apertura de sesión (§5.1 paso 1) ----

/// <summary>
/// Abre la sesión de efectivo: declara fondo y sucursal de operación
/// (`[Decisión 12-A]`); si la caja es ajena consume la autorización
/// (`[Decisión 12-1]`); drena los ajustes pendientes (`[Decisión 12-C]`) y
/// genera el movimiento <c>FondoApertura</c>.
/// </summary>
public sealed record AbrirCajaSesionCommand(
    Guid CajaId,
    Guid SucursalId,
    decimal FondoApertura,
    Guid? AutorizacionAperturaId = null) : IRequest<CajaSesionMutadaResponse>;

public sealed class AbrirCajaSesionValidator : AbstractValidator<AbrirCajaSesionCommand>
{
    public AbrirCajaSesionValidator()
    {
        RuleFor(c => c.CajaId).NotEmpty();
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.FondoApertura).GreaterThanOrEqualTo(0);
    }
}

public sealed class AbrirCajaSesionHandler : IRequestHandler<AbrirCajaSesionCommand, CajaSesionMutadaResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;
    private readonly ISucursalesReadPort _sucursales;
    private readonly IIntegrationEventPublisher _eventos;

    public AbrirCajaSesionHandler(
        FacturacionDbContext db,
        ICurrentEmpresaContext empresa,
        ICurrentUserContext user,
        IClock clock,
        ISucursalesReadPort sucursales,
        IIntegrationEventPublisher eventos)
    {
        _db = db;
        _empresa = empresa;
        _user = user;
        _clock = clock;
        _sucursales = sucursales;
        _eventos = eventos;
    }

    public async Task<CajaSesionMutadaResponse> Handle(
        AbrirCajaSesionCommand command, CancellationToken cancellationToken)
    {
        var (empresaId, usuarioId) = ContextoRequerido.De(_empresa, _user);
        var ahora = _clock.UtcNow;

        var caja = await _db.Cajas
            .Include(c => c.Usuarios)
            .Include(c => c.Sucursales)
            .FirstOrDefaultAsync(c => c.Id == command.CajaId, cancellationToken)
            ?? throw new EntityNotFoundException("CAJA_NO_ENCONTRADA", $"No existe la caja '{command.CajaId}'.");
        if (caja.Estatus != Millet.Catalogos.Domain.EstatusCatalogo.Activo)
            throw new BusinessRuleException("CAJA_INACTIVA", "Una caja inactiva no admite sesiones nuevas.");

        // Sucursal de operación ∈ alcance de la caja (vacío = todas) y activa.
        if (caja.Sucursales.Count > 0 && caja.Sucursales.All(s => s.SucursalId != command.SucursalId))
            throw new BusinessRuleException(
                "SESION_SUCURSAL_FUERA_DE_ALCANCE",
                "La sucursal de operación no pertenece al alcance de la caja.");
        var invalidas = await _sucursales.FiltrarNoActivasAsync([command.SucursalId], cancellationToken);
        if (invalidas.Count > 0)
            throw new BusinessRuleException("SESION_SUCURSAL_NO_ACTIVA", "La sucursal de operación no existe o está inactiva.");

        // Una caja no admite dos sesiones no cerradas (§5.1 → 409; respaldo:
        // índice único parcial ux_caja_sesion_no_cerrada).
        var sesionAbiertaCaja = await _db.CajaSesiones.AsNoTracking()
            .AnyAsync(s => s.CajaId == command.CajaId && s.Estado != EstadoCajaSesion.Cerrada, cancellationToken);
        if (sesionAbiertaCaja)
            throw new ConflictException("SESION_CAJA_YA_ABIERTA", "La caja ya tiene una sesión no cerrada.");

        var zona = await _sucursales.ObtenerZonaHorariaAsync(command.SucursalId, cancellationToken);
        var hoyLocal = DiaOperacion.HoyLocal(ahora, zona);

        // Bloqueo §5.2: el responsable no puede abrir sesión nueva mientras
        // tenga una sesión de un día anterior sin cerrar (cierre extemporáneo
        // primero; no hay auto-cierre).
        var pendientes = await _db.CajaSesiones.AsNoTracking()
            .Where(s => s.ResponsableUsuarioId == usuarioId && s.Estado != EstadoCajaSesion.Cerrada)
            .Select(s => s.DiaOperacion)
            .ToListAsync(cancellationToken);
        if (pendientes.Any(d => d < hoyLocal))
            throw new BusinessRuleException(
                "SESION_DIA_ANTERIOR_PENDIENTE",
                "Tienes una sesión de un día anterior sin cerrar; ciérrala (cierre extemporáneo) antes de abrir otra.");

        // Caja ajena → autorización consumible ([Decisión 12-1]).
        AutorizacionAperturaCaja? autorizacion = null;
        var relacionado = caja.Usuarios.Any(u => u.UsuarioId == usuarioId);
        if (!relacionado)
        {
            if (command.AutorizacionAperturaId is not Guid autorizacionId)
                throw new BusinessRuleException(
                    "SESION_AUTORIZACION_REQUERIDA",
                    "No estás relacionado a la caja; se requiere una autorización de apertura vigente del supervisor.");

            autorizacion = await _db.AutorizacionesAperturaCaja
                .FirstOrDefaultAsync(a => a.Id == autorizacionId, cancellationToken)
                ?? throw new EntityNotFoundException(
                    "AUTORIZACION_NO_ENCONTRADA", $"No existe la autorización '{autorizacionId}'.");
        }

        var sesion = CajaSesion.Abrir(
            empresaId, command.CajaId, command.SucursalId, usuarioId,
            command.FondoApertura, hoyLocal, ahora, autorizacion?.Id);
        autorizacion?.Consumir(sesion.Id, usuarioId, command.CajaId, ahora);
        _db.CajaSesiones.Add(sesion);

        if (command.FondoApertura > 0)
        {
            _db.CajaMovimientos.Add(CajaMovimiento.Crear(
                empresaId, sesion.Id, TipoCajaMovimiento.FondoApertura, CajaSesion.FormaPagoEfectivo,
                command.FondoApertura, usuarioId, "Fondo de apertura"));
        }

        // Drenado de ajustes pendientes ([Decisión 12-C]).
        var ajustes = await _db.CajaAjustesPendientes
            .Where(a => a.CajaId == command.CajaId && a.AplicadoEnSesionId == null)
            .ToListAsync(cancellationToken);
        foreach (var ajuste in ajustes)
        {
            ajuste.MarcarAplicado(sesion.Id);
            _db.CajaMovimientos.Add(CajaMovimiento.Crear(
                empresaId, sesion.Id, TipoCajaMovimiento.AjusteCorreccion, ajuste.FormaPago,
                ajuste.Importe, usuarioId, $"Ajuste pendiente: {ajuste.Motivo}",
                cobroMostradorId: ajuste.CobroMostradorId));
        }

        await _eventos.PublishAsync(new CajaSesionAbiertaIntegrationEvent(
            empresaId, ahora, sesion.Id, sesion.CajaId, sesion.SucursalId,
            usuarioId, sesion.DiaOperacion, sesion.FondoApertura, autorizacion?.Id), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return new CajaSesionMutadaResponse(sesion.Id, sesion.Version, sesion.Estado.ToString(), sesion.DiaOperacion);
    }
}

// ---- Movimiento manual (depósito / retiro, §5.1 paso 2) ----

/// <summary>
/// Registra un depósito o retiro manual en la sesión. <c>Importe</c> viaja
/// positivo; el signo lo pone el tipo (retiro = negativo). Retiros: en v1
/// basta <c>caja.operar</c> y quedan auditados (P4).
/// </summary>
public sealed record RegistrarCajaMovimientoCommand(
    Guid SesionId,
    TipoCajaMovimiento Tipo,
    string FormaPago,
    decimal Importe,
    string Descripcion,
    string? Referencia = null) : IRequest<CajaMovimientoRegistradoResponse>;

public sealed record CajaMovimientoRegistradoResponse(Guid Id, Guid SesionId, decimal Importe);

public sealed class RegistrarCajaMovimientoValidator : AbstractValidator<RegistrarCajaMovimientoCommand>
{
    public RegistrarCajaMovimientoValidator()
    {
        RuleFor(c => c.SesionId).NotEmpty();
        RuleFor(c => c.Tipo)
            .Must(t => t is TipoCajaMovimiento.Deposito or TipoCajaMovimiento.Retiro)
            .WithMessage("Solo se capturan manualmente depósitos y retiros; los demás tipos los genera el sistema.");
        RuleFor(c => c.FormaPago).NotEmpty().MaximumLength(2);
        RuleFor(c => c.Importe).GreaterThan(0);
        RuleFor(c => c.Descripcion).NotEmpty().MaximumLength(254);
        RuleFor(c => c.Referencia!).MaximumLength(100).When(c => c.Referencia is not null);
    }
}

public sealed class RegistrarCajaMovimientoHandler
    : IRequestHandler<RegistrarCajaMovimientoCommand, CajaMovimientoRegistradoResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;
    private readonly ISucursalesReadPort _sucursales;

    public RegistrarCajaMovimientoHandler(
        FacturacionDbContext db,
        ICurrentEmpresaContext empresa,
        ICurrentUserContext user,
        IClock clock,
        ISucursalesReadPort sucursales)
    {
        _db = db;
        _empresa = empresa;
        _user = user;
        _clock = clock;
        _sucursales = sucursales;
    }

    public async Task<CajaMovimientoRegistradoResponse> Handle(
        RegistrarCajaMovimientoCommand command, CancellationToken cancellationToken)
    {
        var (empresaId, usuarioId) = ContextoRequerido.De(_empresa, _user);

        var sesion = await _db.CajaSesiones.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == command.SesionId, cancellationToken)
            ?? throw new EntityNotFoundException("SESION_NO_ENCONTRADA", $"No existe la sesión '{command.SesionId}'.");

        if (sesion.Estado != EstadoCajaSesion.Abierta)
            throw new BusinessRuleException(
                "SESION_NO_ABIERTA", $"Solo una sesión Abierta acepta movimientos (actual: {sesion.Estado}).");
        if (sesion.ResponsableUsuarioId != usuarioId)
            throw new ForbiddenException(
                "SESION_RESPONSABLE_DISTINTO", "Solo el responsable de la sesión puede registrar movimientos.");

        await BloqueoDiaAnterior.ValidarAsync(sesion, _clock.UtcNow, _sucursales, cancellationToken);

        var importe = command.Tipo == TipoCajaMovimiento.Retiro ? -command.Importe : command.Importe;
        var movimiento = CajaMovimiento.Crear(
            empresaId, sesion.Id, command.Tipo, command.FormaPago, importe,
            usuarioId, command.Descripcion, command.Referencia);

        _db.CajaMovimientos.Add(movimiento);
        await _db.SaveChangesAsync(cancellationToken);
        return new CajaMovimientoRegistradoResponse(movimiento.Id, sesion.Id, movimiento.Importe);
    }
}

// ---- Arqueo (§5.1 paso 3) ----

/// <summary>Calcula el esperado por forma de pago y pasa la sesión a EnArqueo.</summary>
public sealed record IniciarArqueoCajaSesionCommand(Guid SesionId, int VersionEsperada)
    : IRequest<CajaSesionMutadaResponse>;

public sealed class IniciarArqueoCajaSesionHandler
    : IRequestHandler<IniciarArqueoCajaSesionCommand, CajaSesionMutadaResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentUserContext _user;

    public IniciarArqueoCajaSesionHandler(FacturacionDbContext db, ICurrentUserContext user)
    {
        _db = db;
        _user = user;
    }

    public async Task<CajaSesionMutadaResponse> Handle(
        IniciarArqueoCajaSesionCommand command, CancellationToken cancellationToken)
    {
        var sesion = await CajaSesionesCargador.CargarConVersionAsync(
            _db, command.SesionId, command.VersionEsperada, cancellationToken);

        if (_user.UserId != sesion.ResponsableUsuarioId)
            throw new ForbiddenException(
                "SESION_RESPONSABLE_DISTINTO", "Solo el responsable de la sesión puede iniciar el arqueo.");

        var esperado = await _db.CajaMovimientos.AsNoTracking()
            .Where(m => m.CajaSesionId == sesion.Id)
            .GroupBy(m => m.FormaPago)
            .Select(g => new { FormaPago = g.Key, Monto = g.Sum(m => m.Importe) })
            .ToListAsync(cancellationToken);

        sesion.IniciarArqueo(esperado.ToDictionary(e => e.FormaPago, e => e.Monto));
        await _db.SaveChangesAsync(cancellationToken);
        return new CajaSesionMutadaResponse(sesion.Id, sesion.Version, sesion.Estado.ToString(), sesion.DiaOperacion);
    }
}

// ---- Cierre / liquidación (§5.1 paso 4, permiso caja.liquidar) ----

/// <summary>
/// Cierra la sesión con el contado físico de efectivo. Diferencia = contado −
/// esperado (solo efectivo, `[Decisión 12-5]`). Si el día local vigente ya es
/// posterior al de operación, el cierre queda marcado extemporáneo (§5.2).
/// </summary>
public sealed record CerrarCajaSesionCommand(
    Guid SesionId,
    int VersionEsperada,
    decimal EfectivoDeclarado,
    string? NotasCierre = null) : IRequest<CajaSesionMutadaResponse>;

public sealed class CerrarCajaSesionValidator : AbstractValidator<CerrarCajaSesionCommand>
{
    public CerrarCajaSesionValidator()
    {
        RuleFor(c => c.EfectivoDeclarado).GreaterThanOrEqualTo(0);
        RuleFor(c => c.NotasCierre!).MaximumLength(500).When(c => c.NotasCierre is not null);
    }
}

public sealed class CerrarCajaSesionHandler : IRequestHandler<CerrarCajaSesionCommand, CajaSesionMutadaResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IClock _clock;
    private readonly ISucursalesReadPort _sucursales;
    private readonly IIntegrationEventPublisher _eventos;

    public CerrarCajaSesionHandler(
        FacturacionDbContext db,
        IClock clock,
        ISucursalesReadPort sucursales,
        IIntegrationEventPublisher eventos)
    {
        _db = db;
        _clock = clock;
        _sucursales = sucursales;
        _eventos = eventos;
    }

    public async Task<CajaSesionMutadaResponse> Handle(
        CerrarCajaSesionCommand command, CancellationToken cancellationToken)
    {
        var ahora = _clock.UtcNow;
        var sesion = await CajaSesionesCargador.CargarConVersionAsync(
            _db, command.SesionId, command.VersionEsperada, cancellationToken);

        var zona = await _sucursales.ObtenerZonaHorariaAsync(sesion.SucursalId, cancellationToken);
        var extemporaneo = DiaOperacion.HoyLocal(ahora, zona) > sesion.DiaOperacion;

        sesion.Cerrar(command.EfectivoDeclarado, command.NotasCierre, extemporaneo, ahora);

        await _eventos.PublishAsync(new CajaSesionCerradaIntegrationEvent(
            sesion.EmpresaId, ahora, sesion.Id, sesion.CajaId, sesion.SucursalId,
            sesion.ResponsableUsuarioId, sesion.DiaOperacion, sesion.FondoApertura,
            sesion.EfectivoTeorico!.Value, sesion.EfectivoDeclarado!.Value, sesion.Diferencia!.Value,
            sesion.CierreExtemporaneo,
            sesion.Cortes.Select(c => new CajaSesionCorteTotal(c.FormaPago, c.MontoSistema, c.MontoDeclarado)).ToList()),
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return new CajaSesionMutadaResponse(sesion.Id, sesion.Version, sesion.Estado.ToString(), sesion.DiaOperacion);
    }
}

// ---- Reabrir (EnArqueo → Abierta, permiso caja.supervisar) ----

/// <summary>Devuelve la sesión de EnArqueo a Abierta cuando falta registrar algo (§5.1).</summary>
public sealed record ReabrirCajaSesionCommand(Guid SesionId, int VersionEsperada)
    : IRequest<CajaSesionMutadaResponse>;

public sealed class ReabrirCajaSesionHandler : IRequestHandler<ReabrirCajaSesionCommand, CajaSesionMutadaResponse>
{
    private readonly FacturacionDbContext _db;

    public ReabrirCajaSesionHandler(FacturacionDbContext db) => _db = db;

    public async Task<CajaSesionMutadaResponse> Handle(
        ReabrirCajaSesionCommand command, CancellationToken cancellationToken)
    {
        var sesion = await CajaSesionesCargador.CargarConVersionAsync(
            _db, command.SesionId, command.VersionEsperada, cancellationToken);

        sesion.Reabrir();
        await _db.SaveChangesAsync(cancellationToken);
        return new CajaSesionMutadaResponse(sesion.Id, sesion.Version, sesion.Estado.ToString(), sesion.DiaOperacion);
    }
}

// ---- Helpers compartidos ----

/// <summary>Carga la sesión (con cortes) verificando If-Match (409 al choque).</summary>
internal static class CajaSesionesCargador
{
    public static async Task<CajaSesion> CargarConVersionAsync(
        FacturacionDbContext db, Guid id, int versionEsperada, CancellationToken cancellationToken)
    {
        var sesion = await db.CajaSesiones
            .Include(s => s.Cortes)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("SESION_NO_ENCONTRADA", $"No existe la sesión '{id}'.");

        if (sesion.Version != versionEsperada)
            throw new ConcurrencyException(nameof(CajaSesion), id);

        return sesion;
    }
}

/// <summary>Empresa + usuario del request; sin cualquiera de los dos, 403.</summary>
internal static class ContextoRequerido
{
    public static (Guid EmpresaId, Guid UsuarioId) De(ICurrentEmpresaContext empresa, ICurrentUserContext user)
    {
        if (empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");
        if (user.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_AUTENTICADO", "No hay usuario autenticado en el contexto del request.");
        return (empresaId, usuarioId);
    }
}

/// <summary>
/// Bloqueo de día anterior (§5.2): una sesión con día de operación anterior
/// al día local vigente no acepta operación nueva — solo cierre extemporáneo.
/// </summary>
internal static class BloqueoDiaAnterior
{
    public static async Task ValidarAsync(
        CajaSesion sesion,
        DateTimeOffset ahoraUtc,
        ISucursalesReadPort sucursales,
        CancellationToken cancellationToken)
    {
        var zona = await sucursales.ObtenerZonaHorariaAsync(sesion.SucursalId, cancellationToken);
        if (DiaOperacion.HoyLocal(ahoraUtc, zona) > sesion.DiaOperacion)
            throw new BusinessRuleException(
                "SESION_DIA_ANTERIOR",
                "La sesión es de un día anterior; solo admite arqueo y cierre extemporáneo (§5.2).");
    }
}
