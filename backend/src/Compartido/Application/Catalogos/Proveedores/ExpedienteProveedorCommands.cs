using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Adjuntos;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Incidencias;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Compartido.Application.Catalogos.Proveedores;

// 86e3pdmee · ADM-01: matriz del expediente configurable por Millet y
// excepciones explícitas a documentos obligatorios (R-ADM-04). Permisos en
// los endpoints: configurar = expediente-configurar; excepciones = validar.

public sealed record TipoDocumentoProveedorDto(
    Guid Id, string Codigo, string Nombre, int Orden, bool ObligatorioPersonaMoral, bool ObligatorioPersonaFisica,
    int? VigenciaMeses, bool SoloPersonaMoral, AdjuntoAplicaOrigen AplicaOrigen, bool Activo, int Version);

public static class TipoDocumentoProveedorMapeo
{
    public static TipoDocumentoProveedorDto ADto(AdjuntoTipoDocumento t) => new(
        t.Id, t.Codigo, t.Nombre, t.Orden, t.ObligatorioPersonaMoral, t.ObligatorioPersonaFisica, t.VigenciaMeses,
        t.SoloPersonaMoral, t.AplicaOrigen, t.Activo, t.Version);
}

public sealed record ListarTiposDocumentoProveedorQuery : IRequest<IReadOnlyList<TipoDocumentoProveedorDto>>;

public sealed class ListarTiposDocumentoProveedorHandler(CompartidoDbContext db)
    : IRequestHandler<ListarTiposDocumentoProveedorQuery, IReadOnlyList<TipoDocumentoProveedorDto>>
{
    public async Task<IReadOnlyList<TipoDocumentoProveedorDto>> Handle(
        ListarTiposDocumentoProveedorQuery request, CancellationToken cancellationToken) =>
        (await db.AdjuntoTiposDocumento.AsNoTracking()
            .Where(t => t.TipoEntidad == ProveedorAdjuntoPropietario.Tipo)
            .OrderBy(t => t.Orden)
            .ToListAsync(cancellationToken))
        .Select(TipoDocumentoProveedorMapeo.ADto).ToList();
}

public sealed record GuardarTipoDocumentoProveedorCommand(
    Guid? Id, int? VersionEsperada, string? Codigo, string Nombre, bool ObligatorioPersonaMoral, bool ObligatorioPersonaFisica,
    int? VigenciaMeses, bool SoloPersonaMoral, AdjuntoAplicaOrigen AplicaOrigen, bool Activo) : IRequest<TipoDocumentoProveedorDto>;

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
            tipo.Configurar(request.Nombre, request.ObligatorioPersonaMoral, request.ObligatorioPersonaFisica, request.VigenciaMeses,
                request.SoloPersonaMoral, request.AplicaOrigen, request.Activo);
        }
        else
        {
            var codigo = request.Codigo?.Trim().ToLowerInvariant() ?? string.Empty;
            if (await db.AdjuntoTiposDocumento.AnyAsync(t => t.TipoEntidad == ProveedorAdjuntoPropietario.Tipo && t.Codigo == codigo, cancellationToken))
                throw new ConflictException("ADJUNTO_TIPO_DUPLICADO", $"Ya existe un documento con código '{codigo}'.");
            var orden = await db.AdjuntoTiposDocumento.Where(t => t.TipoEntidad == ProveedorAdjuntoPropietario.Tipo)
                .Select(t => (int?)t.Orden).MaxAsync(cancellationToken) ?? 0;
            tipo = new AdjuntoTipoDocumento(Guid.CreateVersion7(), ProveedorAdjuntoPropietario.Tipo, codigo, request.Nombre.Trim(),
                orden + 1, false, request.VigenciaMeses, request.SoloPersonaMoral);
            tipo.Configurar(request.Nombre, request.ObligatorioPersonaMoral, request.ObligatorioPersonaFisica, request.VigenciaMeses,
                request.SoloPersonaMoral, request.AplicaOrigen, request.Activo);
            db.AdjuntoTiposDocumento.Add(tipo);
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.RegistrarAsync(
            operacion: "proveedor.expediente-matriz", modulo: "DatosMaestros", entidad: "AdjuntoTipoDocumento",
            entidadId: tipo.Id, aggregateRootId: tipo.Id,
            actorNombre: usuario.UserName ?? "Usuario", actorTipo: "usuario", actorEmail: usuario.Email,
            entidadEtiqueta: tipo.Nombre,
            resumen: $"Matriz del expediente de proveedor: «{tipo.Nombre}» obligatorio moral: {(tipo.ObligatorioPersonaMoral ? "sí" : "no")}, física: {(tipo.ObligatorioPersonaFisica ? "sí" : "no")}{(tipo.Activo ? "" : ", inactivo")}.",
            usuarioId: usuario.UserId, empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new
            {
                tipo.ObligatorioPersonaMoral, tipo.ObligatorioPersonaFisica, tipo.VigenciaMeses, tipo.SoloPersonaMoral,
                AplicaOrigen = tipo.AplicaOrigen.ToString(), tipo.Activo,
                matrizVersion = await ExpedienteAdjuntos.VersionMatrizAsync(db, ProveedorAdjuntoPropietario.Tipo, cancellationToken),
            }),
            cancellationToken: cancellationToken);
        return TipoDocumentoProveedorMapeo.ADto(tipo);
    }
}

