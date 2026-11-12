using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Cheques;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Cheques;

// ============================================================================
// M-372 (TES-09-01/03/05): cheques recibidos. Registrar (en firme pendiente, sin movimiento), confirmar contra la línea
// del estado de cuenta o a mano con referencia (nace el ingreso), y rebote (contramovimiento + aviso a CxC).
// ============================================================================

public sealed record ChequeRecibidoResponse(
    Guid Id, Guid CuentaBancariaId, string Cuenta, Guid ClienteId, string? ClienteNombre, string BancoEmisor, string Numero,
    string Librador, decimal Importe, string Moneda, DateOnly FechaRecepcion, EstadoChequeRecibido Estado,
    Guid? MovimientoId, Guid? ExtractoLineaId, DateTimeOffset? ConfirmadoEn, Guid? ContramovimientoId,
    string? MotivoRebote, DateTimeOffset? RebotadoEn);

public sealed record LineaCandidataChequeResponse(Guid ConciliacionId, Guid ExtractoLineaId, DateOnly Fecha, string Descripcion,
    string? Referencia, decimal Monto);

// ------------------------------------------------------------------ Registrar

public sealed record RegistrarChequeRecibidoCommand(Guid CuentaBancariaId, Guid ClienteId, string BancoEmisor, string Numero,
    string Librador, decimal Importe, DateOnly FechaRecepcion) : IRequest<ChequeRecibidoResponse>;

public sealed class RegistrarChequeRecibidoValidator : AbstractValidator<RegistrarChequeRecibidoCommand>
{
    public RegistrarChequeRecibidoValidator()
    {
        RuleFor(x => x.CuentaBancariaId).NotEmpty().WithMessage("Selecciona la cuenta donde se depositó.");
        RuleFor(x => x.ClienteId).NotEmpty().WithMessage("Selecciona el cliente.");
        RuleFor(x => x.BancoEmisor).NotEmpty().WithMessage("El banco es obligatorio.").MaximumLength(80);
        RuleFor(x => x.Numero).NotEmpty().WithMessage("El número de cheque es obligatorio.").MaximumLength(30);
        RuleFor(x => x.Librador).NotEmpty().WithMessage("El librador es obligatorio.").MaximumLength(200);
        RuleFor(x => x.Importe).GreaterThan(0).WithMessage("El importe debe ser mayor a cero.");
    }
}

public sealed class RegistrarChequeRecibidoHandler(TesoreriaDbContext db, ICurrentEmpresaContext empresa, ICurrentUserContext usuario,
    IClienteReadPort clientes, IClock clock) : IRequestHandler<RegistrarChequeRecibidoCommand, ChequeRecibidoResponse>
{
    public async Task<ChequeRecibidoResponse> Handle(RegistrarChequeRecibidoCommand c, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var empresaId = empresa.Current ?? throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "El usuario no tiene una empresa seleccionada.");
        var cuenta = await db.CuentasBancarias.FirstOrDefaultAsync(x => x.Id == c.CuentaBancariaId, ct)
            ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA", "No se encontró la cuenta bancaria.");
        if ((await clientes.ObtenerVariosAsync([c.ClienteId], ct)).Count == 0)
            throw new BusinessRuleException("CHEQUE_CLIENTE_NO_EXISTE", "El cliente no existe.");
        var banco = c.BancoEmisor.Trim();
        var numero = c.Numero.Trim();
        if (await db.ChequesRecibidos.AnyAsync(x => x.BancoEmisor == banco && x.Numero == numero
                && x.Estado != EstadoChequeRecibido.Rebotado, ct))
            throw new ConflictException("CHEQUE_DUPLICADO", $"El cheque {numero} de {banco} ya está registrado.");

        var cheque = ChequeRecibido.Registrar(empresaId, cuenta, c.ClienteId, banco, numero, c.Librador, c.Importe,
            c.FechaRecepcion, usuario.UserId ?? Guid.Empty, clock.UtcNow);
        db.ChequesRecibidos.Add(cheque);
        await db.SaveChangesAsync(ct);
        return (await ChequesLectura.MapearAsync(db, clientes, [cheque], ct))[0];
    }
}

// ------------------------------------------------------------------ Confirmar

/// <summary>Con <c>ExtractoLineaId</c> se confirma contra la línea del estado de cuenta (fecha y referencia salen de ella);
/// sin ella, a mano con <c>FechaValor</c> y <c>Referencia</c>.</summary>
public sealed record ConfirmarChequeRecibidoCommand(Guid Id, Guid? ExtractoLineaId, DateOnly? FechaValor, string? Referencia)
    : IRequest<ChequeRecibidoResponse>;

public sealed class ConfirmarChequeRecibidoValidator : AbstractValidator<ConfirmarChequeRecibidoCommand>
{
    public ConfirmarChequeRecibidoValidator()
    {
        When(x => x.ExtractoLineaId is null, () =>
        {
            RuleFor(x => x.FechaValor).NotNull().WithMessage("Indica la fecha en que el banco acreditó el cheque.");
            RuleFor(x => x.Referencia).NotEmpty().WithMessage("Indica la referencia del banco.").MaximumLength(120);
        });
    }
}

