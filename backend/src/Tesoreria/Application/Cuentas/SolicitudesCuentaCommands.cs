using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Bancos;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Cuentas;

// M-199: alta y cambio de cuenta bancaria con autorización del Director de Planeación Financiera (Javier Canché, 9 al 11-nov).
// M-200: la cuenta contable de la cuenta bancaria es afectable y de bancos (rol BANCOS o una variante, o hija de ellas).

public sealed record SolicitudCuentaResponse(
    Guid Id, TipoSolicitudCuenta Tipo, Guid? CuentaBancariaId, EstadoSolicitudCuenta Estado,
    DatosCuentaBancaria? DatosVigentes, DatosCuentaBancaria DatosPropuestos,
    Guid SolicitadoPor, DateTimeOffset SolicitadoEn, Guid? ResueltoPor, DateTimeOffset? ResueltoEn, string? MotivoRechazo,
    int? NumeroVersion = null,
    string? SolicitadoPorNombre = null,
    string? ResueltoPorNombre = null);

internal static class SolicitudCuentaMapper
{
    public static SolicitudCuentaResponse ToResponse(SolicitudCuentaBancaria s, int? numeroVersion = null) =>
        new(s.Id, s.Tipo, s.CuentaBancariaId, s.Estado, Enmascarar(s.DatosVigentes), Enmascarar(s.DatosPropuestos)!,
            s.SolicitadoPor, s.SolicitadoEn, s.ResueltoPor, s.ResueltoEn, s.MotivoRechazo, numeroVersion);

    // ADR-0006: número y CLABE enmascarados en la bandeja; se distinguen por los últimos 4 dígitos.
    private static DatosCuentaBancaria? Enmascarar(DatosCuentaBancaria? d) => d is null ? null : d with
    {
        NumeroCuenta = Clabe.Enmascarar(d.NumeroCuenta),
        Clabe = d.Clabe is null ? null : Clabe.Enmascarar(d.Clabe),
    };
}

internal static class CuentaContableBancos
{
    public const string Rol = "BANCOS";

    public static async Task ValidarAsync(ICuentaContableReadPort cuentas, string? codigo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return;
        var v = await cuentas.ValidarParaMovimientoAsync(codigo, OrigenMovimiento.Automatica, ct);
        if (v.Motivo == MotivoRechazoCuenta.NoExiste)
            throw new BusinessRuleException("CTA_CUENTA_CONTABLE_NO_EXISTE", "La cuenta contable no existe.");
        if (!v.Valida || !await cuentas.EsDeRolAsync(codigo, Rol, ct))
            throw new BusinessRuleException("CTA_CUENTA_CONTABLE_NO_BANCOS", "La cuenta debe ser afectable y de bancos.");
    }
}

// --------------------------------------------------- Aprobar / Rechazar

public sealed record AprobarSolicitudCuentaCommand(Guid SolicitudId) : IRequest<SolicitudCuentaResponse>;

public sealed record RechazarSolicitudCuentaCommand(Guid SolicitudId, string Motivo) : IRequest<SolicitudCuentaResponse>;

public sealed class RechazarSolicitudCuentaValidator : AbstractValidator<RechazarSolicitudCuentaCommand>
{
    public RechazarSolicitudCuentaValidator() =>
        RuleFor(c => c.Motivo).NotEmpty().WithMessage("El motivo es obligatorio.").MaximumLength(400);
}