/// <summary>86e3yzemu: pedir la excepción la deja pendiente; la resuelve otra persona (<see cref="ResolverExcepcionExpedienteCommand"/>).</summary>
public sealed record SolicitarExcepcionExpedienteCommand(Guid ProveedorId, Guid TipoDocumentoId, string Motivo) : IRequest<Guid>;

public sealed class SolicitarExcepcionExpedienteHandler(
    CompartidoDbContext db, ICurrentUserContext usuario, ICurrentEmpresaContext empresa, IClock clock, IAuditLogWriter audit)
    : IRequestHandler<SolicitarExcepcionExpedienteCommand, Guid>
{
    public async Task<Guid> Handle(SolicitarExcepcionExpedienteCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await db.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO", "No existe el proveedor solicitado.");
        var tipo = await db.AdjuntoTiposDocumento.AsNoTracking().FirstOrDefaultAsync(t =>
                t.Id == request.TipoDocumentoId && t.TipoEntidad == ProveedorAdjuntoPropietario.Tipo, cancellationToken)
            ?? throw new EntityNotFoundException("ADJUNTO_TIPO_DOCUMENTO_NO_ENCONTRADO", "No existe el tipo de documento del proveedor.");
        if (!tipo.EsObligatorioPara(proveedor.TipoPersona == TipoPersonaProveedor.Moral))
            throw new BusinessRuleException("ADJUNTO_EXCEPCION_NO_APLICA", "Solo se exceptúan documentos obligatorios.");
        if (await db.AdjuntoExcepciones.AnyAsync(x => x.TipoEntidad == ProveedorAdjuntoPropietario.Tipo && x.EntidadId == proveedor.Id
                && x.TipoDocumentoId == tipo.Id && x.RevocadaEn == null && x.Estado != EstadoExcepcionAdjunto.Negada, cancellationToken))
            throw new ConflictException("ADJUNTO_EXCEPCION_DUPLICADA", "Ya hay una excepción pendiente o vigente para ese documento.");

        var excepcion = new AdjuntoExcepcion(ProveedorAdjuntoPropietario.Tipo, proveedor.Id, tipo.Id, request.Motivo,
            usuario.UserId, usuario.UserName, clock.UtcNow);
        db.AdjuntoExcepciones.Add(excepcion);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RegistrarAsync(
            operacion: "proveedor.expediente-excepcion", modulo: "DatosMaestros", entidad: "Proveedor",
            entidadId: proveedor.Id, aggregateRootId: proveedor.Id,
            actorNombre: usuario.UserName ?? "Usuario", actorTipo: "usuario", actorEmail: usuario.Email,
            entidadEtiqueta: $"{proveedor.Clave} · {proveedor.RazonSocial}",
            resumen: $"Excepción solicitada al documento «{tipo.Nombre}»: {excepcion.Motivo}",
            usuarioId: usuario.UserId, empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new { tipoDocumento = tipo.Codigo, excepcion.Motivo }),
            cancellationToken: cancellationToken);
        return excepcion.Id;
    }
}

/// <summary>Otorga o niega una excepción pendiente (la persona que resuelve no puede ser quien la pidió).</summary>
public sealed record ResolverExcepcionExpedienteCommand(Guid ProveedorId, Guid ExcepcionId, bool Otorgar, string? Motivo) : IRequest;

