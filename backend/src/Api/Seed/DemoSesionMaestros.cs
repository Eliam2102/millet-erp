using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Blob;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Api.Seed;

public sealed partial class DemoSesionSeedHostedService
{
    // Razones sociales literales del catálogo ficticio de FiscalAPI (no añadir
    // DEMO al nombre fiscal: el PAC compara RFC + razón social + CP).
    // https://docs.fiscalapi.com/testing-data — consultado 07-oct-2026.
    public static readonly (string Clave, string Rfc, string Nombre, string Cp)[] ClientesSat =
    [
        ("DEMO-CLI-NORMAL", "IIA040805DZ4", "INDISTRIA ILUMINADORA DE ALMACENES", "62661"),
        ("DEMO-CLI-RANURA", "IVD920810GU2", "INNOVACION VALOR Y DESARROLLO", "63901"),
        ("DEMO-CLI-KIJ", "KIJ0906199R1", "KERNEL INDUSTIA JUGUETERA", "28971"),
        ("DEMO-CLI-XIA", "XIA190128J61", "XENON INDUSTRIAL ARTICLES", "76343"),
    ];

    private static async Task SembrarMaestrosAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<CompartidoDbContext>();
        var empresa = await db.Empresas.SingleOrDefaultAsync(e => e.Id == EmpresaId, ct);
        if (await db.Empresas.AnyAsync(e => e.Id != EmpresaId, ct))
            throw new InvalidOperationException("DEMO requiere una sola empresa. Use una base local recién migrada.");
        if (empresa is null)
        {
            empresa = new Empresa(EmpresaId, "DEMO-MILLET", "MID010101AAA", "MILLET INDUSTRIA DE VIDRIO (DEMO)", "601",
                "Calle DEMO", "1", "Colonia DEMO", "Ciudad DEMO", "Municipio DEMO", "Estado DEMO", "MEX",
                codigoPostal: "42501", tasaIvaDefault: .16m);
            db.Empresas.Add(empresa);
        }
        else if (empresa.RazonSocial != "MILLET INDUSTRIA DE VIDRIO (DEMO)" || empresa.RegimenFiscal != "601" || empresa.CodigoPostal != "42501")
            empresa.ActualizarDatos(razonSocial: "MILLET INDUSTRIA DE VIDRIO (DEMO)", regimenFiscal: "601", codigoPostal: "42501", tasaIvaDefault: .16m);

        foreach (var c in ClientesSat)
            if (!await db.Clientes.AnyAsync(x => x.Clave == c.Clave, ct))
                db.Clientes.Add(new Cliente(Id(c.Clave), c.Clave, c.Nombre,
                    rfc: c.Rfc, regimenFiscal: "601", codigoPostalFiscal: c.Cp,
                    usoCfdiDefault: "G03", formaPagoDefault: "03", metodoPagoDefault: "PUE"));
        if (!await db.Clientes.AnyAsync(c => c.Clave == "DEMO-CLI-INCOMPLETO", ct))
            db.Clientes.Add(new Cliente(Id("DEMO-CLI-INCOMPLETO"), "DEMO-CLI-INCOMPLETO", "DEMO Cliente sin datos fiscales"));
        if (!await db.Articulos.AnyAsync(a => a.Clave == "DEMO-ART-PIEZA", ct))
            db.Articulos.Add(new Articulo(Id("DEMO-ART-PIEZA"), "DEMO-ART-PIEZA", "DEMO Pieza de mantenimiento", "PZA",
                precioReferenciaMonto: 100m, precioReferenciaMoneda: "MXN"));
        // La NC automática de ranura reserva su propio folio. Ambas series son
        // fiscales, continuas y globales para la única empresa del sandbox.
        foreach (var (tipo, prefijo) in new[] { (TipoDocumentoSerie.Cfdi, "DEMO-F"), (TipoDocumentoSerie.NotaCredito, "DEMO-NC") })
            if (!await db.Series.AnyAsync(s => s.EmpresaId == EmpresaId && s.SucursalId == null && s.TipoDocumento == tipo && s.Activa, ct))
                db.Series.Add(new Serie(Id(prefijo), EmpresaId, null, tipo, prefijo, null, ReinicioPeriodo.None));
        await db.SaveChangesAsync(ct);

        var tipos = await db.AdjuntoTiposDocumento.Where(t => t.TipoEntidad == "proveedor" && t.Activo && t.Obligatorio)
            .OrderBy(t => t.Orden).ToListAsync(ct);
        if (tipos.Count != 5)
            throw new InvalidOperationException("DEMO: el expediente de persona moral debe tener cinco tipos obligatorios; revisar migraciones.");
        var blob = sp.GetRequiredService<IBlobStoragePort>();
        foreach (var (clave, cantidad) in new[] { ("DEMO-PROV-ACT", 5), ("DEMO-PROV-REV", 3) })
        {
            // Proveedor + expediente se confirman juntos. Si la demo lo validó o
            // añadió documentos, un reinicio respeta ese avance.
            if (await db.Proveedores.AnyAsync(p => p.Clave == clave, ct)) continue;
            var p = new Proveedor(Id(clave), clave, $"{clave} Suministros ficticios", "XAXX010101000", TipoPersonaProveedor.Moral,
                estatus: EstatusCatalogo.EnRevision);
            p.ActualizarDatos(banco: "DEMO Banco ficticio", clabe: "000000000000000000", beneficiario: p.RazonSocial);
            db.Proveedores.Add(p);
            var subidos = new List<string>();
            try
            {
                foreach (var tipo in tipos.Take(cantidad))
                {
                    var bytes = CrearPdf($"{clave} / {tipo.Nombre}");
                    var nombre = $"{clave}-{tipo.Codigo}.pdf";
                    var blobRef = $"proveedor/{p.Id}/{Id(nombre)}.pdf";
                    subidos.Add(blobRef);
                    using var stream = new MemoryStream(bytes);
                    await blob.SubirAsync(blobRef, stream, "application/pdf", ct);
                    db.Adjuntos.Add(new Adjunto(Id(nombre), "proveedor", p.Id, null, tipo.Id, nombre,
                        "application/pdf", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), blobRef,
                        tipo.VigenciaMeses is null ? null : new DateOnly(2027, 1, 12), ActorId, Fecha));
                }
                if (cantidad == 5) p.Validar(ActorId, Fecha);
                await db.SaveChangesAsync(ct);
            }
            catch
            {
                foreach (var key in subidos)
                    try { await blob.EliminarAsync(key, CancellationToken.None); } catch { /* conserva el error original */ }
                throw;
            }
        }
    }
}