public sealed class ConfirmarChequeRecibidoHandler(TesoreriaDbContext db, ICurrentUserContext usuario, IPeriodoContablePort periodo,
    IClienteReadPort clientes, IClock clock) : IRequestHandler<ConfirmarChequeRecibidoCommand, ChequeRecibidoResponse>
{
    public async Task<ChequeRecibidoResponse> Handle(ConfirmarChequeRecibidoCommand c, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var usuarioId = usuario.UserId ?? throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario.");
        var cheque = await db.ChequesRecibidos.FirstOrDefaultAsync(x => x.Id == c.Id, ct)
            ?? throw new EntityNotFoundException("CHEQUE_NO_ENCONTRADO", "No se encontró el cheque.");
        var cuenta = await db.CuentasBancarias.FirstAsync(x => x.Id == cheque.CuentaBancariaId, ct);
        var ahora = clock.UtcNow;

        ExtractoLinea? linea = null;
        DateOnly fecha;
        string referencia;
        if (c.ExtractoLineaId is Guid lineaId)
        {
            linea = await db.ExtractoLineas.FirstOrDefaultAsync(l => l.Id == lineaId, ct)
                ?? throw new EntityNotFoundException("LINEA_NO_ENCONTRADA", "No se encontró la línea del estado de cuenta.");
            var conciliacion = await db.Conciliaciones.AsNoTracking().FirstAsync(x => x.Id == linea.ConciliacionId, ct);
            if (conciliacion.Estado != EstadoConciliacion.Abierta)
                throw new BusinessRuleException("CONCILIACION_CERRADA", "No se puede modificar una conciliación cerrada.");
            if (conciliacion.CuentaBancariaId != cheque.CuentaBancariaId)
                throw new BusinessRuleException("MOVIMIENTO_OTRA_CUENTA", "La línea es de otra cuenta bancaria.");
            if (linea.Estado is not (EstadoExtractoLinea.Pendiente or EstadoExtractoLinea.EnRevision))
                throw new BusinessRuleException("LINEA_YA_RESUELTA", "La línea ya está emparejada o en tránsito.");
            if (linea.Sentido != SentidoMovimiento.Ingreso || linea.Monto != cheque.Importe)
                throw new BusinessRuleException("MATCH_IMPORTE_NO_COINCIDE",
                    $"La línea ({linea.Monto:N2}) no es un abono por el importe del cheque ({cheque.Importe:N2}).");
            fecha = linea.Fecha;
            referencia = string.IsNullOrWhiteSpace(linea.Referencia) ? linea.Descripcion : linea.Referencia;
        }
        else
        {
            fecha = c.FechaValor!.Value;
            referencia = c.Referencia!;
        }

        if (!await periodo.EstaAbiertoAsync(fecha.Year, fecha.Month, ct))
            throw new BusinessRuleException("MOV_PERIODO_CERRADO", $"El período {fecha.Year}/{fecha.Month:00} está cerrado.");

        var mov = cheque.Confirmar(cuenta, fecha, referencia, linea?.Id, usuarioId, ahora);
        db.MovimientosBancarios.Add(mov);
        if (linea is not null)
        {
            linea.Emparejar(mov.Id, MatchTipo.Manual, linea.Monto, usuarioId, ahora);
            mov.MarcarConciliado();
        }
        await db.SaveChangesAsync(ct);
        return (await ChequesLectura.MapearAsync(db, clientes, [cheque], ct))[0];
    }
}

// ------------------------------------------------------------------ Rebote

public sealed record RebotarChequeRecibidoCommand(Guid Id, DateOnly Fecha, string Motivo) : IRequest<ChequeRecibidoResponse>;

public sealed class RebotarChequeRecibidoValidator : AbstractValidator<RebotarChequeRecibidoCommand>
{
    public RebotarChequeRecibidoValidator()
    {
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("El motivo es obligatorio.").MaximumLength(300);
    }
}