public sealed class ResolverExcepcionExpedienteHandler(
    CompartidoDbContext db, ICurrentUserContext usuario, ICurrentEmpresaContext empresa, IClock clock, IAuditLogWriter audit)
    : IRequestHandler<ResolverExcepcionExpedienteCommand>
{
    public async Task Handle(ResolverExcepcionExpedienteCommand request, CancellationToken cancellationToken)
    {
        var excepcion = await db.AdjuntoExcepciones.FirstOrDefaultAsync(x =>
                x.Id == request.ExcepcionId && x.EntidadId == request.ProveedorId && x.TipoEntidad == ProveedorAdjuntoPropietario.Tipo, cancellationToken)
            ?? throw new EntityNotFoundException("ADJUNTO_EXCEPCION_NO_ENCONTRADA", "No existe la excepción.");
        if (request.Otorgar) excepcion.Otorgar(usuario.UserId, usuario.UserName, clock.UtcNow, request.Motivo);
        else excepcion.Negar(usuario.UserId, usuario.UserName, clock.UtcNow, request.Motivo);
        var tipo = await db.AdjuntoTiposDocumento.AsNoTracking().FirstAsync(t => t.Id == excepcion.TipoDocumentoId, cancellationToken);
        var proveedor = await db.Proveedores.AsNoTracking().FirstAsync(p => p.Id == request.ProveedorId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.RegistrarAsync(
            operacion: "proveedor.excepcion-resuelta", modulo: "DatosMaestros", entidad: "Proveedor",
            entidadId: proveedor.Id, aggregateRootId: proveedor.Id,
            actorNombre: usuario.UserName ?? "Usuario", actorTipo: "usuario", actorEmail: usuario.Email,
            entidadEtiqueta: $"{proveedor.Clave} · {proveedor.RazonSocial}",
            resumen: $"Excepción al documento «{tipo.Nombre}» {(request.Otorgar ? "otorgada" : "negada")}.",
            usuarioId: usuario.UserId, empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new { tipoDocumento = tipo.Codigo, estado = excepcion.Estado.ToString(), excepcion.MotivoResolucion }),
            cancellationToken: cancellationToken);
    }
}

/// <summary>86e3yzemu: cotejo del contenido de un documento del expediente (quién, cuándo y resultado).</summary>
public sealed record RevisarDocumentoProveedorCommand(Guid ProveedorId, Guid AdjuntoId, ResultadoRevisionAdjunto Resultado, string? Observacion)
    : IRequest<AdjuntoResponse>;

public sealed class RevisarDocumentoProveedorHandler(
    CompartidoDbContext db, ICurrentUserContext usuario, ICurrentEmpresaContext empresa, IClock clock, IAuditLogWriter audit,
    IIncidenciasPort? incidencias = null)
    : IRequestHandler<RevisarDocumentoProveedorCommand, AdjuntoResponse>
{
    public async Task<AdjuntoResponse> Handle(RevisarDocumentoProveedorCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await db.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO", "No existe el proveedor solicitado.");
        var adjunto = await AdjuntoSoporte.CargarAsync(db, ProveedorAdjuntoPropietario.Tipo, request.ProveedorId, request.AdjuntoId, true, cancellationToken);
        adjunto.Revisar(request.Resultado, request.Observacion,
            usuario.UserId ?? throw new ForbiddenException("USUARIO_REQUERIDO", "No se pudo identificar al usuario que revisa."),
            usuario.UserName, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        var tipo = await AdjuntoSoporte.CargarTipoAsync(db, adjunto.TipoDocumentoId, cancellationToken);
        await audit.RegistrarAsync(
            operacion: "proveedor.expediente-revision", modulo: "DatosMaestros", entidad: "Proveedor",
            entidadId: proveedor.Id, aggregateRootId: proveedor.Id,
            actorNombre: usuario.UserName ?? "Usuario", actorTipo: "usuario", actorEmail: usuario.Email,
            entidadEtiqueta: $"{proveedor.Clave} · {proveedor.RazonSocial}",
            resumen: $"Documento «{tipo.Nombre}» {(request.Resultado == ResultadoRevisionAdjunto.Aceptado ? "aceptado" : "rechazado")} tras revisar su contenido.",
            usuarioId: usuario.UserId, empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new { tipoDocumento = tipo.Codigo, resultado = request.Resultado.ToString(), adjunto.ObservacionRevision }),
            cancellationToken: cancellationToken);
        // U-4 (86e3yzemw, ADM-proveedor-09): el documento observado abre su incidencia; el aceptado resuelve la de su tipo.
        if (incidencias is not null)
        {
            if (request.Resultado == ResultadoRevisionAdjunto.Rechazado)
                await IncidenciasProveedor.AbrirDocumentoObservadoAsync(incidencias, proveedor, adjunto, tipo.Nombre, usuario.UserId, cancellationToken);
            else
                await IncidenciasProveedor.ResolverDocumentoAsync(incidencias, proveedor.Id, adjunto.TipoDocumentoId, tipo.Nombre, cancellationToken);
        }
        return AdjuntoResponse.De(adjunto, tipo, AdjuntoSoporte.Hoy(clock));
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
