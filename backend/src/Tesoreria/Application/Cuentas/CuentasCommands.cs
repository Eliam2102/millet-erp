using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
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
            saldo + (c.SaldoInicial ?? 0),
            c.Version, c.Sucursal, c.Finalidad, c.Titular, c.Firmantes, c.SaldoInicial, c.FechaCorteSaldoInicial);
}

// --------------------------------------------------- Crear

public sealed record CrearCuentaBancariaCommand(
    string Banco,
    string NumeroCuenta,
    string? Clabe,
    string Moneda,
    string? CuentaContableRef = null,
    string? PerfilExtracto = null, string? Sucursal = null, string? Finalidad = null,
    string? Titular = null, string? Firmantes = null) : IRequest<SolicitudCuentaResponse>;

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
        RuleFor(c => c.Sucursal).MaximumLength(120);
        RuleFor(c => c.Finalidad).MaximumLength(400);
        RuleFor(c => c.Titular).MaximumLength(200);
        RuleFor(c => c.Firmantes).MaximumLength(1000);
    }
}

public sealed class CrearCuentaBancariaHandler(
    TesoreriaDbContext db, ICurrentEmpresaContext currentEmpresa, ICurrentUserContext currentUser, IClock clock,
    ICuentaContableReadPort cuentasContables)
    : IRequestHandler<CrearCuentaBancariaCommand, SolicitudCuentaResponse>
{
    public async Task<SolicitudCuentaResponse> Handle(CrearCuentaBancariaCommand command, CancellationToken cancellationToken)
    {
        if (currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada.");

        // El constructor valida y normaliza vía VOs (NumeroCuenta/Clabe); la cuenta no se guarda hasta aprobar (M-199).
        var cuenta = new CuentaBancaria(empresaId, command.Banco, command.NumeroCuenta,
            string.IsNullOrWhiteSpace(command.Clabe) ? null : command.Clabe, command.Moneda,
            string.IsNullOrWhiteSpace(command.CuentaContableRef) ? null : command.CuentaContableRef.Trim(),
            string.IsNullOrWhiteSpace(command.PerfilExtracto) ? null : command.PerfilExtracto.Trim());
        cuenta.ActualizarInventario(command.Sucursal, command.Finalidad, command.Titular, command.Firmantes);
        await CuentaContableBancos.ValidarAsync(cuentasContables, cuenta.CuentaContableRef, cancellationToken);

        // Pre-chequeo amable del único (empresa_id, numero_cuenta), también contra altas pendientes.
        var datos = SolicitudCuentaBancaria.Datos(cuenta);
        var pendientes = await db.SolicitudesCuentaBancaria.AsNoTracking()
            .Where(s => s.Tipo == TipoSolicitudCuenta.Alta && s.Estado == EstadoSolicitudCuenta.Pendiente).ToListAsync(cancellationToken);
        if (await db.CuentasBancarias.AnyAsync(c => c.NumeroCuenta == cuenta.NumeroCuenta, cancellationToken)
            || pendientes.Any(s => s.DatosPropuestos.NumeroCuenta == cuenta.NumeroCuenta))
            throw new ConflictException("CTA_NUMERO_DUPLICADO",
                $"Ya existe una cuenta bancaria o un alta pendiente con el número '{Clabe.Enmascarar(cuenta.NumeroCuenta)}' en la empresa.");

        var solicitud = SolicitudCuentaBancaria.Alta(empresaId, datos, currentUser.UserId ?? Guid.Empty, clock.UtcNow);
        db.SolicitudesCuentaBancaria.Add(solicitud);
        await db.SaveChangesAsync(cancellationToken);
        return SolicitudCuentaMapper.ToResponse(solicitud);
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
    int VersionEsperada, string? Sucursal = null, string? Finalidad = null,
    string? Titular = null, string? Firmantes = null) : IRequest<SolicitudCuentaResponse>;

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
        RuleFor(c => c.Sucursal).MaximumLength(120);
        RuleFor(c => c.Finalidad).MaximumLength(400);
        RuleFor(c => c.Titular).MaximumLength(200);
        RuleFor(c => c.Firmantes).MaximumLength(1000);
    }
}

public sealed class ActualizarCuentaBancariaHandler(
    TesoreriaDbContext db, ICurrentUserContext currentUser, IClock clock, ICuentaContableReadPort cuentasContables)
    : IRequestHandler<ActualizarCuentaBancariaCommand, SolicitudCuentaResponse>
{
    public async Task<SolicitudCuentaResponse> Handle(ActualizarCuentaBancariaCommand command, CancellationToken cancellationToken)
    {
        var cuenta = await db.CuentasBancarias.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == command.CuentaId, cancellationToken)
            ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA", $"No se encontró la cuenta bancaria '{command.CuentaId}'.");
        if (cuenta.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(CuentaBancaria), cuenta.Id);
        if (await db.SolicitudesCuentaBancaria.AnyAsync(s => s.CuentaBancariaId == cuenta.Id && s.Estado == EstadoSolicitudCuenta.Pendiente, cancellationToken))
            throw new ConflictException("CTA_SOLICITUD_PENDIENTE", "La cuenta ya tiene un cambio pendiente de autorizar.");

        var monedaNueva = command.Moneda.Trim().ToUpperInvariant();
        var tieneMovimientos = await db.MovimientosBancarios.AnyAsync(m => m.CuentaBancariaId == cuenta.Id, cancellationToken);
        if (monedaNueva != cuenta.Moneda && (tieneMovimientos || cuenta.SaldoInicial is not null))
            throw new BusinessRuleException("CTA_MONEDA_CON_MOVIMIENTOS",
                "No se puede cambiar la moneda de una cuenta con movimientos registrados.");

        // Se arma la versión propuesta sobre una copia sin seguimiento: la cuenta vigente no cambia hasta aprobar (M-199).
        var vigentes = SolicitudCuentaBancaria.Datos(cuenta);
        cuenta.ActualizarInventario(command.Sucursal, command.Finalidad, command.Titular, command.Firmantes);
        cuenta.ActualizarDatos(command.Banco, monedaNueva, command.CuentaContableRef, command.PerfilExtracto);
        if (command.LimpiarClabe) cuenta.CambiarClabe(null);
        else if (!string.IsNullOrWhiteSpace(command.Clabe)) cuenta.CambiarClabe(command.Clabe);
        var propuestos = SolicitudCuentaBancaria.Datos(cuenta);
        if (propuestos.CuentaContableRef != vigentes.CuentaContableRef)
            await CuentaContableBancos.ValidarAsync(cuentasContables, propuestos.CuentaContableRef, cancellationToken);

        var original = await db.CuentasBancarias.AsNoTracking().FirstAsync(c => c.Id == cuenta.Id, cancellationToken);
        var solicitud = SolicitudCuentaBancaria.Cambio(original, propuestos, currentUser.UserId ?? Guid.Empty, clock.UtcNow);
        db.SolicitudesCuentaBancaria.Add(solicitud);
        await db.SaveChangesAsync(cancellationToken);
        return SolicitudCuentaMapper.ToResponse(solicitud);
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
