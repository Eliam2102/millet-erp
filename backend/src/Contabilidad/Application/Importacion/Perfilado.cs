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
            reabre.Add(new("Códigos fuera de longitud/patrón", "Revisar con Contabilidad el formato de esos códigos; si el formato es correcto, el administrador del sistema ajusta la configuración."));
        if (N("CONTAB_IMPORT_NATURALEZA_DESCONOCIDA") > 0)
            reabre.Add(new("Naturaleza no reconocida", "Corregir la naturaleza en el archivo (Deudora o Acreedora) o confirmar el valor con Contabilidad."));
        if (N("CONTAB_IMPORT_TIPO_DESCONOCIDO") > 0)
            reabre.Add(new("Tipo (acumula/afectable) no reconocido", "Corregir el tipo en el archivo o quitar la columna: el sistema lo calcula por la jerarquía."));
        if (r.Huerfanas > 0)
            reabre.Add(new("Cuentas huérfanas", "Agregar al archivo las cuentas padre que faltan o revisar sus códigos."));
        if (r.ColumnasSinMapeo.Count > 0)
            reabre.Add(new("Columnas que no se cargan (p. ej. Tipo)", "La clasificación «Tipo» solo se usa para reconocer títulos de reporte y rubros; el resto de sus valores no se carga al catálogo."));
        if (r.NivelContableDiscrepancias > 0)
            reabre.Add(new("Nivel contable distinto del derivado", "Revisar el código o el nivel en el archivo; se guarda el nivel que corresponde al código."));
        if (filas.Any(x => x.EnArbol && x.Clase == ClaseCuenta.Cuenta && x.Naturaleza is null))
            reabre.Add(new("Cuentas pendientes de validación (sin naturaleza)", "Contabilidad debe completar la naturaleza (p. ej. de las cuentas de orden); mientras tanto esas cuentas no reciben movimientos."));
        if (filas.Any(x => x.Clase == ClaseCuenta.Rubro))
            reabre.Add(new("Rubros de reporte", "Se asocian a las cuentas de nivel 1 que aparecen debajo de cada rubro en el archivo (propuesta para pruebas). Confirmar con Contabilidad qué suma cada rubro y el tratamiento de «Acumula rubro»."));
        if (N("CONTAB_IMPORT_CONTROL_CONFLICTO") > 0)
            reabre.Add(new("Conflicto con cuentas de control", "Revisar con Contabilidad cuáles son las cuentas de control."));

        var valida = filas.Where(x => x.EnArbol).ToList();
        var cuentas = valida.Where(x => x.Clase == ClaseCuenta.Cuenta).ToList();
        return new PerfilImportacion(
            Resumen: new
            {
                FilasLeidas = filas.Count + r.Vacias,
                r.Vacias,
                Invalidas = filas.Count(x => x.TieneErrores),
                r.ColumnasEncontradas, r.ColumnasFaltantes, r.ColumnasIgnoradas,
                Codificacion = new { Fallback1252 = r.CodificacionFallback, CaracteresReemplazo = r.CaracteresReemplazo },
                CodigosRellenados = filas.Count(x => x.Rellenado),
                FilasTitulo = r.FilasTitulo,
                Rubros = valida.Count(x => x.Clase == ClaseCuenta.Rubro),
                CuentasConRubro = valida.Count(x => x.RubroCodigo is not null),
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
                // P19: tipo calculado por la jerarquía (o el explícito si la derivación está apagada).
                Acumulan = cuentas.Count(x => x.Tipo == TipoCuenta.Titulo),
                Afectables = cuentas.Count(x => x.Tipo == TipoCuenta.Afectable),
                PadresAConvertir = r.PadresAConvertir.Count,
            },
            Control: new
            {
                Coincidencias = filas.Count(x => x.ControlCoincide),
                Conflictos = N("CONTAB_IMPORT_CONTROL_CONFLICTO"),
                MarcadasPorArchivo = filas.Count(x => x.Control != CuentaControl.Ninguna),
            },
            // P25: solo cuentas (no rubros) sin naturaleza; el tipo ya no queda pendiente.
            PendientesValidacion: new
            {
                SinNaturaleza = cuentas.Count(x => x.Naturaleza is null),
                Pendientes = cuentas.Count(x => x.Naturaleza is null),
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
