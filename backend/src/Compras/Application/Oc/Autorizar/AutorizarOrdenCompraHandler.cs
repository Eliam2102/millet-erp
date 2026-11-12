using MediatR;
using Millet.Compras.Application.Oc.Eventos;
using Millet.Compras.Application.Proveedores;
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
    private readonly ICurrentUserPermissions? _permisos;
    private readonly Millet.Compras.Application.PublicPorts.IFacturaServicioCeReadPort? _facturasCe;

    public AutorizarOrdenCompraHandler(
        ComprasDbContext db,
        CompartidoDbContext compartido,
        ICurrentUserContext currentUser,
        IClock clock,
        IPublisher publisher, ICurrentUserPermissions? permisos = null, Millet.Compras.Application.PublicPorts.IFacturaServicioCeReadPort? facturasCe = null)
    {
        _db = db;
        _compartido = compartido;
        _currentUser = currentUser;
        _clock = clock;
        _publisher = publisher; _permisos = permisos; _facturasCe = facturasCe;
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

        await FirmasServicioCeGuard.ExigirAsync(oc, command.Nivel, _permisos, cancellationToken);

        if (oc.EsServicioComercioExterior && command.Nivel == Millet.Compras.Domain.NivelAutorizacion.Nivel2
            && (_facturasCe is null || !await _facturasCe.TienePasivoVigenteAsync(oc.EmpresaId, oc.Id, cancellationToken)))
            throw new BusinessRuleException("CE_SERVICIO_SIN_PASIVO", "Registre primero la factura de servicio en Cuentas por Pagar antes de solicitar la segunda firma de Dirección Comercial.");

        // C10 — re-validar proveedor activo en cada transición.
        var proveedor = await _compartido.Proveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == oc.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"Proveedor '{oc.ProveedorId}' no encontrado.");
        if (proveedor.Estatus != EstatusCatalogo.Activo)
        {
            throw ProveedorNoUtilizable.Error(proveedor.Clave, proveedor.Estatus, $"El proveedor '{proveedor.Clave}' está {proveedor.Estatus} y no puede autorizar OCs.");
        }
        ProveedorNoUtilizable.RechazarSiBloqueado(proveedor);

        var resultado = oc.Autorizar(
            autorizacionId: Guid.CreateVersion7(),
            nivel: command.Nivel,
            usuarioId: userId,
            fechaHora: _clock.UtcNow,
            notas: command.Notas);

        activity?.SetTag("compras.oc.folio", oc.Folio.Valor);
        activity?.SetTag("compras.oc.estado", oc.Estado.ToString());

        if (resultado.OrdenCompraAutorizada is not null)
        {
            await _publisher.Publish(resultado.OrdenCompraAutorizada, cancellationToken);
        }

        // ADR-0009: el mapper encola antes de guardar estado + outbox.
        await _db.SaveChangesAsync(cancellationToken);

        if (resultado.OrdenCompraAutorizada is not null)
        {
            // El PDF consulta el estado persistido y tiene su propio guardado.
            await _publisher.Publish(new OrdenCompraAutorizadaPersistida(
                oc.Id, oc.Folio.Valor), cancellationToken);
        }
    }
}
