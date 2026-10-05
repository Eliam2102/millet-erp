using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application.Dimensiones;

/// <summary>
/// Configuración de las reglas de dimensión (<c>Contabilidad:Dimensiones</c>, F1-CON-02). Mientras Contabilidad no confirme
/// su política, los valores por defecto son los del plan (D5/D6) y se cambian aquí, no en código.
/// </summary>
public sealed class DimensionesOpciones
{
    public const string Seccion = "Contabilidad:Dimensiones";

    /// <summary>D5: requerimiento cuando ninguna regla vigente aplica. Por defecto opcional (no bloquea).</summary>
    public RequerimientoDimension SinReglaEs { get; set; } = RequerimientoDimension.Opcional;

    /// <summary>K10.2/V49: la ubicación (Dim1) del centro debe estar ligada a la sucursal del movimiento, salvo centros corporativos.</summary>
    public bool ExigirSucursalDelCentro { get; set; } = true;

    /// <summary>D4: permite crear o cerrar reglas con fechas anteriores a hoy (carga inicial de reglas reales).</summary>
    public bool PermitirVigenciaRetroactiva { get; set; }

    /// <summary>Zona horaria que define "hoy" para las vigencias (IANA).</summary>
    public string ZonaHoraria { get; set; } = "America/Merida";

    /// <summary>Nombre visible de cada dimensión en mensajes (mismos rótulos que el catálogo de Centros de Costo).</summary>
    public Dictionary<string, string> NombresDimension { get; set; } = new();

    public void AplicarDefaults()
    {
        NombresDimension.TryAdd(nameof(DimensionContable.Dim1), "Dimensión 1");
        NombresDimension.TryAdd(nameof(DimensionContable.Dim2), "Dimensión 2");
        NombresDimension.TryAdd(nameof(DimensionContable.Dim3), "Dimensión 3");
        NombresDimension.TryAdd(nameof(DimensionContable.Proyecto), "Proyecto");
        NombresDimension.TryAdd(nameof(DimensionContable.Cliente), "Cliente");
        NombresDimension.TryAdd(nameof(DimensionContable.Proveedor), "Proveedor");
        NombresDimension.TryAdd(nameof(DimensionContable.Banco), "Banco (cuenta bancaria)");
    }

    public IEnumerable<string> Validar()
    {
        if (!Enum.IsDefined(SinReglaEs)) yield return "SinReglaEs debe ser Obligatorio, Opcional o NoAplica.";
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(ZonaHoraria, out _)) yield return $"ZonaHoraria '{ZonaHoraria}' no es una zona IANA reconocida.";
    }

    public string Nombre(DimensionContable d) => NombresDimension.GetValueOrDefault(d.ToString(), d.ToString());

    public DateOnly Hoy(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById(ZonaHoraria)).DateTime);
}
