using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Domain.Adjuntos;
using Millet.SharedKernel.Application.Blob;
using System.Security.Cryptography;

namespace Millet.Api.Seed;
public sealed partial class DemoSesionSeedHostedService
{
    private static async Task SembrarFinanzasCeAsync(IServiceProvider sp, CancellationToken ct)
    {
        var ce = sp.GetRequiredService<ComercioExteriorDbContext>();
        var maestros = sp.GetRequiredService<CompartidoDbContext>();
        var compras = sp.GetRequiredService<ComprasDbContext>();
        var almacen = sp.GetRequiredService<AlmacenDbContext>();
        var conceptos = await ce.ConceptosGasto.Where(c => c.EmpresaId == EmpresaId).ToListAsync(ct);
        if (conceptos.Count == 0) { conceptos = ConceptoGasto.CrearCatalogo(EmpresaId).ToList(); ce.ConceptosGasto.AddRange(conceptos); await ce.SaveChangesAsync(ct); }
        var id = Id("DEMO-CE-P1-FINANZAS");
        if (await ce.Expedientes.AnyAsync(e => e.Id == id, ct)) return;
        var original = await compras.OrdenesCompra.SingleAsync(o => o.Id == Id("DEMO-CE-OC-IMPORTACION"), ct);
        var articulo = await maestros.Articulos.SingleAsync(a => a.Clave == "DEMO-ART-PIEZA", ct);
        var depto = await maestros.Departamentos.SingleAsync(d => d.Clave == "COMPRAS", ct);
        var ocId = Id("DEMO-CE-P1-OC");
        var oc = await compras.OrdenesCompra.SingleOrDefaultAsync(o => o.Id == ocId, ct);
        if (oc is null)
        {
            oc = new OrdenCompra(ocId, EmpresaId, Millet.Compras.Domain.Oc.Folio.Parse("OC-DEMO2026-000206"), 2026,
                original.ProveedorId, original.SucursalDestinoId, original.CondicionesPagoId, original.UsoPrincipalId,
                CapturistaComprasDemoId, CapturistaComprasDemoId, new(2026, 10, 15), moneda: "MXN", sinRequisicionPrevia: true,
                esImportacion: true, observaciones: "DEMO P1 · importación ficticia de dos partidas", motivoSinRequisicion: "DEMO P1 de costo puesto");
            oc.ActualizarInformacionImportacion(original.InformacionImportacion);
            oc.AgregarLineaManual(Id("DEMO-CE-P1-OC/A"), articulo.Id, 1000, "PZA", 200, depto.Id);
            oc.AgregarLineaManual(Id("DEMO-CE-P1-OC/B"), articulo.Id, 1000, "PZA", 300, depto.Id);
            oc.ActualizarDatosFactura("G03", "PPD", "99");
            oc.RecalcularImpuestos(); oc.EnviarAAutorizacion(Fecha);
            oc.Autorizar(Id("DEMO-CE-P1/N1"), NivelAutorizacion.Nivel1, JefeComprasDemoId, Fecha, "DEMO P1");
            oc.Autorizar(Id("DEMO-CE-P1/N2"), NivelAutorizacion.Nivel2, DireccionDemoId, Fecha, "DEMO P1");
            compras.OrdenesCompra.Add(oc); await compras.SaveChangesAsync(ct);
        }
        var sub = await (from s in almacen.SubAlmacenes join a in almacen.Almacenes on s.AlmacenId equals a.Id
            where a.SucursalId == original.SucursalDestinoId && s.Clave == "INSUMOS" orderby s.Id select s).FirstAsync(ct);
        var ubicacion = Id("DEMO-CE-P1-RACK");
        if (!await almacen.Ubicaciones.AnyAsync(u => u.Id == ubicacion, ct))
        { almacen.Ubicaciones.Add(new(ubicacion, sub.Id, "DEMO-P1", "DEMO P1 · costo puesto")); await almacen.SaveChangesAsync(ct); }
        var recepcionId = Id("DEMO-CE-P1-RECEPCION");
        if (!await almacen.Movimientos.AnyAsync(m => m.Id == recepcionId, ct))
        {
            var m = new MovimientoInventario(recepcionId, TipoMovimiento.EntradaCompra, EmpresaId, new(2026, 10, 15));
            m.AgregarLinea(new(Id("DEMO-CE-P1-RECEPCION/A"), m.Id, 1, articulo.Id, 1000, "PZA", 200, ubicacionId: ubicacion, lineaOcId: Id("DEMO-CE-P1-OC/A")));
            m.AgregarLinea(new(Id("DEMO-CE-P1-RECEPCION/B"), m.Id, 2, articulo.Id, 1000, "PZA", 300, ubicacionId: ubicacion, lineaOcId: Id("DEMO-CE-P1-OC/B")));
            m.Registrar(new FolioMovimiento("M-ENT2026-900206"), CapturistaComprasDemoId);
            almacen.Movimientos.Add(m);
            almacen.Entry(m).Property(x => x.OcId).CurrentValue = ocId;
            almacen.Entry(m).Property(x => x.PackingListBlobRef).CurrentValue = "DEMO P1 · packing ficticio";
            await almacen.SaveChangesAsync(ct);
        }
        var e = new Expediente(id, EmpresaId, "CE-IMP-2026-900206", TipoExpediente.Importacion, original.SucursalDestinoId,
            "DEMO P1 · gastos y anticipo ficticios", ocId, original.InformacionImportacion!.Value.IncotermId!.Value, "MXN", CapturistaComprasDemoId, []);
        e.InicializarHitos(new(2026, 10, 15));
        e.AgregarParticipante(Id("DEMO-CE-P1-AGENTE"), "DEMO · agente aduanal", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, original.ProveedorId, null);
        e.GuardarPartidaCosto(Id("DEMO-CE-P1-RECEPCION/A"), null, "DEMO · partida A", 200000, 2000, "DEMO-PED-P1");
        e.GuardarPartidaCosto(Id("DEMO-CE-P1-RECEPCION/B"), null, "DEMO · partida B", 300000, 6000, "DEMO-PED-P1");
        foreach (var (codigo, importe) in new[] { ("FLETE_INT", 40000m), ("IVA", 80000m) })
        {
            var concepto = conceptos.Single(c => c.Codigo == codigo);
            e.GuardarGasto(Id("DEMO-CE-P1/GASTO/" + codigo), new(concepto.Id, original.ProveedorId, "DEMO-FAC-" + codigo, "MXN", importe, new(2026, 10, 15), "DEMO-PED-P1", importe - 1000), concepto, 1, "MXN");
        }
        var tipo = await maestros.AdjuntoTiposDocumento.SingleAsync(t => t.Codigo == "IMP_PEDIMENTO" && t.TipoEntidad == "expediente_ce", ct);
        var evidencia = Id("DEMO-CE-P1/EVIDENCIA"); var bytes = CrearPdf("DEMO P1 · pedimento y pago ficticios · no es comprobante fiscal");
        var clave = $"expediente_ce/{id}/{evidencia}.pdf";
        if (!await maestros.Adjuntos.AnyAsync(a => a.Id == evidencia, ct))
        {
            using var stream = new MemoryStream(bytes); await sp.GetRequiredService<IBlobStoragePort>().SubirAsync(clave, stream, "application/pdf", ct);
            maestros.Adjuntos.Add(new Adjunto(evidencia, "expediente_ce", id, EmpresaId, tipo.Id, "DEMO-P1-pedimento-pago.pdf", "application/pdf", bytes.Length,
                Convert.ToHexStringLower(SHA256.HashData(bytes)), clave, null, CapturistaComprasDemoId, Fecha)); await maestros.SaveChangesAsync(ct);
        }
        e.RegistrarDocumento(Id("DEMO-CE-P1/DOCUMENTO"), new(tipo.Id, tipo.Codigo, tipo.Nombre, true, false), evidencia, null, CapturistaComprasDemoId, "DEMO-PED-P1");
        var anticipo = e.RegistrarAnticipo(Id("DEMO-CE-P1/ANTICIPO"), original.ProveedorId, "DEMO-PED-P1", new(140000, 9000, 1000), new(2026, 10, 15), "DEMO-PAGO-150000");
        anticipo.Comprobar(new(135000, 6500, 1000), evidencia, evidencia, new(2026, 10, 15));
        ce.Expedientes.Add(e); await ce.SaveChangesAsync(ct);
    }
}
