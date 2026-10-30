namespace Millet.ActivosFijos.Domain;

// Fuente: CATALOGO_MILLET.xlsx, Plan de cuentas (5). Los títulos .00 no reciben movimientos.
public sealed record ClaseCatalogoMillet(string Clave, string Nombre, string Activo, string? Depreciacion, string? Gasto, decimal? Tasa, bool Depreciable)
{
    // Millet, 26-oct-2026: cómputo 33 % en contable y fiscal; contable = fiscal en todas las clases.
    public decimal? TasaContable => Tasa;
}
public static class CatalogoClasesMillet
{
    public static readonly IReadOnlyList<ClaseCatalogoMillet> Clases = Array.AsReadOnly(new ClaseCatalogoMillet[]
    {
        new("151", "TERRENOS", "151.01.00.000.000", null, null, 0m, false),
        new("152", "EDIFICIOS", "152.01.00.000.000", "171.01.00.000", "613.01.00.000.000", 5m, true),
        new("153", "MAQUINARIA Y EQUIPO", "153.01.00.000.000", "171.02.00.000", "613.02.00.000.000", 10m, true),
        new("154", "AUTOMOVILES AUTOBUSES CAM DE CARGA MONTAC Y REMOLQUES", "154.01.00.000.000", "171.03.00.000", "613.03.00.000.000", 25m, true),
        new("155", "MOBILIARIO Y EQUIPO DE OFICINA", "155.01.00.000.000", "171.04.00.000", "613.04.00.000.000", 10m, true),
        new("156", "EQUIPO DE COMPUTO", "156.01.00.000.000", "171.05.00.000", "613.05.00.000.000", 33m, true),
        new("157", "EQUIPO DE COMUNICACION", "157.01.00.000.000", "171.06.00.000", "613.06.00.000.000", 10m, true),
        new("159", "OBRAS EN PROCESO DE ACTIVOS FIJOS", "159.01.00.000.000", null, null, null, false),
        new("160", "OTROS ACTIVOS FIJOS", "160.01.00.000.000", "171.08.00.000", "613.08.00.000.000", null, true),
        new("165", "EQUIPO DE COMUNICACIONES TELEFONICAS", "165.01.00.000.000", "171.13.00.000", null, 10m, true),
        new("166", "EQUIPO DE COMUNICACION SATELITAL", "166.01.00.000.000", "171.14.00.000", null, 10m, true),
        new("169", "OTRA MAQUINARIA Y EQUIPO", "169.01.00.000.000", "171.18.00.000", "613.18.00.000.000", 10m, true),
        new("170", "ADAPTACIONES Y MEJORAS", "170.01.00.000.000", "171.17.00.000", "613.17.00.000.000", null, true),
    });
}
