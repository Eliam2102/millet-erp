using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Devoluciones;

// ============================================================================
// T9.6: comandos de devolución de dinero a clientes. El tope por origen
// (T9.6-a) se comprueba bajo advisory lock porque dos solicitudes simultáneas
// sobre el mismo depósito podrían pasarlo.
// ============================================================================

public sealed record SolicitarDevolucionDepositoCommand(
    Guid MovimientoId, Guid ClienteId, decimal Importe, string Motivo,
    string Banco, string Clabe, string Titular) : IRequest<DevolucionClienteResponse>;

public sealed class SolicitarDevolucionDepositoValidator : AbstractValidator<SolicitarDevolucionDepositoCommand>
{
    public SolicitarDevolucionDepositoValidator()
    {
        RuleFor(c => c.MovimientoId).NotEmpty();
        RuleFor(c => c.ClienteId).NotEmpty();
        RuleFor(c => c.Importe).GreaterThan(0);
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
        RuleFor(c => c.Banco).NotEmpty().MaximumLength(80);
        RuleFor(c => c.Clabe).NotEmpty().Length(18);
        RuleFor(c => c.Titular).NotEmpty().MaximumLength(200);
    }
}

public sealed class SolicitarDevolucionDepositoHandler
    : IRequestHandler<SolicitarDevolucionDepositoCommand, DevolucionClienteResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClienteReadPort _clientes;
    private readonly IClock _clock;

    public SolicitarDevolucionDepositoHandler(
        TesoreriaDbContext db, ICurrentEmpresaContext currentEmpresa, ICurrentUserContext currentUser,
        IClienteReadPort clientes, IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser; _clientes = clientes; _clock = clock;
    }

    public async Task<DevolucionClienteResponse> Handle(SolicitarDevolucionDepositoCommand command, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational()) return await SolicitarAsync(command, cancellationToken);
        DevolucionClienteResponse? resultado = null;
        await Millet.SharedKernel.Infrastructure.Persistence.PostgresAdvisoryLock.ExecuteAsync(_db, DevolucionLectura.LockKey,
            async ct => resultado = await SolicitarAsync(command, ct), cancellationToken);
        return resultado!;
    }

    private async Task<DevolucionClienteResponse> SolicitarAsync(SolicitarDevolucionDepositoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario que solicita la devolución.");

        var movimiento = await _db.MovimientosBancarios.FirstOrDefaultAsync(m => m.Id == command.MovimientoId, cancellationToken)
            ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO", $"No se encontró el movimiento '{command.MovimientoId}'.");

        if (await _db.DepositosConfirmacion.AnyAsync(d => d.MovimientoId == movimiento.Id && d.Estado == EstadoDepositoConfirmacion.Confirmada, cancellationToken))
            throw new BusinessRuleException("DEV_DEPOSITO_CONFIRMADO",
                "El depósito ya se confirmó contra una propuesta de Crédito y Cobranza; no es un depósito erróneo. Corrige la aplicación en CxC antes de devolverlo.");

        if (movimiento.BeneficiarioTipo == BeneficiarioTipo.Cliente && movimiento.BeneficiarioRef is Guid referencia && referencia != command.ClienteId)
            throw new BusinessRuleException("DEV_CLIENTE_DISTINTO_AL_DEPOSITO", "El depósito está identificado con otro cliente.");

        var cliente = await _clientes.ObtenerVariosAsync([command.ClienteId], cancellationToken);
        if (!cliente.TryGetValue(command.ClienteId, out var clienteRef))
            throw new BusinessRuleException("DEV_CLIENTE_NO_ENCONTRADO", "El cliente seleccionado no existe.");

        var comprometido = await _db.DevolucionesCliente.AsNoTracking()
            .Where(x => x.MovimientoOrigenId == movimiento.Id
                && (x.Estado == EstadoDevolucionCliente.Solicitada || x.Estado == EstadoDevolucionCliente.Autorizada || x.Estado == EstadoDevolucionCliente.Pagada))
            .SumAsync(x => (decimal?)x.Importe, cancellationToken) ?? 0m;
        var disponible = movimiento.Monto - comprometido;

        var folio = await DevolucionLectura.SiguienteFolioAsync(_db, empresaId, cancellationToken);
        var devolucion = DevolucionCliente.SolicitarPorDepositoErroneo(
            empresaId, folio, movimiento, command.ClienteId, command.Importe, disponible, command.Motivo,
            new CuentaDestinoDevolucion(command.Banco, command.Clabe, command.Titular), usuarioId, _clock.UtcNow);

        _db.DevolucionesCliente.Add(devolucion);
        await _db.SaveChangesAsync(cancellationToken);

        // La CLABE completa: quien la capturó acaba de escribirla.
        return DevolucionLectura.ToResponse(devolucion, clienteRef, disponible, verCuentaCompleta: true);
    }
}

