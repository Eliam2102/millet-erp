using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Millet.Compartido.Application.Adjuntos;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Blob;
using Microsoft.Extensions.Logging;
using Npgsql;
using Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.RespuestaProveedor;

public sealed record CompromisoEntregaInput(Guid LineaOrdenCompraId, decimal Cantidad, DateOnly FechaPrometida);

public sealed record RegistrarRespuestaProveedorCommand(Guid OrdenCompraId, TipoRespuestaProveedorOc? Tipo,
    DateOnly? FechaRespuesta, string? Evidencia, string? ContactoProveedor, string? Motivo,
    IReadOnlyList<CompromisoEntregaInput> Compromisos, Stream? Archivo = null,
    string? NombreArchivo = null, string? ContentType = null) : IRequest<RespuestaProveedorOcResponse>;

public sealed class RegistrarRespuestaProveedorValidator : AbstractValidator<RegistrarRespuestaProveedorCommand>
{
    public RegistrarRespuestaProveedorValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty();
        RuleFor(c => c.Tipo).IsInEnum();
        RuleFor(c => c.Archivo).Must(a => a is { CanSeek: true, Length: > 0 })
            .WithMessage("Adjunta el archivo de evidencia de la respuesta del proveedor.");
        RuleFor(c => c.NombreArchivo).NotEmpty().MaximumLength(255);
        RuleFor(c => c.ContentType).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Evidencia).MaximumLength(300);
        RuleFor(c => c.ContactoProveedor).MaximumLength(120);
        RuleFor(c => c.Motivo).MaximumLength(500);
        RuleFor(c => c.Compromisos).NotNull().Must(c => c is null || c.Count <= 200)
            .WithMessage("Una respuesta admite máximo 200 fechas prometidas.");

        When(c => c.Tipo == TipoRespuestaProveedorOc.Rechazo, () =>
        {
            RuleFor(c => c.Motivo).NotEmpty().WithMessage("Indica el motivo del rechazo.");
            RuleFor(c => c.Compromisos).Empty().WithMessage("Un rechazo no lleva fechas prometidas.");
        });

        RuleForEach(c => c.Compromisos).ChildRules(x =>
        {
            x.RuleFor(i => i.LineaOrdenCompraId).NotEmpty();
            x.RuleFor(i => i.Cantidad).GreaterThan(0).WithMessage("La cantidad prometida debe ser mayor a cero.");
            x.RuleFor(i => i.FechaPrometida).NotEmpty();
        });
    }
}

public sealed class RegistrarRespuestaProveedorHandler(ComprasDbContext db, ICurrentUserContext currentUser,
    IClock clock, CompartidoDbContext compartido, IMediator mediator, IBlobStoragePort blob,
    ILogger<RegistrarRespuestaProveedorHandler> logger) : IRequestHandler<RegistrarRespuestaProveedorCommand, RespuestaProveedorOcResponse>
{
    public async Task<RespuestaProveedorOcResponse> Handle(RegistrarRespuestaProveedorCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        // Las respuestas se incluyen: el dominio numera con su conteo.
        var oc = await db.OrdenesCompra.Include(o => o.Lineas).Include(o => o.RespuestasProveedor)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", "No se encontró la orden de compra.");
        var ahora = clock.UtcNow;
        var hoy = FechaContable.HoyLocal(ahora);
        var respuesta = oc.RegistrarRespuestaProveedor(command.Tipo, command.FechaRespuesta, hoy, command.Evidencia,
            command.ContactoProveedor, command.Motivo,
            command.Compromisos.Select(c => new CompromisoEntregaPropuesto(c.LineaOrdenCompraId, c.Cantidad, c.FechaPrometida)).ToList(),
            userId, ahora);
        // Comparte la transacción con el servicio de adjuntos: no se registra una respuesta sin archivo.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var anterior = compartido.Database.GetDbConnection();
        var opciones = RelationalOptionsExtension.Extract(compartido.GetService<IDbContextOptions>());
        var propia = opciones.Connection is null || opciones.IsConnectionOwned;
        var restaurada = propia ? (NpgsqlConnection)((ICloneable)anterior).Clone() : anterior;
        string? blobRef = null;
        try
        {
            await compartido.Database.CloseConnectionAsync();
            compartido.Database.SetDbConnection(db.Database.GetDbConnection(), contextOwnsConnection: false);
            await compartido.Database.UseTransactionAsync(tx.GetDbTransaction(), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            var tipoDocumentoId = await compartido.AdjuntoTiposDocumento
                .Where(t => t.TipoEntidad == "oc_respuesta_proveedor" && t.Codigo == "evidencia_respuesta" && t.Activo)
                .Select(t => t.Id).SingleAsync(cancellationToken);
            var adjunto = await mediator.Send(new SubirAdjuntoCommand("oc_respuesta_proveedor", respuesta.Id,
                tipoDocumentoId, command.NombreArchivo!, command.ContentType!, command.Archivo!), cancellationToken);
            blobRef = compartido.Adjuntos.Local.Single(a => a.Id == adjunto.Id).BlobRef;
            await tx.CommitAsync(cancellationToken);
            return RespuestaProveedorMapper.ToResponse(respuesta, vigente: true);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            if (blobRef is not null)
            {
                try { await blob.EliminarAsync(blobRef, CancellationToken.None); }
                catch (Exception error) { logger.LogError(error, "No se pudo limpiar la evidencia de la respuesta {RespuestaId}", respuesta.Id); }
            }
            throw;
        }
        finally
        {
            await compartido.Database.UseTransactionAsync(null, CancellationToken.None);
            compartido.Database.SetDbConnection(restaurada, contextOwnsConnection: propia);
        }
    }
}
