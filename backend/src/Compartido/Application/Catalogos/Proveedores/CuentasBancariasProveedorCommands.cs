using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Adjuntos;
using Millet.SharedKernel.Domain.Bancos;

namespace Millet.Compartido.Application.Catalogos.Proveedores;

// 86e3pdmee · ADM-02: varias cuentas bancarias por proveedor con validación
// contra estado de cuenta (decisión del owner, 17-oct). Los permisos se
// aplican en los endpoints: capturar = bancarios-editar; validar, rechazar,
// inhabilitar y reactivar = cuentas-administrar.

public sealed record CuentaBancariaProveedorDto(
    Guid Id,
    string Banco,
    string Numero,
    bool EsClabe,
    string? Swift,
    string Moneda,
    string Titular,
    bool Predeterminada,
    EstadoCuentaBancariaProveedor Estado,
    string? CapturadaPorNombre,
    DateTimeOffset CapturadaEn,
    bool CapturadaPorMi,
    string? ValidadaPorNombre,
    DateTimeOffset? ValidadaEn,
    Guid? DocumentoAdjuntoId,
    string? NotaValidacion,
    string? MotivoEstado,
    bool NumeroCompleto,
    int Version);

public sealed record ListarCuentasBancariasProveedorQuery(Guid ProveedorId, bool VerCompleta)
    : IRequest<IReadOnlyList<CuentaBancariaProveedorDto>>;

public sealed class ListarCuentasBancariasProveedorHandler
    : IRequestHandler<ListarCuentasBancariasProveedorQuery, IReadOnlyList<CuentaBancariaProveedorDto>>
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentUserContext _currentUser;

    public ListarCuentasBancariasProveedorHandler(CompartidoDbContext db, ICurrentUserContext currentUser)
    {
        _db = db; _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<CuentaBancariaProveedorDto>> Handle(
        ListarCuentasBancariasProveedorQuery request, CancellationToken cancellationToken)
    {
        var cuentas = await _db.CuentasBancariasProveedor.AsNoTracking()
            .Where(c => c.ProveedorId == request.ProveedorId)
            .OrderBy(c => c.Estado).ThenByDescending(c => c.Predeterminada).ThenBy(c => c.CapturadaEn)
            .ToListAsync(cancellationToken);
        return cuentas.Select(c => CuentasBancariasProveedor.ADto(c, request.VerCompleta, _currentUser.UserId)).ToList();
    }
}

public sealed record AgregarCuentaBancariaProveedorCommand(
    Guid ProveedorId, string Banco, string? Clabe, string? NumeroCuenta, string? Swift, string Moneda, string Titular)
    : IRequest<CuentaBancariaProveedorDto>;

public sealed class AgregarCuentaBancariaProveedorValidator : AbstractValidator<AgregarCuentaBancariaProveedorCommand>
{
    public AgregarCuentaBancariaProveedorValidator()
    {
        RuleFor(c => c.ProveedorId).NotEmpty();
        RuleFor(c => c.Banco).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Titular).NotEmpty().MaximumLength(254);
        RuleFor(c => c.Moneda).NotEmpty().Must(m => CuentaBancariaProveedor.Monedas.Contains(m?.Trim().ToUpperInvariant()))
            .WithMessage("La moneda debe ser MXN o USD.");
        RuleFor(c => c).Must(c => !string.IsNullOrWhiteSpace(c.Clabe) || !string.IsNullOrWhiteSpace(c.NumeroCuenta))
            .WithName("Clabe").WithMessage("Captura la CLABE (nacional) o el número de cuenta (extranjera).");
    }
}

public sealed class AgregarCuentaBancariaProveedorHandler
    : IRequestHandler<AgregarCuentaBancariaProveedorCommand, CuentaBancariaProveedorDto>
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IClock _clock;
    private readonly IAuditLogWriter _audit;

    public AgregarCuentaBancariaProveedorHandler(
        CompartidoDbContext db, ICurrentUserContext currentUser, ICurrentEmpresaContext empresa, IClock clock, IAuditLogWriter audit)
    {
        _db = db; _currentUser = currentUser; _empresa = empresa; _clock = clock; _audit = audit;
    }

    public async Task<CuentaBancariaProveedorDto> Handle(
        AgregarCuentaBancariaProveedorCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await CuentasBancariasProveedor.ProveedorAsync(_db, request.ProveedorId, cancellationToken);
        var cuenta = CuentaBancariaProveedor.Capturar(
            proveedor.Id, request.Banco, request.Clabe, request.NumeroCuenta, request.Swift,
            request.Moneda, request.Titular, _currentUser.UserId, _currentUser.UserName, _clock.UtcNow);

        var duplicada = await _db.CuentasBancariasProveedor.AsNoTracking().AnyAsync(c =>
            c.ProveedorId == proveedor.Id && c.Estado != EstadoCuentaBancariaProveedor.Rechazada &&
            (cuenta.Clabe != null ? c.Clabe == cuenta.Clabe : c.NumeroCuenta == cuenta.NumeroCuenta), cancellationToken);
        if (duplicada)
            throw new ConflictException("PROVEEDOR_CUENTA_DUPLICADA",
                $"La cuenta {Clabe.Enmascarar(cuenta.Identificador)} ya está registrada para este proveedor.");

        _db.CuentasBancariasProveedor.Add(cuenta);
        await _db.SaveChangesAsync(cancellationToken);
        await CuentasBancariasProveedor.AuditarAsync(_audit, _currentUser, _empresa, proveedor, cuenta,
            "proveedor.cuenta-agregada", "agregada, queda por validar", null, cancellationToken);
        return CuentasBancariasProveedor.ADto(cuenta, true, _currentUser.UserId);
    }
}

