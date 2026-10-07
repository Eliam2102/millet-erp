using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Blob;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>Contenido para streaming. El llamador (endpoint) es dueño del <see cref="Contenido"/> y lo cierra.</summary>
public sealed record ContenidoAdjuntoResponse(Stream Contenido, string ContentType, string NombreArchivo);

/// <summary>Descarga autenticada (previews del front). Audita la descarga; un adjunto dado de baja es 404.</summary>
public sealed record ObtenerContenidoAdjuntoQuery(string TipoEntidad, Guid EntidadId, Guid AdjuntoId)
    : IRequest<ContenidoAdjuntoResponse>;

public sealed class ObtenerContenidoAdjuntoHandler
    : IRequestHandler<ObtenerContenidoAdjuntoQuery, ContenidoAdjuntoResponse>
{
    private readonly AdjuntoAcceso _acceso;
    private readonly CompartidoDbContext _db;
    private readonly IBlobStoragePort _blob;

    public ObtenerContenidoAdjuntoHandler(AdjuntoAcceso acceso, CompartidoDbContext db, IBlobStoragePort blob)
    {
        _acceso = acceso;
        _db = db;
        _blob = blob;
    }

    public async Task<ContenidoAdjuntoResponse> Handle(ObtenerContenidoAdjuntoQuery request, CancellationToken cancellationToken)
    {
        var (propietario, padre) = await _acceso.AutorizarAsync(
            request.TipoEntidad, request.EntidadId, AdjuntoOperacion.Ver, cancellationToken);
        var adjunto = await AdjuntoSoporte.CargarAsync(
            _db, propietario.TipoEntidad, request.EntidadId, request.AdjuntoId, false, cancellationToken);
        if (adjunto.EstaDeBaja)
        {
            throw new EntityNotFoundException("ADJUNTO_NO_ENCONTRADO", $"No se encontró el adjunto '{request.AdjuntoId}'.");
        }

        var respuesta = await AbrirAsync(_blob, adjunto, cancellationToken);
        await _acceso.AuditarAsync(
            "descarga", propietario.TipoEntidad, request.EntidadId, adjunto.Id,
            $"Descargó '{adjunto.NombreArchivo}' ({padre.Etiqueta})", padre, ct: cancellationToken);
        return respuesta;
    }

    internal static async Task<ContenidoAdjuntoResponse> AbrirAsync(
        IBlobStoragePort blob, Adjunto adjunto, CancellationToken cancellationToken)
    {
        try
        {
            var stream = await blob.ObtenerStreamAsync(adjunto.BlobRef, cancellationToken);
            return new ContenidoAdjuntoResponse(stream, adjunto.ContentType, adjunto.NombreArchivo);
        }
        catch (FileNotFoundException)
        {
            throw new EntityNotFoundException(
                "ADJUNTO_ARCHIVO_NO_DISPONIBLE", "El archivo del adjunto no está disponible en el almacenamiento.");
        }
    }
}

/// <summary>
/// Descarga por enlace temporal (endpoint anónimo: el token es la credencial). Devuelve <c>null</c> si el
/// token es inválido, manipulado o expiró (el endpoint responde 401 <c>ADJUNTO_ENLACE_INVALIDO</c>).
/// Re-verifica que el adjunto siga existiendo y sin baja (404 si no).
/// </summary>
public sealed record ObtenerContenidoPorEnlaceQuery(string Token) : IRequest<ContenidoAdjuntoResponse?>;

public sealed class ObtenerContenidoPorEnlaceHandler
    : IRequestHandler<ObtenerContenidoPorEnlaceQuery, ContenidoAdjuntoResponse?>
{
    private readonly IAdjuntoEnlaceTokenService _tokens;
    private readonly AdjuntoAcceso _acceso;
    private readonly CompartidoDbContext _db;
    private readonly IBlobStoragePort _blob;

    public ObtenerContenidoPorEnlaceHandler(
        IAdjuntoEnlaceTokenService tokens, AdjuntoAcceso acceso, CompartidoDbContext db, IBlobStoragePort blob)
    {
        _tokens = tokens;
        _acceso = acceso;
        _db = db;
        _blob = blob;
    }

    public async Task<ContenidoAdjuntoResponse?> Handle(ObtenerContenidoPorEnlaceQuery request, CancellationToken cancellationToken)
    {
        if (_tokens.Validar(request.Token) is not { } claims) return null;

        var adjunto = await _db.Adjuntos.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == claims.AdjuntoId, cancellationToken);
        if (adjunto is null || adjunto.EstaDeBaja)
        {
            throw new EntityNotFoundException("ADJUNTO_NO_ENCONTRADO", $"No se encontró el adjunto '{claims.AdjuntoId}'.");
        }

        var respuesta = await ObtenerContenidoAdjuntoHandler.AbrirAsync(_blob, adjunto, cancellationToken);
        await _acceso.AuditarAsync(
            "descarga", adjunto.TipoEntidad, adjunto.EntidadId, adjunto.Id,
            $"Descargó '{adjunto.NombreArchivo}' por enlace temporal",
            usuarioId: claims.UsuarioId, actorNombre: "Enlace temporal", ct: cancellationToken);
        return respuesta;
    }
}
