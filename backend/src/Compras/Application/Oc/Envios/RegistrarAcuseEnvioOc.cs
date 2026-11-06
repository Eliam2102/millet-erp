using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Envios;

/// <summary>Guarda la evidencia de entrega o el acuse del proveedor de un envío ya aceptado (G-11).</summary>
public sealed record RegistrarAcuseEnvioOcCommand(Guid OrdenCompraId, Guid EnvioId, string? Referencia) : IRequest<EnvioOcResponse>;

public sealed class RegistrarAcuseEnvioOcHandler(ComprasDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<RegistrarAcuseEnvioOcCommand, EnvioOcResponse>
{
    public async Task<EnvioOcResponse> Handle(RegistrarAcuseEnvioOcCommand command, CancellationToken cancellationToken)
    {
        if (usuario.UserId is not Guid userId || userId == Guid.Empty) throw new UnauthorizedAccessException("Sin usuario autenticado.");
        var envio = await db.EnviosOc.FirstOrDefaultAsync(e => e.Id == command.EnvioId && e.OrdenCompraId == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("OC_ENVIO_NO_ENCONTRADO", "No se encontró el envío de la orden de compra.");
        envio.RegistrarAcuse(command.Referencia, usuario.UserName ?? "Usuario sin nombre", clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return EnvioOcResponse.From(envio);
    }
}
