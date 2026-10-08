using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Millet.Facturacion.Application.Repp.EmitirRepp;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Repp.Pendientes;

public sealed record RevisarReppPendienteCommand(Guid Id, string FormaPago, IReadOnlyList<RelacionRepp> Facturas) : IRequest;
public sealed record DescartarReppPendienteCommand(Guid Id, string Motivo) : IRequest;
public sealed record EmitirReppPendienteCommand(Guid Id) : IRequest<EmitirReppPendienteResultado>;
public sealed record EmitirReppPendientesLoteCommand(IReadOnlyList<Guid> Ids) : IRequest<IReadOnlyList<EmitirReppPendienteResultado>>;
public sealed record EmitirReppPendienteResultado(Guid Id, bool Emitido, Guid? ReciboPagoId, string? Codigo, string? Mensaje);

public sealed class RevisarReppPendienteValidator : AbstractValidator<RevisarReppPendienteCommand>
{
    public RevisarReppPendienteValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.FormaPago).NotEmpty().MaximumLength(5);
        RuleFor(c => c.Facturas).NotEmpty();
        RuleForEach(c => c.Facturas).ChildRules(f =>
        {
            f.RuleFor(l => l.FacturaVentaId).NotEmpty();
            f.RuleFor(l => l.Importe).GreaterThan(0).PrecisionScale(18, 6, true);
        });
    }
}

public sealed class DescartarReppPendienteValidator : AbstractValidator<DescartarReppPendienteCommand>
{
    public DescartarReppPendienteValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(1000);
    }
}

public sealed class EmitirReppPendientesLoteValidator : AbstractValidator<EmitirReppPendientesLoteCommand>
{
    public EmitirReppPendientesLoteValidator()
    {
        RuleFor(c => c.Ids).NotEmpty().Must(ids => ids is { Count: <= 50 }).WithMessage("El lote admite hasta 50 pendientes.");
        RuleForEach(c => c.Ids).NotEmpty();
        RuleFor(c => c.Ids).Must(ids => ids is null || ids.Distinct().Count() == ids.Count).WithMessage("No repitas pendientes en el lote.");
    }
}

public sealed class RevisarReppPendienteHandler(FacturacionDbContext db, ReppPendienteServicio servicio)
    : IRequestHandler<RevisarReppPendienteCommand>
{
    public async Task Handle(RevisarReppPendienteCommand c, CancellationToken cancellationToken)
    {
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var p = await servicio.ObtenerAsync(c.Id, true, cancellationToken);
        await servicio.ValidarAsync(p, c.FormaPago, c.Facturas, cancellationToken);
        p.Revisar(c.FormaPago, c.Facturas);
        await db.SaveChangesAsync(cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);
    }
}

public sealed class DescartarReppPendienteHandler(FacturacionDbContext db, ReppPendienteServicio servicio)
    : IRequestHandler<DescartarReppPendienteCommand>
{
    public async Task Handle(DescartarReppPendienteCommand c, CancellationToken cancellationToken)
    {
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var p = await servicio.ObtenerAsync(c.Id, true, cancellationToken);
        p.Descartar(c.Motivo);
        await db.SaveChangesAsync(cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);
    }
}

public sealed class EmitirReppPendienteHandler(FacturacionDbContext db, ReppPendienteServicio servicio,
    IReppBancarioReadPort bancario, ISender sender, ILogger<EmitirReppPendienteHandler>? logger = null) : IRequestHandler<EmitirReppPendienteCommand, EmitirReppPendienteResultado>
{
    public async Task<EmitirReppPendienteResultado> Handle(EmitirReppPendienteCommand c, CancellationToken cancellationToken)
    {
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var p = await servicio.ObtenerAsync(c.Id, true, cancellationToken);
        if (p.Estado == EstadoReppPendiente.Emitido)
            return new(p.Id, true, p.ReciboPagoId, null, null);
        try
        {
            var relacion = p.Facturas.Select(f => new RelacionRepp(f.FacturaVentaId, f.Importe)).ToArray();
            if (db.Database.IsRelational())
                foreach (var facturaId in relacion.Select(f => f.FacturaVentaId).Distinct().Order())
                    await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM facturacion.comprobante WHERE id = {facturaId} AND empresa_id = {p.EmpresaId} FOR UPDATE").ToListAsync(cancellationToken);
            await servicio.ValidarAsync(p, p.FormaPago, relacion, cancellationToken);
            var tc = await servicio.TipoCambioAsync(p, cancellationToken);
            var sucursal = await bancario.SucursalEmisoraAsync(cancellationToken);
            // Propiedad interna: el endpoint manual no puede asociar un pendiente desde el body.
            await sender.Send(new EmitirReppCommand(sucursal,
                new DateTimeOffset(p.FechaValor.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-6)).ToUniversalTime(),
                p.Moneda, tc, p.FormaPago, null, null, p.Referencia,
                relacion.Select(f => new ReppFacturaPago(f.FacturaVentaId, f.Importe)).ToArray())
                { Pendiente = p }, cancellationToken);
            // Recibo, outbox y estado del pendiente fueron guardados por EmitirReppHandler.
        }
        catch (BusinessRuleException ex)
        {
            if (p.Estado == EstadoReppPendiente.Pendiente)
            {
                p.RegistrarError(ex.Code, ex.Message);
                await db.SaveChangesAsync(cancellationToken);
            }
            if (tx is not null) await tx.CommitAsync(cancellationToken);
            return new(p.Id, false, p.IntentoReciboPagoId, ex.Code, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Error de emisión del pendiente REP {PendienteId}", p.Id);
            if (tx is not null)
            {
                await tx.RollbackAsync(cancellationToken);
                await tx.DisposeAsync();
            }
            db.ChangeTracker.Clear();
            var fallido = await servicio.ObtenerAsync(c.Id, false, cancellationToken);
            fallido.RegistrarError("REPP_RESULTADO_INCIERTO",
                "No se pudo confirmar el resultado. Verifica el intento con el PAC antes de volver a emitir.");
            await db.SaveChangesAsync(cancellationToken);
            return new(fallido.Id, false, fallido.IntentoReciboPagoId, fallido.UltimoErrorCodigo, fallido.UltimoErrorMensaje);
        }
        if (tx is not null) await tx.CommitAsync(cancellationToken);
        return new(p.Id, p.Estado == EstadoReppPendiente.Emitido,
            p.ReciboPagoId ?? p.IntentoReciboPagoId, p.UltimoErrorCodigo, p.UltimoErrorMensaje);
    }
}

public sealed class EmitirReppPendientesLoteHandler(IServiceScopeFactory scopes, ILogger<EmitirReppPendientesLoteHandler> logger)
    : IRequestHandler<EmitirReppPendientesLoteCommand, IReadOnlyList<EmitirReppPendienteResultado>>
{
    public async Task<IReadOnlyList<EmitirReppPendienteResultado>> Handle(EmitirReppPendientesLoteCommand c, CancellationToken cancellationToken)
    {
        var resultados = new List<EmitirReppPendienteResultado>();
        foreach (var id in c.Ids)
        {
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                resultados.Add(await scope.ServiceProvider.GetRequiredService<ISender>().Send(new EmitirReppPendienteCommand(id), cancellationToken));
            }
            catch (EntityNotFoundException ex) { resultados.Add(new(id, false, null, ex.Code, ex.Message)); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "No se pudo completar el pendiente REP {PendienteId} del lote", id);
                resultados.Add(new(id, false, null, "REPP_ERROR_OPERATIVO", "No se pudo completar este pago. Consulta la bandeja antes de reintentar."));
            }
        }
        return resultados;
    }
}
