using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;

namespace Millet.Integraciones.Aw.Application.Productos;

public sealed record AwProductoMapeoResultado(AplicarProductoAwSnapshot? Snapshot, string? Error)
{
    public bool EsValido => Snapshot is not null;
}

/// <summary>Mapper puro fila A+W → <see cref="AplicarProductoAwSnapshot"/> (doc 06 §3). Nulo nunca pasa a 0.</summary>
public static class AwProductoSnapshotMapper
{
    public static AwProductoMapeoResultado Mapear(
        AwProductoOrigenFila f, DateTime leidoEnUtc, IReadOnlyDictionary<string, ReglaFiscalProductoAw>? reglas = null)
    {
        var referencia = Limpiar(f.ProductoRef);
        if (referencia is null) return new(null, "producto_ref vacío.");
        var descripcion = Limpiar(f.Descripcion);
        if (descripcion is null) return new(null, $"descripcion vacía (referencia {referencia}).");

        var variantes = new List<ProductoAwVarianteDato>();
        foreach (var v in f.Variantes)
        {
            var clave = Limpiar(v.ClaveVariante);
            if (clave is null) return new(null, $"variante sin clave_variante (referencia {referencia}).");
            variantes.Add(new(clave, v.AltoMm, v.AnchoMm, v.EspesorMm, Limpiar(v.Composicion)));
        }

        var componentes = new List<ProductoAwComponenteDato>();
        foreach (var c in f.Componentes ?? [])
        {
            var cref = Limpiar(c.Ref);
            if (cref is null) return new(null, $"componente sin ref (referencia {referencia}, orden {c.Orden}).");
            componentes.Add(new(c.Orden, c.Nivel, c.PadreOrden, cref, Limpiar(c.Descripcion), Limpiar(c.Tipo), c.EspesorMm));
        }

        var tipo = Limpiar(f.Tipo);
        ReglaFiscalProductoAw? regla = null;
        if (tipo is not null) reglas?.TryGetValue(tipo, out regla);

        return new(new AplicarProductoAwSnapshot(
            referencia, descripcion, NormalizarUnidad(f.UnidadMedida), f.Baja, variantes, leidoEnUtc,
            AwProductosOptions.VersionContrato, AwProductosOptions.VersionMapeo,
            TransaccionOrigenUtc: AUtc(f.TransactionTime),
            CodigoModelo: Limpiar(f.CodigoModelo), Grupo: Limpiar(f.Grupo), Tipo: Limpiar(f.Tipo), Componentes: componentes,
            Wgr: Limpiar(f.Wgr), WgrDescripcion: Limpiar(f.WgrDescripcion),
            ClaveProdServSat: regla?.ClaveProdServSat, ClaveUnidadSatSugerida: regla?.ClaveUnidadSat, ObjetoImp: regla?.ObjetoImp,
            TasaIvaTraslado: regla?.TasaIvaTraslado, FraccionArancelaria: regla?.FraccionArancelaria, UnidadAduana: regla?.UnidadAduana), null);
    }

    /// <summary>
    /// UPPER(REPLACE(unidad,'²','2'), '³'→'3' y alias de A+W a códigos del catálogo <c>UnidadMedida</c>:
    /// <c>&lt;INDF&gt;</c> (indefinido) = null, <c>M LIN.</c> = M, <c>LTR</c> = L. Vacío = null (sin equivalencia).
    /// </summary>
    public static string? NormalizarUnidad(string? u) => Limpiar(u)?.Replace('²', '2').Replace('³', '3').ToUpperInvariant() switch
    {
        "<INDF>" => null,
        "M LIN." => "M",
        "LTR" => "L",
        var x => x,
    };

    // Npgsql rechaza DateTime Kind=Unspecified en timestamptz; el origen entrega la hora ya en UTC sin zona.
    private static DateTime? AUtc(DateTime? t) =>
        t is { Kind: DateTimeKind.Unspecified } u ? DateTime.SpecifyKind(u, DateTimeKind.Utc) : t?.ToUniversalTime();

    private static string? Limpiar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
