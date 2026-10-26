using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Activos;
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
                "Pedido de prueba con origen A+W simulado localmente; sin conexión ni escritura al origen.", 1, "15", ranura);
            pedido.AgregarLinea(null, "Vidrio templado 6 mm", "30171700", "H87", 10, 100, 0, false, tasaIva: .16m)
                .EstablecerDatosImpresion("ITEM.1 REF: 1001 - DEMO - CW-01", 150.500m, 215.278m);
            pedido.RecalcularTotal();
            db.PedidosFacturables.Add(pedido);
        }
        await db.SaveChangesAsync(ct);

        await SembrarVentaDirectaAsync(db, maestros, mid.Id, ct);
    }

    /// <summary>
    /// F6.1: pedidos manuales clasificados (venta directa servicio/activo y
    /// emergencia) + una autorización de venta de activo para probar CA6.12. Se
    /// facturan con la serie VD-MID; no tocan A+W.
    /// </summary>
    private static async Task SembrarVentaDirectaAsync(
        FacturacionDbContext db, CompartidoDbContext maestros, Guid sucursalId, CancellationToken ct)
    {
        foreach (var (clave, clienteClave, clase, subtipo, comportamiento, motivo, descripcion, claveSat, unidad, precio) in new (string, string,
            ClasePedidoManual, SubtipoVentaDirecta?, ComportamientoFiscal, string?, string, string, string, decimal)[]
        {
            ("DEMO-PED-VD-SERV", "DEMO-CLI-KIJ", ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Servicio,
                ComportamientoFiscal.MostradorInmediato, null, "DEMO Servicio de instalación de cancel", "72151500", "E48", 1500m),
            ("DEMO-PED-VD-ACTIVO", "DEMO-CLI-XIA", ClasePedidoManual.VentaDirecta, SubtipoVentaDirecta.Activo,
                ComportamientoFiscal.VentaActivoFijo, null, "DEMO Camioneta de reparto usada (activo fijo)", "25101500", "H87", 85000m),
            ("DEMO-PED-EMERG", "DEMO-CLI-NORMAL", ClasePedidoManual.Emergencia, null,
                ComportamientoFiscal.MostradorInmediato, "A+W fuera de servicio (demo)", "DEMO Vidrio templado 6 mm", "30171700", "H87", 100m),
        })
        {
            if (await db.PedidosFacturables.AnyAsync(p => p.EmpresaId == EmpresaId && p.NumeroPedido == clave, ct)) continue;
            var cliente = await maestros.Clientes.SingleAsync(c => c.Clave == clienteClave, ct);
            var pedido = PedidoFacturable.CrearManual(EmpresaId, clave, sucursalId, cliente.Id, cliente.RazonSocial,
                1, comportamiento, "MXN", null, null, "DEMO: pedido manual ficticio (F6.1); no se informa a A+W.", ActorId,
                clase, subtipo, motivo, null);
            pedido.AgregarLinea(null, descripcion, claveSat, unidad, 1, precio, 0, false, tasaIva: .16m);
            pedido.RecalcularTotal();
            db.PedidosFacturables.Add(pedido);
        }

        // CA6.12: autorización del Contador General insertada directo (DEMO) para
        // emitir DEMO-PED-VD-ACTIVO sin depender de Activos Fijos, que sigue en
        // NoOpActivosFijosReadPort (PLATFORM-TODO vigente).
        const string activoRef = "DEMO-ACT-CAMIONETA";
        if (!await db.AutorizacionesVentaActivo.AnyAsync(a => a.EmpresaId == EmpresaId && a.ActivoRef == activoRef, ct))
            db.AutorizacionesVentaActivo.Add(AutorizacionVentaActivo.Crear(EmpresaId, activoRef,
                "DEMO Camioneta de reparto usada (activo ficticio)", 250000m, 180000m, false, 85000m, ActorId, Fecha));
        await db.SaveChangesAsync(ct);
    }
}
