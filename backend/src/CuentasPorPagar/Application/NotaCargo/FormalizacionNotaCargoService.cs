using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Application.Integration.Mappers;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Domain.NotaCargo.Events;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.Events;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
using Cargo = Millet.CuentasPorPagar.Domain.NotaCargo.NotaCargo;
using Nc = Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor;
namespace Millet.CuentasPorPagar.Application.NotaCargo;
// La nota de cargo por devolución nace sin IVA (cantidad × costo); se compara contra el subtotal de la NC 03 (decisión 30-oct).
public sealed class FormalizacionNotaCargoService(CuentasPorPagarDbContext db, IPublisher publisher,
    PasivoAutorizadoParaPagoMapper pasivos, FacturaPagoAplicadoMapper pagos, IDependenciaRevisoraReadPort dependencias,
    ILogger<FormalizacionNotaCargoService> logger)
{
    public static readonly Guid MotivoDevolucionPendienteId = Guid.Parse("00000007-1001-0000-0000-000000000004");

    // La salida física de la devolución aplica la nota al saldo de la factura sin intervención de usuario.
    // Devuelve false (la nota se queda en Borrador) si no hay factura vigente con saldo suficiente.
    public async Task<bool> AplicarPorDevolucionAsync(Cargo cargo, DateTimeOffset ahora, CancellationToken ct)
    {
        if (cargo.FacturaOrigenId is not Guid facturaId || cargo.DevolucionAProveedorId is null || cargo.Estado != EstadoNotaCargo.Borrador) return false;
        var factura = await db.FacturasProveedor.FirstOrDefaultAsync(f => f.Id == facturaId, ct);
        if (factura is null || factura.Estado is EstadoPasivo.Pagada or EstadoPasivo.Cancelada || factura.ProveedorId != cargo.ProveedorId ||
            factura.Moneda != cargo.Moneda || cargo.Monto > factura.SaldoPendiente)
        {
            logger.LogWarning("La nota de cargo {Folio} por devolución no se aplicó: la factura {FacturaId} no existe, está pagada/cancelada o no tiene saldo suficiente.",
                cargo.Folio.Valor, facturaId);
            return false;
        }
        factura.AplicarNotaCargo(cargo.Monto, cargo.Id, DateOnly.FromDateTime(ahora.UtcDateTime));
        cargo.AplicarPorDevolucion(ahora);
        await publisher.Publish(new NotaCargoAutorizadaDomainEvent(cargo.EmpresaId, cargo.Id, cargo.ProveedorId, cargo.Monto, cargo.FacturaOrigenId, ahora,
            Subtotal: cargo.Monto, Iva: 0, Moneda: cargo.Moneda, TipoCambio: cargo.TipoCambio, SucursalId: cargo.SucursalId), ct);
        await RepublicarAsync(factura, ahora, factura.Movimientos.LastOrDefault(), ct);
        await IntentarPorCargoAsync(cargo, factura, ahora, ct);
        return true;
    }
    // La NC 03 pudo capturarse antes de la salida física: al nacer el cargo se busca su NC por factura + devolución.
    // Una sola coincidencia por subtotal formaliza sola; varias las elige el usuario (POST /notas-cargo/{id}/formalizar); si hay NC 03
    // pero ninguna coincide, la factura se detiene en revisión.
    private async Task IntentarPorCargoAsync(Cargo cargo, Domain.FacturaProveedor.FacturaProveedor factura, DateTimeOffset ahora, CancellationToken ct)
    {
        var ncs = await db.NotasCreditoProveedor.Where(n => n.EmpresaId == cargo.EmpresaId && n.FacturaOrigenId == cargo.FacturaOrigenId &&
            n.ProveedorId == cargo.ProveedorId && n.Moneda == cargo.Moneda && n.TipoRelacionCfdi == TipoRelacionCfdi.Devolucion &&
            n.Estado == EstadoNotaCredito.Abierta && n.MontoAplicado == 0).ToListAsync(ct);
        var ids = ncs.Select(n => n.Id).ToList();
        var ligadas = await db.NotasCargo.Where(n => n.NotaCreditoProveedorId != null && ids.Contains(n.NotaCreditoProveedorId.Value))
            .Select(n => n.NotaCreditoProveedorId).ToListAsync(ct);
        var candidatas = ncs.Where(n => !ligadas.Contains(n.Id)).ToList();
        if (candidatas.Count == 0) return;
        var coinciden = candidatas.Where(n => n.Subtotal == cargo.Monto).ToList();
        if (coinciden.Count == 1) await FormalizarAsync(cargo, coinciden[0], ahora, ct);
        else if (coinciden.Count == 0) await EnviarARevisionAsync(factura, ahora, ct);
    }
    // Mismo bloque que DetencionPorDanoService: motivo DEVOLUCION_PENDIENTE hacia Compras; no guarda, lo hace quien invoca.
    private async Task EnviarARevisionAsync(Domain.FacturaProveedor.FacturaProveedor factura, DateTimeOffset ahora, CancellationToken ct)
    {
        if (factura.Estado is not (EstadoPasivo.Capturada or EstadoPasivo.Autorizada) || factura.EnRevision) return;
        var motivo = await db.MotivosRevision.AsNoTracking().FirstOrDefaultAsync(m => m.Id == MotivoDevolucionPendienteId, ct);
        if (motivo is null || !motivo.Activo)
        {
            logger.LogWarning("El motivo de revisión Devolución pendiente ({MotivoId}) no existe o está inactivo; no se detiene la factura {FacturaId}.",
                MotivoDevolucionPendienteId, factura.Id);
            return;
        }
        var lista = await dependencias.ListarAsync(ct);
        DependenciaRevisoraDto? Area(string? codigo) => lista.FirstOrDefault(d => d.Activa
            && string.Equals(d.Codigo, codigo, StringComparison.OrdinalIgnoreCase));
        var area = Area(motivo.DependenciaRevisoraDefaultCodigo) ?? Area("CXP");
        var dependenciaId = area?.Id ?? EventListeners.Tesoreria.CancelacionPasivoSolicitadaHandler.DependenciaCxpId;
        if (area is null)
            logger.LogWarning("No existen los departamentos {Codigo} ni CXP; la factura {FacturaId} se detiene con el área CxP por defecto.",
                motivo.DependenciaRevisoraDefaultCodigo, factura.Id);
        factura.EnviarARevision(MotivoDevolucionPendienteId, dependenciaId, null, ahora);
        logger.LogInformation("Factura {FacturaId} detenida: hay NC 03 de devolución y ninguna coincide con el subtotal de la nota de cargo.", factura.Id);
    }
    // Mismo reenvío a Tesorería que AplicarNotaCargoHandler: pasivo con la elegibilidad nueva y, si hubo, el pago aplicado.
    private async Task RepublicarAsync(Domain.FacturaProveedor.FacturaProveedor factura, DateTimeOffset ahora, MovimientoPasivo? aplicacion, CancellationToken ct)
    {
        if (factura.Estado == EstadoPasivo.Autorizada)
            await pasivos.Handle(new FacturaProveedorAutorizadaDomainEvent(factura.EmpresaId, factura.Id, factura.OrdenCompraId, ahora), ct);
        if (aplicacion is not null)
            await pagos.Handle(new FacturaProveedorPagoAplicadoDomainEvent(
                factura.EmpresaId, factura.Id, factura.OrdenCompraId, factura.ImporteSaldado, ahora, aplicacion.Id), ct);
    }
    public async Task IntentarAsync(Nc nc, DateTimeOffset ahora, CancellationToken ct)
    {
        if (nc.TipoRelacionCfdi != TipoRelacionCfdi.Devolucion || nc.FacturaOrigenId is null || nc.Estado == EstadoNotaCredito.Cancelada || nc.MontoAplicado > 0) return;
        var cargos = await db.NotasCargo.Where(n => n.EmpresaId == nc.EmpresaId && n.FacturaOrigenId == nc.FacturaOrigenId &&
            n.ProveedorId == nc.ProveedorId && n.Moneda == nc.Moneda && n.Monto == nc.Subtotal &&
            n.Estado == EstadoNotaCargo.Aplicada && n.NotaCreditoProveedorId == null).ToListAsync(ct);
        // Un match ambiguo requiere elección explícita; nunca se toma el primero por fecha.
        if (await db.NotasCargo.AnyAsync(n => n.NotaCreditoProveedorId == nc.Id, ct)) return;
        if (cargos.Count == 1) await FormalizarAsync(cargos[0], nc, ahora, ct);
        // Hay cargos de devolución por formalizar para la factura y ninguno coincide con el subtotal de la NC: a revisión.
        else if (cargos.Count == 0 && nc.Estado == EstadoNotaCredito.Abierta && await db.NotasCargo.AnyAsync(n => n.EmpresaId == nc.EmpresaId &&
            n.FacturaOrigenId == nc.FacturaOrigenId && n.ProveedorId == nc.ProveedorId && n.Moneda == nc.Moneda && n.DevolucionAProveedorId != null &&
            n.Estado == EstadoNotaCargo.Aplicada && n.NotaCreditoProveedorId == null, ct))
        {
            var factura = await db.FacturasProveedor.FirstOrDefaultAsync(f => f.Id == nc.FacturaOrigenId, ct);
            if (factura is not null) await EnviarARevisionAsync(factura, ahora, ct);
        }
    }
    // La NC SUSTITUYE a la nota de cargo (nunca ambas): descuenta lo que el cargo no cubrió (IVA) y libera la retención de IVA.
    // Devuelve false (nada cambia) si la factura ya no admite aplicaciones; el pasivo ya pagado se revisa a mano.
    public async Task<bool> FormalizarAsync(Cargo cargo, Nc nc, DateTimeOffset ahora, CancellationToken ct)
    {
        if (nc.TipoRelacionCfdi != TipoRelacionCfdi.Devolucion || nc.Estado == EstadoNotaCredito.Cancelada ||
            nc.ProveedorId != cargo.ProveedorId || nc.EmpresaId != cargo.EmpresaId || nc.Moneda != cargo.Moneda ||
            nc.Subtotal != cargo.Monto || nc.FacturaOrigenId != cargo.FacturaOrigenId || cargo.FacturaOrigenId is null)
            throw new BusinessRuleException("NCG_NC_NO_COINCIDE", "La NC fiscal tipo 03 debe corresponder a la factura, proveedor, moneda e importe de la nota de cargo.");
        if (await db.NotasCargo.AnyAsync(n => n.NotaCreditoProveedorId == nc.Id && n.Id != cargo.Id, ct))
            throw new BusinessRuleException("NCG_NC_YA_VINCULADA", "La NC ya formaliza otra nota de cargo.");
        if (nc.MontoAplicado > 0)
            throw new BusinessRuleException("NCG_NC_YA_APLICADA", "La NC ya fue descontada. Revisa las aplicaciones antes de formalizar el cargo para evitar una deducción doble.");
        var factura = await db.FacturasProveedor.FirstOrDefaultAsync(f => f.Id == cargo.FacturaOrigenId, ct);
        var adicional = nc.Total - cargo.Monto;
        if (factura is null || factura.Estado is EstadoPasivo.Pagada or EstadoPasivo.Cancelada || nc.Estado != EstadoNotaCredito.Abierta ||
            adicional < 0 || adicional > factura.SaldoPendiente)
        {
            logger.LogWarning("La NC {NotaCreditoId} no formalizó la nota de cargo {Folio}: la factura no existe, está pagada/cancelada, la NC no está abierta o su total no cubre el cargo.",
                nc.Id, cargo.Folio.Valor);
            return false;
        }
        cargo.Formalizar(nc.Id, ahora);
        nc.AplicarMonto(nc.Total);
        if (adicional > 0) factura.AplicarNotaCredito(adicional, DateOnly.FromDateTime(ahora.UtcDateTime), nc.Id);
        if (cargo.DevolucionAProveedorId is Guid devolucion)
            await publisher.Publish(new NotaCreditoFiscalDevolucionRecibidaDomainEvent(
                nc.EmpresaId, nc.Id, cargo.Id, devolucion, nc.ProveedorId, nc.Total, nc.UuidCfdi, ahora), ct);
        await RepublicarAsync(factura, ahora, adicional > 0 ? factura.Movimientos.LastOrDefault() : null, ct);
        return true;
    }
}
