using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Repp;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Repp;

// ============================================================================
// TES-PR8 (§3.6.b, TES-4 sabor b): registro del REPP que el proveedor
// emite a Millet por pagos PPD. UUID único + fecha + XML a Blob
// (ADR-0024) → publica tesoreria.repp-proveedor.recibido.v1 (contrato
// congelado desde TES-PR4) → CxP libera el motivo de revisión FALTA_REPP.
// Validación fiscal del UUID post-MVP [T-G10,
// PLATFORM-TODO(<ValidacionReppRecibido>)].
// ============================================================================

public sealed record RegistrarReppRecibidoCommand(
    Guid FacturaProveedorId,
    Guid UuidComplemento,
    DateOnly FechaComplemento,
    // XML del complemento en base64; opcional en MVP (registro manual con UUID, T-G3).
    string? XmlBase64 = null) : IRequest<ReppRecibidoResponse>;

public sealed record ReppRecibidoResponse(
    Guid Id,
    Guid FacturaProveedorId,
    Guid UuidComplemento,
    DateOnly FechaComplemento,
    string? XmlBlobRef,
    DateTimeOffset RegistradoEn);

public sealed class RegistrarReppRecibidoValidator : AbstractValidator<RegistrarReppRecibidoCommand>
{
    public RegistrarReppRecibidoValidator()
    {
        RuleFor(c => c.FacturaProveedorId).NotEmpty();
        RuleFor(c => c.UuidComplemento).NotEmpty();
        RuleFor(c => c.FechaComplemento).NotEmpty();
        RuleFor(c => c.XmlBase64)
            .Must(x => x is null || EsBase64(x))
            .WithMessage("El XML debe venir codificado en base64.");
    }

    private static bool EsBase64(string valor)
    {
        var buffer = new Span<byte>(new byte[(valor.Length * 3 / 4) + 4]);
        return Convert.TryFromBase64String(valor, buffer, out _);
    }
}

public sealed class RegistrarReppRecibidoHandler
    : IRequestHandler<RegistrarReppRecibidoCommand, ReppRecibidoResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IReppXmlBlobStorage _blob;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;

    public RegistrarReppRecibidoHandler(
        TesoreriaDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IReppXmlBlobStorage blob,
        IIntegrationEventPublisher publisher,
        IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser;
        _blob = blob; _publisher = publisher; _clock = clock;
    }

    public async Task<ReppRecibidoResponse> Handle(
        RegistrarReppRecibidoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario que registra el REPP.");

        // El pasivo debe existir en la proyección (todo pasivo autorizado
        // pasó por aquí; la fila persiste con saldo 0 tras el pago).
        var existePasivo = await _db.PasivosPendientesPago
            .AnyAsync(p => p.FacturaProveedorId == command.FacturaProveedorId, cancellationToken);
        if (!existePasivo)
            throw new EntityNotFoundException("PASIVO_NO_ENCONTRADO",
                $"El pasivo '{command.FacturaProveedorId}' no está en la proyección de Tesorería.");

        // Pre-check amable del UUID único; el índice ux_repp_recibido_uuid
        // es la red final ante concurrencia.
        var uuidYaRegistrado = await _db.ReppsProveedorRecibidos
            .AnyAsync(r => r.UuidComplemento == command.UuidComplemento, cancellationToken);
        if (uuidYaRegistrado)
            throw new BusinessRuleException("REPP_UUID_DUPLICADO",
                $"El complemento con UUID '{command.UuidComplemento}' ya está registrado.");

        var ahora = _clock.UtcNow;

        string? xmlBlobRef = null;
        if (!string.IsNullOrWhiteSpace(command.XmlBase64))
        {
            using var xml = new MemoryStream(Convert.FromBase64String(command.XmlBase64), writable: false);
            xmlBlobRef = await _blob.GuardarXmlAsync(
                command.UuidComplemento.ToString("D"), command.FechaComplemento, xml, cancellationToken);
        }

        var repp = ReppProveedorRecibido.Registrar(
            empresaId: empresaId,
            facturaProveedorId: command.FacturaProveedorId,
            uuidComplemento: command.UuidComplemento,
            fechaComplemento: command.FechaComplemento,
            xmlBlobRef: xmlBlobRef,
            registradoPor: usuarioId,
            ahora: ahora);
        _db.ReppsProveedorRecibidos.Add(repp);

        // Contrato congelado (TES-PR4): UuidComplementoPago viaja como
        // string y FechaComplemento como DateTimeOffset — así los espera
        // CxP para MarcarReppRecibido() (libera FALTA_REPP).
        await _publisher.PublishAsync(new ReppProveedorRecibidoIntegrationEvent(
            EmpresaId: empresaId,
            OcurridoEn: ahora,
            FacturaProveedorId: command.FacturaProveedorId,
            UuidComplementoPago: command.UuidComplemento.ToString("D").ToUpperInvariant(),
            FechaComplemento: new DateTimeOffset(
                command.FechaComplemento.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return new ReppRecibidoResponse(
            repp.Id, repp.FacturaProveedorId, repp.UuidComplemento,
            repp.FechaComplemento, repp.XmlBlobRef, repp.RegistradoEn);
    }
}