public sealed record ValidarCuentaBancariaProveedorCommand(
    Guid ProveedorId, Guid CuentaId, int VersionEsperada, Guid DocumentoAdjuntoId, bool CotejoCompleto, string? Nota)
    : IRequest<CuentaBancariaProveedorDto>;

public sealed class ValidarCuentaBancariaProveedorHandler
    : IRequestHandler<ValidarCuentaBancariaProveedorCommand, CuentaBancariaProveedorDto>
{
    /// <summary>Código del tipo de adjunto de proveedor que respalda la cuenta (seed 00000011-0001-…-06).</summary>
    public const string TipoDocumentoSoporte = "estado_cuenta_bancaria";

    private readonly CompartidoDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IClock _clock;
    private readonly IAuditLogWriter _audit;

    public ValidarCuentaBancariaProveedorHandler(
        CompartidoDbContext db, ICurrentUserContext currentUser, ICurrentEmpresaContext empresa, IClock clock, IAuditLogWriter audit)
    {
        _db = db; _currentUser = currentUser; _empresa = empresa; _clock = clock; _audit = audit;
    }

    public async Task<CuentaBancariaProveedorDto> Handle(
        ValidarCuentaBancariaProveedorCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await CuentasBancariasProveedor.ProveedorAsync(_db, request.ProveedorId, cancellationToken);
        var cuenta = await CuentasBancariasProveedor.CuentaAsync(_db, request.ProveedorId, request.CuentaId, request.VersionEsperada, cancellationToken);

        var ahora = _clock.UtcNow;
        var documento = await (
            from a in _db.Adjuntos.AsNoTracking()
            join t in _db.AdjuntoTiposDocumento.AsNoTracking() on a.TipoDocumentoId equals t.Id
            where a.Id == request.DocumentoAdjuntoId && a.EntidadId == proveedor.Id
                  && t.TipoEntidad == "proveedor" && t.Codigo == TipoDocumentoSoporte
            select a).FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("PROVEEDOR_CUENTA_SIN_DOCUMENTO",
                "Adjunta al proveedor el estado de cuenta o la carta bancaria y selecciónalo para validar.");
        if (documento.EstadoEn(DateOnly.FromDateTime(ahora.UtcDateTime)) is EstadoAdjunto.Baja or EstadoAdjunto.Vencido)
            throw new BusinessRuleException("PROVEEDOR_CUENTA_DOCUMENTO_VENCIDO",
                "El estado de cuenta o la carta bancaria está vencido o dado de baja: pide uno de los últimos 3 meses.");

        cuenta.Validar(_currentUser.UserId, _currentUser.UserName, documento.Id, request.CotejoCompleto, request.Nota, ahora);
        var hayPredeterminada = await _db.CuentasBancariasProveedor.AnyAsync(c =>
            c.ProveedorId == proveedor.Id && c.Moneda == cuenta.Moneda && c.Predeterminada && c.Id != cuenta.Id, cancellationToken);
        if (!hayPredeterminada) cuenta.MarcarPredeterminada(true);
        await _db.SaveChangesAsync(cancellationToken);
        await CuentasBancariasProveedor.AuditarAsync(_audit, _currentUser, _empresa, proveedor, cuenta,
            "proveedor.cuenta-validada", "validada contra estado de cuenta", request.Nota, cancellationToken);
        return CuentasBancariasProveedor.ADto(cuenta, true, _currentUser.UserId);
    }
}

public enum AccionCuentaBancariaProveedor { Rechazar, Inhabilitar, Reactivar, MarcarPredeterminada }

public sealed record CambiarCuentaBancariaProveedorCommand(
    Guid ProveedorId, Guid CuentaId, int VersionEsperada, AccionCuentaBancariaProveedor Accion, string? Motivo)
    : IRequest<CuentaBancariaProveedorDto>;

