using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.RespuestaProveedor;

/// <summary>Marca una respuesta del proveedor como errónea conservándola en el historial (G-11).</summary>
public sealed record AnularRespuestaProveedorCommand(Guid OrdenCompraId, Guid RespuestaId, string? Motivo)
    : IRequest<RespuestaProveedorOcResponse>;

public sealed class AnularRespuestaProveedorHandler(ComprasDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<AnularRespuestaProveedorCommand, RespuestaProveedorOcResponse>
{
    public async Task<RespuestaProveedorOcResponse> Handle(AnularRespuestaProveedorCommand command, CancellationToken cancellationToken)
    {
        if (usuario.UserId is not Guid userId) throw new UnauthorizedAccessException("Sin usuario autenticado.");
        var oc = await db.OrdenesCompra.Include(o => o.RespuestasProveedor).ThenInclude(r => r.Compromisos)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", "No se encontró la orden de compra.");
        var respuesta = oc.AnularRespuestaProveedor(command.RespuestaId, command.Motivo, userId, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return RespuestaProveedorMapper.ToResponse(respuesta, vigente: false);
    }
}
