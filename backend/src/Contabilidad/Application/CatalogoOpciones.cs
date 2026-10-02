using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application;

/// <summary>
/// Todo lo que depende del formato de Millet (§20.1), sección <c>Contabilidad:Catalogo</c>.
/// No son secretos. Los defaults son permisivos y genéricos: NO fingen ser el formato real;
/// cambiar de formato = editar esta sección (y fixtures), nunca código.
/// Las colecciones nacen vacías y <see cref="AplicarDefaults"/> rellena lo omitido, porque el
/// binder de configuración AÑADE a las colecciones con valores por defecto en vez de reemplazarlas.
/// </summary>
public sealed class CatalogoOpciones
{
    public const string Seccion = "Contabilidad:Catalogo";
    public const string ModoPorSegmentos = "PorSegmentos";
    public const string ModoPorColumna = "PorColumna";

    public CodigoOpciones Codigo { get; set; } = new();
    public int NivelMaximo { get; set; } = 10;
    public JerarquiaOpciones Jerarquia { get; set; } = new();
    public NaturalezaOpciones Naturaleza { get; set; } = new();
    /// <summary>Activa R5 (coherencia de naturaleza hijo-padre). Apagada hasta que Contabilidad la confirme (P2).</summary>
    public bool HerenciaNaturaleza { get; set; }
    public TipoOpciones Tipo { get; set; } = new();
    public ImportacionOpciones Importacion { get; set; } = new();
    /// <summary>Cuentas de control reales (las define Contabilidad, P3). Vacía por defecto.</summary>
    public List<CuentaControlOpcion> CuentasControl { get; set; } = [];
    /// <summary>Orígenes permitidos por tipo de control (clave: Clientes|Proveedores).</summary>
    public Dictionary<string, List<string>> OrigenesControl { get; set; } = [];

    public sealed class CodigoOpciones
    {
        public string Patron { get; set; } = string.Empty;
        public int LongitudMin { get; set; } = 1;
        public int LongitudMax { get; set; } = 30;
        public List<string> Separadores { get; set; } = [];
        /// <summary>Longitud fija por segmento a la que se rellena un segmento numérico sin ceros. Vacío = apagado.</summary>
        public List<int> RellenoCeros { get; set; } = [];
    }

    public sealed class NaturalezaOpciones
    {
        public List<string> Valores { get; set; } = [];
        public Dictionary<string, List<string>> Aliases { get; set; } = [];
    }

    public sealed class TipoOpciones
    {
        public Dictionary<string, List<string>> Aliases { get; set; } = [];
    }

    public sealed class ImportacionOpciones
    {
        public int MaxFilas { get; set; } = 5000;
        public Dictionary<string, List<string>> Columnas { get; set; } = [];
        /// <summary>Cabeceras (normalizadas) conocidas que NO se mapean a ningún campo y se ignoran con advertencia (P16).</summary>
        public List<string> ColumnasSinMapeo { get; set; } = [];
    }

    /// <summary>P15: cómo se deduce el padre de una cuenta.</summary>
    public sealed class JerarquiaOpciones
    {
        /// <summary>PorSegmentos (padre = código con el último segmento numérico distinto de cero puesto a ceros) o PorColumna (solo <c>codigo_padre</c>).</summary>
        public string Modo { get; set; } = string.Empty;

        /// <summary>
        /// Alta manual: el código de una cuenta hija debe empezar con la parte significativa del código del
        /// padre y agregar exactamente un nivel (p. ej. bajo <c>100.10.00.00</c> ⇒ <c>100.10.30.00</c>).
        /// Encendida por defecto; regla a confirmar con Contabilidad. No aplica al editar (el código es inmutable).
        /// </summary>
        public bool ExigirCodigoEnRamaDelPadre { get; set; } = true;
    }

    public sealed class CuentaControlOpcion
    {
        public string Codigo { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
    }

    /// <summary>
    /// Columnas canónicas. <c>nivel_contable</c> solo se VALIDA contra el nivel derivado (P16).
    /// <c>naturaleza</c>, <c>tipo</c> y <c>cuenta_control</c> son opcionales: no se suponen (P14).
    /// </summary>
    public static readonly string[] ColumnasCanonicas =
        ["fuente", "codigo_origen", "codigo", "nombre", "codigo_padre", "naturaleza", "tipo_cuenta",
         "cuenta_control", "codigo_agrupador", "grupo_reporte", "nivel_contable"];

    public static readonly string[] ColumnasObligatorias = ["codigo", "nombre"];

    // OJO (P16): "cuenta" es el NOMBRE y "numero" el código (hoja base); "tipo" y "nivel de cuenta SAT"
    // NO son el título/afectable ni se mapean (ver ColumnasSinMapeo).
    private static readonly Dictionary<string, string[]> ColumnasDefault = new()
    {
        ["fuente"] = ["fuente", "source"],
        ["codigo_origen"] = ["codigo_origen", "origen", "source_code"],
        ["codigo"] = ["codigo", "numero", "account_code", "clave"],
        ["nombre"] = ["nombre", "cuenta", "descripcion", "name", "description"],
        ["codigo_padre"] = ["codigo_padre", "padre", "cuenta_padre", "parent", "parent_code"],
        ["naturaleza"] = ["naturaleza", "nature"],
        ["tipo_cuenta"] = ["titulo_afectable", "afectabilidad", "account_type"],
        ["cuenta_control"] = ["cuenta_control", "control"],
        ["codigo_agrupador"] = ["codigo_agrupador", "agrupador", "codigo_agrupador_sat"],
        ["grupo_reporte"] = ["grupo_reporte", "grupo"],
        ["nivel_contable"] = ["nivel_contable"],
    };