// ============================================================================
// R2: editar, cancelar, autorizar y rechazar. Versión esperada (ADR-0012);
// la segregación (quien solicita no autoriza) vive en el dominio.
// ============================================================================

internal static class DevolucionTransiciones
{
    public static (Guid EmpresaId, Guid UsuarioId) Contexto(
        ICurrentEmpresaContext empresa, ICurrentUserContext usuario, string accion)
    {
        if (empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada.");
        if (usuario.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", $"No se pudo identificar al usuario que {accion}.");
        return (empresaId, usuarioId);
    }

    public static async Task<DevolucionCliente> CargarAsync(TesoreriaDbContext db, Guid id, int versionEsperada, CancellationToken ct)
    {
        var d = await db.DevolucionesCliente.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("DEV_NO_ENCONTRADA", $"No se encontró la devolución '{id}'.");
        if (d.Version != versionEsperada) throw new ConcurrencyException(nameof(DevolucionCliente), d.Id);
        return d;
    }

    public static async Task ExigirSolicitanteOAutorizadorAsync(DevolucionCliente d, Guid usuarioId, ICurrentUserPermissions permisos, CancellationToken ct)
    {
        if (d.SolicitadaPor is Guid s && s != usuarioId && !await permisos.TieneAsync("tesoreria.devoluciones.autorizar", ct))
            throw new BusinessRuleException("DEV_NO_ES_SOLICITANTE",
                "Solo quien solicitó la devolución o un autorizador puede editarla o cancelarla.");
    }

    public static async Task<DevolucionClienteResponse> RespuestaAsync(TesoreriaDbContext db, IClienteReadPort clientes, ICurrentUserPermissions permisos, DevolucionCliente d, CancellationToken ct)
    {
        var c = await clientes.ObtenerVariosAsync([d.ClienteId], ct);
        c.TryGetValue(d.ClienteId, out var cliente);
        var disponible = d.ImporteMaximoOrigen - await DevolucionLectura.ComprometidoAsync(db, d, ct);
        return DevolucionLectura.ToResponse(d, cliente, disponible, await permisos.TieneAsync("tesoreria.movimientos.ver-cuenta-completa", ct));
    }
}

public sealed record EditarDevolucionCommand(
    Guid Id, Guid ClienteId, decimal Importe, string Motivo,
    string? Banco, string? Clabe, string? Titular, int VersionEsperada) : IRequest<DevolucionClienteResponse>;

public sealed class EditarDevolucionValidator : AbstractValidator<EditarDevolucionCommand>
{
    public EditarDevolucionValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.ClienteId).NotEmpty();
        RuleFor(c => c.Importe).GreaterThan(0);
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
        RuleFor(c => c.Banco).MaximumLength(80);
        RuleFor(c => c.Clabe).Length(18).When(c => c.Clabe is not null);
        RuleFor(c => c.Titular).MaximumLength(200);
    }
}

public sealed class EditarDevolucionHandler : IRequestHandler<EditarDevolucionCommand, DevolucionClienteResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IClienteReadPort _clientes;
    private readonly IClock _clock;

    public EditarDevolucionHandler(TesoreriaDbContext db, ICurrentEmpresaContext currentEmpresa, ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos, IClienteReadPort clientes, IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser; _permisos = permisos; _clientes = clientes; _clock = clock;
    }

    public async Task<DevolucionClienteResponse> Handle(EditarDevolucionCommand command, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational()) return await EditarAsync(command, cancellationToken);
        DevolucionClienteResponse? resultado = null;
        await Millet.SharedKernel.Infrastructure.Persistence.PostgresAdvisoryLock.ExecuteAsync(_db, DevolucionLectura.LockKey,
            async ct => resultado = await EditarAsync(command, ct), cancellationToken);
        return resultado!;
    }

    private async Task<DevolucionClienteResponse> EditarAsync(EditarDevolucionCommand command, CancellationToken ct)
    {
        var (_, usuarioId) = DevolucionTransiciones.Contexto(_currentEmpresa, _currentUser, "edita");
        var d = await DevolucionTransiciones.CargarAsync(_db, command.Id, command.VersionEsperada, ct);
        await DevolucionTransiciones.ExigirSolicitanteOAutorizadorAsync(d, usuarioId, _permisos, ct);

        if (d.Origen == OrigenDevolucionCliente.DepositoErroneo && command.ClienteId != d.ClienteId)
        {
            var existe = await _clientes.ObtenerVariosAsync([command.ClienteId], ct);
            if (!existe.ContainsKey(command.ClienteId))
                throw new BusinessRuleException("DEV_CLIENTE_NO_ENCONTRADO", "El cliente seleccionado no existe.");
        }

        var capturados = new[] { command.Banco, command.Clabe, command.Titular }.Count(x => !string.IsNullOrWhiteSpace(x));
        if (capturados is > 0 and < 3)
            throw new BusinessRuleException("DEV_DESTINO_INCOMPLETO", "Captura banco, CLABE y titular de la cuenta destino antes de autorizar.");
        var destino = capturados == 3 ? new CuentaDestinoDevolucion(command.Banco!, command.Clabe!, command.Titular!) : null;

        var disponible = d.ImporteMaximoOrigen - await DevolucionLectura.ComprometidoAsync(_db, d, ct);
        d.Editar(command.ClienteId, command.Importe, disponible, command.Motivo, destino, usuarioId, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
        return await DevolucionTransiciones.RespuestaAsync(_db, _clientes, _permisos, d, ct);
    }
}

