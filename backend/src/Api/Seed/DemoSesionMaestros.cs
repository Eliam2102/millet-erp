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
        else if (empresa.RazonSocial.StartsWith("Empresa inicial pendiente", StringComparison.Ordinal))
        {
            // La empresa que crea la migración inicial queda con datos DEMO una sola vez; después se respeta
            // lo que un administrador edite (PRU-A: reiniciar la API ya no regresa la razón social).
            empresa.ActualizarDatos(razonSocial: "MILLET INDUSTRIA DE VIDRIO (DEMO)", regimenFiscal: "601", codigoPostal: "42501", tasaIvaDefault: .16m);
        }


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
        await SembrarSedesMilletAsync(db, ct);
        // Completa sólo las asignaciones DEMO faltantes, sin reemplazar puestos operados.
        var sucursalesDemo = await db.Sucursales.Where(s => s.EmpresaId == EmpresaId && ClavesSedesDemo.Contains(s.Clave)).Select(s => s.Id).ToArrayAsync(ct);
        var asignacionesDemo = await db.SucursalDepartamentos.Where(a => a.EmpresaId == EmpresaId && sucursalesDemo.Contains(a.SucursalId) && a.Estatus == EstatusCatalogo.Activo).ToListAsync(ct);
        foreach (var a in asignacionesDemo)
        {
            if (await db.SucursalPuestos.AnyAsync(p => p.SucursalId == a.SucursalId && p.DepartamentoId == a.DepartamentoId && p.Estatus == EstatusCatalogo.Activo && db.Puestos.Any(puesto => puesto.Id == p.PuestoId && puesto.Estatus == EstatusCatalogo.Activo), ct)) continue;
            var departamento = await db.Departamentos.SingleAsync(d => d.Id == a.DepartamentoId, ct);
            var claveBase = "DEMO-" + departamento.Clave[..Math.Min(departamento.Clave.Length, 15)];
            for (var intento = 0; ; intento++)
            {
                var sufijo = intento == 0 ? "" : "-" + intento.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var clavePuesto = claveBase[..Math.Min(claveBase.Length, 20 - sufijo.Length)] + sufijo;
                var puesto = await db.Puestos.SingleOrDefaultAsync(p => p.EmpresaId == EmpresaId && p.Clave == clavePuesto, ct);
                if (puesto is not null && (puesto.Estatus != EstatusCatalogo.Activo || await db.SucursalPuestos.AnyAsync(p => p.SucursalId == a.SucursalId && p.DepartamentoId == a.DepartamentoId && p.PuestoId == puesto.Id, ct))) continue;
                if (puesto is null)
                {
                    puesto = new Puesto(Guid.CreateVersion7(), EmpresaId, clavePuesto, "DEMO · Personal de " + departamento.Nombre);
                    db.Puestos.Add(puesto);
                }
                db.SucursalPuestos.Add(new SucursalPuesto(Guid.CreateVersion7(), EmpresaId, a.SucursalId, puesto.Id, a.DepartamentoId));
                await db.SaveChangesAsync(ct);
                break;
            }
        }

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

    // Sedes de Millet en la demo: una empresa con 5 sedes (Conkal, Chichí Suárez, Circuito, Cancún y Planta Pintura).
    // Las 3 sucursales del catálogo de pruebas (MID, MTY y QRO) se renombran en su lugar para conservar órdenes,
    // usuarios y almacenes ya ligados; la clave interna no cambia (es inmutable). Circuito y Planta Pintura se agregan.
    // Solo renombra si conserva el nombre de prueba: respeta lo que un administrador haya editado.
    // Los domicilios son ficticios (sin datos reales de Millet en el repositorio).
    public static readonly string[] ClavesSedesDemo = ["MID", "MTY", "QRO", "CIR", "PIN"];

    private static readonly (string Clave, string NombrePrueba, string Nombre, TipoSucursal Tipo, string Ciudad, string Municipio, string Estado, string Cp)[] SedesMillet =
    [
        ("MID", "Planta México Centro", "Conkal", TipoSucursal.Planta, "Conkal", "Conkal", "Yucatán", "97345"),
        ("MTY", "Planta Monterrey", "Cancún", TipoSucursal.Taller, "Cancún", "Benito Juárez", "Quintana Roo", "77500"),
        ("QRO", "Planta Querétaro", "Chichí Suárez", TipoSucursal.Taller, "Mérida", "Mérida", "Yucatán", "97306"),
        ("CIR", "", "Circuito", TipoSucursal.Taller, "Mérida", "Mérida", "Yucatán", "97000"),
        ("PIN", "", "Planta Pintura", TipoSucursal.Planta, "Mérida", "Mérida", "Yucatán", "97000"),
    ];

    private static async Task SembrarSedesMilletAsync(CompartidoDbContext db, CancellationToken ct)
    {
        foreach (var sede in SedesMillet)
        {
            var sucursal = await db.Sucursales.SingleOrDefaultAsync(s => s.EmpresaId == EmpresaId && s.Clave == sede.Clave, ct);
            if (sucursal is null)
            {
                sucursal = new Sucursal(Id("DEMO-SEDE/" + sede.Clave), EmpresaId, sede.Clave, sede.Nombre, sede.Tipo,
                    "Domicilio DEMO", "S/N", "Colonia DEMO", sede.Ciudad, sede.Municipio, sede.Estado, sede.Cp, "México");
                db.Sucursales.Add(sucursal);
            }
            else if (sucursal.Nombre == sede.NombrePrueba)
            {
                sucursal.ActualizarDatos(nombre: sede.Nombre, tipo: sede.Tipo, ciudad: sede.Ciudad, municipio: sede.Municipio,
                    estado: sede.Estado, codigoPostal: sede.Cp);
            }
        }
        await db.SaveChangesAsync(ct);

        // Las sedes nuevas reciben los mismos departamentos que la sede principal, para dar de alta empleados.
        var principal = await db.Sucursales.SingleAsync(s => s.EmpresaId == EmpresaId && s.Clave == "MID", ct);
        var departamentos = await db.SucursalDepartamentos.Where(a => a.SucursalId == principal.Id && a.Estatus == EstatusCatalogo.Activo)
            .Select(a => a.DepartamentoId).ToListAsync(ct);
        foreach (var clave in new[] { "CIR", "PIN" })
        {
            var sede = await db.Sucursales.SingleAsync(s => s.EmpresaId == EmpresaId && s.Clave == clave, ct);
            foreach (var departamentoId in departamentos)
                if (!await db.SucursalDepartamentos.AnyAsync(a => a.SucursalId == sede.Id && a.DepartamentoId == departamentoId, ct))
                    db.SucursalDepartamentos.Add(new SucursalDepartamento(Id($"DEMO-SEDE-DEPTO/{clave}/{departamentoId}"), EmpresaId, sede.Id, departamentoId));
        }
        await db.SaveChangesAsync(ct);
    }
}