public sealed class RebotarChequeRecibidoHandler(TesoreriaDbContext db, ICurrentUserContext usuario, IPeriodoContablePort periodo,
    IIntegrationEventPublisher publisher, IClienteReadPort clientes, IClock clock)
    : IRequestHandler<RebotarChequeRecibidoCommand, ChequeRecibidoResponse>
{
    public async Task<ChequeRecibidoResponse> Handle(RebotarChequeRecibidoCommand c, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var usuarioId = usuario.UserId ?? throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario.");
        var cheque = await db.ChequesRecibidos.FirstOrDefaultAsync(x => x.Id == c.Id, ct)
            ?? throw new EntityNotFoundException("CHEQUE_NO_ENCONTRADO", "No se encontró el cheque.");
        MovimientoBancario? ingreso = null;
        if (cheque.MovimientoId is Guid movId)
        {
            ingreso = await db.MovimientosBancarios.FirstAsync(m => m.Id == movId, ct);
            if (!await periodo.EstaAbiertoAsync(c.Fecha.Year, c.Fecha.Month, ct))
                throw new BusinessRuleException("MOV_PERIODO_CERRADO", $"El período {c.Fecha.Year}/{c.Fecha.Month:00} está cerrado.");
        }
        var ahora = clock.UtcNow;
        var contra = cheque.Rebotar(ingreso, c.Fecha, c.Motivo, usuarioId, ahora);
        if (contra is not null) db.MovimientosBancarios.Add(contra);
        await publisher.PublishAsync(new ChequeRebotadoIntegrationEvent(cheque.EmpresaId, ahora, cheque.Id, cheque.ClienteId,
            cheque.BancoEmisor, cheque.Numero, cheque.Importe, cheque.Moneda, cheque.MotivoRebote!, contra is not null), ct);
        await db.SaveChangesAsync(ct);
        return (await ChequesLectura.MapearAsync(db, clientes, [cheque], ct))[0];
    }
}

// ------------------------------------------------------------------ Consultas

public sealed record ChequesRecibidosQuery(EstadoChequeRecibido? Estado) : IRequest<IReadOnlyList<ChequeRecibidoResponse>>;

public sealed class ChequesRecibidosHandler(TesoreriaDbContext db, IClienteReadPort clientes)
    : IRequestHandler<ChequesRecibidosQuery, IReadOnlyList<ChequeRecibidoResponse>>
{
    public async Task<IReadOnlyList<ChequeRecibidoResponse>> Handle(ChequesRecibidosQuery q, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var cheques = await db.ChequesRecibidos.AsNoTracking()
            .Where(x => q.Estado == null || x.Estado == q.Estado)
            .OrderByDescending(x => x.FechaRecepcion).ThenByDescending(x => x.RegistradoEn)
            .Take(500).ToListAsync(ct);
        return await ChequesLectura.MapearAsync(db, clientes, cheques, ct);
    }
}

/// <summary>Abonos sin emparejar del estado de cuenta, en conciliaciones abiertas de la cuenta, por el importe del cheque.</summary>
public sealed record LineasCandidatasChequeQuery(Guid ChequeId) : IRequest<IReadOnlyList<LineaCandidataChequeResponse>>;

public sealed class LineasCandidatasChequeHandler(TesoreriaDbContext db)
    : IRequestHandler<LineasCandidatasChequeQuery, IReadOnlyList<LineaCandidataChequeResponse>>
{
    public async Task<IReadOnlyList<LineaCandidataChequeResponse>> Handle(LineasCandidatasChequeQuery q, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var cheque = await db.ChequesRecibidos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.ChequeId, ct)
            ?? throw new EntityNotFoundException("CHEQUE_NO_ENCONTRADO", "No se encontró el cheque.");
        var abiertas = db.Conciliaciones.Where(c => c.CuentaBancariaId == cheque.CuentaBancariaId && c.Estado == EstadoConciliacion.Abierta)
            .Select(c => c.Id);
        return await db.ExtractoLineas.AsNoTracking()
            .Where(l => abiertas.Contains(l.ConciliacionId) && l.Abono == cheque.Importe
                && (l.Estado == EstadoExtractoLinea.Pendiente || l.Estado == EstadoExtractoLinea.EnRevision))
            .OrderBy(l => l.Fecha)
            .Select(l => new LineaCandidataChequeResponse(l.ConciliacionId, l.Id, l.Fecha, l.Descripcion, l.Referencia, l.Abono!.Value))
            .ToListAsync(ct);
    }
}

internal static class ChequesLectura
{
    public static async Task<IReadOnlyList<ChequeRecibidoResponse>> MapearAsync(TesoreriaDbContext db, IClienteReadPort clientes,
        IReadOnlyList<ChequeRecibido> cheques, CancellationToken ct)
    {
        var cuentaIds = cheques.Select(x => x.CuentaBancariaId).Distinct().ToList();
        var cuentas = await db.CuentasBancarias.AsNoTracking().Where(x => cuentaIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => $"{x.Banco} ···{x.NumeroCuenta[^Math.Min(4, x.NumeroCuenta.Length)..]}", ct);
        var nombres = await clientes.ObtenerVariosAsync(cheques.Select(x => x.ClienteId).Distinct().ToList(), ct);
        return cheques.Select(x => new ChequeRecibidoResponse(x.Id, x.CuentaBancariaId, cuentas.GetValueOrDefault(x.CuentaBancariaId, ""),
            x.ClienteId, nombres.TryGetValue(x.ClienteId, out var n) ? n.RazonSocial : null, x.BancoEmisor, x.Numero, x.Librador,
            x.Importe, x.Moneda, x.FechaRecepcion, x.Estado, x.MovimientoId, x.ExtractoLineaId, x.ConfirmadoEn, x.ContramovimientoId,
            x.MotivoRebote, x.RebotadoEn)).ToList();
    }
}