public sealed record CancelarDevolucionCommand(Guid Id, string Motivo, int VersionEsperada) : IRequest<DevolucionClienteResponse>;

public sealed class CancelarDevolucionValidator : AbstractValidator<CancelarDevolucionCommand>
{
    public CancelarDevolucionValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class CancelarDevolucionHandler : IRequestHandler<CancelarDevolucionCommand, DevolucionClienteResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IClienteReadPort _clientes;
    private readonly IClock _clock;

    public CancelarDevolucionHandler(TesoreriaDbContext db, ICurrentEmpresaContext currentEmpresa, ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos, IClienteReadPort clientes, IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser; _permisos = permisos; _clientes = clientes; _clock = clock;
    }

    public async Task<DevolucionClienteResponse> Handle(CancelarDevolucionCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var (_, usuarioId) = DevolucionTransiciones.Contexto(_currentEmpresa, _currentUser, "cancela");
        var d = await DevolucionTransiciones.CargarAsync(_db, command.Id, command.VersionEsperada, ct);
        await DevolucionTransiciones.ExigirSolicitanteOAutorizadorAsync(d, usuarioId, _permisos, ct);
        d.Cancelar(command.Motivo, usuarioId, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
        return await DevolucionTransiciones.RespuestaAsync(_db, _clientes, _permisos, d, ct);
    }
}

public sealed record AutorizarDevolucionCommand(Guid Id, int VersionEsperada) : IRequest<DevolucionClienteResponse>;

public sealed class AutorizarDevolucionValidator : AbstractValidator<AutorizarDevolucionCommand>
{
    public AutorizarDevolucionValidator() => RuleFor(c => c.Id).NotEmpty();
}

public sealed class AutorizarDevolucionHandler : IRequestHandler<AutorizarDevolucionCommand, DevolucionClienteResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IClienteReadPort _clientes;
    private readonly IClock _clock;

    public AutorizarDevolucionHandler(TesoreriaDbContext db, ICurrentEmpresaContext currentEmpresa, ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos, IClienteReadPort clientes, IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser; _permisos = permisos; _clientes = clientes; _clock = clock;
    }

    public async Task<DevolucionClienteResponse> Handle(AutorizarDevolucionCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var (_, usuarioId) = DevolucionTransiciones.Contexto(_currentEmpresa, _currentUser, "autoriza");
        var d = await DevolucionTransiciones.CargarAsync(_db, command.Id, command.VersionEsperada, ct);
        d.Autorizar(usuarioId, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
        return await DevolucionTransiciones.RespuestaAsync(_db, _clientes, _permisos, d, ct);
    }
}

public sealed record RechazarDevolucionCommand(Guid Id, string Motivo, int VersionEsperada) : IRequest<DevolucionClienteResponse>;

public sealed class RechazarDevolucionValidator : AbstractValidator<RechazarDevolucionCommand>
{
    public RechazarDevolucionValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class RechazarDevolucionHandler : IRequestHandler<RechazarDevolucionCommand, DevolucionClienteResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IClienteReadPort _clientes;
    private readonly IClock _clock;

    public RechazarDevolucionHandler(TesoreriaDbContext db, ICurrentEmpresaContext currentEmpresa, ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos, IClienteReadPort clientes, IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser; _permisos = permisos; _clientes = clientes; _clock = clock;
    }

    public async Task<DevolucionClienteResponse> Handle(RechazarDevolucionCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var (_, usuarioId) = DevolucionTransiciones.Contexto(_currentEmpresa, _currentUser, "rechaza");
        var d = await DevolucionTransiciones.CargarAsync(_db, command.Id, command.VersionEsperada, ct);
        d.Rechazar(command.Motivo, usuarioId, _clock.UtcNow);
        await _db.SaveChangesAsync(ct);
        return await DevolucionTransiciones.RespuestaAsync(_db, _clientes, _permisos, d, ct);
    }
}
