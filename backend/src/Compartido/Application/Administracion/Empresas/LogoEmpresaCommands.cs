using System.Buffers.Binary;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Blob;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Empresas;

public sealed record SubirLogoEmpresaCommand(Guid Id, byte[] Contenido) : IRequest<LogoEmpresaResponse>;
public sealed record QuitarLogoEmpresaCommand(Guid Id) : IRequest<LogoEmpresaResponse>;
public sealed record ObtenerLogoEmpresaQuery(Guid Id) : IRequest<LogoEmpresaArchivo?>;
public sealed record LogoEmpresaResponse(DateTimeOffset? LogoActualizadoEn);
public sealed record LogoEmpresaArchivo(Stream Contenido, string ContentType);

public static class LogoEmpresaFormato
{
    public const int MaxBytes = 1024 * 1024;

    public static string Validar(byte[] contenido)
    {
        if (contenido.Length > MaxBytes)
            throw new BusinessRuleException("EMPRESA_LOGO_TAMANO", "El logo excede 1 MB. Selecciona una imagen más pequeña.");
        var bytes = contenido.AsSpan();
        int ancho = 0;
        string? tipo = null;
        if (bytes.Length >= 33 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && bytes.Slice(12, 4).SequenceEqual("IHDR"u8) && BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(8, 4)) == 13)
        {
            ancho = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
            if (BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4)) > 0) tipo = "image/png";
        }
        else if (bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8)
        {
            var pos = 2;
            while (pos + 4 <= bytes.Length && bytes[pos] == 0xff)
            {
                while (pos < bytes.Length && bytes[pos] == 0xff) pos++;
                if (pos + 3 > bytes.Length) break;
                var marcador = bytes[pos++];
                if (marcador is 0xd9 or 0xda) break;
                var longitud = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(pos, 2));
                if (longitud < 2 || pos + longitud > bytes.Length) break;
                if (marcador is >= 0xc0 and <= 0xcf and not (0xc4 or 0xc8 or 0xcc) && longitud >= 8)
                {
                    ancho = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(pos + 5, 2));
                    if (BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(pos + 3, 2)) > 0) tipo = "image/jpeg";
                    break;
                }
                pos += longitud;
            }
        }
        if (tipo is null)
            throw new BusinessRuleException("EMPRESA_LOGO_FORMATO", "El archivo no contiene una imagen PNG o JPG válida. Selecciona otro archivo.");
        if (ancho < 200)
            throw new BusinessRuleException("EMPRESA_LOGO_TAMANO", "El logo debe medir al menos 200 píxeles de ancho. Selecciona una imagen más grande.");
        return tipo;
    }
}

public sealed class SubirLogoEmpresaValidator : AbstractValidator<SubirLogoEmpresaCommand>
{
    public SubirLogoEmpresaValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Contenido).NotNull().WithErrorCode("EMPRESA_LOGO_FORMATO").WithMessage("Selecciona un archivo PNG o JPG.");
    }
}

public sealed class LogoEmpresaHandler(CompartidoDbContext db, IBlobStoragePort blobs, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<SubirLogoEmpresaCommand, LogoEmpresaResponse>,
      IRequestHandler<QuitarLogoEmpresaCommand, LogoEmpresaResponse>,
      IRequestHandler<ObtenerLogoEmpresaQuery, LogoEmpresaArchivo?>
{
    public async Task<LogoEmpresaResponse> Handle(SubirLogoEmpresaCommand request, CancellationToken cancellationToken)
    {
        var tipo = LogoEmpresaFormato.Validar(request.Contenido);
        var empresa = await db.Empresas.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("EMPRESA_NO_ENCONTRADA", "No se encontró la empresa. Recarga la lista de empresas.");
        var clave = $"empresas/{empresa.Id}/logo/{Guid.CreateVersion7()}.{(tipo == "image/png" ? "png" : "jpg")}";
        using var stream = new MemoryStream(request.Contenido, writable: false);
        await blobs.SubirAsync(clave, stream, tipo, cancellationToken);
        empresa.AsignarLogo(clave, tipo, usuario.UserId ?? throw new UnauthorizedAccessException(), clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return new(empresa.LogoActualizadoEn);
    }

    public async Task<LogoEmpresaResponse> Handle(QuitarLogoEmpresaCommand request, CancellationToken cancellationToken)
    {
        var empresa = await db.Empresas.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("EMPRESA_NO_ENCONTRADA", "No se encontró la empresa. Recarga la lista de empresas.");
        empresa.QuitarLogo(usuario.UserId ?? throw new UnauthorizedAccessException(), clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return new(empresa.LogoActualizadoEn);
    }

    public async Task<LogoEmpresaArchivo?> Handle(ObtenerLogoEmpresaQuery request, CancellationToken cancellationToken)
    {
        var empresa = await db.Empresas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (empresa?.LogoBlobRef is null) return null;
        try { return new(await blobs.ObtenerStreamAsync(empresa.LogoBlobRef, cancellationToken), empresa.LogoContentType!); }
        catch (FileNotFoundException) { return null; }
    }
}
