using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Cfdi.SolicitudesCancelacion;

/// <summary>
/// Decide la solicitud. Es solo una decisión interna: no cambia el CFDI ni el pasivo. Si se acepta y
/// después el SAT reporta el CFDI como Cancelado, la sincronización (<c>FiscalCfdiReceiverAdapter</c>)
/// cancela el pasivo o lo manda a revisión; si el SAT sigue diciendo Vigente, no pasa nada.
/// <para>
/// M-096: aceptar la cancelación de un CFDI cuya factura ya tiene pagos (también parciales) exige que
/// el proveedor ya haya mandado el CFDI sustituto —elegido del buzón de CFDI ya ingresados y relacionado
/// con tipo 04 al UUID cancelado— y el permiso del Contador General. Rechazar siempre se permite.
/// </para>
/// </summary>
public sealed record DecidirSolicitudCancelacionCommand(
    Guid CfdiId,
    Guid SolicitudId,
    bool Aceptar,
    string Comentario,
    string? EvidenciaBlobRef,
    string? EvidenciaNombre,
    Guid? SustitutoCfdiId = null) : IRequest<SolicitudCancelacionResponse>;

public sealed class DecidirSolicitudCancelacionValidator : AbstractValidator<DecidirSolicitudCancelacionCommand>
{
    public DecidirSolicitudCancelacionValidator()
    {
        RuleFor(c => c.CfdiId).NotEmpty();
        RuleFor(c => c.SolicitudId).NotEmpty();
        RuleFor(c => c.Comentario).NotEmpty().WithErrorCode("SOLICITUD_CANCELACION_EVIDENCIA_VACIA")
            .WithMessage("La decisión requiere un comentario de evidencia.").MaximumLength(1000);
        RuleFor(c => c.EvidenciaBlobRef).MaximumLength(400);
        RuleFor(c => c.EvidenciaNombre).MaximumLength(255);
    }
}

public sealed class DecidirSolicitudCancelacionHandler
    : IRequestHandler<DecidirSolicitudCancelacionCommand, SolicitudCancelacionResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;
    private readonly ICurrentUserPermissions _permisos;
    private readonly ICfdiBlobStorage _blobStorage;
    private readonly IXmlCfdiParser _parser;

    public DecidirSolicitudCancelacionHandler(
        CuentasPorPagarDbContext db, ICurrentUserContext currentUser, IClock clock,
        ICurrentUserPermissions permisos, ICfdiBlobStorage blobStorage, IXmlCfdiParser parser)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
        _permisos = permisos; _blobStorage = blobStorage; _parser = parser;
    }

    public async Task<SolicitudCancelacionResponse> Handle(
        DecidirSolicitudCancelacionCommand command, CancellationToken cancellationToken)
    {
        var solicitud = await _db.SolicitudesCancelacionCfdi
            .FirstOrDefaultAsync(s => s.Id == command.SolicitudId && s.CfdiRecibidoId == command.CfdiId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SOLICITUD_CANCELACION_NO_ENCONTRADA", $"No existe la solicitud '{command.SolicitudId}' para ese CFDI.");

        Guid? autorizadoPor = null;
        if (command.Aceptar && await TieneFacturaConPagosAsync(command.CfdiId, cancellationToken))
        {
            if (!await _permisos.TieneAsync(PermisosCanonicos.CuentasPorPagarCfdiAceptarCancelacionPagada, cancellationToken))
                throw new ForbiddenException("CANCELACION_PAGADA_SIN_PERMISO",
                    "La factura de este CFDI ya tiene pagos: aceptar su cancelación requiere la autorización del Contador General.");
            await ValidarSustitutoAsync(command, cancellationToken);
            autorizadoPor = _currentUser.UserId;
        }

        var ahora = _clock.UtcNow;
        solicitud.Decidir(command.Aceptar, command.Comentario, command.EvidenciaBlobRef, command.EvidenciaNombre,
            _currentUser.UserId, ahora, command.SustitutoCfdiId, autorizadoPor);

        await _db.SaveChangesAsync(cancellationToken);
        return SolicitudCancelacionResponse.De(solicitud);
    }

    /// <summary>Hay una factura vigente ligada al CFDI con al menos un pago aplicado (parcial o total).</summary>
    private Task<bool> TieneFacturaConPagosAsync(Guid cfdiId, CancellationToken ct) =>
        _db.FacturasProveedor.AsNoTracking().AnyAsync(
            f => f.CfdiRecibidoId == cfdiId && f.Estado != EstadoPasivo.Cancelada && f.ImportePagado > 0m, ct);

    private async Task ValidarSustitutoAsync(DecidirSolicitudCancelacionCommand command, CancellationToken ct)
    {
        if (command.SustitutoCfdiId is not { } sustitutoId || sustitutoId == command.CfdiId)
            throw new BusinessRuleException("CANCELACION_PAGADA_SIN_SUSTITUTO",
                "La factura ya tiene pagos: la cancelación solo se acepta cuando el proveedor ya mandó el CFDI que la sustituye. " +
                "Elige el CFDI sustituto del buzón.");

        var original = await _db.CfdisRecibidos.AsNoTracking().FirstAsync(c => c.Id == command.CfdiId, ct);
        var sustituto = await _db.CfdisRecibidos.AsNoTracking().FirstOrDefaultAsync(c => c.Id == sustitutoId, ct)
            ?? throw new BusinessRuleException("CANCELACION_SUSTITUTO_NO_ENCONTRADO",
                "El CFDI sustituto no está ingresado en el buzón.");
        if (!string.Equals(sustituto.RfcEmisor.Valor, original.RfcEmisor.Valor, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("CANCELACION_SUSTITUTO_OTRO_PROVEEDOR",
                "El CFDI sustituto debe ser del mismo proveedor que el CFDI que se cancela.");
        if (string.IsNullOrEmpty(sustituto.XmlBlobRef))
            throw new BusinessRuleException("CANCELACION_SUSTITUTO_SIN_XML",
                "El CFDI sustituto no tiene XML almacenado; no se puede comprobar su relación con el cancelado.");

        await using var xml = await _blobStorage.LeerXmlAsync(sustituto.XmlBlobRef, ct)
            ?? throw new BusinessRuleException("CANCELACION_SUSTITUTO_SIN_XML",
                "El XML del CFDI sustituto no está disponible; no se puede comprobar su relación con el cancelado.");
        var relaciona = _parser.Parsear(xml).CfdiRelacionados?.Any(r =>
                r.TipoRelacion == "04"
                && r.Uuids.Any(u => string.Equals(u, original.UuidCfdi.Valor, StringComparison.OrdinalIgnoreCase))) == true;
        if (!relaciona)
            throw new BusinessRuleException("CANCELACION_SUSTITUTO_NO_RELACIONADO",
                "El CFDI sustituto no trae la relación «04 Sustitución de los CFDI previos» con el UUID del CFDI que se cancela.");
    }
}
