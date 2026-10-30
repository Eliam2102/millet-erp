using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Application;

public sealed record ColumnaFiscal(string Clave, string Etiqueta, string Tipo, int? Decimales = null);
public sealed record ReporteFiscal(string Titulo, DateTimeOffset GeneradoEn, IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ColumnaFiscal> Columnas, IReadOnlyList<FilaFiscal> Filas, IReadOnlyDictionary<string, decimal> Totales);
public sealed record CapturaInpc(int Anio, int Mes, decimal Valor);

public sealed class FiscalServicio(ActivosFijosDbContext db, ActivoServicio acceso, LibroServicio libro, IClock clock)
{
    public async Task<ReporteFiscal> ReporteAsync(int ejercicio, int mesCorte, CancellationToken ct)
    {
        await acceso.PermisoAsync("activos_fijos.activos.ver", ct);
        if (ejercicio is < 1 or > 9998 || mesCorte is < 1 or > 12)
            throw new BusinessRuleException("AF_CORTE_INVALIDO", "Capture ejercicio y mes de corte válidos.");
        var ids = await acceso.SucursalesAsync(null, ct);
        var corte = CalculoDepreciacion.Cierre(new(ejercicio, mesCorte, 1));
        var activos = await db.Activos.AsNoTracking().Include(a => a.Historial).Include(a => a.Traslados).AsSplitQuery().ToListAsync(ct);
        var clases = await db.Clases.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var indices = (await db.Set<InpcMensual>().AsNoTracking().ToListAsync(ct)).ToDictionary(x => new DateOnly(x.Anio, x.Mes, 1), x => x.Valor);
        var mismoMes = await libro.MismoMesAsync(ct);
        var todos = activos.ToDictionary(a => a.Id);
        var filas = activos.Where(a => a.Estado != EstadoActivo.PorCapitalizar && a.InicioUso <= corte && (a.CorteCarga == null || a.CorteCarga <= corte)
            && (ids == null || ids.Contains(CalculoDepreciacion.CustodiaAFecha(a, corte).SucursalId)))
            .OrderBy(a => clases[a.ClaseId].Clave).ThenBy(a => a.Numero)
            .Select(a => CalculoFiscal.Calcular(a, clases[a.ClaseId], ejercicio, mesCorte, mismoMes, indices,
                a.ActivoMejoradoId is Guid id && todos.TryGetValue(id, out var mejorado) ? clases[mejorado.ClaseId] : null)).ToList();
        var totales = new Dictionary<string, decimal>();
        void Totalizar(string prefijo, IEnumerable<FilaFiscal> grupo)
        {
            var lista = grupo.ToList();
            totales[prefijo + "montoOriginalInversion"] = lista.Sum(f => f.MontoOriginalInversion);
            totales[prefijo + "deduccionHistorica"] = lista.Sum(f => f.DeduccionHistorica);
            totales[prefijo + "deduccionAcumuladaHistorica"] = lista.Sum(f => f.DeduccionAcumuladaHistorica);
            if (lista.All(f => f.DeduccionActualizada != null)) totales[prefijo + "deduccionActualizada"] = lista.Sum(f => f.DeduccionActualizada ?? 0);
        }
        Totalizar("", filas);
        foreach (var grupo in filas.GroupBy(f => f.Clase)) Totalizar(grupo.Key + ".", grupo);
        return new("Depreciación fiscal", clock.UtcNow, new Dictionary<string,string?> { ["Ejercicio"] = ejercicio.ToString(CultureInfo.InvariantCulture), ["Mes de corte"] = mesCorte.ToString(CultureInfo.InvariantCulture) },
            [new("numero", "Activo", "texto"), new("descripcion", "Descripción", "texto"), new("clase", "Clase", "texto"),
             new("montoOriginalInversion", "Monto original de inversión", "moneda"), new("tasaFiscal", "Tasa fiscal %", "numero", 4),
             new("mesesUso", "Meses completos", "numero"), new("deduccionHistorica", "Deducción histórica del ejercicio", "moneda"),
             new("factor", "Factor INPC", "numero", 4), new("deduccionActualizada", "Deducción actualizada", "moneda"),
             new("deduccionAcumuladaHistorica", "Acumulada histórica", "moneda"), new("observacion", "Observación", "texto")], filas, totales);
    }
    public async Task<IReadOnlyList<InpcMensual>> ListarInpcAsync(CancellationToken ct)
    {
        await acceso.PermisoAsync("activos_fijos.clases.administrar", ct);
        return await db.Set<InpcMensual>().AsNoTracking().OrderByDescending(x => x.Anio).ThenByDescending(x => x.Mes).ToListAsync(ct);
    }
    public async Task GuardarInpcAsync(IReadOnlyList<CapturaInpc> filas, CancellationToken ct)
    {
        await acceso.PermisoAsync("activos_fijos.clases.administrar", ct);
        if (filas.Count is < 1 or > 1200) throw new BusinessRuleException("AF_INPC_INVALIDO", "Capture entre 1 y 1200 meses.");
        var nuevas = filas.Select(f => new InpcMensual(libro.Empresa, f.Anio, f.Mes, f.Valor)).ToList();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await libro.BloquearAsync(ct);
        var existentes = await db.Set<InpcMensual>().ToListAsync(ct);
        if (nuevas.GroupBy(f => new { f.Anio, f.Mes }).Any(g => g.Count() > 1)
            || nuevas.Any(f => existentes.Any(e => e.Anio == f.Anio && e.Mes == f.Mes)))
            throw new BusinessRuleException("AF_INPC_DUPLICADO", "Ya existe un INPC para ese mes. La carga no reemplaza valores existentes.");
        db.AddRange(nuevas); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public static IReadOnlyList<CapturaInpc> LeerExcel(byte[] contenido)
    {
        if (contenido.Length is 0 or > ArchivoLibro.LimiteBytes) throw ArchivoInvalido();
        try
        {
            using var stream = new MemoryStream(contenido);
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
                if (zip.Entries.Count > 2000 || zip.Entries.Sum(e => e.Length) > 50 * 1024 * 1024) throw ArchivoInvalido();
            stream.Position = 0;
            using var excel = new XLWorkbook(stream);
            var hoja = excel.Worksheets.First();
            var rango = hoja.RangeUsed();
            if (rango == null || rango.ColumnCount() != 2 || rango.RowCount() is < 2 or > 1201) throw ArchivoInvalido();
            var filas = new List<CapturaInpc>();
            foreach (var fila in rango.Rows().Skip(1))
            {
                var mes = fila.Cell(1); var valor = fila.Cell(2);
                if (mes.HasFormula || valor.HasFormula || !DateOnly.TryParseExact(mes.GetString() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)) throw ArchivoInvalido();
                var numero = valor.DataType == XLDataType.Number ? Convert.ToDecimal(valor.GetDouble(), CultureInfo.InvariantCulture) : ArchivoLibro.Monto(valor.GetString());
                filas.Add(new(fecha.Year, fecha.Month, numero));
            }
            return filas;
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IOException or FormatException or System.Xml.XmlException or InvalidOperationException or OverflowException)
        { throw ArchivoInvalido(); }
    }
    private static BusinessRuleException ArchivoInvalido() => new("AF_INPC_ARCHIVO_INVALIDO", "Use un Excel de hasta 10 MB con encabezados y dos columnas: AAAA-MM (texto), valor. Sin fórmulas; máximo 1200 meses.");
}
