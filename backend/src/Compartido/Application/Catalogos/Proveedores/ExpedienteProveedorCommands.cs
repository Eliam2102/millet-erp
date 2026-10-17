using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Adjuntos;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Compartido.Application.Catalogos.Proveedores;

// 86e3pdmee · ADM-01: matriz del expediente configurable por Millet y
// excepciones explícitas a documentos obligatorios (R-ADM-04). Permisos en
// los endpoints: configurar = expediente-configurar; excepciones = validar.

public sealed record TipoDocumentoProveedorDto(
    Guid Id, string Codigo, string Nombre, int Orden, bool Obligatorio, int? VigenciaMeses,
    bool SoloPersonaMoral, bool SoloNacional, bool Activo, int Version);

public sealed record ListarTiposDocumentoProveedorQuery : IRequest<IReadOnlyList<TipoDocumentoProveedorDto>>;

public sealed class ListarTiposDocumentoProveedorHandler(CompartidoDbContext db)
    : IRequestHandler<ListarTiposDocumentoProveedorQuery, IReadOnlyList<TipoDocumentoProveedorDto>>
{
    public async Task<IReadOnlyList<TipoDocumentoProveedorDto>> Handle(
        ListarTiposDocumentoProveedorQuery request, CancellationToken cancellationToken) =>
        await db.AdjuntoTiposDocumento.AsNoTracking()
            .Where(t => t.TipoEntidad == ProveedorAdjuntoPropietario.Tipo)
            .OrderBy(t => t.Orden)
            .Select(t => new TipoDocumentoProveedorDto(t.Id, t.Codigo, t.Nombre, t.Orden, t.Obligatorio, t.VigenciaMeses,
                t.SoloPersonaMoral, t.SoloNacional, t.Activo, t.Version))
            .ToListAsync(cancellationToken);
}

public sealed record GuardarTipoDocumentoProveedorCommand(
    Guid? Id, int? VersionEsperada, string? Codigo, string Nombre, bool Obligatorio, int? VigenciaMeses,
    bool SoloPersonaMoral, bool SoloNacional, bool Activo) : IRequest<TipoDocumentoProveedorDto>;

public sealed class GuardarTipoDocumentoProveedorHandler(
    CompartidoDbContext db, ICurrentUserContext usuario, ICurrentEmpresaContext empresa, IAuditLogWriter audit)
    : IRequestHandler<GuardarTipoDocumentoProveedorCommand, TipoDocumentoProveedorDto>
{
    public async Task<TipoDocumentoProveedorDto> Handle(GuardarTipoDocumentoProveedorCommand request, CancellationToken cancellationToken)
    {
        AdjuntoTipoDocumento tipo;
        if (request.Id is Guid id)
        {
            tipo = await db.AdjuntoTiposDocumento.FirstOrDefaultAsync(t => t.Id == id && t.TipoEntidad == ProveedorAdjuntoPropietario.Tipo, cancellationToken)
                ?? throw new EntityNotFoundException("ADJUNTO_TIPO_DOCUMENTO_NO_ENCONTRADO", "No existe el tipo de documento del proveedor.");
            if (request.VersionEsperada != tipo.Version)
                throw new ConcurrencyException(nameof(AdjuntoTipoDocumento), tipo.Id);
            tipo.Configurar(request.Nombre, request.Obligatorio, request.VigenciaMeses, request.SoloPersonaMoral, request.SoloNacional, request.Activo);
        }
        else
        {
            var codigo = request.Codigo?.Trim().ToLowerInvariant() ?? string.Empty;
            if (await db.AdjuntoTiposDocumento.AnyAsync(t => t.TipoEntidad == ProveedorAdjuntoPropietario.Tipo && t.Codigo == codigo, cancellationToken))
                throw new ConflictException("ADJUNTO_TIPO_DUPLICADO", $"Ya existe un documento con código '{codigo}'.");
            var orden = await db.AdjuntoTiposDocumento.Where(t => t.TipoEntidad == ProveedorAdjuntoPropietario.Tipo)
                .Select(t => (int?)t.Orden).MaxAsync(cancellationToken) ?? 0;
            tipo = new AdjuntoTipoDocumento(Guid.CreateVersion7(), ProveedorAdjuntoPropietario.Tipo, codigo, request.Nombre.Trim(),
                orden + 1, request.Obligatorio, request.VigenciaMeses, request.SoloPersonaMoral);
            tipo.Configurar(request.Nombre, request.Obligatorio, request.VigenciaMeses, request.SoloPersonaMoral, request.SoloNacional, request.Activo);
            db.AdjuntoTiposDocumento.Add(tipo);
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.RegistrarAsync(
            operacion: "proveedor.expediente-matriz", modulo: "DatosMaestros", entidad: "AdjuntoTipoDocumento",
            entidadId: tipo.Id, aggregateRootId: tipo.Id,
            actorNombre: usuario.UserName ?? "Usuario", actorTipo: "usuario", actorEmail: usuario.Email,
            entidadEtiqueta: tipo.Nombre,
            resumen: $"Matriz del expediente de proveedor: «{tipo.Nombre}» {(tipo.Obligatorio ? "obligatorio" : "opcional")}{(tipo.Activo ? "" : ", inactivo")}.",
            usuarioId: usuario.UserId, empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new { tipo.Obligatorio, tipo.VigenciaMeses, tipo.SoloPersonaMoral, tipo.SoloNacional, tipo.Activo }),
            cancellationToken: cancellationToken);
        return new TipoDocumentoProveedorDto(tipo.Id, tipo.Codigo, tipo.Nombre, tipo.Orden, tipo.Obligatorio, tipo.VigenciaMeses,
            tipo.SoloPersonaMoral, tipo.SoloNacional, tipo.Activo, tipo.Version);
    }
}

