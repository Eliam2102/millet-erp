using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Infrastructure;
using Millet.Compras.Application.Integration;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;

namespace Millet.Api.Seed;

public sealed partial class DemoSesionSeedHostedService
{
    private static async Task SembrarComprasAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ComprasDbContext>();
        var maestros = sp.GetRequiredService<CompartidoDbContext>();
        var sucursales = await maestros.Sucursales.Where(s => s.EmpresaId == EmpresaId).ToDictionaryAsync(s => s.Clave, ct);
        var departamento = await maestros.Departamentos.SingleAsync(d => d.Clave == "COMPRAS", ct);
        var proveedor = await maestros.Proveedores.SingleAsync(p => p.Clave == "DEMO-PROV-ACT", ct);
        var articulo = await maestros.Articulos.SingleAsync(a => a.Clave == "DEMO-ART-PIEZA", ct);
        var condicion = await maestros.CondicionesPago.OrderBy(c => c.Clave).FirstAsync(ct);
        var uso = await maestros.UsosPrincipales.OrderBy(u => u.Clave).FirstAsync(ct);
        var blob = sp.GetRequiredService<IAlmacenarBlobPort>();
        var publisher = sp.GetRequiredService<IPublisher>();
        var tipos = await db.TiposDocumentoOc.Where(t => t.Activo && (t.Clave == "cotizacion" || t.Clave == "correo_autorizacion"))
            .ToListAsync(ct);
        if (tipos.Count != 2) throw new InvalidOperationException("DEMO: faltan los tipos de adjunto de OC; aplicar migraciones.");

        foreach (var sucursal in new[] { "MID", "MTY" })
        {
            var clave = "DEMO-OC-" + sucursal;
            var ocId = Id(clave);
            if (await db.OrdenesCompra.AnyAsync(o => o.EmpresaId == EmpresaId && (o.Id == ocId || o.ReferenciaProveedor == clave), ct)) continue;
            var archivos = new List<string>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                Requisicion? rq = null;
                if (sucursal == "MID")
                {
                    rq = await db.Requisiciones.Include(r => r.Lineas)
                        .SingleOrDefaultAsync(r => r.EmpresaId == EmpresaId && r.Descripcion == "DEMO-RQ-MID", ct);
                    if (rq is null)
                    {
                        rq = new Requisicion(Id("DEMO-RQ-MID"), EmpresaId, Millet.Compras.Domain.Folio.Parse("DEMO2026-000001"), 2026,
                            Clasificacion.OrdenCompra, sucursales[sucursal].Id, departamento.Id, null,
                            ActorId, ActorId, Prioridad.Normal, Fecha, proveedorSugeridoId: proveedor.Id, descripcion: "DEMO-RQ-MID");
                        rq.AgregarLinea(Id("DEMO-RQ-LINEA"), articulo.Id, 10, "PZA", Money.Mxn(100));
                        rq.EnviarAAutorizacion(Fecha);
                        rq.RegistrarAutorizacion(Id("DEMO-RQ-N1"), NivelAutorizacion.Nivel1, ActorId, Fecha, RequiereNivel.N1YN2, "DEMO autorización ficticia");
                        rq.RegistrarAutorizacion(Id("DEMO-RQ-N2"), NivelAutorizacion.Nivel2, ActorId, Fecha, RequiereNivel.N1YN2, "DEMO autorización ficticia");
                        rq.RegistrarCubrimiento([new CubrimientoLinea(rq.Lineas.Single().Id, 0, 10)], Fecha);
                        db.Requisiciones.Add(rq);
                        await sp.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(
                            new RequisicionAutorizadaIntegrationEvent(EmpresaId, Fecha, rq.Id), ct);
                    }
                }
                var oc = new OrdenCompra(Id(clave), EmpresaId, Millet.Compras.Domain.Oc.Folio.Parse(sucursal == "MID" ? "OC-DEMO2026-000101" : "OC-DEMO2026-000102"),
                    2026, proveedor.Id, sucursales[sucursal].Id, condicion.Id, uso.Id, CapturistaComprasDemoId, CapturistaComprasDemoId,
                    new DateOnly(2026, 10, 12), sinRequisicionPrevia: rq is null,
                    motivoSinRequisicion: rq is null ? "DEMO acceso restringido a MTY" : null, observaciones: clave);
                oc.ActualizarReferenciaProveedor(clave);
                if (rq is not null)
                {
                    oc.AgregarLineaDesdeRequisicion(Id(clave + "/LINEA"), articulo.Id, 10, "PZA", 100, departamento.Id, rq.Id, rq.Lineas.Single().Id);
                    rq.ComprometerEnOc(oc.Id);
                }
                else oc.AgregarLineaManual(Id(clave + "/LINEA"), articulo.Id, 10, "PZA", 100, departamento.Id);
                foreach (var tipo in tipos)
                {
                    var nombre = $"{clave}-{tipo.Clave}.pdf";
                    var bytes = CrearPdf(nombre);
                    using var stream = new MemoryStream(bytes);
                    var url = await blob.SubirAsync(Id(nombre), stream, "application/pdf", nombre, ct);
                    archivos.Add(url);
                    oc.AdjuntarDocumento(Id(nombre), tipo.Id, nombre, url, "application/pdf", bytes.Length, Fecha, CapturistaComprasDemoId);
                }
                oc.RecalcularImpuestos();
                oc.EnviarAAutorizacion(Fecha);
                oc.Autorizar(Id(clave + "/N1"), NivelAutorizacion.Nivel1, JefeComprasDemoId, Fecha, "DEMO autorización ficticia N1");
                var autorizacion = oc.Autorizar(Id(clave + "/N2"), NivelAutorizacion.Nivel2, DireccionDemoId, Fecha, "DEMO autorización ficticia N2");
                db.OrdenesCompra.Add(oc);
                // ADR-0009: el mapper encola el evento en el mismo contexto antes
                // de guardar estado y Outbox. No disparar MatrizAprobacionSatisfecha:
                // su flujo de cubrimiento crearía otra OC automática.
                await publisher.Publish(autorizacion.OrdenCompraAutorizada!, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                foreach (var url in archivos)
                    try { await blob.EliminarAsync(url, CancellationToken.None); } catch { /* conserva el error original */ }
                throw;
            }
        }
    }
}
