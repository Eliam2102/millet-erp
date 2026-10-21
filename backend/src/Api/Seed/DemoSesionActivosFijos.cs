using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
namespace Millet.Api.Seed;
public sealed partial class DemoSesionSeedHostedService
{
    private static async Task SembrarActivosFijosAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ActivosFijosDbContext>();
        var maestros = sp.GetRequiredService<CompartidoDbContext>();
        var sucursal = await maestros.Sucursales.SingleAsync(x => x.EmpresaId == EmpresaId && x.Clave == "MID", ct);
        foreach (var (clave, nombre, tasa) in new[] { ("EDI", "Edificios", 5m), ("MAQ", "Maquinaria y equipo industrial", 10m), ("MOB", "Mobiliario y equipo de oficina", 10m), ("TRA", "Automóviles y equipo de transporte", 25m), ("COM", "Equipo de cómputo", 30m), ("HER", "Herramientas y moldes", 35m) })
            if (!await db.Clases.AnyAsync(x => x.EmpresaId == EmpresaId && x.Clave == clave, ct))
                db.Clases.Add(new ClaseActivo(Id("DEMO-AF-CLASE-" + clave), EmpresaId, clave, nombre + " · propuesta VILO", tasa));
        await db.SaveChangesAsync(ct);
        var clases = await db.Clases.Where(x => x.EmpresaId == EmpresaId).ToDictionaryAsync(x => x.Clave, ct);
        foreach (var (clave, descripcion, valor, importacion) in new[] { ("MAQ", "Montacargas DEMO", 500000m, TipoImportacionActivo.No), ("TRA", "Camioneta DEMO", 650000m, TipoImportacionActivo.Definitivo), ("COM", "Equipo de cómputo DEMO", 30000m, TipoImportacionActivo.No) })
        {
            var id = Id("DEMO-AF-" + clave);
            if (await db.Activos.AnyAsync(x => x.Id == id, ct)) continue;
            db.Activos.Add(new Activo(id, EmpresaId, "DEMO-AF-" + clave, new DatosActivo(descripcion, clases[clave].Id, "DEMO", "DEMO", "DEMO-" + clave,
                new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), valor, "MXN", 1,
                new CustodiaActivo(CapturistaComprasDemoId, sucursal.Id, "Ubicación DEMO", Guid.Empty, null), importacion,
                importacion == TipoImportacionActivo.Definitivo ? "DEMO-PEDIMENTO-NO-FISCAL" : null, false, "Documento ficticio DEMO")));
        }
        await db.SaveChangesAsync(ct);
        var compras = sp.GetRequiredService<ComprasDbContext>();
        var ocId = Id("DEMO-AF-OC-MIXTA");
        if (!await compras.OrdenesCompra.AnyAsync(x => x.Id == ocId, ct))
        {
            var proveedor = await maestros.Proveedores.SingleAsync(x => x.Clave == "DEMO-PROV-ACT", ct);
            var articulo = await maestros.Articulos.SingleAsync(x => x.Clave == "DEMO-ART-PIEZA", ct);
            var departamento = await maestros.Departamentos.SingleAsync(x => x.Clave == "COMPRAS", ct);
            var condicion = await maestros.CondicionesPago.OrderBy(x => x.Clave).FirstAsync(ct);
            var uso = await maestros.UsosPrincipales.OrderBy(x => x.Clave).FirstAsync(ct);
            var oc = new OrdenCompra(ocId, EmpresaId, Millet.Compras.Domain.Oc.Folio.Parse("OC-DEMO2026-000301"), 2026, proveedor.Id, sucursal.Id, condicion.Id, uso.Id,
                CapturistaComprasDemoId, CapturistaComprasDemoId, new DateOnly(2026, 1, 1), sinRequisicionPrevia: true, observaciones: "OC mixta DEMO: inventario y activo fijo", motivoSinRequisicion: "Demostración AF");
            oc.AgregarLineaManual(Id("DEMO-AF-SILICON"), articulo.Id, 10, "PZA", 100, departamento.Id, descripcionExtendida: "10 cajas de silicón DEMO");
            oc.AgregarLineaManual(Id("DEMO-AF-MONTACARGAS"), articulo.Id, 1, "PZA", 500000, departamento.Id, descripcionExtendida: "Montacargas DEMO", esActivoFijo: true, claseActivoId: clases["MAQ"].Id);
            oc.RecalcularImpuestos(); oc.EnviarAAutorizacion(Fecha);
            oc.Autorizar(Id("DEMO-AF-N1"), NivelAutorizacion.Nivel1, JefeComprasDemoId, Fecha, "Autorización DEMO");
            oc.Autorizar(Id("DEMO-AF-N2"), NivelAutorizacion.Nivel2, DireccionDemoId, Fecha, "Autorización DEMO");
            compras.OrdenesCompra.Add(oc); await compras.SaveChangesAsync(ct);
        }
    }
}