    public void AplicarDefaults()
    {
        if (string.IsNullOrEmpty(Jerarquia.Modo)) Jerarquia.Modo = ModoPorSegmentos;
        if (Importacion.ColumnasSinMapeo.Count == 0) Importacion.ColumnasSinMapeo = ["tipo", "nivel_de_cuenta_sat"];
        if (string.IsNullOrEmpty(Codigo.Patron)) Codigo.Patron = "^[A-Z0-9][A-Z0-9.-]*$";
        if (Codigo.Separadores.Count == 0) Codigo.Separadores = [".", "-"];
        if (Naturaleza.Valores.Count == 0)
            Naturaleza.Valores = [nameof(NaturalezaCuenta.Deudora), nameof(NaturalezaCuenta.Acreedora)];
        Naturaleza.Aliases.TryAdd(nameof(NaturalezaCuenta.Deudora), ["deudora", "deudor", "d"]);
        Naturaleza.Aliases.TryAdd(nameof(NaturalezaCuenta.Acreedora), ["acreedora", "acreedor", "a"]);
        Tipo.Aliases.TryAdd(nameof(TipoCuenta.Titulo), ["titulo", "t"]);
        Tipo.Aliases.TryAdd(nameof(TipoCuenta.Afectable), ["afectable", "a"]);
        foreach (var (canonica, alias) in ColumnasDefault)
            Importacion.Columnas.TryAdd(canonica, [.. alias]);
        OrigenesControl.TryAdd(nameof(CuentaControl.Clientes), [nameof(OrigenMovimiento.AuxiliarCxC)]);
        OrigenesControl.TryAdd(nameof(CuentaControl.Proveedores), [nameof(OrigenMovimiento.AuxiliarCxP)]);
    }

    /// <summary>Errores de consistencia (vacío = válida). Se usa con <c>ValidateOnStart</c>.</summary>
    public IEnumerable<string> Validar()
    {
        Regex? patron = null;
        try { patron = new Regex(Codigo.Patron, RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
        catch (ArgumentException) { }
        if (patron is null) yield return "Codigo.Patron no es una expresión regular válida.";
        if (Codigo.LongitudMin < 1 || Codigo.LongitudMax > 30 || Codigo.LongitudMin > Codigo.LongitudMax)
            yield return "Codigo.LongitudMin/LongitudMax deben cumplir 1 <= min <= max <= 30 (columna varchar(30)).";
        if (Codigo.RellenoCeros.Any(n => n is < 1 or > 30))
            yield return "Codigo.RellenoCeros solo admite longitudes entre 1 y 30.";
        if (NivelMaximo is < 1 or > 50) yield return "NivelMaximo debe estar entre 1 y 50.";
        if (Importacion.MaxFilas < 1) yield return "Importacion.MaxFilas debe ser positivo.";
        if (Jerarquia.Modo is not (ModoPorSegmentos or ModoPorColumna))
            yield return "Jerarquia.Modo debe ser PorSegmentos o PorColumna.";
        if (Naturaleza.Valores.Any(v => !Enum.TryParse<NaturalezaCuenta>(v, out _)))
            yield return "Naturaleza.Valores solo admite Deudora/Acreedora.";
        foreach (var (valor, alias) in Naturaleza.Aliases)
            if (!Naturaleza.Valores.Contains(valor) || alias.Count == 0)
                yield return $"Naturaleza.Aliases: '{valor}' no está en Naturaleza.Valores o no tiene alias.";
        foreach (var (valor, alias) in Tipo.Aliases)
            if (!Enum.TryParse<TipoCuenta>(valor, out _) || alias.Count == 0)
                yield return $"Tipo.Aliases: '{valor}' no es Titulo/Afectable o no tiene alias.";
        foreach (var (canonica, alias) in Importacion.Columnas)
            if (!ColumnasCanonicas.Contains(canonica) || alias.Count == 0)
                yield return $"Importacion.Columnas: '{canonica}' no es una columna canónica o no tiene alias.";
        foreach (var c in CuentasControl)
        {
            if (!Enum.TryParse<CuentaControl>(c.Tipo, out var t) || t == CuentaControl.Ninguna
                || string.IsNullOrWhiteSpace(c.Codigo))
                yield return "CuentasControl: cada entrada requiere codigo y tipo Clientes|Proveedores.";
            else if (patron is not null && !patron.IsMatch(c.Codigo.Trim().ToUpperInvariant()))
                yield return "CuentasControl: un código listado no cumple Codigo.Patron.";
        }
        foreach (var (tipo, origenes) in OrigenesControl)
            if (!Enum.TryParse<CuentaControl>(tipo, out _)
                || origenes.Any(o => !Enum.TryParse<OrigenMovimiento>(o, out _)))
                yield return $"OrigenesControl: '{tipo}' u origen no reconocido.";
    }

    public static CatalogoOpciones Predeterminadas()
    {
        var o = new CatalogoOpciones();
        o.AplicarDefaults();
        return o;
    }
}