public sealed class ResolverSolicitudCuentaHandler(TesoreriaDbContext db, ICurrentUserContext currentUser, IClock clock)
    : IRequestHandler<AprobarSolicitudCuentaCommand, SolicitudCuentaResponse>,
      IRequestHandler<RechazarSolicitudCuentaCommand, SolicitudCuentaResponse>
{
    public async Task<SolicitudCuentaResponse> Handle(AprobarSolicitudCuentaCommand command, CancellationToken cancellationToken)
    {
        var s = await CargarAsync(command.SolicitudId, cancellationToken);
        var cuenta = s.CuentaBancariaId is Guid id ? await db.CuentasBancarias.FirstOrDefaultAsync(c => c.Id == id, cancellationToken) : null;
        if (s.Tipo == TipoSolicitudCuenta.Alta)
        {
            var numero = s.DatosPropuestos.NumeroCuenta;
            if (await db.CuentasBancarias.AnyAsync(c => c.NumeroCuenta == numero, cancellationToken))
                throw new ConflictException("CTA_NUMERO_DUPLICADO", "Ya existe una cuenta bancaria con ese número en la empresa.");
        }
        var nueva = s.Aprobar(currentUser.UserId ?? Guid.Empty, clock.UtcNow, cuenta);
        if (nueva is not null) db.CuentasBancarias.Add(nueva);
        await db.SaveChangesAsync(cancellationToken);
        return SolicitudCuentaMapper.ToResponse(s);
    }

    public async Task<SolicitudCuentaResponse> Handle(RechazarSolicitudCuentaCommand command, CancellationToken cancellationToken)
    {
        var s = await CargarAsync(command.SolicitudId, cancellationToken);
        s.Rechazar(currentUser.UserId ?? Guid.Empty, command.Motivo, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return SolicitudCuentaMapper.ToResponse(s);
    }

    private async Task<SolicitudCuentaBancaria> CargarAsync(Guid id, CancellationToken ct) =>
        await db.SolicitudesCuentaBancaria.FirstOrDefaultAsync(s => s.Id == id, ct)
        ?? throw new EntityNotFoundException("CTA_SOLICITUD_NO_ENCONTRADA", "No se encontró la solicitud.");
}

// --------------------------------------------------- Consultas

/// <summary>Bandeja de solicitudes; sin estado = todas, más recientes primero.</summary>
public sealed record ListarSolicitudesCuentaQuery(EstadoSolicitudCuenta? Estado = null) : IRequest<IReadOnlyList<SolicitudCuentaResponse>>;

/// <summary>Historial de versiones de una cuenta: las solicitudes aprobadas, v1 el alta.</summary>
public sealed record HistorialCuentaQuery(Guid CuentaId) : IRequest<IReadOnlyList<SolicitudCuentaResponse>>;

public sealed class SolicitudesCuentaQueriesHandler(TesoreriaDbContext db, Millet.Tesoreria.Domain.Ports.ICorteNombresReadPort? nombres = null)
    : IRequestHandler<ListarSolicitudesCuentaQuery, IReadOnlyList<SolicitudCuentaResponse>>,
      IRequestHandler<HistorialCuentaQuery, IReadOnlyList<SolicitudCuentaResponse>>
{
    public async Task<IReadOnlyList<SolicitudCuentaResponse>> Handle(ListarSolicitudesCuentaQuery request, CancellationToken cancellationToken) =>
        await ConNombresAsync((await db.SolicitudesCuentaBancaria.AsNoTracking()
            .Where(s => request.Estado == null || s.Estado == request.Estado)
            .OrderByDescending(s => s.SolicitadoEn).Take(200).ToListAsync(cancellationToken))
            .Select(s => SolicitudCuentaMapper.ToResponse(s)).ToList(), cancellationToken);

    public async Task<IReadOnlyList<SolicitudCuentaResponse>> Handle(HistorialCuentaQuery request, CancellationToken cancellationToken) =>
        await ConNombresAsync((await db.SolicitudesCuentaBancaria.AsNoTracking()
            .Where(s => s.CuentaBancariaId == request.CuentaId && s.Estado == EstadoSolicitudCuenta.Aprobada)
            .OrderBy(s => s.ResueltoEn).ToListAsync(cancellationToken))
            .Select((s, i) => SolicitudCuentaMapper.ToResponse(s, i + 1)).ToList(), cancellationToken);

    private async Task<IReadOnlyList<SolicitudCuentaResponse>> ConNombresAsync(List<SolicitudCuentaResponse> lista, CancellationToken ct)
    {
        if (nombres is null || lista.Count == 0) return lista;
        var ids = lista.SelectMany(s => new[] { (Guid?)s.SolicitadoPor, s.ResueltoPor }).OfType<Guid>().Distinct().ToList();
        var n = await nombres.NombresCajerosAsync(ids, ct);
        return lista.Select(s => s with
        {
            SolicitadoPorNombre = n.GetValueOrDefault(s.SolicitadoPor),
            ResueltoPorNombre = s.ResueltoPor is Guid r ? n.GetValueOrDefault(r) : null,
        }).ToList();
    }
}
