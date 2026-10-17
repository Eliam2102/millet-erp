using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Catalogos.Proveedores;

/// <summary>
/// 86e3pdmee · ADM-01: origen (nacional/extranjero), régimen fiscal y
/// retención esperada del proveedor. Reemplaza los tres valores; null deja
/// el dato «por revisar».
/// </summary>
public sealed record ActualizarClasificacionFiscalProveedorCommand(
    Guid ProveedorId, OrigenProveedor? Origen, string? RegimenFiscalCodigo, string? ConceptoRetencion) : IRequest;

public sealed class ActualizarClasificacionFiscalProveedorHandler(
    CompartidoDbContext db,
    ICurrentUserContext usuario,
    ICurrentEmpresaContext empresa,
    IAuditLogWriter audit) : IRequestHandler<ActualizarClasificacionFiscalProveedorCommand>
{
    public async Task Handle(ActualizarClasificacionFiscalProveedorCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await db.Proveedores.FirstOrDefaultAsync(p => p.Id == request.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO", "No existe el proveedor solicitado.");
        var regimen = string.IsNullOrWhiteSpace(request.RegimenFiscalCodigo) ? null : request.RegimenFiscalCodigo.Trim();
        if (regimen is not null && request.Origen != OrigenProveedor.Extranjero
            && !await db.RegimenesFiscales.AsNoTracking().AnyAsync(r => r.Codigo == regimen && r.Estatus == EstatusCatalogo.Activo, cancellationToken))
            throw new BusinessRuleException("PROVEEDOR_REGIMEN_NO_EXISTE", $"El régimen fiscal {regimen} no existe en el catálogo del SAT.");

        var antes = new { proveedor.Origen, proveedor.RegimenFiscalCodigo, proveedor.ConceptoRetencion };
        proveedor.ActualizarClasificacionFiscal(request.Origen, regimen, request.ConceptoRetencion);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RegistrarAsync(
            operacion: "proveedor.clasificacion-fiscal",
            modulo: "DatosMaestros", entidad: "Proveedor",
            entidadId: proveedor.Id, aggregateRootId: proveedor.Id,
            actorNombre: usuario.UserName ?? "Usuario", actorTipo: "usuario", actorEmail: usuario.Email,
            entidadEtiqueta: $"{proveedor.Clave} · {proveedor.RazonSocial}",
            resumen: "Origen, régimen fiscal y retención del proveedor actualizados.",
            usuarioId: usuario.UserId, empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new { antes, despues = new { proveedor.Origen, proveedor.RegimenFiscalCodigo, proveedor.ConceptoRetencion } }),
            cancellationToken: cancellationToken);
    }
}
