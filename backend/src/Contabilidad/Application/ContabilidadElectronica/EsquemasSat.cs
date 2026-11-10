using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Millet.Contabilidad.Application.ContabilidadElectronica;

/// <summary>
/// K10.8: esquemas oficiales del SAT de contabilidad electrónica 1.3 (anexo 24), embebidos en el ensamblado y descargados de
/// <c>http://www.sat.gob.mx/esquemas/ContabilidadE/1_3/</c>. Se cargan sin resolver URLs (el import del catálogo de tipos ya está en el
/// conjunto), así la validación no depende de la red.
/// </summary>
public static class EsquemasSat
{
    public const string Version = "1.3";
    private const string Base = "http://www.sat.gob.mx/esquemas/ContabilidadE/1_3/";
    public const string NsCatalogo = Base + "CatalogoCuentas";
    public const string NsBalanza = Base + "BalanzaComprobacion";
    public const string NsPolizas = Base + "PolizasPeriodo";
    public const string NsTipos = Base + "CatalogosParaEsqContE";

    public const string UbicacionCatalogo = NsCatalogo + " " + NsCatalogo + "/CatalogoCuentas_1_3.xsd";
    public const string UbicacionBalanza = NsBalanza + " " + NsBalanza + "/BalanzaComprobacion_1_3.xsd";
    public const string UbicacionPolizas = NsPolizas + " " + NsPolizas + "/PolizasPeriodo_1_3.xsd";

    private static readonly Lazy<XmlSchemaSet> Conjunto = new(Cargar);
    private static readonly Lazy<HashSet<string>> CodigosAgrupador = new(LeerCodigosAgrupador);

    /// <summary>Códigos agrupadores válidos (<c>c_CodAgrup</c> del catálogo del SAT).</summary>
    public static bool EsCodigoAgrupadorValido(string? codigo) =>
        !string.IsNullOrWhiteSpace(codigo) && CodigosAgrupador.Value.Contains(codigo.Trim());

    /// <summary>Valida el documento contra los esquemas del SAT; devuelve los errores (vacío = válido).</summary>
    public static IReadOnlyList<string> Validar(XDocument documento)
    {
        var errores = new List<string>();
        documento.Validate(Conjunto.Value, (_, e) =>
        {
            if (e.Severity == XmlSeverityType.Error) errores.Add(e.Message);
        });
        return errores;
    }

    private static XmlSchemaSet Cargar()
    {
        var set = new XmlSchemaSet { XmlResolver = null };
        foreach (var archivo in new[] { "CatalogosParaEsqContE.xsd", "CatalogoCuentas_1_3.xsd", "BalanzaComprobacion_1_3.xsd", "PolizasPeriodo_1_3.xsd" })
        {
            using var reader = XmlReader.Create(Recurso(archivo), new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit });
            set.Add(XmlSchema.Read(reader, null)!);
        }
        set.Compile();
        return set;
    }

    private static HashSet<string> LeerCodigosAgrupador()
    {
        using var stream = Recurso("CatalogosParaEsqContE.xsd");
        var xsd = XDocument.Load(stream);
        XNamespace xs = "http://www.w3.org/2001/XMLSchema";
        var tipo = xsd.Root!.Elements(xs + "simpleType").Single(e => (string?)e.Attribute("name") == "c_CodAgrup");
        return tipo.Descendants(xs + "enumeration").Select(e => (string)e.Attribute("value")!).ToHashSet(StringComparer.Ordinal);
    }

    private static Stream Recurso(string archivo)
    {
        var asm = typeof(EsquemasSat).Assembly;
        var nombre = asm.GetManifestResourceNames().Single(n => n.EndsWith("." + archivo, StringComparison.Ordinal));
        return asm.GetManifestResourceStream(nombre)!;
    }
}
