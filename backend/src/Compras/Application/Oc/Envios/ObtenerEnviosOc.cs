using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Correo;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Envios;

public sealed record EnvioOcResponse(Guid Id, string Destinatario, Guid UsuarioId, string UsuarioNombre,
    DateTimeOffset Fecha, string Modo, string Resultado)
{
    public static EnvioOcResponse From(EnvioOc e) => new(e.Id, e.Destinatario, e.UsuarioId, e.UsuarioNombre, e.Fecha, e.Modo, e.Resultado);
}
public sealed record EnviosOcResponse(string? CorreoProveedor, bool EsSimulado, bool PuedeEnviar,
    string? MotivoBloqueo, IReadOnlyList<EnvioOcResponse> Envios);
public sealed record ObtenerEnviosOcQuery(Guid OrdenCompraId) : IRequest<EnviosOcResponse>;

public sealed class ObtenerEnviosOcHandler(ComprasDbContext db, IProveedorReadPort proveedores, ICorreoDocumentoService correo)
    : IRequestHandler<ObtenerEnviosOcQuery, EnviosOcResponse>
{
    public async Task<EnviosOcResponse> Handle(ObtenerEnviosOcQuery query, CancellationToken cancellationToken)
    {
        var oc = await db.OrdenesCompra.AsNoTracking().Include(o => o.Autorizaciones)
            .FirstOrDefaultAsync(o => o.Id == query.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", "No se encontró la orden de compra.");
        var catalogo = await proveedores.ObtenerPorIdsAsync([oc.ProveedorId], cancellationToken);
        var motivo = EnvioOc.MotivoBloqueo(oc);
        if (motivo is null && !await db.OrdenCompraPdfs.AnyAsync(p => p.OrdenCompraId == oc.Id, cancellationToken))
            motivo = "La orden aún no tiene PDF generado. Solicita revisar su generación antes de enviarla.";
        var envios = await db.EnviosOc.AsNoTracking().Where(e => e.OrdenCompraId == oc.Id)
            .OrderByDescending(e => e.Fecha).ThenByDescending(e => e.Id).ToListAsync(cancellationToken);
        return new(catalogo.GetValueOrDefault(oc.ProveedorId)?.Email, correo.EsSimulado, motivo is null,
            motivo, envios.Select(EnvioOcResponse.From).ToList());
    }
}