public sealed class CambiarCuentaBancariaProveedorHandler
    : IRequestHandler<CambiarCuentaBancariaProveedorCommand, CuentaBancariaProveedorDto>
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IAuditLogWriter _audit;

    public CambiarCuentaBancariaProveedorHandler(
        CompartidoDbContext db, ICurrentUserContext currentUser, ICurrentEmpresaContext empresa, IAuditLogWriter audit)
    {
        _db = db; _currentUser = currentUser; _empresa = empresa; _audit = audit;
    }

    public async Task<CuentaBancariaProveedorDto> Handle(
        CambiarCuentaBancariaProveedorCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await CuentasBancariasProveedor.ProveedorAsync(_db, request.ProveedorId, cancellationToken);
        var cuenta = await CuentasBancariasProveedor.CuentaAsync(_db, request.ProveedorId, request.CuentaId, request.VersionEsperada, cancellationToken);
        var motivo = request.Motivo ?? string.Empty;
        string operacion, resumen;
        switch (request.Accion)
        {
            case AccionCuentaBancariaProveedor.Rechazar:
                cuenta.Rechazar(motivo); operacion = "rechazada"; resumen = "rechazada";
                break;
            case AccionCuentaBancariaProveedor.Inhabilitar:
                cuenta.Inhabilitar(motivo); operacion = "inhabilitada"; resumen = "inhabilitada";
                break;
            case AccionCuentaBancariaProveedor.Reactivar:
                cuenta.Reactivar(motivo); operacion = "reactivada"; resumen = $"reactivada ({cuenta.Estado})";
                break;
            case AccionCuentaBancariaProveedor.MarcarPredeterminada:
                if (cuenta.Estado != EstadoCuentaBancariaProveedor.Activa)
                    throw new BusinessRuleException("PROVEEDOR_CUENTA_NO_PREDETERMINABLE", "Solo una cuenta activa puede ser la predeterminada.");
                var otras = await _db.CuentasBancariasProveedor.Where(c =>
                    c.ProveedorId == proveedor.Id && c.Moneda == cuenta.Moneda && c.Predeterminada && c.Id != cuenta.Id)
                    .ToListAsync(cancellationToken);
                otras.ForEach(c => c.MarcarPredeterminada(false));
                cuenta.MarcarPredeterminada(true); operacion = "predeterminada"; resumen = $"predeterminada para {cuenta.Moneda}";
                break;
            default:
                throw new BusinessRuleException("PROVEEDOR_CUENTA_ACCION_INVALIDA", "Acción no válida.");
        }
        await _db.SaveChangesAsync(cancellationToken);
        await CuentasBancariasProveedor.AuditarAsync(_audit, _currentUser, _empresa, proveedor, cuenta,
            $"proveedor.cuenta-{operacion}", resumen, request.Motivo, cancellationToken);
        return CuentasBancariasProveedor.ADto(cuenta, true, _currentUser.UserId);
    }
}

internal static class CuentasBancariasProveedor
{
    public static CuentaBancariaProveedorDto ADto(CuentaBancariaProveedor c, bool verCompleta, Guid? usuarioId) => new(
        c.Id, c.Banco, verCompleta ? c.Identificador : Clabe.Enmascarar(c.Identificador), c.Clabe is not null, c.Swift,
        c.Moneda, c.Titular, c.Predeterminada, c.Estado, c.CapturadaPorNombre, c.CapturadaEn,
        usuarioId is not null && usuarioId == c.CapturadaPorId, c.ValidadaPorNombre, c.ValidadaEn,
        c.DocumentoAdjuntoId, c.NotaValidacion, c.MotivoEstado, verCompleta, c.Version);

    public static async Task<Proveedor> ProveedorAsync(CompartidoDbContext db, Guid proveedorId, CancellationToken ct) =>
        await db.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == proveedorId, ct)
        ?? throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO", $"No existe proveedor con id '{proveedorId}'.");

    public static async Task<CuentaBancariaProveedor> CuentaAsync(
        CompartidoDbContext db, Guid proveedorId, Guid cuentaId, int versionEsperada, CancellationToken ct)
    {
        var cuenta = await db.CuentasBancariasProveedor.FirstOrDefaultAsync(c => c.Id == cuentaId && c.ProveedorId == proveedorId, ct)
            ?? throw new EntityNotFoundException("PROVEEDOR_CUENTA_NO_ENCONTRADA", $"No existe la cuenta '{cuentaId}' para este proveedor.");
        if (cuenta.Version != versionEsperada)
            throw new ConcurrencyException(nameof(CuentaBancariaProveedor), cuenta.Id);
        return cuenta;
    }

    /// <summary>Bitácora con el número enmascarado (nunca completo).</summary>
    public static Task AuditarAsync(
        IAuditLogWriter audit, ICurrentUserContext user, ICurrentEmpresaContext empresa, Proveedor proveedor,
        CuentaBancariaProveedor cuenta, string operacion, string resumen, string? motivo, CancellationToken ct) =>
        audit.RegistrarAsync(
            operacion: operacion,
            modulo: "DatosMaestros",
            entidad: "CuentaBancariaProveedor",
            entidadId: cuenta.Id,
            aggregateRootId: proveedor.Id,
            actorNombre: user.UserName ?? "Usuario",
            actorTipo: "usuario",
            actorEmail: user.Email,
            entidadEtiqueta: $"{proveedor.Clave} · {cuenta.Banco} {Clabe.Enmascarar(cuenta.Identificador)}",
            resumen: $"Cuenta {cuenta.Banco} {Clabe.Enmascarar(cuenta.Identificador)} ({cuenta.Moneda}) del proveedor {proveedor.Clave} {resumen}.",
            usuarioId: user.UserId,
            empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new { estado = cuenta.Estado.ToString(), cuenta.Predeterminada, motivo }),
            cancellationToken: ct);
}
