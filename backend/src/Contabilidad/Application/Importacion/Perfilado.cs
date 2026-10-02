using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Application.Importacion;

public sealed record EjemploError(int Fila, string? Columna);
public sealed record ErrorAgrupado(string Codigo, string Severidad, int Conteo, IReadOnlyList<EjemploError> Ejemplos);
public sealed record QueSeReabre(string Hallazgo, string Decision);
public sealed record ColumnaSinMapeoPerfil(string Columna, int FilasConValor, int ValoresDistintos);

/// <summary>
/// Reporte de perfilado (§20.3, §20.10). Solo conteos, números de fila y códigos de error:
/// no vuelca nombres ni códigos de cuenta (§20.9). Lo único textual de celdas son los valores de
/// naturaleza/tipo no reconocidos (necesarios para declarar el alias), con tope.
/// </summary>
public sealed record PerfilImportacion(
    object Resumen, object Distribuciones, object Estructura, object Control, object PendientesValidacion,
    object NivelContable, IReadOnlyList<ColumnaSinMapeoPerfil> ColumnasSinMapeo,
    IReadOnlyList<ErrorAgrupado> PorCodigoError, IReadOnlyList<QueSeReabre> QueSeReabre);

public sealed class Perfilador(FormatoCatalogo f)
{
    private const int MaxEjemplos = 5;

    public PerfilImportacion Perfilar(ResultadoAnalisis r)
    {
        var filas = r.Filas;
        var porError = r.Hallazgos.Where(e => e.Fila > 0).GroupBy(e => (e.Codigo, e.Severidad))
            .Select(g => new ErrorAgrupado(g.Key.Codigo, g.Key.Severidad, g.Count(),
                [.. g.Take(MaxEjemplos).Select(e => new EjemploError(e.Fila, e.Columna))]))
            .OrderBy(g => g.Severidad).ThenByDescending(g => g.Conteo).ToList();
        int N(string codigo) => porError.Where(e => e.Codigo == codigo).Sum(e => e.Conteo);

        Dictionary<string, int> Histograma(IEnumerable<int> valores) =>
            valores.GroupBy(v => v).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => g.Count());

        var conCodigo = filas.Where(x => x.Codigo is not null).ToList();
        var seps = f.Opciones.Codigo.Separadores;

        var reabre = new List<QueSeReabre>();
        if (N("CONTAB_IMPORT_CODIGO_FORMATO") > 0)
            reabre.Add(new("Códigos fuera de longitud/patrón", "P2: formato del código (Codigo.Patron, LongitudMin/Max)"));
        if (N("CONTAB_IMPORT_NATURALEZA_DESCONOCIDA") > 0)
            reabre.Add(new("Naturaleza no reconocida", "P2/P3: aliases de naturaleza"));
        if (N("CONTAB_IMPORT_TIPO_DESCONOCIDO") > 0)
            reabre.Add(new("Tipo (título/afectable) no reconocido", "P2/P16: aliases de tipo"));
        if (r.Huerfanas > 0)
            reabre.Add(new("Cuentas huérfanas", "P1/P15: archivo incompleto o estructura de código distinta"));
        if (r.ColumnasSinMapeo.Count > 0)
            reabre.Add(new("Columnas informativas sin mapeo (p. ej. Tipo)", "P16: significado a confirmar con Contabilidad"));
        if (r.NivelContableDiscrepancias > 0)
            reabre.Add(new("Nivel contable distinto del derivado", "P15: jerarquía por segmentos"));
        if (filas.Any(x => x.EnArbol && x.Tipo is null || x.EnArbol && x.Naturaleza is null))
            reabre.Add(new("Cuentas pendientes de validación (sin naturaleza o sin tipo)", "P14: validación de Contabilidad"));
        if (N("CONTAB_IMPORT_CONTROL_CONFLICTO") > 0)
            reabre.Add(new("Conflicto con cuentas de control", "P3: lista CuentasControl"));

        var valida = filas.Where(x => x.EnArbol).ToList();
        return new PerfilImportacion(
            Resumen: new
            {
                FilasLeidas = filas.Count + r.Vacias,
                r.Vacias,
                Invalidas = filas.Count(x => x.TieneErrores),
                r.ColumnasEncontradas, r.ColumnasFaltantes, r.ColumnasIgnoradas,
                Codificacion = new { Fallback1252 = r.CodificacionFallback, CaracteresReemplazo = r.CaracteresReemplazo },
                CodigosRellenados = filas.Count(x => x.Rellenado),
                Acciones = filas.GroupBy(x => x.Accion).ToDictionary(g => g.Key, g => g.Count()),
                Huella = r.Huella,
            },
            Distribuciones: new
            {
                LongitudCodigo = Histograma(conCodigo.Select(x => x.LongitudCodigo)),
                Nivel = Histograma(valida.Where(x => x.Nivel > 0).Select(x => x.Nivel)),
                SegmentosPorCodigo = Histograma(conCodigo.Select(x => x.Segmentos)),
                Separadores = seps.ToDictionary(s => s, s => conCodigo.Sum(x => x.Codigo!.Count(c => c.ToString() == s))),
                Naturaleza = new
                {
                    Reconocidos = filas.Where(x => x.NaturalezaTxt is not null && f.ParseNaturaleza(x.NaturalezaTxt) is not null)
                        .GroupBy(x => f.ParseNaturaleza(x.NaturalezaTxt)!.Value.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                    NoReconocidos = r.NaturalezaNoReconocida.Count,
                    ValoresNoReconocidos = r.NaturalezaNoReconocida.Distinct().Take(10).ToList(),
                },
                Tipo = new
                {
                    Reconocidos = filas.Where(x => x.TipoTxt is not null && f.ParseTipo(x.TipoTxt) is not null)
                        .GroupBy(x => f.ParseTipo(x.TipoTxt)!.Value.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                    NoReconocidos = r.TipoNoReconocido.Count,
                    ValoresNoReconocidos = r.TipoNoReconocido.Distinct().Take(10).ToList(),
                },
            },
            Estructura: new
            {
                r.Huerfanas, r.Ciclos, r.DuplicadosCodigo, r.DuplicadosOrigen, r.TitulosSinHijos, r.AfectablesConHijos,
                r.ProfundidadMaxima, f.Opciones.NivelMaximo,
                HojasSinTipo = valida.Count(x => x.Tipo is null),
            },
            Control: new
            {
                Coincidencias = filas.Count(x => x.ControlCoincide),
                Conflictos = N("CONTAB_IMPORT_CONTROL_CONFLICTO"),
                MarcadasPorArchivo = filas.Count(x => x.Control != CuentaControl.Ninguna),
            },
            PendientesValidacion: new
            {
                SinNaturaleza = valida.Count(x => x.Naturaleza is null),
                SinTipo = valida.Count(x => x.Tipo is null),
                Pendientes = valida.Count(x => x.Naturaleza is null || x.Tipo is null),
            },
            NivelContable: new
            {
                Comparadas = r.NivelContableComparadas,
                Discrepancias = r.NivelContableDiscrepancias,
                Ejemplos = filas.Where(x => x.Errores.Any(e => e.Codigo == "CONTAB_IMPORT_NIVEL_DISCREPANTE"))
                    .Take(MaxEjemplos).Select(x => x.Fila).ToList(),
            },
            ColumnasSinMapeo: [.. r.ColumnasSinMapeo.Select(c => new ColumnaSinMapeoPerfil(c.Columna, c.FilasConValor, c.ValoresDistintos))],
            PorCodigoError: porError,
            QueSeReabre: reabre);
    }
}
