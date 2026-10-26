using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Blob;
using Millet.SharedKernel.Domain.Adjuntos;
namespace Millet.Api.Seed;
public sealed partial class DemoSesionSeedHostedService
{
    private static async Task SembrarConservacionActivosAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<ActivosFijosDbContext>(); var maestros = sp.GetRequiredService<CompartidoDbContext>();
        var clock = sp.GetRequiredService<IClock>(); var hoy = clock.HoyLocal();
        var a = await db.Activos.SingleAsync(x => x.Id == Id("DEMO-AF-MAQ"), ct);
        var adjuntoId = Id("DEMO-AF-GARANTIA-ARCHIVO");
        if (!await db.Documentos.AnyAsync(x => x.AdjuntoId == adjuntoId, ct))
        {
            if (!await maestros.Adjuntos.AnyAsync(x => x.Id == adjuntoId, ct))
            {
                var bytes = CrearPdf("Garantía de montacargas DEMO - documento ficticio");
                var clave = $"activo_fijo/{a.Id}/{adjuntoId}.pdf";
                using var contenido = new MemoryStream(bytes);
                await sp.GetRequiredService<IBlobStoragePort>().SubirAsync(clave, contenido, "application/pdf", ct);
                maestros.Adjuntos.Add(new Adjunto(adjuntoId, "activo_fijo", a.Id, EmpresaId, Guid.Parse("0000000f-0003-0000-0000-000000000003"), "Garantia-DEMO.pdf", "application/pdf", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), clave, null, CapturistaComprasDemoId, clock.UtcNow));
                await maestros.SaveChangesAsync(ct);
            }
            db.Documentos.Add(new DocumentoActivo(a, new(TipoDocumentoActivo.Garantia, adjuntoId, "DEMO-GARANTIA", "Proveedor ficticio DEMO", hoy, hoy, hoy.AddDays(20), "Datos ficticios. Vence 20 días después de la primera siembra.")));
            await db.SaveChangesAsync(ct);
        }
        if (!await db.Conteos.AnyAsync(x => x.Partidas.Any(p => p.Numero == "DEMO-SOBRANTE"), ct))
        {
            var activos = await db.Activos.Where(x => x.EmpresaId == EmpresaId && x.Numero.StartsWith("DEMO-AF-") && x.SucursalId == a.SucursalId).OrderBy(x => x.Numero).Take(3).ToListAsync(ct);
            var conteo = new ConteoActivo(EmpresaId, a.SucursalId, null, CapturistaComprasDemoId, activos);
            for (var i = 0; i < activos.Count; i++)
                conteo.Capturar(activos[i].Numero, (ResultadoConteo)(i + 1), i == 2 ? "Otra ubicación DEMO" : null, "Evidencia ficticia del conteo DEMO", CapturistaComprasDemoId);
            conteo.Capturar("DEMO-SOBRANTE", ResultadoConteo.Sobrante, "Patio DEMO", "Equipo ficticio sin registro", CapturistaComprasDemoId);
            conteo.Cerrar(CapturistaComprasDemoId, clock.UtcNow);
            db.Conteos.Add(conteo); await db.SaveChangesAsync(ct);
        }
    }
}
