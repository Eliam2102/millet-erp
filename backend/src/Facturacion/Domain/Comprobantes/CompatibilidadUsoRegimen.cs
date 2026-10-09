using System.Collections.Frozen;

namespace Millet.Facturacion.Domain.Comprobantes;

/// <summary>c_UsoCFDI: columna Régimen Fiscal Receptor (U1.9, D1).</summary>
public static class CompatibilidadUsoRegimen
{
    private static readonly FrozenSet<string> Generales = new[]
        { "601", "603", "606", "612", "620", "621", "622", "623", "624", "625", "626" }.ToFrozenSet();
    private static readonly FrozenSet<string> Deducciones = new[]
        { "605", "606", "607", "608", "611", "612", "614", "615", "625" }.ToFrozenSet();
    private static readonly FrozenSet<string> SinEfectosYPagos = new[]
        { "601", "603", "605", "606", "607", "608", "610", "611", "612", "614", "615", "616", "620", "621", "622", "623", "624", "625", "626" }.ToFrozenSet();

    public static bool EsCompatible(string uso, string regimen) => uso switch
    {
        "G01" or "G02" or "G03" or "I01" or "I02" or "I03" or "I04" or "I05" or "I06" or "I07" or "I08" => Generales.Contains(regimen),
        "D01" or "D02" or "D03" or "D04" or "D05" or "D06" or "D07" or "D08" or "D09" or "D10" => Deducciones.Contains(regimen),
        "S01" or "CP01" => SinEfectosYPagos.Contains(regimen),
        "CN01" => regimen == "605",
        _ => false,
    };
}
