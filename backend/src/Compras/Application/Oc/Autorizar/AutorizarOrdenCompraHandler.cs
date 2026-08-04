using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Serilog.Context;

namespace Millet.Compras.Application.Oc.Autorizar;

/// <summary>
/// Handler de <see cref="AutorizarOrdenCompraCommand"/>. Lookup +
/// validación C10 (proveedor activo, defense in depth — ya validado al
/// EnviarAAutorizacion, pero re-validamos por si el proveedor se
/// bloqueó entre transiciones) + invoca el agregado para registrar
/// firma + transicionar + emitir evento si N2.
/// </summary>
public sealed class AutorizarOrdenCompraHandler : IRequestHandler<AutorizarOrdenCompraCommand>
{
    private readonly ComprasDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;

    public AutorizarOrdenCompraHandler(
        ComprasDbContext db,
        CompartidoDbContext compartido,
        ICurrentUserContext currentUser,
        IClock clock,
        IPublisher publisher)
    {
        _db = db;
        _compartido = compartido;
        _currentUser = currentUser;
        _clock = clock;
        _publisher = publisher;
    }

    public async Task Handle(AutorizarOrdenCompraCommand command, CancellationToken cancellationToken)
    {
        // F10-PR1: observabilidad — OrdenCompraId en LogContext + Activity tags
        // para correlación end-to-end en Application Insights.
        using var _ocId = LogContext.PushProperty("OrdenCompraId", command.OrdenCompraId);
        using var activity = ComprasActivitySource.Instance.StartActivity("Compras.AutorizarOc");
        activity?.SetTag("compras.oc.id", command.OrdenCompraId);
        activity?.SetTag("compras.autorizacion.nivel", command.Nivel.ToString());

        if (_currentUser.UserId is not Guid userId)
        {
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        }

        var oc = await _db.OrdenesCompra
            .Include(o => o.Autorizaciones)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}'.");

        // C10 — re-validar proveedor activo en cada transición.
        var proveedor = await _compartido.Proveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == oc.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"Proveedor '{oc.ProveedorId}' no encontrado.");
        if (proveedor.Estatus != EstatusCatalogo.Activo)
        {
            throw new BusinessRuleException(
                "PROVEEDOR_INACTIVO",
                $"El proveedor '{proveedor.Clave}' está {proveedor.Estatus} y no puede autorizar OCs.");
        }

        var resultado = oc.Autorizar(
            autorizacionId: Guid.CreateVersion7(),
            nivel: command.Nivel,
            usuarioId: userId,
            fechaHora: _clock.UtcNow,
            notas: command.Notas);

        await _db.SaveChangesAsync(cancellationToken);

        activity?.SetTag("compras.oc.folio", oc.Folio.Valor);
        activity?.SetTag("compras.oc.estado", oc.Estado.ToString());

        if (resultado.OrdenCompraAutorizada is not null)
        {
            await _publisher.Publish(resultado.OrdenCompraAutorizada, cancellationToken);
        }
    }
}
