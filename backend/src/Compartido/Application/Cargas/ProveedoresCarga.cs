using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Application.Catalogos;
using Millet.DatosMaestros.Domain;
using static Millet.DatosMaestros.Application.Cargas.CargaMaestrosDatos;

namespace Millet.DatosMaestros.Application.Cargas;

public sealed class ProveedoresCarga(CompartidoDbContext db) : IAdaptadorCargaMaestros
{
    private readonly Dictionary<int, string> clavesAsignadas = [];
    public async Task PrepararArchivoAsync(TablaMaestros tabla, CancellationToken cancellationToken)
    {
        clavesAsignadas.Clear();
        var claves = await db.Proveedores.AsNoTracking().Select(p => p.Clave).ToListAsync(cancellationToken);
        var siguiente = claves.Where(c => c.Length == 7 && c[0] == 'P' && c[1..].All(char.IsAsciiDigit))
            .Select(c => int.Parse(c.AsSpan(1), CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Max();
        foreach (var fila in tabla.Filas.OrderBy(f => f.Texto("Grupo"), StringComparer.Ordinal).ThenBy(f => f.Texto("Clave legacy"), StringComparer.Ordinal))
            clavesAsignadas[fila.Numero] = "P" + (++siguiente).ToString("D6", CultureInfo.InvariantCulture);
    }
    public string Catalogo => "proveedores";
    public DbContext Db => db;
    public IReadOnlyList<ColumnaCarga> Columnas { get; } = [
        new("Clave legacy", "CardCode de SAP; llave preferida para actualizar. Obligatoria si el RFC es genérico.", "card_code", "ClaveLegacy"),
        new("Grupo", "Grupo SAP informativo.", "group_name"), new("Prefijo", "Prefijo SAP informativo; la carga de pruebas usa P más consecutivo.", "prefijo"),
        new("Razón social", "Obligatoria para alta; hasta 254 caracteres.", "razon_social"),
        new("Nombre comercial", "Opcional; para alta usa razón social si falta.", "nombre_comercial"),
        new("RFC", "12 o 13 caracteres; único salvo genéricos SAT. Vacío en alta = XEXX010101000."),
        new("Email", "Opcional; correo válido."), new("Teléfono", "Opcional; hasta 50 caracteres.", "telefono"),
        new("Moneda", "Código ISO 4217; MXP se convierte a MXN; ## indica multimoneda.", "currency"),
        new("Días de pago", "Opcional; entero de 0 a 365.", "condiciones_pago_dias"),
        new("Clave", "Opcional en alta; por omisión P más consecutivo de seis dígitos, continuando el máximo existente (regla de carga de pruebas).")];
    public IReadOnlyList<string> LlavesArchivo(FilaMaestros fila)
    {
        var rfc = fila.Texto("RFC");
        return (fila.Texto("Clave legacy") is { } legacy ? new[] { "legacy:" + legacy } : Array.Empty<string>())
            .Concat(rfc is not null && !Proveedor.EsRfcGenerico(rfc) ? ["rfc:" + rfc.ToUpperInvariant()] : []).ToList();
    }
    public async Task<PlanMaestros> PrepararAsync(FilaMaestros fila, CancellationToken ct)
    {
        var f = fila;
        var legacy = f.Texto("Clave legacy"); var rfc = f.Texto("RFC")?.ToUpperInvariant();
        var ligable = rfc is not null && !Proveedor.EsRfcGenerico(rfc);
        if (legacy is null && !ligable) throw Error("Clave legacy", "Capture CardCode o un RFC no genérico para identificar al proveedor.");
        var p = legacy is null ? null : await db.Proveedores.AsNoTracking().SingleOrDefaultAsync(x => x.ClaveLegacy == legacy, ct);
        if (p is null && ligable) p = await db.Proveedores.AsNoTracking().SingleOrDefaultAsync(x => x.Rfc == rfc, ct);
        if (ligable && await db.Proveedores.AnyAsync(x => x.Rfc == rfc && (p == null || x.Id != p.Id), ct)) throw Error("RFC", "El RFC pertenece a otro proveedor.");
        var monedaTexto = f.Texto("Moneda"); monedaTexto = monedaTexto == "MXP" ? "MXN" : monedaTexto;
        var moneda = monedaTexto == "##" ? null : monedaTexto is null ? p?.MonedaPreferidaId : await db.Monedas.Where(x => x.Codigo == monedaTexto).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (monedaTexto is not null and not "##" && moneda is null) throw Error("Moneda", "La moneda no existe. Regístrela antes de cargar.");
        short? dias = f.Texto("Días de pago") is { } d ? short.TryParse(d, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v is >= 0 and <= 365 ? v : throw Error("Días de pago", "Capture un entero entre 0 y 365.") : null;
        var razon = f.Texto("Razón social"); var comercial = f.Texto("Nombre comercial") ?? (p is null ? razon : null);
        var email = f.Texto("Email"); var telefono = f.Texto("Teléfono");
        var tipo = rfc is null ? p?.TipoPersona ?? TipoPersonaProveedor.Moral : rfc.Length == 13 && !Proveedor.EsRfcGenerico(rfc) ? TipoPersonaProveedor.Fisica : TipoPersonaProveedor.Moral;
        if (p is null && !clavesAsignadas.ContainsKey(f.Numero)) await PrepararArchivoAsync(new([f], []), ct);
        var clave = p?.Clave ?? f.Texto("Clave") ?? clavesAsignadas[f.Numero];
        if (clave.Length > 7 && f.Texto("Clave") is null && p is null) throw Error("Clave", "Se agotó el consecutivo de seis dígitos para proveedores. Revise el catálogo antes de continuar.");
        object comando = p is null ? new CrearProveedorCommand(clave, f.Requerido("Razón social"), rfc ?? Proveedor.RfcGenericoExtranjero, tipo, comercial, dias, moneda, email, telefono, ClaveLegacy: legacy, ActivarCargaSap: true)
            : new ActualizarProveedorCommand(p.Id, razon, comercial, rfc, tipo, dias, moneda, email, telefono, LimpiarMonedaPreferida: monedaTexto == "##");
        var plan = Plan(p, comando, (legacy is null ? Array.Empty<string>() : ["legacy:" + legacy]).Concat(ligable ? ["rfc:" + rfc] : []).Concat(p is null ? ["clave:" + clave] : ["id:" + p.Id]),
            ("Clave", p?.Clave, clave), ("Razón social", p?.RazonSocial, razon), ("Nombre comercial", p?.NombreComercial, comercial), ("RFC", p?.Rfc, rfc),
            ("Tipo de persona", p?.TipoPersona, tipo), ("Días de pago", p?.CondicionesPagoDias, dias), ("Moneda", p?.MonedaPreferidaId, moneda), ("Email", p?.Email, email), ("Teléfono", p?.Telefono, telefono));
        if (monedaTexto == "##" && p?.MonedaPreferidaId is not null)
            plan = plan with { Comando = comando, Cambios = plan.Cambios.Append(new("Moneda", p.MonedaPreferidaId.ToString(), "Sin moneda preferida")).ToList() };
        return plan;
    }
}