public sealed record OtorgarExcepcionExpedienteCommand(Guid ProveedorId, Guid TipoDocumentoId, string Motivo) : IRequest<Guid>;

public sealed class OtorgarExcepcionExpedienteHandler(
    CompartidoDbContext db, ICurrentUserContext usuario, ICurrentEmpresaContext empresa, IClock clock, IAuditLogWriter audit)
    : IRequestHandler<OtorgarExcepcionExpedienteCommand, Guid>
{
    public async Task<Guid> Handle(OtorgarExcepcionExpedienteCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await db.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO", "No existe el proveedor solicitado.");
        var tipo = await db.AdjuntoTiposDocumento.AsNoTracking().FirstOrDefaultAsync(t =>
                t.Id == request.TipoDocumentoId && t.TipoEntidad == ProveedorAdjuntoPropietario.Tipo, cancellationToken)
            ?? throw new EntityNotFoundException("ADJUNTO_TIPO_DOCUMENTO_NO_ENCONTRADO", "No existe el tipo de documento del proveedor.");
        if (!tipo.Obligatorio)
            throw new BusinessRuleException("ADJUNTO_EXCEPCION_NO_APLICA", "Solo se exceptúan documentos obligatorios.");
        if (await db.AdjuntoExcepciones.AnyAsync(x => x.TipoEntidad == ProveedorAdjuntoPropietario.Tipo && x.EntidadId == proveedor.Id
                && x.TipoDocumentoId == tipo.Id && x.RevocadaEn == null, cancellationToken))
            throw new ConflictException("ADJUNTO_EXCEPCION_DUPLICADA", "Ya hay una excepción vigente para ese documento.");

        var excepcion = new AdjuntoExcepcion(ProveedorAdjuntoPropietario.Tipo, proveedor.Id, tipo.Id, request.Motivo,
            usuario.UserId, usuario.UserName, clock.UtcNow);
        db.AdjuntoExcepciones.Add(excepcion);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RegistrarAsync(
            operacion: "proveedor.expediente-excepcion", modulo: "DatosMaestros", entidad: "Proveedor",
            entidadId: proveedor.Id, aggregateRootId: proveedor.Id,
            actorNombre: usuario.UserName ?? "Usuario", actorTipo: "usuario", actorEmail: usuario.Email,
            entidadEtiqueta: $"{proveedor.Clave} · {proveedor.RazonSocial}",
            resumen: $"Excepción al documento «{tipo.Nombre}»: {excepcion.Motivo}",
            usuarioId: usuario.UserId, empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new { tipoDocumento = tipo.Codigo, excepcion.Motivo }),
            cancellationToken: cancellationToken);
        return excepcion.Id;
    }
}

public sealed record RevocarExcepcionExpedienteCommand(Guid ProveedorId, Guid ExcepcionId) : IRequest;

public sealed class RevocarExcepcionExpedienteHandler(
    CompartidoDbContext db, ICurrentUserContext usuario, ICurrentEmpresaContext empresa, IClock clock, IAuditLogWriter audit)
    : IRequestHandler<RevocarExcepcionExpedienteCommand>
{
    public async Task Handle(RevocarExcepcionExpedienteCommand request, CancellationToken cancellationToken)
    {
        var excepcion = await db.AdjuntoExcepciones.FirstOrDefaultAsync(x =>
                x.Id == request.ExcepcionId && x.EntidadId == request.ProveedorId && x.TipoEntidad == ProveedorAdjuntoPropietario.Tipo, cancellationToken)
            ?? throw new EntityNotFoundException("ADJUNTO_EXCEPCION_NO_ENCONTRADA", "No existe la excepción.");
        excepcion.Revocar(usuario.UserId, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RegistrarAsync(
            operacion: "proveedor.expediente-revocada", modulo: "DatosMaestros", entidad: "Proveedor",
            entidadId: request.ProveedorId, aggregateRootId: request.ProveedorId,
            actorNombre: usuario.UserName ?? "Usuario", actorTipo: "usuario", actorEmail: usuario.Email,
            entidadEtiqueta: request.ProveedorId.ToString(),
            resumen: "Excepción del expediente revocada.",
            usuarioId: usuario.UserId, empresaId: empresa.Current,
            cancellationToken: cancellationToken);
    }
}
