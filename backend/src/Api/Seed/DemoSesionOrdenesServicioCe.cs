using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Infrastructure;
namespace Millet.Api.Seed;

public sealed partial class DemoSesionSeedHostedService
{
    private static async Task SembrarOrdenesServicioCeAsync(IServiceProvider sp, CancellationToken ct)
    {
        var ce = sp.GetRequiredService<ComercioExteriorDbContext>();
        var compras = sp.GetRequiredService<ComprasDbContext>();
        var maestros = sp.GetRequiredService<CompartidoDbContext>();
        var e = await ce.Expedientes.Include(x => x.Gastos).SingleAsync(x => x.Id == Id("DEMO-CE-P1-FINANZAS"), ct);
        if (e.Gastos.Any(g => g.Id == Id("DEMO-CE-P6/FACTURA-2"))) return;
        var original = await compras.OrdenesCompra.SingleAsync(o => o.Id == e.OcId, ct);
        var servicioId = Id("DEMO-CE-P6/ORDEN");
        var oc = await compras.OrdenesCompra.Include(o => o.Lineas).SingleOrDefaultAsync(o => o.Id == servicioId, ct);
        if (oc is null)
        {
            var articulo = await maestros.Articulos.FirstAsync(a => a.Clave == "DEMO-ART-PIEZA", ct);
            var depto = await maestros.Departamentos.SingleAsync(d => d.Clave == "COMPRAS", ct);
            oc = new OrdenCompra(servicioId, EmpresaId, Millet.Compras.Domain.Oc.Folio.Parse("OC-DEMO2026-000207"), 2026,
                original.ProveedorId, original.SucursalDestinoId, original.CondicionesPagoId, original.UsoPrincipalId,
                CapturistaComprasDemoId, CapturistaComprasDemoId, new(2026, 10, 15), moneda: "MXN", sinRequisicionPrevia: true,
                observaciones: "DEMO P6 · servicios del agente aduanal · datos ficticios", motivoSinRequisicion: "DEMO P6");
            oc.AgregarLineaManual(Id("DEMO-CE-P6/LINEA"), articulo.Id, 1, "SRV", 43103.45m, depto.Id,
                descripcionExtendida: "DEMO · honorarios del agente aduanal", esServicio: true);
            oc.RecalcularImpuestos();
            var tipo = await compras.TiposDocumentoOc.SingleAsync(t => t.Clave == "cotizacion", ct);
            var bytes = CrearPdf("DEMO P6 · cotización ficticia del agente aduanal · total MXN 50,000.00");
            using var stream = new MemoryStream(bytes);
            var adjunto = Id("DEMO-CE-P6/COTIZACION");
            var url = await sp.GetRequiredService<IAlmacenarBlobPort>().SubirAsync(adjunto, stream, "application/pdf", "DEMO-P6-cotizacion.pdf", ct);
            oc.AdjuntarDocumento(adjunto, tipo.Id, "DEMO-P6-cotizacion.pdf", url, "application/pdf", bytes.Length, Fecha, CapturistaComprasDemoId);
            oc.ActualizarDatosFactura("G03", "PPD", "99");
            oc.EnviarAAutorizacion(Fecha);
            oc.Autorizar(Id("DEMO-CE-P6/N1"), NivelAutorizacion.Nivel1, JefeComprasDemoId, Fecha, "DEMO P6");
            oc.Autorizar(Id("DEMO-CE-P6/N2"), NivelAutorizacion.Nivel2, DireccionDemoId, Fecha, "DEMO P6");
            compras.OrdenesCompra.Add(oc); await compras.SaveChangesAsync(ct);
        }
        var concepto = await ce.ConceptosGasto.SingleAsync(c => c.EmpresaId == EmpresaId && c.Codigo == "HONORARIOS", ct);
        var copia = new OrdenServicioCopia(oc.Id, oc.Folio.Valor, oc.CalcularTotales().TotalAPagar, oc.Moneda, oc.CalcularTotales().TotalAPagar, 1);
        foreach (var (id, factura, importe) in new[] {
            (Id("DEMO-CE-P1/GASTO/HONORARIOS"), "DEMO-P6-FACTURA-1", 30000m),
            (Id("DEMO-CE-P6/FACTURA-2"), "DEMO-P6-FACTURA-2", 22000m) })
            e.GuardarGasto(id, new(concepto.Id, oc.ProveedorId, factura, "MXN", importe, new(2026, 10, 15),
                "DEMO-PED-P1", OrdenServicioId: oc.Id), concepto, 1, "MXN", copia);
        await ce.SaveChangesAsync(ct);
    }
}
