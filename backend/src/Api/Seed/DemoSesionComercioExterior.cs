using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Blob;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Api.Seed;
public sealed partial class DemoSesionSeedHostedService
{
    // Referencias, archivos y compromisos explícitamente ficticios para la sesión DEMO.
    private static async Task SembrarComercioExteriorAsync(IServiceProvider sp, CancellationToken ct)
    {
        var maestros = sp.GetRequiredService<CompartidoDbContext>();
        var compras = sp.GetRequiredService<ComprasDbContext>();
        var ce = sp.GetRequiredService<ComercioExteriorDbContext>();
        var sucursal = await maestros.Sucursales.SingleAsync(s => s.EmpresaId == EmpresaId && s.Clave == "MID", ct);
        var exw = await maestros.Incoterms.SingleAsync(i => i.Codigo == "EXW", ct);
        var ddp = await maestros.Incoterms.SingleAsync(i => i.Codigo == "DDP", ct);
        var proveedor = await maestros.Proveedores.SingleAsync(p => p.Clave == "DEMO-PROV-ACT", ct);
        var ocId = Id("DEMO-CE-OC-IMPORTACION");
        var oc = await compras.OrdenesCompra.Include(x => x.Lineas).SingleOrDefaultAsync(x => x.Id == ocId, ct);
        if (oc is null)
        {
            var condicion = await maestros.CondicionesPago.OrderBy(x => x.Clave).FirstAsync(ct);
            var uso = await maestros.UsosPrincipales.OrderBy(x => x.Clave).FirstAsync(ct);
            oc = new OrdenCompra(ocId, EmpresaId, Millet.Compras.Domain.Oc.Folio.Parse("OC-DEMO2026-000201"), 2026, proveedor.Id, sucursal.Id, condicion.Id, uso.Id, CapturistaComprasDemoId, CapturistaComprasDemoId, new DateOnly(2026, 10, 15), moneda: "USD", tipoCambio: 18.5m, sinRequisicionPrevia: true, esImportacion: true, observaciones: "Compra de importación", motivoSinRequisicion: "Operación de importación");
            oc.ActualizarInformacionImportacion(new InformacionImportacion(exw.Id, "US", "DEMO-CONT-CE", "DEMO-RUTA", "2026-W42"));
            compras.OrdenesCompra.Add(oc);
        }
        // Actualiza también el origen ficticio de una ejecución anterior del seed.
        if (oc.Estado == EstadoOrdenCompra.Borrador)
        {
            var articulo = await maestros.Articulos.SingleAsync(x => x.Clave == "DEMO-ART-PIEZA", ct);
            var departamento = await maestros.Departamentos.SingleAsync(x => x.Clave == "COMPRAS", ct);
            if (oc.Lineas.Count == 0)
                oc.AgregarLineaManual(Id("DEMO-CE-OC-IMPORTACION/LINEA"), articulo.Id, 10, "PZA", 100, departamento.Id);
            oc.RecalcularImpuestos();
            oc.EnviarAAutorizacion(Fecha);
            oc.Autorizar(Id("DEMO-CE-OC-IMPORTACION/N1"), NivelAutorizacion.Nivel1, JefeComprasDemoId, Fecha, "Autorización de Compras");
            oc.Autorizar(Id("DEMO-CE-OC-IMPORTACION/N2"), NivelAutorizacion.Nivel2, DireccionDemoId, Fecha, "Autorización de Dirección");
            await compras.SaveChangesAsync(ct);
        }

        var tipos = await maestros.AdjuntoTiposDocumento.Where(t => t.TipoEntidad == "expediente_ce" && t.Activo).ToListAsync(ct);
        foreach (var tipo in new[]
        {
            TipoExpediente.Importacion,
            TipoExpediente.Exportacion
        }

        )
        {
            var clave = tipo == TipoExpediente.Importacion ? "DEMO-CE-IMPORTACION" : "DEMO-CE-EXPORTACION";
            var existente = await ce.Expedientes.Include(e => e.Hitos).SingleOrDefaultAsync(e => e.Id == Id(clave), ct);
            if (existente is not null)
            {
                existente.InicializarHitos(tipo == TipoExpediente.Importacion ? oc.FechaDocumento : FechaContable.HoyLocal(Fecha));
                if (existente.Estado == EstadoExpediente.Abierto && tipo == TipoExpediente.Importacion && existente.Hitos.Single(h => h.Clave == "planeado") is { FechaCompromiso: null, FechaReal: null, Omitido: false })
                    existente.CambiarCompromiso("planeado", new DateOnly(2026, 10, 14), CapturistaComprasDemoId, null);
                await ce.SaveChangesAsync(ct);
                continue;
            }
            var e = new Expediente(Id(clave), EmpresaId, tipo == TipoExpediente.Importacion ? "CE-IMP-2026-900001" : "CE-EXP-2026-900002", tipo, sucursal.Id, clave + " · datos ficticios", tipo == TipoExpediente.Importacion ? ocId : null, tipo == TipoExpediente.Importacion ? exw.Id : ddp.Id, "USD", CapturistaComprasDemoId, tipo == TipoExpediente.Exportacion ? ["Distribuidora de Cristales del Caribe"] : []);
            e.InicializarHitos(tipo == TipoExpediente.Importacion ? oc.FechaDocumento : FechaContable.HoyLocal(Fecha));
            if (tipo == TipoExpediente.Importacion)
                e.CambiarCompromiso("planeado", new DateOnly(2026, 10, 14), CapturistaComprasDemoId, null);
            e.AgregarParticipante(Id(clave + "/AGENTE"), "Agencia Aduanal del Sureste", TipoParticipante.AgenteAduanal, "Contacto de operaciones", PapelParticipante.Revisa, null, null);
            var codigos = tipo == TipoExpediente.Importacion ? new[]
            {
                "IMP_FACTURA",
                "IMP_PACKING",
                "IMP_TRANSPORTE"
            }

            : new[]
            {
                "EXP_PACKING"
            };
            foreach (var codigo in codigos)
            {
                var t = tipos.Single(x => x.Codigo == codigo);
                var adjuntoId = Id(clave + "/ARCHIVO/" + codigo);
                if (!await maestros.Adjuntos.AnyAsync(a => a.Id == adjuntoId, ct))
                {
                    var bytes = CrearPdf(clave + " · " + t.Nombre);
                    var blobClave = $"expediente_ce/{e.Id}/{adjuntoId}.pdf";
                    using var stream = new MemoryStream(bytes);
                    await sp.GetRequiredService<IBlobStoragePort>().SubirAsync(blobClave, stream, "application/pdf", ct);
                    maestros.Adjuntos.Add(new Adjunto(adjuntoId, "expediente_ce", e.Id, EmpresaId, t.Id, codigo + "-DEMO.pdf", "application/pdf", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), blobClave, null, CapturistaComprasDemoId, Fecha));
                    await maestros.SaveChangesAsync(ct);
                }

                e.RegistrarDocumento(Id(clave + "/DOCUMENTO/" + codigo), new(t.Id, t.Codigo, t.Nombre, t.Obligatorio, false), adjuntoId, null, CapturistaComprasDemoId);
            }

            if (tipo == TipoExpediente.Exportacion)
            {
                var isf = tipos.Single(x => x.Codigo == "EXP_ISF");
                e.MarcarNoAplica(Id(clave + "/NO-APLICA/ISF"), new(isf.Id, isf.Codigo, isf.Nombre, isf.Obligatorio, true), "Ruta fuera de Estados Unidos", CapturistaComprasDemoId);
            }

            ce.Expedientes.Add(e);
            await sp.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(new ExpedienteCeAbiertoEvent(EmpresaId, Fecha, e.Id, e.Folio), ct);
            await ce.SaveChangesAsync(ct);
        }
        await SembrarFinanzasCeAsync(sp, ct);
        await SembrarContenedoresCeAsync(sp, sucursal.Id, ddp.Id, ct);
    }
}
