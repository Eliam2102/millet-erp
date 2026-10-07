using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Catalogos.Proveedores;

public sealed record ValidarProveedorCommand(Guid ProveedorId) : IRequest<ValidarProveedorResponse>;

public sealed record ValidarProveedorResponse(
    Guid Id,
    string Clave,
    EstatusCatalogo Estatus,
    DateTimeOffset ValidadoEn);

public sealed class ValidarProveedorHandler : IRequestHandler<ValidarProveedorCommand, ValidarProveedorResponse>
{
    private readonly CompartidoDbContext _db;
    private readonly IExpedienteProveedorReadPort _expedientePort;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IClock _clock;
    private readonly IAuditLogWriter _audit;

    public ValidarProveedorHandler(
        CompartidoDbContext db,
        IExpedienteProveedorReadPort expedientePort,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext empresa,
        IClock clock,
        IAuditLogWriter audit)
    {
        _db = db;
        _expedientePort = expedientePort;
        _currentUser = currentUser;
        _empresa = empresa;
        _clock = clock;
        _audit = audit;
    }

    public async Task<ValidarProveedorResponse> Handle(
        ValidarProveedorCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await _db.Proveedores
            .FirstOrDefaultAsync(p => p.Id == request.ProveedorId, cancellationToken);

        if (proveedor is null)
        {
            throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"No existe proveedor con id '{request.ProveedorId}'.");
        }

        if (proveedor.Estatus != EstatusCatalogo.EnRevision)
        {
            throw new BusinessRuleException(
                "PROVEEDOR_NO_EN_REVISION",
                $"Solo se pueden validar proveedores en estado 'EnRevision'. Estado actual: '{proveedor.Estatus}'.");
        }

        // F1-ADM-05 G1.1 / G1.2: Validar completitud del expediente documental vía el puerto de G1.2.
        var expediente = await _expedientePort.ObtenerAsync(proveedor.Id, cancellationToken);
        if (expediente is null || !expediente.Completo)
        {
            var detallesFaltantes = new List<string>();
            if (expediente?.Faltantes is { Count: > 0 })
                detallesFaltantes.Add($"faltantes: [{string.Join(", ", expediente.Faltantes)}]");
            if (expediente?.Vencidos is { Count: > 0 })
                detallesFaltantes.Add($"vencidos: [{string.Join(", ", expediente.Vencidos)}]");

            var motivo = detallesFaltantes.Count > 0
                ? string.Join("; ", detallesFaltantes)
                : "documentos obligatorios pendientes o vencidos";

            throw new BusinessRuleException(
                "PROVEEDOR_EXPEDIENTE_INCOMPLETO",
                $"No se puede validar el proveedor porque su expediente documental está incompleto o tiene documentos vencidos ({motivo}).");
        }

        var ahora = _clock.UtcNow;
        var validadorId = _currentUser.UserId ?? Guid.Empty;

        proveedor.Validar(validadorId, ahora);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.RegistrarAsync(
            operacion: "proveedor.validado",
            modulo: "DatosMaestros",
            entidad: "Proveedor",
            entidadId: proveedor.Id,
            aggregateRootId: proveedor.Id,
            actorNombre: _currentUser.UserName ?? "Usuario",
            actorTipo: "usuario",
            actorEmail: _currentUser.Email,
            entidadEtiqueta: $"{proveedor.Clave} · {proveedor.RazonSocial}",
            resumen: $"Proveedor {proveedor.Clave} validado y activado por CxP con expediente documental completo.",
            usuarioId: validadorId,
            empresaId: _empresa.Current,
            cambios: JsonSerializer.Serialize(new { estatusAnterior = "EnRevision", nuevoEstatus = "Activo" }),
            metadatos: JsonSerializer.Serialize(new { proveedor.Clave, proveedor.Rfc, tipoPersona = proveedor.TipoPersona.ToString() }),
            cancellationToken: cancellationToken);

        return new ValidarProveedorResponse(
            proveedor.Id,
            proveedor.Clave,
            proveedor.Estatus,
            ahora);
    }
}
