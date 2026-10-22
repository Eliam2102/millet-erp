using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Application;
public sealed record TotalSap(string Clase, decimal Valor, decimal Acumulada);
public sealed record SolicitudCarga(string Csv, string VersionArchivo, DateOnly Corte, IReadOnlyList<TotalSap> Totales);
public sealed record ErrorCarga(int Renglon, string Codigo, string Mensaje);
public sealed record CuadreCarga(string Clase, decimal Valor, decimal Acumulada, decimal Neto, decimal? ValorSap, decimal? AcumuladaSap,
    decimal? DiferenciaValor, decimal? DiferenciaAcumulada);
public sealed record ResultadoCarga(bool Valida, IReadOnlyList<ErrorCarga> Errores, IReadOnlyList<CuadreCarga> Cuadre, Guid? CargaId = null, bool Idempotente = false);
public sealed class CargaLibroServicio(ActivosFijosDbContext db, ActivoServicio acceso, LibroServicio libro, ICatalogosActivoReadPort catalogos)
{
    public const string Plantilla = "numero,clase,descripcion,fechaAdquisicion,inicioUso,valorOriginal,depreciacionAcumulada,responsableId,sucursalId,ubicacion,centroCostoId,equipoId,importado,pedimento,arrendado\n";
    public async Task<ResultadoCarga> EjecutarAsync(SolicitudCarga request, bool confirmar, CancellationToken ct)
    {
        await acceso.PermisoAsync("activos_fijos.carga.ejecutar", ct);
        if (string.IsNullOrWhiteSpace(request.VersionArchivo) || request.Corte == default || Encoding.UTF8.GetByteCount(request.Csv) > 5_000_000)
            throw new BusinessRuleException("AF_ARCHIVO_INVALIDO", "Capture versión y corte; límite del CSV: 5 MB.");
        var huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Csv)));
        await using var tx = confirmar ? await db.Database.BeginTransactionAsync(ct) : null;
        if (confirmar) await libro.BloquearAsync(ct);
        var anterior = await db.Set<CargaLibro>().SingleOrDefaultAsync(x => x.Huella == huella && x.VersionArchivo == request.VersionArchivo, ct);
        if (anterior != null)
        {
            if (anterior.Corte != request.Corte) throw new BusinessRuleException("AF_CARGA_CORTE_DISTINTO", "El archivo y versión ya se cargaron con otro corte.");
            // No se exponen totales de otras sucursales a quien no puede consultar todo el lote.
            foreach (var sucursal in await db.Activos.Where(x => x.DocumentoSoporte == $"SAP:{anterior.Id}").Select(x => x.SucursalId).Distinct().ToListAsync(ct))
                await acceso.SucursalAsync(sucursal, true, ct);
            return new(true, [], JsonSerializer.Deserialize<List<CuadreCarga>>(anterior.CuadreJson)!, anterior.Id, true);
        }
        var carga = new CargaLibro { EmpresaId = libro.Empresa, Huella = huella, VersionArchivo = request.VersionArchivo, Corte = request.Corte };
        var errores = new List<ErrorCarga>(); var activos = new List<Activo>();
        var clases = await db.Clases.Where(x => x.Activa).ToDictionaryAsync(x => x.Clave, ct);
        var numeros = (await db.Activos.Select(x => x.Numero).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var reader = new TextFieldParser(new StringReader(request.Csv)); reader.SetDelimiters(","); reader.HasFieldsEnclosedInQuotes = true;
        string[]? encabezados;
        try { encabezados = reader.ReadFields(); }
        catch (MalformedLineException) { return new(false, [new(1, "AF_COLUMNAS_INVALIDAS", "El encabezado CSV está mal formado.")], []); }
        if (encabezados == null || !encabezados.SequenceEqual(Plantilla.Trim().Split(','))) return new(false, [new(1, "AF_COLUMNAS_INVALIDAS", "Use exactamente la plantilla descargable.")], []);
        var renglon = 1;
        while (!reader.EndOfData && errores.Count < 500)
        {
            renglon++;
            try
            {
                var f = reader.ReadFields()!;
                if (f.Length != 15) throw new FormatException("Se requieren 15 columnas.");
                if (!clases.TryGetValue(f[1], out var clase)) throw new BusinessRuleException("AF_CLASE_NO_EXISTE", "La clase no existe o está inactiva.");
                if (!numeros.Add(f[0])) throw new BusinessRuleException("AF_NUMERO_DUPLICADO", "Número repetido en el archivo o libro.");
                DateOnly Fecha(string v) => DateOnly.ParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                decimal Monto(string v) => decimal.Parse(v, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                bool SiNo(string v) => v.ToLowerInvariant() switch { "si" => true, "no" => false, _ => throw new FormatException("Arrendado debe ser si o no.") };
                var importacion = f[12].ToLowerInvariant() switch { "no" => TipoImportacionActivo.No, "temporal" => TipoImportacionActivo.Temporal, "definitivo" => TipoImportacionActivo.Definitivo, _ => throw new FormatException("Importado debe ser no, temporal o definitivo.") };
                var custodia = new CustodiaActivo(Guid.Parse(f[7]), Guid.Parse(f[8]), f[9], Guid.Parse(f[10]), string.IsNullOrWhiteSpace(f[11]) ? null : Guid.Parse(f[11]));
                await acceso.SucursalAsync(custodia.SucursalId, true, ct);
                await catalogos.ValidarCustodiaAsync(custodia, true, ct);
                var a = new Activo(Guid.CreateVersion7(), libro.Empresa, f[0], new(f[2], clase.Id, null, null, null, Fecha(f[3]), Fecha(f[4]), Monto(f[5]), "MXN", 1, custodia, importacion, f[13], SiNo(f[14]), $"SAP:{carga.Id}"));
                a.CargarLibro(request.Corte, Monto(f[6])); activos.Add(a);
            }
            catch (BusinessRuleException e) { errores.Add(new(renglon, e.Code, e.Message)); }
            catch (Exception e) when (e is FormatException or OverflowException or MalformedLineException) { errores.Add(new(renglon, "AF_FORMATO_INVALIDO", e.Message)); }
        }
        if (activos.Count == 0 && errores.Count == 0) errores.Add(new(2, "AF_ARCHIVO_VACIO", "Agregue al menos un activo."));
        if (request.Totales.GroupBy(x => x.Clase).Any(x => x.Count() > 1)) errores.Add(new(0, "AF_TOTALES_DUPLICADOS", "Capture un total por clase."));
        foreach (var total in request.Totales)
            if (!clases.ContainsKey(total.Clase) || total.Valor < 0 || total.Acumulada < 0 || total.Acumulada > total.Valor)
                errores.Add(new(0, "AF_TOTAL_INVALIDO", $"Revise clase y totales de balanza: {total.Clase}."));
        var cuadre = clases.Values.Where(c => activos.Any(a => a.ClaseId == c.Id) || request.Totales.Any(x => x.Clase == c.Clave)).Select(c =>
        {
            var valor = activos.Where(a => a.ClaseId == c.Id).Sum(a => a.ValorActual);
            var acumulada = activos.Where(a => a.ClaseId == c.Id).Sum(a => a.DepreciacionAcumulada);
            var sap = request.Totales.FirstOrDefault(x => x.Clase == c.Clave);
            return new CuadreCarga(c.Clave, valor, acumulada, valor - acumulada, sap?.Valor, sap?.Acumulada, sap == null ? null : valor - sap.Valor, sap == null ? null : acumulada - sap.Acumulada);
        }).ToList();
        if (confirmar && errores.Count == 0)
        {
            await libro.FechaModificableAsync(request.Corte, ct);
            carga.CuadreJson = JsonSerializer.Serialize(cuadre); db.Add(carga); db.Activos.AddRange(activos);
            await db.SaveChangesAsync(ct); await tx!.CommitAsync(ct);
        }
        return new(errores.Count == 0, errores, cuadre, confirmar && errores.Count == 0 ? carga.Id : null);
    }
}
