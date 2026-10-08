using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Api.Seed;

public sealed partial class DemoSesionSeedHostedService
{
    private static async Task SembrarFacturacionAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<FacturacionDbContext>();
        var maestros = sp.GetRequiredService<CompartidoDbContext>();
        var mid = await maestros.Sucursales.SingleAsync(s => s.EmpresaId == EmpresaId && s.Clave == "MID", ct);
        foreach (var (clave, clienteClave, ranura) in new (string, string, decimal?)[]
        {
            ("DEMO-PED-NORMAL", "DEMO-CLI-NORMAL", null),
            ("DEMO-PED-RANURA", "DEMO-CLI-RANURA", 116m),
            ("DEMO-PED-INCOMPLETO", "DEMO-CLI-INCOMPLETO", null),
        })
        {
            if (await db.PedidosFacturables.AnyAsync(p => p.EmpresaId == EmpresaId && p.NumeroPedido == clave, ct)) continue;
            var cliente = await maestros.Clientes.SingleAsync(c => c.Clave == clienteClave, ct);
            var pedido = PedidoFacturable.ImportarDesdeAw(EmpresaId, clave, mid.Id, cliente.Id, cliente.RazonSocial,
                1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null,
                "DEMO: origen A+W simulado localmente; sin conexión ni escritura al origen.", 1, "15", ranura);
            pedido.AgregarLinea(null, "DEMO Vidrio templado 6 mm", "30171700", "H87", 10, 100, 0, false, tasaIva: .16m);
            pedido.RecalcularTotal();
            db.PedidosFacturables.Add(pedido);
        }
        await db.SaveChangesAsync(ct);
    }
}
