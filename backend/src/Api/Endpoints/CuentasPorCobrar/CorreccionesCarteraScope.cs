using Microsoft.EntityFrameworkCore;
using Millet.Api.Web;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorCobrar;

/// <summary>
/// M-237 · ADR-0051: las correcciones de cartera modifican una factura o un movimiento por id, así que
/// el alcance por sucursal se verifica sobre la factura afectada (movimiento y corrección se resuelven a su factura).
/// El documento inexistente o sin sucursal verificable solo lo atiende el corporativo.
/// </summary>
public static class CorreccionesCarteraScope
{
    private const string Permiso = PermisosCanonicos.CuentasPorCobrarCarteraGestionarTodasSucursales;

    public static Task VerificarFacturaAsync(DocumentoSucursalScope scope, Guid facturaCarteraId, CancellationToken ct)
        => scope.VerificarAsync("factura_cartera", facturaCarteraId, Permiso, ct);

    public static async Task VerificarMovimientoAsync(
        CuentasPorCobrarDbContext db, DocumentoSucursalScope scope, Guid movimientoId, CancellationToken ct)
    {
        var facturaId = await db.MovimientosCartera.AsNoTracking()
            .Where(x => x.Id == movimientoId).Select(x => (Guid?)x.FacturaCarteraId).FirstOrDefaultAsync(ct);
        await VerificarFacturaAsync(scope, facturaId ?? Guid.Empty, ct);
    }

    public static async Task VerificarCorreccionAsync(
        CuentasPorCobrarDbContext db, DocumentoSucursalScope scope, Guid correccionId, CancellationToken ct)
    {
        var facturaId = await db.CorreccionesCartera.AsNoTracking()
            .Where(x => x.Id == correccionId).Select(x => (Guid?)x.FacturaCarteraId).FirstOrDefaultAsync(ct);
        await VerificarFacturaAsync(scope, facturaId ?? Guid.Empty, ct);
    }
}
