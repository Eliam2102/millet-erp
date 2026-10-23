using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Envios;

public sealed record RegistrarEnvioOcCommand(Guid OrdenCompraId, string? Destinatario, string Modo) : IRequest<EnvioOc>;

public sealed class RegistrarEnvioOcValidator : AbstractValidator<RegistrarEnvioOcCommand>
{
    public RegistrarEnvioOcValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty();
        RuleFor(c => c.Destinatario).MaximumLength(254);
        RuleFor(c => c.Modo).Must(m => m is "correo" or "manual").WithMessage("Selecciona envío por correo o registro manual.");
    }
}

public sealed class RegistrarEnvioOcHandler(ComprasDbContext db, ICurrentUserContext usuario, IClock clock,
    IProveedorReadPort proveedores, IAlmacenarBlobPort blob, ICorreoDocumentoService correo,
    ILogger<RegistrarEnvioOcHandler> logger)
    : IRequestHandler<RegistrarEnvioOcCommand, EnvioOc>
{
    public async Task<EnvioOc> Handle(RegistrarEnvioOcCommand command, CancellationToken cancellationToken)
    {
        if (usuario.UserId is not Guid userId || userId == Guid.Empty) throw new UnauthorizedAccessException("Sin usuario autenticado.");
        var oc = await db.OrdenesCompra.Include(o => o.Autorizaciones).FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", "No se encontró la orden de compra.");
        if (EnvioOc.MotivoBloqueo(oc) is { } motivo) throw new BusinessRuleException("OC_ENVIO_NO_AUTORIZADA", motivo);
        var pdf = await db.OrdenCompraPdfs.SingleOrDefaultAsync(p => p.OrdenCompraId == oc.Id, cancellationToken)
            ?? throw new BusinessRuleException("OC_ENVIO_SIN_PDF", "La orden aún no tiene PDF generado. Solicita revisar su generación antes de enviarla.");
        var destinatario = command.Destinatario;
        if (string.IsNullOrWhiteSpace(destinatario))
        {
            var catalogo = await proveedores.ObtenerPorIdsAsync([oc.ProveedorId], cancellationToken);
            destinatario = catalogo.GetValueOrDefault(oc.ProveedorId)?.Email;
        }
        var envio = new EnvioOc(oc, pdf, destinatario, command.Modo, userId,
            usuario.UserName ?? "Usuario sin nombre", clock.UtcNow);
        db.EnviosOc.Add(envio);
        // Persistir primero el intento: un fallo del transporte no borra la trazabilidad.
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            if (command.Modo == "correo")
            {
                await using var stream = await blob.ObtenerStreamAsync(pdf.BlobUrl, cancellationToken);
                using var contenido = new MemoryStream();
                await stream.CopyToAsync(contenido, cancellationToken);
                await correo.EnviarAsync(new CorreoDocumento(oc.EmpresaId, envio.Destinatario,
                    $"Orden de compra {oc.Folio.Valor}", $"{oc.Folio.Valor}.pdf", contenido.ToArray()), cancellationToken);
            }
            envio.Completar(correo.EsSimulado);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Sin el correo del proveedor en el log: es dato personal.
            logger.LogWarning(ex, "Falló el envío de la OC {OrdenCompraId} por correo.", oc.Id);
            envio.Fallar();
        }
        await db.SaveChangesAsync(CancellationToken.None);
        return envio;
    }
}
