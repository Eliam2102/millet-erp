using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Cuentas;

// ============================================================================
// CRUD del catálogo de cuentas bancarias propias (TES-7 revisada: el CRUD
// vive en Tesorería con permiso dedicado `tesoreria.cuentas.administrar`;
// el seed script queda solo para bootstrap de ambientes).
//
// Reglas:
// - `NumeroCuenta` inmutable post-creación (identidad natural, único por
//   empresa — ux_cuenta_bancaria_empresa_numero); corrección = desactivar
//   y recrear.
// - `Moneda` editable solo mientras la cuenta no tenga movimientos.
// - CLABE write-only para el cliente: en actualización null = sin cambio,
//   `LimpiarClabe` = poner null (así el form no necesita des-enmascarar).
// - Respuestas con el mismo masking PII de SaldosPorCuentaQuery.
// ============================================================================

internal static class CuentaBancariaMapper
{
    public static CuentaSaldoResponse ToResponse(CuentaBancaria c, decimal saldo, bool verCompleta) =>
        new(c.Id,
            c.Banco,
            verCompleta ? c.NumeroCuenta : Clabe.Enmascarar(c.NumeroCuenta),
            c.Clabe is null ? null : verCompleta ? c.Clabe : Clabe.Enmascarar(c.Clabe),
            c.Moneda,
            c.CuentaContableRef,
            c.PerfilExtracto,
            c.Activa,
            saldo,
            c.Version);
}

// --------------------------------------------------- Crear

public sealed record CrearCuentaBancariaCommand(
    string Banco,
    string NumeroCuenta,
    string? Clabe,
    string Moneda,
    string? CuentaContableRef = null,
    string? PerfilExtracto = null) : IRequest<CuentaSaldoResponse>;

public sealed class CrearCuentaBancariaValidator : AbstractValidator<CrearCuentaBancariaCommand>
{
    public CrearCuentaBancariaValidator()
    {
        RuleFor(c => c.Banco).NotEmpty().MaximumLength(120);
        RuleFor(c => c.NumeroCuenta).NotEmpty().MaximumLength(40);
        RuleFor(c => c.Clabe).MaximumLength(18);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.CuentaContableRef).MaximumLength(40);
        RuleFor(c => c.PerfilExtracto).MaximumLength(40);
    }
}

public sealed class CrearCuentaBancariaHandler
    : IRequestHandler<CrearCuentaBancariaCommand, CuentaSaldoResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserPermissions _permissions;

    public CrearCuentaBancariaHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserPermissions permissions)
    {
        _db = db; _currentEmpresa = currentEmpresa; _permissions = permissions;
    }

    public async Task<CuentaSaldoResponse> Handle(
        CrearCuentaBancariaCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        // El constructor valida y normaliza vía VOs (NumeroCuenta/Clabe).
        var cuenta = new CuentaBancaria(
            empresaId,
            command.Banco,
            command.NumeroCuenta,
            string.IsNullOrWhiteSpace(command.Clabe) ? null : command.Clabe,
            command.Moneda,
            string.IsNullOrWhiteSpace(command.CuentaContableRef) ? null : command.CuentaContableRef.Trim(),
            string.IsNullOrWhiteSpace(command.PerfilExtracto) ? null : command.PerfilExtracto.Trim());

        // Pre-chequeo amable del único (empresa_id, numero_cuenta); el índice
        // ux_cuenta_bancaria_empresa_numero respalda ante carreras.
        var duplicada = await _db.CuentasBancarias
            .AnyAsync(c => c.NumeroCuenta == cuenta.NumeroCuenta, cancellationToken);
        if (duplicada)
            throw new ConflictException("CTA_NUMERO_DUPLICADO",
                $"Ya existe una cuenta bancaria con el número '{cuenta.NumeroCuenta}' en la empresa.");

        _db.CuentasBancarias.Add(cuenta);
        await _db.SaveChangesAsync(cancellationToken);

        var verCompleta = await _permissions.TieneAsync(
            PermisosCanonicos.TesoreriaMovimientosVerCuentaCompleta, cancellationToken);
        return CuentaBancariaMapper.ToResponse(cuenta, 0m, verCompleta);
    }
}

// --------------------------------------------------- Actualizar

public sealed record ActualizarCuentaBancariaCommand(
    Guid CuentaId,
    string Banco,
    string Moneda,
    string? Clabe,
    bool LimpiarClabe,
    string? CuentaContableRef,
    string? PerfilExtracto,
    int VersionEsperada) : IRequest<CuentaSaldoResponse>;

