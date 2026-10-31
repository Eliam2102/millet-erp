#:project ../../backend/src/DatosMaestros/Millet.DatosMaestros.csproj
// Equivalencia SAP CardCode -> Cliente.ReferenciaExterna (brecha 10, 86e3x4g77). Solo lee archivos: no conecta a SAP ni al ERP.
// Entradas CSV con encabezado: sap.csv = CardCode,Nombre,Rfc[,Moneda,Saldo]; erp.csv = Id,ReferenciaExterna,RazonSocial,Rfc
//   (erp.csv sale de: SELECT id AS "Id", referencia_externa AS "ReferenciaExterna", razon_social AS "RazonSocial", rfc AS "Rfc" FROM compartido.clientes).
//   Los encabezados no distinguen mayúsculas ni guiones bajos: id,referencia_externa,razon_social,rfc también sirve.
// Salida: ligados.csv, a-revision.csv, sin-cliente.csv en <carpeta salida>; la carga de saldos (TR5-SAL) consume ligados.csv.
// Uso: dotnet run equivalencia.cs -- sap.csv erp.csv carpeta-salida
using System.Globalization;
using Microsoft.VisualBasic.FileIO;
using Millet.DatosMaestros.Domain;

if (args.Length != 3) { Console.Error.WriteLine("uso: equivalencia.cs <sap.csv> <erp.csv> <carpeta-salida>"); return 2; }

static List<Dictionary<string, string>> Leer(string ruta)
{
    using var p = new TextFieldParser(ruta, System.Text.Encoding.UTF8) { HasFieldsEnclosedInQuotes = true };
    p.SetDelimiters(",");
    var h = p.ReadFields() ?? throw new InvalidDataException($"{ruta} sin encabezado");
    var filas = new List<Dictionary<string, string>>();
    while (!p.EndOfData) { var v = p.ReadFields()!; filas.Add(h.Select((n, i) => (n, v: i < v.Length ? v[i] : "")).ToDictionary(x => x.n.Replace("_", ""), x => x.v, StringComparer.OrdinalIgnoreCase)); }
    return filas;
}
static string Q(string? s) => s is null ? "" : s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
static string V(Dictionary<string, string> f, string k) => f.GetValueOrDefault(k) ?? "";

var sap = Leer(args[0]).Select(f => new ClienteSap(V(f, "CardCode"), V(f, "Nombre"), V(f, "Rfc"),
    V(f, "Moneda") is { Length: > 0 } m ? m : null,
    decimal.TryParse(V(f, "Saldo"), NumberStyles.Number, CultureInfo.InvariantCulture, out var s) ? s : null)).ToList();
var erp = Leer(args[1]).Select(f => new ClienteErpRef(Guid.Parse(V(f, "Id")), V(f, "ReferenciaExterna"), V(f, "RazonSocial"), V(f, "Rfc"))).ToList();

var r = EquivalenciaClientesSap.Clasificar(sap, erp);
Directory.CreateDirectory(args[2]);
static string Sap(ClienteSap s) => string.Join(',', Q(s.CardCode), Q(s.Nombre), Q(s.Rfc), Q(s.Moneda), s.Saldo?.ToString(CultureInfo.InvariantCulture));
File.WriteAllLines(Path.Combine(args[2], "ligados.csv"), ["CardCode,Nombre,Rfc,Moneda,Saldo,ClienteId", .. r.Ligados.Select(l => $"{Sap(l.Sap)},{l.ClienteId}")]);
File.WriteAllLines(Path.Combine(args[2], "a-revision.csv"), ["CardCode,Nombre,Rfc,Moneda,Saldo,ClienteId,Motivo", .. r.ARevision.Select(l => $"{Sap(l.Sap)},{l.ClienteId},{Q(l.Motivo)}")]);
File.WriteAllLines(Path.Combine(args[2], "sin-cliente.csv"), ["CardCode,Nombre,Rfc,Moneda,Saldo", .. r.SinCliente.Select(Sap)]);
Console.WriteLine($"ligados={r.Ligados.Count} a-revision={r.ARevision.Count} sin-cliente={r.SinCliente.Count}");
return 0;
