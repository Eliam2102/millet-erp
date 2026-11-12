using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Proformas.Queries;
using Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.CartaPorte.Tramos;

/// <summary>Tramo del traslado con su estado para la pantalla: sin Carta Porte, con su Carta Porte, o con el documento del tercero.</summary>
public sealed record TramoTrasladoResponse(
    Guid Id,
    Guid PedidoFacturableId,
    int Orden,
    string Modalidad,
    string Origen,
    string Destino,
    decimal? DistanciaKm,
    string? OrigenCodigoPostal,
    string? OrigenEstado,
    string? DestinoCodigoPostal,
    string? DestinoEstado,
    DateTimeOffset? FechaSalida,
    Guid? CartaPorteId,
    string? CartaPorteFolio,
    string? CartaPorteEstado,
    string? TerceroNombre,
    string? TerceroRfc,
    string? DocumentoTipo,
    string? DocumentoFolio,
    string? DocumentoUuid,
    DateOnly? DocumentoFecha,
    string Estado)
{
    public static TramoTrasladoResponse De(TramoTraslado t, string? cpFolio = null, string? cpEstado = null) => new(
        t.Id, t.PedidoFacturableId, t.Orden, t.Modalidad.ToString(), t.Origen, t.Destino, t.DistanciaKm,
        t.OrigenCodigoPostal, t.OrigenEstado, t.DestinoCodigoPostal, t.DestinoEstado, t.FechaSalida,
        t.CartaPorteId, cpFolio, cpEstado, t.TerceroNombre, t.TerceroRfc, t.DocumentoTipo, t.DocumentoFolio,
        t.DocumentoUuid, t.DocumentoFecha, EstadoDe(t, cpEstado));

    /// <summary>PorEmitir | CartaPorte{Estado} | SinDocumentoTercero | DocumentoTercero.</summary>
    public static string EstadoDe(TramoTraslado t, string? cpEstado) => t.Modalidad switch
    {
        ModalidadTramo.VehiculoPropio => t.CartaPorteId is null ? "PorEmitir" : $"CartaPorte{cpEstado}",
        _ => t.TieneDocumentoTercero ? "DocumentoTercero" : "SinDocumentoTercero",
    };
}

// ---------------------------------------------------------------- crear
/// <summary>
/// Agrega un tramo al traslado del pedido (siguiente número). Sin <see cref="Destino"/> toma la dirección de entrega del pedido
/// (A+W: entrega <c>AL_</c>, o la principal <c>AH_</c> si la de entrega viene vacía) cuando la vista está disponible.
/// </summary>
public sealed record CrearTramoTrasladoCommand(
    Guid PedidoFacturableId,
    ModalidadTramo Modalidad,
    string Origen,
    string? Destino = null,
    decimal? DistanciaKm = null,
    string? OrigenCodigoPostal = null,
    string? OrigenEstado = null,
    string? DestinoCodigoPostal = null,
    string? DestinoEstado = null,
    DateTimeOffset? FechaSalida = null) : IRequest<TramoTrasladoResponse>;

public sealed class CrearTramoTrasladoValidator : AbstractValidator<CrearTramoTrasladoCommand>
{
    public CrearTramoTrasladoValidator()
    {
        RuleFor(c => c.PedidoFacturableId).NotEmpty();
        RuleFor(c => c.Modalidad).IsInEnum();
        RuleFor(c => c.Origen).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Destino).MaximumLength(200);
        RuleFor(c => c.DistanciaKm).GreaterThanOrEqualTo(0).When(c => c.DistanciaKm.HasValue);
        RuleFor(c => c.OrigenCodigoPostal).MaximumLength(5);
        RuleFor(c => c.DestinoCodigoPostal).MaximumLength(5);
        RuleFor(c => c.OrigenEstado).MaximumLength(3);
        RuleFor(c => c.DestinoEstado).MaximumLength(3);
    }
}

