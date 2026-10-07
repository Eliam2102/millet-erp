using MediatR;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>Ruta del endpoint anónimo de descarga por enlace temporal (debe coincidir con la API).</summary>
public static class AdjuntoRutas
{
    public const string DescargaPorEnlace = "/api/v1/adjuntos/descargas/";
}

/// <summary>URL relativa de un solo uso práctico (TTL corto) y su expiración (UTC).</summary>
public sealed record EnlaceDescargaResponse(string Url, DateTimeOffset ExpiraEn);

/// <summary>Mismos permisos y alcance que ver. Un adjunto dado de baja no emite enlace (404).</summary>
public sealed record EmitirEnlaceDescargaCommand(string TipoEntidad, Guid EntidadId, Guid AdjuntoId)
    : IRequest<EnlaceDescargaResponse>;

public sealed class EmitirEnlaceDescargaHandler : IRequestHandler<EmitirEnlaceDescargaCommand, EnlaceDescargaResponse>
{
    private readonly AdjuntoAcceso _acceso;
    private readonly CompartidoDbContext _db;
    private readonly IAdjuntoEnlaceTokenService _tokens;

    public EmitirEnlaceDescargaHandler(AdjuntoAcceso acceso, CompartidoDbContext db, IAdjuntoEnlaceTokenService tokens)
    {
        _acceso = acceso;
        _db = db;
        _tokens = tokens;
    }

    public async Task<EnlaceDescargaResponse> Handle(EmitirEnlaceDescargaCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = _acceso.UsuarioId;
        var (propietario, padre) = await _acceso.AutorizarAsync(
            request.TipoEntidad, request.EntidadId, AdjuntoOperacion.Ver, cancellationToken);
        var adjunto = await AdjuntoSoporte.CargarAsync(
            _db, propietario.TipoEntidad, request.EntidadId, request.AdjuntoId, false, cancellationToken);
        if (adjunto.EstaDeBaja)
        {
            throw new EntityNotFoundException("ADJUNTO_NO_ENCONTRADO", $"No se encontró el adjunto '{request.AdjuntoId}'.");
        }

        var emitido = _tokens.Emitir(adjunto.Id, usuarioId);
        await _acceso.AuditarAsync(
            "enlace", propietario.TipoEntidad, request.EntidadId, adjunto.Id,
            $"Emitió enlace temporal de descarga de '{adjunto.NombreArchivo}' ({padre.Etiqueta})",
            padre, ct: cancellationToken);

        return new EnlaceDescargaResponse(
            AdjuntoRutas.DescargaPorEnlace + Uri.EscapeDataString(emitido.Token), emitido.ExpiraEn);
    }
}
