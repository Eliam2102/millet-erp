using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Blob;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>
/// Sube un adjunto a una entidad dueña (F1-ADM-11 G1.2). <see cref="Contenido"/> debe ser seekable
/// (el endpoint entrega el stream del multipart) para poder leer la cabecera antes de subir.
/// </summary>
public sealed record SubirAdjuntoCommand(
    string TipoEntidad,
    Guid EntidadId,
    Guid TipoDocumentoId,
    string NombreArchivo,
    string ContentType,
    Stream Contenido,
    DateOnly? VigenteHasta = null) : IRequest<AdjuntoResponse>;

public sealed class SubirAdjuntoValidator : AbstractValidator<SubirAdjuntoCommand>
{
    public SubirAdjuntoValidator()
    {
        RuleFor(x => x.TipoEntidad).NotEmpty().MaximumLength(60);
        RuleFor(x => x.EntidadId).NotEmpty();
        RuleFor(x => x.TipoDocumentoId).NotEmpty();
        RuleFor(x => x.NombreArchivo).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Contenido).NotNull()
            .Must(s => s is { CanSeek: true }).WithMessage("El contenido del archivo debe ser un stream posicionable.");
    }
}

/// <summary>
/// Orden (contrato adm-11 §3 y §6): permiso -> padre -> alcance -> operación -> tipo de documento ->
/// política de archivo -> vigencia -> blob -> BD. Si falla la BD (o el blob a medias) el blob se
/// compensa: 0 huérfanos. El blob nunca se borra por baja lógica; solo por esta compensación.
/// </summary>
public sealed class SubirAdjuntoHandler : IRequestHandler<SubirAdjuntoCommand, AdjuntoResponse>
{
    private const int BytesCabecera = 16;

    private readonly AdjuntoAcceso _acceso;
    private readonly CompartidoDbContext _db;
    private readonly IBlobStoragePort _blob;
    private readonly IOptions<AdjuntosPoliticaOptions> _politica;
    private readonly IClock _clock;

    public SubirAdjuntoHandler(
        AdjuntoAcceso acceso,
        CompartidoDbContext db,
        IBlobStoragePort blob,
        IOptions<AdjuntosPoliticaOptions> politica,
        IClock clock)
    {
        _acceso = acceso;
        _db = db;
        _blob = blob;
        _politica = politica;
        _clock = clock;
    }

    public async Task<AdjuntoResponse> Handle(SubirAdjuntoCommand request, CancellationToken cancellationToken)
    {
        var usuarioId = _acceso.UsuarioId;
        var (propietario, padre) = await _acceso.AutorizarAsync(
            request.TipoEntidad, request.EntidadId, AdjuntoOperacion.Subir, cancellationToken);

        var tipo = await _db.AdjuntoTiposDocumento.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TipoDocumentoId
                && t.TipoEntidad == propietario.TipoEntidad && t.Activo, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ADJUNTO_TIPO_DOCUMENTO_NO_ENCONTRADO",
                $"No existe un tipo de documento activo '{request.TipoDocumentoId}' para {propietario.TipoEntidad}.");

        if (tipo.SoloPersonaMoral && padre.EsPersonaMoral == false)
        {
            throw new BusinessRuleException(
                "ADJUNTO_TIPO_NO_APLICA",
                $"El documento '{tipo.Nombre}' aplica solo a personas morales.");
        }

        var contenido = request.Contenido;
        var nombre = Path.GetFileName(request.NombreArchivo.Trim());
        if (contenido.Length == 0)
        {
            throw new BusinessRuleException("ADJUNTO_ARCHIVO_VACIO", "El archivo está vacío.");
        }

        // Política: tamaño, extensión, MIME y firma. Solo se lee la cabecera.
        var cabecera = new byte[BytesCabecera];
        contenido.Position = 0;
        var leidos = await contenido.ReadAtLeastAsync(cabecera, cabecera.Length, throwOnEndOfStream: false, cancellationToken);
        contenido.Position = 0;
        _politica.Value.ParaEntidad(propietario.TipoEntidad)
            .ValidarInstancia(nombre, request.ContentType, contenido.Length, cabecera.AsSpan(0, leidos));

        var hoy = AdjuntoSoporte.Hoy(_clock);
        var vigenteHasta = request.VigenteHasta;
        if (vigenteHasta is { } v && v < hoy)
        {
            throw new BusinessRuleException(
                "ADJUNTO_VIGENCIA_PASADA", "La fecha de vigencia no puede ser anterior a hoy.");
        }
        vigenteHasta ??= tipo.VigenciaMeses is { } meses ? hoy.AddMonths(meses) : null;

        var id = Guid.NewGuid();
        var clave = $"{propietario.TipoEntidad}/{request.EntidadId}/{id}{Path.GetExtension(nombre).ToLowerInvariant()}";

        try
        {
            using var hashing = new HashingStream(contenido);
            await _blob.SubirAsync(clave, hashing, request.ContentType, cancellationToken);

            var adjunto = new Adjunto(
                id, propietario.TipoEntidad, request.EntidadId, padre.EmpresaId, tipo.Id, nombre,
                request.ContentType, contenido.Length, hashing.HashHex(), clave, vigenteHasta,
                usuarioId, _clock.UtcNow);

            _db.Adjuntos.Add(adjunto);
            await _db.SaveChangesAsync(cancellationToken);
            return AdjuntoResponse.De(adjunto, tipo, hoy);
        }
        catch
        {
            // Compensación: sin fila no debe quedar blob. Best-effort; el error original se conserva.
            try { await _blob.EliminarAsync(clave, CancellationToken.None); } catch { /* ya hay un error en curso */ }
            throw;
        }
    }
}
