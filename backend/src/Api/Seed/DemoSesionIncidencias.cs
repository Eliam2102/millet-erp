using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Blob;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Api.Seed;
public sealed partial class DemoSesionSeedHostedService
{
    private static async Task SembrarIncidenciasCeAsync(IServiceProvider sp, Guid sucursalId, Guid incotermId, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ComercioExteriorDbContext>(); var maestros = sp.GetRequiredService<CompartidoDbContext>();
        foreach (var rectificacion in new[] { true, false })
        {
            var codigo = rectificacion ? "EXP_PEDIMENTO" : "EXP_PACKING";
            var tipoDoc = await maestros.AdjuntoTiposDocumento.SingleAsync(t => t.TipoEntidad == "expediente_ce" && t.Codigo == codigo, ct);
            var requisito = new RequisitoDocumento(tipoDoc.Id, tipoDoc.Codigo, tipoDoc.Nombre, tipoDoc.Obligatorio, false);
            var clave = rectificacion ? "DEMO-CE-INC-RECTIFICACION" : "DEMO-CE-INC-DANADA";
            if (await db.Expedientes.AnyAsync(e => e.Id == Id(clave), ct)) continue;
            var e = new Expediente(Id(clave), EmpresaId, rectificacion ? "CE-EXP-2026-900005" : "CE-EXP-2026-900006", TipoExpediente.Exportacion, sucursalId,
                clave + " · datos ficticios", null, incotermId, "USD", CapturistaComprasDemoId, ["DEMO cliente ficticio"]);
            var hoy = FechaContable.HoyLocal(Fecha); e.InicializarHitos(hoy);
            List<Guid> archivos = [];
            foreach (var numero in rectificacion ? new[] { "ORIGINAL", "RECTIFICADO" } : new[] { "EVIDENCIA" })
            {
                var adjuntoId = Id(clave + "/" + numero); archivos.Add(adjuntoId);
                if (await maestros.Adjuntos.AnyAsync(a => a.Id == adjuntoId, ct)) continue;
                var bytes = CrearPdf(clave + " · " + numero + " · datos ficticios");
                var blobClave = $"expediente_ce/{e.Id}/{adjuntoId}.pdf";
                using var stream = new MemoryStream(bytes);
                await sp.GetRequiredService<IBlobStoragePort>().SubirAsync(blobClave, stream, "application/pdf", ct);
                maestros.Adjuntos.Add(new Adjunto(adjuntoId, "expediente_ce", e.Id, EmpresaId, tipoDoc.Id, numero + "-DEMO.pdf", "application/pdf", bytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(bytes)), blobClave, null, CapturistaComprasDemoId, Fecha));
            }
            await maestros.SaveChangesAsync(ct);
            if (rectificacion)
            {
                e.RegistrarDocumento(Id(clave + "/DOCUMENTO"), requisito, archivos[0], null, CapturistaComprasDemoId, "DEMO-PED-ORIGINAL");
                e.RegistrarPedimentoExportacion("DEMO-PED-ORIGINAL", "A1", hoy, hoy);
            }
            var incidencia = Id(clave + "/INCIDENCIA");
            e.AbrirIncidencia(incidencia, new(rectificacion ? TipoIncidencia.RectificacionPedimento : TipoIncidencia.MercanciaDanada, hoy, CapturistaComprasDemoId,
                rectificacion ? "DEMO corrección de clasificación del pedimento" : "DEMO mercancía dañada al descargar", EvidenciasIds: [archivos[^1]], HitoClave: rectificacion ? "despacho-aduanal" : "entregado"), archivos, CapturistaComprasDemoId, Fecha, hoy);
            if (rectificacion) e.CambiarIncidencia(incidencia, EstadoIncidencia.EnAtencion, "DEMO revisión del pedimento", null, [], null, CapturistaComprasDemoId, Fecha, archivos);
            if (rectificacion) e.CambiarIncidencia(incidencia, EstadoIncidencia.Resuelta, "DEMO se corrigió la clasificación y se emitió nuevo pedimento", "DEMO usar pedimento rectificado en IVA", [], null,
                CapturistaComprasDemoId, Fecha, archivos, requisito, archivos[1], "DEMO-PED-RECTIFICADO");
            db.Expedientes.Add(e); await db.SaveChangesAsync(ct);
        }
    }
}
