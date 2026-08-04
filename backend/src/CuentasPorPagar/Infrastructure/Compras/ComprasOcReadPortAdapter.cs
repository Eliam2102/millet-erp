using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.CuentasPorPagar.Domain.Ports.Compras;

namespace Millet.CuentasPorPagar.Infrastructure.Compras;

/// <summary>
/// Adapter real de <see cref="IComprasOcReadPort"/> (F5-PR1). Reemplaza
/// el <c>NoOpComprasOcReadPort</c> del F0-PR1.
///
/// <para>
/// Lee directamente <c>compras.ordenes_compra</c> via
/// <see cref="ComprasDbContext"/> — patrón válido porque CxP referencia
/// el proyecto Compras (Compras nunca referencia CxP, no hay ciclo).
/// Carga el agregado completo (AsNoTracking) y proyecta a los DTOs
/// neutros (<see cref="OrdenCompraDto"/> + <see cref="LineaOcDto"/>)
/// definidos en CxP.Domain.Ports.
/// </para>
///
/// <para>
/// <b>Total = <see cref="OrdenCompra.CalcularTotales"/>.TotalAPagar</b>:
/// el total contra el que la conciliación 3-way compara el total de la
/// factura (que viene CON IVA del CFDI) debe ser el total a pagar de la
/// OC — subtotal post-descuentos + gastos adicionales + IVA − retención
/// ISR + redondeo (diseño §4.9). Sumar solo cantidad×precio (sin IVA)
/// hacía que TODA factura gravada excediera la tolerancia default de
/// $0.99 y se cancelara como RechazadaPorTolerancia en la captura
/// (incidente verificación e2e 2026-07-15). Reusar el cálculo del
/// dominio evita divergencias con lo que Compras muestra en pantalla.
/// </para>
///
/// <para>
/// **Estados facturables**: el handler de captura (F3-PR1) acepta
/// <c>Autorizada</c>, <c>EnRecepcion</c>, <c>Recibida</c> o
/// <c>EnFacturacion</c>. Estos no son estados nativos de
/// <see cref="EstadoOrdenCompra"/>; cuando la state machine de OC
/// avance (F4+ de Compras OC) los sub-estados se reflejarán. Por
/// ahora el adapter mapea <see cref="EstadoOrdenCompra.Autorizada"/> a
/// <c>"Autorizada"</c> — suficiente para que el handler de captura
/// proceda.
/// </para>
/// </summary>
public sealed class ComprasOcReadPortAdapter : IComprasOcReadPort
{
    private readonly ComprasDbContext _db;

    public ComprasOcReadPortAdapter(ComprasDbContext db) { _db = db; }

    // Nota: sin AsNoTracking — las queries no-tracking sobre agregados con
    // owned types (DescuentoLinea/DescuentoGlobal) fallan en el provider
    // InMemory que usan los unit tests (KeyNotFoundException en el shaper).
    // El costo de tracking es despreciable (una OC + sus líneas en un
    // DbContext scoped de vida corta) y el port es de solo lectura.
    public async Task<OrdenCompraDto?> ObtenerAsync(Guid ordenCompraId, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == ordenCompraId, cancellationToken);

        return oc is null ? null : MapToDto(oc);
    }

    public async Task<IReadOnlyList<OrdenCompraDto>> ListarAutorizadasPorProveedorAsync(
        Guid proveedorId,
        CancellationToken cancellationToken)
    {
        var ocs = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .Where(o => o.ProveedorId == proveedorId && o.Estado == EstadoOrdenCompra.Autorizada)
            .ToListAsync(cancellationToken);

        return ocs.Select(MapToDto).ToList();
    }

    // Internal para testear el mapeo (Total = TotalAPagar) sin EF: el
    // provider InMemory no materializa complex types (DescuentoLinea) y
    // no hay proyecto de integration tests de CxP contra Postgres real.
    internal static OrdenCompraDto MapToDto(OrdenCompra oc)
    {
        var totales = oc.CalcularTotales();
        var lineas = oc.Lineas
            .Select(l => new LineaOcDto(
                Id: l.Id,
                ArticuloId: l.ArticuloId,
                Cantidad: l.Cantidad,
                PrecioUnitario: l.PrecioUnitario,
                CantidadFacturada: l.CantidadFacturada,
                CantidadRecibida: l.CantidadRecibida))
            .ToList();

        return new OrdenCompraDto(
            Id: oc.Id,
            Folio: oc.Folio.Valor,
            ProveedorId: oc.ProveedorId,
            EmpresaId: oc.EmpresaId,
            SucursalId: oc.SucursalDestinoId,
            Total: totales.TotalAPagar,
            Estado: oc.Estado.ToString(),
            Lineas: lineas);
    }
}
