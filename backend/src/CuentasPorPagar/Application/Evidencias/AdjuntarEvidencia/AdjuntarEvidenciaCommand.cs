using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Evidencias;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Evidencias.AdjuntarEvidencia;

/// <summary>
/// Adjunta una evidencia polimórfica (§4.10 del 00-levantamiento,
/// F4-PR2). MVP: solo soporta
/// <see cref="TipoDocumentoEvidencia.FacturaProveedor"/>. Otros tipos
/// llegan en F6/F7 — el handler los bloquea con código
/// <c>EVIDENCIA_TIPO_DOC_NO_SOPORTADO_EN_MVP</c>.
///
/// <para>
/// El endpoint hace upload del blob ANTES de invocar el comando; el
/// command recibe la <see cref="ArchivoBlobRef"/> ya creada — patrón
/// igual que Compras OC adjuntos.
/// </para>
/// </summary>
public sealed record AdjuntarEvidenciaCommand(
    TipoDocumentoEvidencia TipoDocumento,
    Guid DocumentoId,
    TipoEvidencia Tipo,
    string ArchivoBlobRef,
    string NombreArchivo,
    string ContentType,
    long? TamanioBytes,
    string Comentario,
    EstadoFirmaFisica EstadoFirmaFisica,
    DateOnly? FechaLimiteFirmaFisica) : IRequest<AdjuntarEvidenciaResponse>;

public sealed record AdjuntarEvidenciaResponse(
    Guid Id,
    string ArchivoBlobRef,
    EstadoFirmaFisica EstadoFirmaFisica,
    int Version);

public sealed class AdjuntarEvidenciaValidator : AbstractValidator<AdjuntarEvidenciaCommand>
{
    public AdjuntarEvidenciaValidator()
    {
        RuleFor(c => c.DocumentoId).NotEmpty();
        RuleFor(c => c.NombreArchivo).NotEmpty().MaximumLength(255);
        RuleFor(c => c.ArchivoBlobRef).NotEmpty().MaximumLength(400);
        RuleFor(c => c.ContentType).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Comentario).NotEmpty().MaximumLength(1000);
        RuleFor(c => c.FechaLimiteFirmaFisica)
            .NotNull()
            .When(c => c.EstadoFirmaFisica == EstadoFirmaFisica.Pendiente)
            .WithMessage("Si la firma física está Pendiente, FechaLimiteFirmaFisica es obligatoria.");
    }
}

public sealed class AdjuntarEvidenciaHandler : IRequestHandler<AdjuntarEvidenciaCommand, AdjuntarEvidenciaResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;

    public AdjuntarEvidenciaHandler(
        CuentasPorPagarDbContext db,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IClock clock)
    {
        _db = db; _currentUser = currentUser; _currentEmpresa = currentEmpresa; _clock = clock;
    }

    public async Task<AdjuntarEvidenciaResponse> Handle(AdjuntarEvidenciaCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        // MVP guard: solo FacturaProveedor por ahora.
        if (command.TipoDocumento != TipoDocumentoEvidencia.FacturaProveedor)
        {
            throw new BusinessRuleException(
                "EVIDENCIA_TIPO_DOC_NO_SOPORTADO_EN_MVP",
                $"En F4-PR2 solo se soporta TipoDocumento=FacturaProveedor. Tipo recibido: {command.TipoDocumento}. " +
                "AnticipoProveedor entra en F6, NotaCargo en F6, ComprobacionGastos en F7.");
        }

        var factura = await _db.FacturasProveedor
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == command.DocumentoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "FACTURA_NO_ENCONTRADA",
                $"No se encontró la factura con id '{command.DocumentoId}'.");

        var evidencia = EvidenciaAutorizacion.Adjuntar(
            empresaId: empresaId,
            tipoDocumento: command.TipoDocumento,
            documentoId: command.DocumentoId,
            tipo: command.Tipo,
            archivoBlobRef: command.ArchivoBlobRef,
            nombreArchivo: command.NombreArchivo,
            contentType: command.ContentType,
            tamanioBytes: command.TamanioBytes,
            comentario: command.Comentario,
            estadoFirmaFisica: command.EstadoFirmaFisica,
            fechaLimiteFirmaFisica: command.FechaLimiteFirmaFisica,
            capturadoPor: _currentUser.UserId,
            ahora: _clock.UtcNow);

        _db.EvidenciasAutorizacion.Add(evidencia);
        await _db.SaveChangesAsync(cancellationToken);

        return new AdjuntarEvidenciaResponse(
            Id: evidencia.Id,
            ArchivoBlobRef: evidencia.ArchivoBlobRef,
            EstadoFirmaFisica: evidencia.EstadoFirmaFisica,
            Version: evidencia.Version);
    }
}
