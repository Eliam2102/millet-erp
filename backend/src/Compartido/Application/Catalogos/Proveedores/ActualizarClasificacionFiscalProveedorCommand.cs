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
        await ClasificacionFiscalProveedor.ValidarRegimenAsync(db, proveedor.TipoPersona, request.Origen, regimen, cancellationToken);

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

internal static class ClasificacionFiscalProveedor
{
    /// <summary>
    /// El régimen (salvo extranjeros, que no lo llevan) debe existir y corresponder al tipo de persona:
    /// 601 no es de una persona física ni 612 de una moral (86e3pdmee · ADM-01).
    /// </summary>
    public static async Task ValidarRegimenAsync(
        CompartidoDbContext db, TipoPersonaProveedor tipoPersona, OrigenProveedor? origen, string? regimen, CancellationToken ct)
    {
        if (regimen is null || origen == OrigenProveedor.Extranjero) return;
        var r = await db.RegimenesFiscales.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Codigo == regimen && x.Estatus == EstatusCatalogo.Activo, ct)
            ?? throw new BusinessRuleException("PROVEEDOR_REGIMEN_NO_EXISTE", $"El régimen fiscal {regimen} no existe en el catálogo del SAT.");
        var moral = tipoPersona == TipoPersonaProveedor.Moral;
        if (moral ? !r.AplicaPersonaMoral : !r.AplicaPersonaFisica)
            throw new BusinessRuleException("PROVEEDOR_REGIMEN_NO_APLICA",
                $"El régimen {regimen} ({r.Nombre}) no aplica a una persona {(moral ? "moral" : "física")}.");
    }
}