public sealed class ActualizarCuentaBancariaValidator : AbstractValidator<ActualizarCuentaBancariaCommand>
{
    public ActualizarCuentaBancariaValidator()
    {
        RuleFor(c => c.CuentaId).NotEmpty();
        RuleFor(c => c.Banco).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.Clabe).MaximumLength(18);
        RuleFor(c => c.Clabe)
            .Null()
            .When(c => c.LimpiarClabe)
            .WithMessage("No se puede enviar una CLABE nueva y LimpiarClabe a la vez.");
        RuleFor(c => c.CuentaContableRef).MaximumLength(40);
        RuleFor(c => c.PerfilExtracto).MaximumLength(40);
    }
}

public sealed class ActualizarCuentaBancariaHandler
    : IRequestHandler<ActualizarCuentaBancariaCommand, CuentaSaldoResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentUserPermissions _permissions;

    public ActualizarCuentaBancariaHandler(TesoreriaDbContext db, ICurrentUserPermissions permissions)
    {
        _db = db; _permissions = permissions;
    }

    public async Task<CuentaSaldoResponse> Handle(
        ActualizarCuentaBancariaCommand command, CancellationToken cancellationToken)
    {
        var cuenta = await _db.CuentasBancarias
            .FirstOrDefaultAsync(c => c.Id == command.CuentaId, cancellationToken)
            ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA",
                $"No se encontró la cuenta bancaria '{command.CuentaId}'.");
        if (cuenta.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(CuentaBancaria), cuenta.Id);

        var monedaNueva = command.Moneda.Trim().ToUpperInvariant();
        decimal saldo = 0m;
        var tieneMovimientos = false;
        var agregado = await _db.MovimientosBancarios.AsNoTracking()
            .Where(m => m.CuentaBancariaId == cuenta.Id)
            .GroupBy(m => m.CuentaBancariaId)
            .Select(g => new
            {
                Saldo = g.Sum(m => m.Sentido == SentidoMovimiento.Ingreso ? m.Monto : -m.Monto),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (agregado is not null)
        {
            saldo = agregado.Saldo;
            tieneMovimientos = true;
        }

        if (monedaNueva != cuenta.Moneda && tieneMovimientos)
            throw new BusinessRuleException("CTA_MONEDA_CON_MOVIMIENTOS",
                "No se puede cambiar la moneda de una cuenta con movimientos registrados.");

        cuenta.ActualizarDatos(command.Banco, monedaNueva, command.CuentaContableRef, command.PerfilExtracto);
        if (command.LimpiarClabe)
            cuenta.CambiarClabe(null);
        else if (!string.IsNullOrWhiteSpace(command.Clabe))
            cuenta.CambiarClabe(command.Clabe);

        await _db.SaveChangesAsync(cancellationToken);

        var verCompleta = await _permissions.TieneAsync(
            PermisosCanonicos.TesoreriaMovimientosVerCuentaCompleta, cancellationToken);
        return CuentaBancariaMapper.ToResponse(cuenta, saldo, verCompleta);
    }
}

// --------------------------------------------------- Activar / Desactivar

public sealed record CambiarEstadoCuentaBancariaCommand(
    Guid CuentaId,
    bool Activa,
    int VersionEsperada) : IRequest<CuentaSaldoResponse>;

public sealed class CambiarEstadoCuentaBancariaValidator
    : AbstractValidator<CambiarEstadoCuentaBancariaCommand>
{
    public CambiarEstadoCuentaBancariaValidator()
    {
        RuleFor(c => c.CuentaId).NotEmpty();
    }
}

public sealed class CambiarEstadoCuentaBancariaHandler
    : IRequestHandler<CambiarEstadoCuentaBancariaCommand, CuentaSaldoResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentUserPermissions _permissions;

    public CambiarEstadoCuentaBancariaHandler(TesoreriaDbContext db, ICurrentUserPermissions permissions)
    {
        _db = db; _permissions = permissions;
    }

    public async Task<CuentaSaldoResponse> Handle(
        CambiarEstadoCuentaBancariaCommand command, CancellationToken cancellationToken)
    {
        var cuenta = await _db.CuentasBancarias
            .FirstOrDefaultAsync(c => c.Id == command.CuentaId, cancellationToken)
            ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA",
                $"No se encontró la cuenta bancaria '{command.CuentaId}'.");
        if (cuenta.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(CuentaBancaria), cuenta.Id);

        if (command.Activa) cuenta.Activar();
        else cuenta.Desactivar();

        await _db.SaveChangesAsync(cancellationToken);

        var saldo = await _db.MovimientosBancarios.AsNoTracking()
            .Where(m => m.CuentaBancariaId == cuenta.Id)
            .SumAsync(m => m.Sentido == SentidoMovimiento.Ingreso ? m.Monto : -m.Monto, cancellationToken);
        var verCompleta = await _permissions.TieneAsync(
            PermisosCanonicos.TesoreriaMovimientosVerCuentaCompleta, cancellationToken);
        return CuentaBancariaMapper.ToResponse(cuenta, saldo, verCompleta);
    }
}
