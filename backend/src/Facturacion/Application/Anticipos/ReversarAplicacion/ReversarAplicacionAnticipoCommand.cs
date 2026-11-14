using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Anticipos.ReversarAplicacion;

/// <summary>
/// M-344: reversa interna autorizada de la aplicación de un anticipo a una factura. Devuelve el importe al saldo del
/// anticipo y publica el evento para que CxC lo devuelva al saldo de la factura, ambos ligados por el mismo
/// <c>ReversaId</c>. Quien aplicó el anticipo (emisor de la NC de amortización) no puede autorizar la reversa y el motivo
/// es obligatorio. «Interna»: la NC de amortización NO se cancela ante el SAT.
/// </summary>
public sealed record ReversarAplicacionAnticipoCommand(Guid AnticipoId, Guid FacturaVentaId, string Motivo)
    : IRequest<ReversaAplicacionAnticipoResponse>;

public sealed record ReversaAplicacionAnticipoResponse(
    Guid ReversaId, Guid AnticipoId, Guid FacturaVentaId, decimal Importe, string Motivo,
    string AutorizadoPorNombre, DateTimeOffset ReversadoEn, decimal SaldoAnticipo, string EstadoAnticipo);

public sealed class ReversarAplicacionAnticipoValidator : AbstractValidator<ReversarAplicacionAnticipoCommand>
{
    public ReversarAplicacionAnticipoValidator()
    {
        RuleFor(c => c.AnticipoId).NotEmpty();
        RuleFor(c => c.FacturaVentaId).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty()
            .Must(m => m.Trim().Length is >= ReversaAplicacionAnticipo.MotivoMin and <= ReversaAplicacionAnticipo.MotivoMax)
            .WithMessage($"El motivo debe tener de {ReversaAplicacionAnticipo.MotivoMin} a {ReversaAplicacionAnticipo.MotivoMax} caracteres.");
    }
}

public sealed class ReversarAplicacionAnticipoHandler(
    FacturacionDbContext db,
    ICurrentUserContext usuario,
    IUsuariosReadPort usuarios,
    IIntegrationEventPublisher eventos,
    IAuditLogWriter audit,
    ICurrentEmpresaContext empresa,
    IClock clock) : IRequestHandler<ReversarAplicacionAnticipoCommand, ReversaAplicacionAnticipoResponse>
{
    public async Task<ReversaAplicacionAnticipoResponse> Handle(ReversarAplicacionAnticipoCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = usuario.UserId ?? throw new ForbiddenException("USUARIO_REQUERIDO", "No se pudo identificar al usuario.");
        var anticipo = await db.Anticipos.Include(a => a.Vinculaciones)
            .FirstOrDefaultAsync(a => a.Id == request.AnticipoId, cancellationToken)
            ?? throw new EntityNotFoundException("ANTICIPO_NO_ENCONTRADO", "No existe el anticipo solicitado.");
        var vinculacion = anticipo.Vinculaciones.FirstOrDefault(v => v.FacturaVentaId == request.FacturaVentaId && v.NcAmortizacionId is not null)
            ?? throw new BusinessRuleException("ANTICIPO_SIN_APLICACION", "El anticipo no tiene una aplicación a esa factura que revertir.");

        // Quien aplicó = emisor de la NC de amortización de esa aplicación.
        var ncId = vinculacion.NcAmortizacionId!.Value;
        var aplicadoPor = await db.NotasCredito.AsNoTracking().Where(n => n.Id == ncId)
            .Select(n => n.UsuarioEmisorId).FirstOrDefaultAsync(cancellationToken);

        var nombres = await usuarios.ObtenerNombresAsync([usuarioId], cancellationToken);
        var nombre = nombres.TryGetValue(usuarioId, out var n) ? n : usuario.UserName ?? "Usuario";
        var ahora = clock.UtcNow;
        var reversa = anticipo.RevertirAplicacion(request.FacturaVentaId, request.Motivo, aplicadoPor, usuarioId, nombre, ahora);
        db.ReversasAplicacionAnticipo.Add(reversa);

        await eventos.PublishAsync(new AnticipoAplicacionReversadaIntegrationEvent(
            anticipo.EmpresaId, ahora, reversa.Id, anticipo.Id, reversa.FacturaVentaId, reversa.NcAmortizacionId,
            anticipo.ClienteId, reversa.Importe, reversa.Motivo), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await audit.RegistrarAsync(
            operacion: "anticipo.aplicacion-reversada", modulo: "Facturacion", entidad: "Anticipo",
            entidadId: anticipo.Id, aggregateRootId: anticipo.Id,
            actorNombre: nombre, actorTipo: "usuario", actorEmail: usuario.Email,
            entidadEtiqueta: anticipo.PedidoOrigenRef is { } p ? $"Anticipo · pedido {p}" : "Anticipo",
            resumen: $"Se revirtió la aplicación de {reversa.Importe:N2} {anticipo.Moneda} a la factura: {reversa.Motivo}",
            usuarioId: usuarioId, empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new { ReversaId = reversa.Id, reversa.FacturaVentaId, reversa.NcAmortizacionId, reversa.Importe }),
            cancellationToken: cancellationToken);

        return new ReversaAplicacionAnticipoResponse(reversa.Id, anticipo.Id, reversa.FacturaVentaId, reversa.Importe, reversa.Motivo,
            reversa.AutorizadoPorNombre, reversa.ReversadoEn, anticipo.Saldo, anticipo.Estado.ToString());
    }
}