public sealed class CrearTramoTrasladoHandler(
    FacturacionDbContext db, IAlcanceCajaEvaluator alcance, ICurrentEmpresaContext empresa,
    IDireccionEntregaPedidoReadPort direcciones) : IRequestHandler<CrearTramoTrasladoCommand, TramoTrasladoResponse>
{
    public async Task<TramoTrasladoResponse> Handle(CrearTramoTrasladoCommand command, CancellationToken cancellationToken)
    {
        if (empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");
        var pedido = await ProformaAlcance.AsegurarPedidoVisibleAsync(db, alcance, command.PedidoFacturableId, cancellationToken);

        var destino = command.Destino;
        string? destinoCp = command.DestinoCodigoPostal;
        if (string.IsNullOrWhiteSpace(destino) && await direcciones.ObtenerAsync(pedido.Id, cancellationToken) is { } d)
        {
            var entrega = DireccionEntregaPedido.Resolver(d);
            destino = DireccionEntregaPedido.Texto(entrega);
            destinoCp ??= entrega.CodigoPostal;
        }
        if (string.IsNullOrWhiteSpace(destino))
            throw new BusinessRuleException("TRAMO_DESTINO_REQUERIDO",
                "Indica el destino del tramo: el pedido no trae dirección de entrega.");

        var orden = (await db.TramosTraslado.Where(t => t.PedidoFacturableId == pedido.Id).MaxAsync(t => (int?)t.Orden, cancellationToken) ?? 0) + 1;
        var tramo = TramoTraslado.Crear(empresaId, pedido.SucursalId, pedido.Id, orden, command.Modalidad, command.Origen, destino,
            command.DistanciaKm, command.OrigenCodigoPostal, command.OrigenEstado, destinoCp, command.DestinoEstado, command.FechaSalida);
        db.TramosTraslado.Add(tramo);
        await db.SaveChangesAsync(cancellationToken);
        return TramoTrasladoResponse.De(tramo);
    }
}

// ------------------------------------------------------- documento tercero
/// <summary>Registra el documento del transportista tercero que ampara el tramo (el tramo no emite Carta Porte de Millet).</summary>
public sealed record RegistrarDocumentoTerceroCommand(
    Guid PedidoFacturableId,
    Guid TramoId,
    string TerceroNombre,
    string DocumentoTipo,
    string? TerceroRfc = null,
    string? Folio = null,
    string? Uuid = null,
    DateOnly? Fecha = null) : IRequest<TramoTrasladoResponse>;

public sealed class RegistrarDocumentoTerceroValidator : AbstractValidator<RegistrarDocumentoTerceroCommand>
{
    public RegistrarDocumentoTerceroValidator()
    {
        RuleFor(c => c.TramoId).NotEmpty();
        RuleFor(c => c.TerceroNombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.TerceroRfc).MaximumLength(13);
        RuleFor(c => c.DocumentoTipo).NotEmpty().MaximumLength(40);
        RuleFor(c => c.Folio).MaximumLength(60);
        RuleFor(c => c.Uuid).MaximumLength(36);
    }
}

public sealed class RegistrarDocumentoTerceroHandler(
    FacturacionDbContext db, IAlcanceCajaEvaluator alcance, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<RegistrarDocumentoTerceroCommand, TramoTrasladoResponse>
{
    public async Task<TramoTrasladoResponse> Handle(RegistrarDocumentoTerceroCommand command, CancellationToken cancellationToken)
    {
        await ProformaAlcance.AsegurarPedidoVisibleAsync(db, alcance, command.PedidoFacturableId, cancellationToken);
        var tramo = await db.TramosTraslado.FirstOrDefaultAsync(
                t => t.Id == command.TramoId && t.PedidoFacturableId == command.PedidoFacturableId, cancellationToken)
            ?? throw new EntityNotFoundException("TRAMO_NO_ENCONTRADO", $"No existe el tramo '{command.TramoId}' en el pedido.");
        tramo.RegistrarDocumentoTercero(command.TerceroNombre, command.TerceroRfc, command.DocumentoTipo, command.Folio,
            command.Uuid, command.Fecha, usuario.UserName, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return TramoTrasladoResponse.De(tramo);
    }
}

// ------------------------------------------------------------------ listar
public sealed record ListarTramosPedidoQuery(Guid PedidoFacturableId) : IRequest<IReadOnlyList<TramoTrasladoResponse>>;

public sealed class ListarTramosPedidoHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    : IRequestHandler<ListarTramosPedidoQuery, IReadOnlyList<TramoTrasladoResponse>>
{
    public async Task<IReadOnlyList<TramoTrasladoResponse>> Handle(ListarTramosPedidoQuery query, CancellationToken cancellationToken)
    {
        await ProformaAlcance.AsegurarPedidoVisibleAsync(db, alcance, query.PedidoFacturableId, cancellationToken);
        var tramos = await db.TramosTraslado.AsNoTracking()
            .Where(t => t.PedidoFacturableId == query.PedidoFacturableId).OrderBy(t => t.Orden).ToListAsync(cancellationToken);
        var cpIds = tramos.Where(t => t.CartaPorteId != null).Select(t => t.CartaPorteId!.Value).ToList();
        var cps = await db.CartasPorte.AsNoTracking().Where(c => cpIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Folio, c.Estado }).ToDictionaryAsync(c => c.Id, cancellationToken);
        return tramos.Select(t => t.CartaPorteId is Guid id && cps.TryGetValue(id, out var cp)
            ? TramoTrasladoResponse.De(t, cp.Folio, cp.Estado.ToString())
            : TramoTrasladoResponse.De(t)).ToList();
    }
}
