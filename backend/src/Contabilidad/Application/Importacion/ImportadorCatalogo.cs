using System.Security.Cryptography;
using System.Text;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Importacion;

public static class Accion
{
    public const string Crear = "Crear", Actualizar = "Actualizar", SinCambios = "SinCambios", Rechazar = "Rechazar";
    /// <summary>P21: fila de título de reporte sin código; no es una cuenta y no se carga.</summary>
    public const string Omitir = "Omitida";
}

/// <summary>Error/advertencia accionable por fila (§20.4). <c>Fila</c> 0 = hallazgo del archivo completo.</summary>
public sealed record ErrorFila(int Fila, string? Columna, string Codigo, string Severidad, string Mensaje, string Sugerencia);

public sealed record CuentaExistente(
    Guid Id, string Codigo, string Nombre, string? PadreCodigo, NaturalezaCuenta? Naturaleza, TipoCuenta? Tipo,
    CuentaControl Control, string? Agrupador, string? Grupo, bool Activa, bool Usada,
    ClaseCuenta Clase = ClaseCuenta.Cuenta, string? RubroCodigo = null);

/// <summary>Foto del catálogo de la empresa. <c>OrigenACodigo</c>: (fuente, código de origen) → código de cuenta.</summary>
public sealed record ExistenteCatalogo(
    IReadOnlyList<CuentaExistente> Cuentas,
    IReadOnlyDictionary<(string Fuente, string Origen), string> OrigenACodigo)
{
    public static ExistenteCatalogo Vacio { get; } = new([], new Dictionary<(string, string), string>());
}

public sealed class FilaAnalizada
{
    public int Fila { get; init; }
    public string Accion { get; set; } = Importacion.Accion.Rechazar;
    public List<ErrorFila> Errores { get; } = [];
    public string? Codigo { get; set; }
    public string Fuente { get; set; } = string.Empty;
    public string? CodigoOrigen { get; set; }
    public string? Nombre { get; set; }
    public string? PadreCodigo { get; set; }
    public string? PadreExplicito { get; set; }
    public NaturalezaCuenta? Naturaleza { get; set; }
    public TipoCuenta? Tipo { get; set; }
    public CuentaControl Control { get; set; }
    public string? Agrupador { get; set; }
    public string? Grupo { get; set; }
    public Guid? ExistenteId { get; set; }
    public bool OrigenNuevo { get; set; }
    public int Nivel { get; set; }
    // Para el perfilado (conteos; nunca se exportan valores de celdas)
    public string? NaturalezaTxt { get; set; }
    public string? TipoTxt { get; set; }
    public string? ControlTxt { get; set; }
    public string? NivelContableTxt { get; set; }
    public bool EnArbol { get; set; }
    public bool Rellenado { get; set; }
    public int LongitudCodigo { get; set; }
    public int Segmentos { get; set; }
    public bool ControlCoincide { get; set; }
    public string? ClasificacionTxt { get; set; }
    /// <summary>P21: fila sin código que la columna de clasificación marca como título de reporte.</summary>
    public bool EsTituloReporte { get; set; }
    public ClaseCuenta Clase { get; set; }
    /// <summary>P24: rubro (código) al que queda asociada una cuenta de nivel 1.</summary>
    public string? RubroCodigo { get; set; }

    public bool TieneErrores => Errores.Any(e => e.Severidad == "Error");
}

public sealed class ResultadoAnalisis
{
    public List<ErrorFila> Archivo { get; } = [];
    public List<FilaAnalizada> Filas { get; } = [];
    public string Huella { get; set; } = string.Empty;
    public int Vacias { get; set; }
    public int CaracteresReemplazo { get; set; }
    public bool CodificacionFallback { get; set; }
    public List<string> ColumnasEncontradas { get; } = [];
    public List<string> ColumnasFaltantes { get; } = [];
    public List<string> ColumnasIgnoradas { get; } = [];
    public List<(string Columna, int FilasConValor, int ValoresDistintos)> ColumnasSinMapeo { get; } = [];
    public List<string> NaturalezaNoReconocida { get; } = [];
    public List<string> TipoNoReconocido { get; } = [];
    public Dictionary<string, int> NivelesCalculados { get; } = [];
    public List<string> Ciclos { get; } = [];
    public int Huerfanas { get; set; }
    public int DuplicadosCodigo { get; set; }
    public int DuplicadosOrigen { get; set; }
    public int TitulosSinHijos { get; set; }
    public int AfectablesConHijos { get; set; }
    public int ProfundidadMaxima { get; set; }
    public int NivelContableComparadas { get; set; }
    public int NivelContableDiscrepancias { get; set; }
    public int FilasTitulo { get; set; }
    /// <summary>P20: cuentas existentes afectables que reciben hijas en este archivo y pasarán a acumular.</summary>
    public HashSet<string> PadresAConvertir { get; } = [];

    public IEnumerable<ErrorFila> Hallazgos => Archivo.Concat(Filas.SelectMany(f => f.Errores));
    public bool PuedeAplicar => Filas.Count > 0 && Filas.All(f => f.Accion != Importacion.Accion.Rechazar)
        && Archivo.All(e => e.Severidad != "Error");
}

/// <summary>
/// Núcleo del importador (§6, §20): función pura sobre (tabla cruda, catálogo existente, configuración).
/// Vista previa, perfilado y aplicar llaman a este mismo método: no hay lógica duplicada ni de
/// normalización ni de validación. No escribe en BD ni registra valores de celdas.
/// </summary>
public sealed class ImportadorCatalogo(FormatoCatalogo f)
{
    private static readonly string[] ColumnasSemanticas = ["naturaleza", "tipo_cuenta", "cuenta_control"];

    private sealed class Nodo
    {
        public required string Codigo { get; init; }
        public string? Padre { get; set; }
        public NaturalezaCuenta? Naturaleza { get; set; }
        public TipoCuenta? Tipo { get; set; }
        public bool Activa { get; set; } = true;
        public bool Rubro { get; set; }
        public FilaAnalizada? Fila { get; set; }
        public int Nivel { get; set; } = -1;
    }

    public ResultadoAnalisis Analizar(TablaCruda tabla, string? fuenteDefault, ExistenteCatalogo ex)
    {
        var o = f.Opciones;
        var res = new ResultadoAnalisis
        {
            Vacias = tabla.Vacias,
            CaracteresReemplazo = tabla.CaracteresReemplazo,
            CodificacionFallback = tabla.CodificacionFallback,
        };
        if (tabla.Filas.Count > o.Importacion.MaxFilas)
            throw new BusinessRuleException("CONTAB_IMPORT_LIMITE_FILAS",
                $"El archivo trae {tabla.Filas.Count} filas con datos; el máximo configurado es {o.Importacion.MaxFilas}. Divida el archivo en lotes.");

        // ── 1. Columnas ──────────────────────────────────────────────────────
        var idx = new Dictionary<string, int>();
        var sinMapeo = new List<(string Nombre, int Indice)>();
        var deriva = o.Tipo.DerivarPorJerarquia;
        int? idxClasif = null;
        for (var i = 0; i < tabla.Columnas.Count; i++)
        {
            var cab = tabla.Columnas[i];
            if (idxClasif is null && f.EsColumnaClasificacion(cab)) idxClasif = i;
            var canonica = f.ColumnaCanonica(cab);
            if (canonica is not null && idx.TryAdd(canonica, i)) { res.ColumnasEncontradas.Add(canonica); continue; }
            var n = FormatoCatalogo.NormalizarCabecera(cab);
            var informativa = canonica is null && f.EsColumnaSinMapeo(cab);
            if (informativa) sinMapeo.Add((n, i));
            res.ColumnasIgnoradas.Add(n.Length == 0 ? $"(columna {i + 1} sin nombre)" : n);
            res.Archivo.Add(Err(0, n, informativa ? "CONTAB_IMPORT_COLUMNA_SIN_MAPEO" : "CONTAB_IMPORT_COLUMNA_IGNORADA", "Advertencia",
                canonica is not null ? $"La columna '{n}' repite el campo '{canonica}'; se usa la primera."
                : informativa ? $"La columna '{n}' es informativa y no se importa (sin equivalencia confirmada)."
                : $"La columna '{n}' no tiene alias conocido y no se importa."));
        }
        var faltantes = CatalogoOpciones.ColumnasObligatorias.Where(c => !idx.ContainsKey(c)).ToList();
        res.ColumnasFaltantes.AddRange(CatalogoOpciones.ColumnasCanonicas.Where(c => !idx.ContainsKey(c)));
        if (faltantes.Count > 0)
            throw new BusinessRuleException("CONTAB_IMPORT_COLUMNA_FALTANTE",
                $"Falta la columna obligatoria {string.Join(", ", faltantes.Select(c => $"«{Columna(c)}»"))}. "
                + $"Encabezados encontrados en el archivo: {string.Join(", ", tabla.Columnas.Where(x => !string.IsNullOrWhiteSpace(x)))}. "
                + "Encabezados que se aceptan: "
                + string.Join("; ", faltantes.Select(c => $"{Columna(c)}: {string.Join(", ", f.AliasesColumna(c))}")) + ".");
        foreach (var c in ColumnasSemanticas.Where(c => !idx.ContainsKey(c) && !(deriva && c == "tipo_cuenta")))
            res.Archivo.Add(Err(0, c, "CONTAB_IMPORT_CAMPO_PENDIENTE", "Advertencia",
                c == "cuenta_control"
                    ? "El archivo no trae la columna «Cuenta de control»: las cuentas se cargan sin control (Ninguna)."
                    : $"El archivo no trae la columna «{Columna(c)}»: ese dato queda pendiente de validación en todas las cuentas."));
        if (o.Jerarquia.Modo == CatalogoOpciones.ModoPorColumna && !idx.ContainsKey("codigo_padre"))
            res.Archivo.Add(Err(0, "codigo_padre", "CONTAB_IMPORT_COLUMNA_FALTANTE", "Advertencia",
                "El archivo no trae la columna codigo_padre: todas las cuentas se cargarán como cuentas raíz."));
        if (tabla.CodificacionFallback || tabla.CaracteresReemplazo > 0)
            res.Archivo.Add(Err(0, null, "CONTAB_IMPORT_CODIFICACION", "Advertencia",
                tabla.CodificacionFallback
                    ? "El archivo no era UTF-8 válido; se leyó como Windows-1252."
                    : $"Se detectaron {tabla.CaracteresReemplazo} caracteres de reemplazo (texto corrupto)."));
        foreach (var (nombre, i) in sinMapeo)
        {
            var vals = tabla.Filas.Select(r => i < r.Celdas.Length ? FormatoCatalogo.Texto(r.Celdas[i]) : null)
                .Where(v => v is not null).ToList();
            res.ColumnasSinMapeo.Add((nombre, vals.Count, vals.Distinct().Count()));
        }

        // ── 2. Normalización fila por fila ───────────────────────────────────
        var huella = new StringBuilder();
        var porCodigo = new Dictionary<string, FilaAnalizada>();
        var origenesArchivo = new Dictionary<(string, string), int>();
        var controlCfg = o.CuentasControl.ToDictionary(
            c => FormatoCatalogo.Codigo(c.Codigo)!, c => Enum.Parse<CuentaControl>(c.Tipo));
        var existentes = ex.Cuentas.ToDictionary(c => c.Codigo);
        var nodos = ex.Cuentas.ToDictionary(c => c.Codigo,
            c => new Nodo { Codigo = c.Codigo, Padre = c.PadreCodigo, Naturaleza = c.Naturaleza, Tipo = c.Tipo, Activa = c.Activa, Rubro = c.Clase == ClaseCuenta.Rubro });

        foreach (var (num, celdas) in tabla.Filas)
        {
            var fila = new FilaAnalizada { Fila = num };
            res.Filas.Add(fila);
            string? C(string col) => idx.TryGetValue(col, out var i) && i < celdas.Length ? FormatoCatalogo.Texto(celdas[i]) : null;

            fila.ClasificacionTxt = idxClasif is { } ic && ic < celdas.Length ? FormatoCatalogo.Texto(celdas[ic]) : null;
            var codigo = FormatoCatalogo.Codigo(C("codigo"));
            if (codigo is null && f.EsTipoTitulo(fila.ClasificacionTxt))
            {
                // P21: título de presentación del reporte; no es una cuenta. No entra en la huella ni en el árbol.
                fila.EsTituloReporte = true;
                fila.Accion = Accion.Omitir;
                res.FilasTitulo++;
                fila.Errores.Add(Err(num, "codigo", "CONTAB_IMPORT_FILA_TITULO", "Advertencia",
                    "Fila de título de reporte (sin código): no es una cuenta y no se carga."));
                continue;
            }
            fila.Clase = f.EsTipoRubro(fila.ClasificacionTxt) ? ClaseCuenta.Rubro : ClaseCuenta.Cuenta;
            var esRubro = fila.Clase == ClaseCuenta.Rubro;
            if (codigo is null)
                fila.Errores.Add(Err(num, "codigo", "CONTAB_IMPORT_CODIGO_FORMATO", "Error", "El código está vacío."));
            else
            {
                (codigo, fila.Rellenado) = f.Rellenar(codigo);
                if (fila.Rellenado)
                    fila.Errores.Add(Err(num, "codigo", "CONTAB_IMPORT_CODIGO_RELLENADO", "Advertencia",
                        "Se completó con ceros un segmento del código que había perdido el cero inicial."));
                var motivo = f.MotivoCodigoInvalido(codigo);
                if (motivo is not null)
                    fila.Errores.Add(Err(num, "codigo", "CONTAB_IMPORT_CODIGO_FORMATO", "Error", $"Código fuera de formato: {motivo}."));
                fila.LongitudCodigo = codigo.Length;
                fila.Segmentos = f.NumeroSegmentos(codigo);
            }
            fila.Codigo = codigo;

            var nombre = C("nombre");
            if (nombre is null || nombre.Length > 254)
                fila.Errores.Add(Err(num, "nombre", "CONTAB_CUENTA_NOMBRE_INVALIDO", "Error",
                    nombre is null ? "El nombre está vacío." : "El nombre excede 254 caracteres."));
            fila.Nombre = nombre;

            fila.Fuente = (C("fuente") ?? fuenteDefault ?? "ARCHIVO").ToUpperInvariant();
            fila.CodigoOrigen = C("codigo_origen");
            if (fila.Fuente.Length > 40 || fila.CodigoOrigen?.Length > 60)
                fila.Errores.Add(Err(num, "codigo_origen", "CONTAB_IMPORT_CODIGO_FORMATO", "Error",
                    "La fuente admite 40 caracteres y el código de origen 60."));

            var padre = FormatoCatalogo.Codigo(C("codigo_padre"));
            if (padre is not null)
            {
                var (p, rel) = f.Rellenar(padre);
                padre = p;
                fila.Rellenado |= rel;
            }
            fila.PadreExplicito = padre;

            fila.NaturalezaTxt = C("naturaleza");
            fila.Naturaleza = f.ParseNaturaleza(fila.NaturalezaTxt);
            if (fila.NaturalezaTxt is null)
            {
                // P25: un rubro (agrupación de reporte) no exige naturaleza; una cuenta de orden sin naturaleza sí queda pendiente.
                if (idx.ContainsKey("naturaleza") && !esRubro)
                    fila.Errores.Add(Err(num, "naturaleza", "CONTAB_IMPORT_CAMPO_PENDIENTE", "Advertencia",
                        "Naturaleza vacía: la cuenta queda pendiente de validación."));
            }
            else if (fila.Naturaleza is null)
            {
                res.NaturalezaNoReconocida.Add(FormatoCatalogo.Clave(fila.NaturalezaTxt));
                fila.Errores.Add(Err(num, "naturaleza", "CONTAB_IMPORT_NATURALEZA_DESCONOCIDA", "Error", "Naturaleza no reconocida."));
            }

            fila.TipoTxt = C("tipo_cuenta");
            fila.Tipo = f.ParseTipo(fila.TipoTxt);
            if (fila.TipoTxt is null)
            {
                if (idx.ContainsKey("tipo_cuenta") && !deriva && !esRubro)
                    fila.Errores.Add(Err(num, "tipo_cuenta", "CONTAB_IMPORT_CAMPO_PENDIENTE", "Advertencia",
                        "Tipo (título/afectable) vacío: la cuenta queda pendiente de validación."));
            }
            else if (fila.Tipo is null)
            {
                res.TipoNoReconocido.Add(FormatoCatalogo.Clave(fila.TipoTxt));
                fila.Errores.Add(Err(num, "tipo_cuenta", "CONTAB_IMPORT_TIPO_DESCONOCIDO", "Error", "Tipo no reconocido."));
            }

            fila.ControlTxt = C("cuenta_control");
            var controlCol = FormatoCatalogo.ParseControl(fila.ControlTxt);
            if (fila.ControlTxt is not null && controlCol is null)
                fila.Errores.Add(Err(num, "cuenta_control", "CONTAB_IMPORT_CONTROL_CONFLICTO", "Error",
                    "Valor de cuenta_control no reconocido (Ninguna, Clientes o Proveedores)."));
            // Cuentas de control (P3): la lista de configuración manda cuando existe.
            if (codigo is not null && controlCfg.TryGetValue(codigo, out var cfg))
            {
                if (controlCol is not null && controlCol != cfg)
                    fila.Errores.Add(Err(num, "cuenta_control", "CONTAB_IMPORT_CONTROL_CONFLICTO", "Error",
                        "La columna cuenta_control contradice la configuración de cuentas de control."));
                else fila.ControlCoincide = true;
                fila.Control = cfg;
            }
            else if (controlCol is { } cc && controlCol != CuentaControl.Ninguna && controlCfg.Count > 0)
            {
                fila.Errores.Add(Err(num, "cuenta_control", "CONTAB_IMPORT_CONTROL_CONFLICTO", "Error",
                    "El archivo marca esta cuenta como de control, pero no está en la lista de cuentas de control del sistema."));
                fila.Control = cc;
            }
            else fila.Control = controlCol ?? CuentaControl.Ninguna;

            fila.Agrupador = C("codigo_agrupador");
            fila.Grupo = C("grupo_reporte");
            fila.NivelContableTxt = C("nivel_contable");
            if (fila.Agrupador?.Length > 30 || fila.Grupo?.Length > 60)
                fila.Errores.Add(Err(num, "codigo_agrupador", "CONTAB_CUENTA_NOMBRE_INVALIDO", "Error",
                    "El código agrupador admite 30 caracteres y el grupo de reporte 60."));

            huella.Append(string.Join('\u001f', fila.Fuente, fila.CodigoOrigen, codigo, nombre, padre,
                fila.Naturaleza?.ToString() ?? fila.NaturalezaTxt, fila.Tipo?.ToString() ?? fila.TipoTxt,
                fila.Control, fila.Agrupador, fila.Grupo, fila.NivelContableTxt, fila.Clase)).Append('\n');

            // Duplicados dentro del archivo
            if (codigo is not null && !porCodigo.TryAdd(codigo, fila))
            {
                res.DuplicadosCodigo++;
                fila.Errores.Add(Err(num, "codigo", "CONTAB_IMPORT_CODIGO_DUPLICADO_EN_ARCHIVO", "Error",
                    $"El código ya apareció en la fila {porCodigo[codigo].Fila}."));
                continue;
            }
            if (fila.CodigoOrigen is not null && !origenesArchivo.TryAdd((fila.Fuente, fila.CodigoOrigen), num))
            {
                res.DuplicadosOrigen++;
                fila.Errores.Add(Err(num, "codigo_origen", "CONTAB_IMPORT_CODIGO_DUPLICADO_EN_ARCHIVO", "Error",
                    $"La pareja fuente + código de origen ya apareció en la fila {origenesArchivo[(fila.Fuente, fila.CodigoOrigen)]}."));
                continue;
            }
            if (codigo is null || fila.Errores.Any(e => e.Severidad == "Error" && e.Columna == "codigo")) continue;

            // ── 3. Identidad contra el catálogo existente y valores efectivos ──
            CuentaExistente? actual = null;
            if (fila.CodigoOrigen is not null && ex.OrigenACodigo.TryGetValue((fila.Fuente, fila.CodigoOrigen), out var codOrigen))
            {
                if (codOrigen != codigo)
                {
                    fila.Errores.Add(Err(num, "codigo_origen", "CONTAB_IMPORT_ORIGEN_CODIGO_DISTINTO", "Error",
                        "Ese código de origen ya corresponde a una cuenta con otro código; el código no se modifica por importación."));
                    continue;
                }
                actual = existentes.GetValueOrDefault(codigo);
            }
            else actual = existentes.GetValueOrDefault(codigo);
            fila.ExistenteId = actual?.Id;
            fila.OrigenNuevo = fila.CodigoOrigen is not null && !ex.OrigenACodigo.ContainsKey((fila.Fuente, fila.CodigoOrigen));

            // No destructivo: celda vacía conserva el valor existente.
            fila.Naturaleza ??= actual?.Naturaleza;
            fila.Tipo ??= actual?.Tipo;
            if (controlCol is null && !controlCfg.ContainsKey(codigo)) fila.Control = actual?.Control ?? CuentaControl.Ninguna;
            fila.Agrupador ??= actual?.Agrupador;
            fila.Grupo ??= actual?.Grupo;
            fila.PadreCodigo = esRubro ? null : fila.PadreExplicito
                ?? (o.Jerarquia.Modo == CatalogoOpciones.ModoPorSegmentos ? f.PadrePorSegmentos(codigo)
                    : idx.ContainsKey("codigo_padre") ? null : actual?.PadreCodigo);
            if (esRubro && fila.PadreExplicito is not null)
                fila.Errores.Add(Err(num, "codigo_padre", "CONTAB_CUENTA_RUBRO_INVALIDO", "Error",
                    "Un rubro es una agrupación de reporte: no tiene cuenta padre."));
            fila.EnArbol = true;
            nodos[codigo] = new Nodo
            {
                Codigo = codigo, Padre = fila.PadreCodigo, Naturaleza = fila.Naturaleza, Tipo = fila.Tipo,
                Activa = actual?.Activa ?? true, Rubro = esRubro, Fila = fila,
            };
        }
        res.Huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(huella.ToString()))).ToLowerInvariant();

        // ── 4. Estructura: padres, ciclos, niveles ───────────────────────────
        var hijos = nodos.Values.Where(n => n.Padre is not null && nodos.ContainsKey(n.Padre))
            .GroupBy(n => n.Padre!).ToDictionary(g => g.Key, g => g.Count());
        var usoSubarbol = SubarbolesConUso(ex);

        int Nivel(Nodo n, HashSet<string> visitando)
        {
            if (n.Nivel >= 0) return n.Nivel;
            if (!visitando.Add(n.Codigo)) return 0;
            var r = n.Padre is null ? 1
                : nodos.TryGetValue(n.Padre, out var p) ? Nivel(p, visitando) is var np and > 0 ? np + 1 : 0 : 0;
            visitando.Remove(n.Codigo);
            n.Nivel = r;
            return r;
        }

        foreach (var n in nodos.Values) { n.Nivel = -1; }
        foreach (var n in nodos.Values) Nivel(n, []);
        foreach (var (codigo, n) in nodos) res.NivelesCalculados[codigo] = Math.Max(n.Nivel, 0);

        // P24: asociación de rubros por orden del archivo (propuesta del TL): cada cuenta de nivel 1 queda en el último rubro
        // visto arriba de ella. Antes del primer rubro (o con la opción apagada) se conserva el rubro que ya tenga.
        string? rubroActual = null;
        foreach (var fila in res.Filas.Where(x => x.EnArbol))
        {
            if (fila.Clase == ClaseCuenta.Rubro) { rubroActual = o.Importacion.RubroPorOrden ? fila.Codigo : null; continue; }
            if (nodos[fila.Codigo!].Nivel == 1)
                fila.RubroCodigo = rubroActual ?? existentes.GetValueOrDefault(fila.Codigo!)?.RubroCodigo;
        }

        foreach (var fila in res.Filas.Where(x => x.EnArbol))
        {
            var n = nodos[fila.Codigo!];
            var num = fila.Fila;
            if (n.Padre is not null)
            {
                if (!nodos.TryGetValue(n.Padre, out var padre))
                {
                    res.Huerfanas++;
                    fila.Errores.Add(Err(num, "codigo_padre", "CONTAB_IMPORT_PADRE_INEXISTENTE", "Error",
                        "El padre (explícito o deducido por segmentos) no existe en el archivo ni en el catálogo."));
                }
                else if (padre.Rubro)
                    fila.Errores.Add(Err(num, "codigo_padre", "CONTAB_CUENTA_PADRE_INVALIDO", "Error",
                        "El padre es un rubro de reporte: los rubros agrupan cuentas de nivel 1 pero no tienen cuentas debajo en el árbol."));
                else if (!deriva && padre.Tipo == TipoCuenta.Afectable)
                    fila.Errores.Add(Err(num, "codigo_padre", "CONTAB_CUENTA_PADRE_NO_ES_TITULO", "Error",
                        "El padre está marcado como afectable; solo un título puede tener hijas."));
                else if (deriva && padre.Fila is null && padre.Tipo == TipoCuenta.Afectable)
                {
                    // P20: una cuenta existente que hoy recibe movimientos recibe hijas: pasa a acumular, si se puede.
                    var exPadre = existentes[padre.Codigo];
                    if (exPadre.Usada)
                        fila.Errores.Add(Err(num, "codigo_padre", "CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO", "Error",
                            "La cuenta padre ya tiene movimientos: no puede pasar a acumular para recibir cuentas debajo."));
                    else if (exPadre.Control != CuentaControl.Ninguna)
                        fila.Errores.Add(Err(num, "codigo_padre", "CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE", "Error",
                            "La cuenta padre es colectiva y debe seguir recibiendo los movimientos de su módulo: no puede tener cuentas debajo."));
                    else if (res.PadresAConvertir.Add(padre.Codigo))
                        fila.Errores.Add(Err(num, "codigo_padre", "CONTAB_IMPORT_PADRE_CONVERTIDO", "Advertencia",
                            "La cuenta padre hoy recibe movimientos (sin ninguno registrado) y pasará a acumular al recibir esta cuenta debajo."));
                }
                else if (!padre.Activa && padre.Fila is null && fila.ExistenteId is null)
                    fila.Errores.Add(Err(num, "codigo_padre", "CONTAB_CUENTA_PADRE_INVALIDO", "Error", "El padre existe pero está inactivo."));
                else if (o.HerenciaNaturaleza && padre.Naturaleza is { } pn && n.Naturaleza is { } hn && pn != hn)
                    fila.Errores.Add(Err(num, "naturaleza", "CONTAB_CUENTA_NATURALEZA_INVALIDA", "Error",
                        "La naturaleza no coincide con la de la cuenta padre."));
            }
            var ciclo = Ciclo(n, nodos);
            if (ciclo is not null)
            {
                var cadena = string.Join(" → ", ciclo.Select(c => nodos[c].Fila?.Fila.ToString() ?? "(existente)"));
                if (fila.Fila == ciclo.Min(c => nodos[c].Fila?.Fila ?? int.MaxValue)) res.Ciclos.Add($"filas {cadena}");
                fila.Errores.Add(Err(num, "codigo_padre", "CONTAB_IMPORT_CICLO", "Error", $"Jerarquía cíclica: filas {cadena}."));
            }
            else if (n.Nivel > 0)
            {
                res.ProfundidadMaxima = Math.Max(res.ProfundidadMaxima, n.Nivel);
                fila.Nivel = n.Nivel;
                if (n.Nivel > o.NivelMaximo)
                    fila.Errores.Add(Err(num, "codigo", "CONTAB_CUENTA_NIVEL_EXCEDIDO", "Error",
                        $"El nivel {n.Nivel} excede el máximo configurado ({o.NivelMaximo})."));
                if (fila.NivelContableTxt is not null)
                {
                    res.NivelContableComparadas++;
                    if (!int.TryParse(fila.NivelContableTxt, out var nc) || nc != n.Nivel)
                    {
                        res.NivelContableDiscrepancias++;
                        fila.Errores.Add(Err(num, "nivel_contable", "CONTAB_IMPORT_NIVEL_DISCREPANTE", "Advertencia",
                            $"El nivel contable del archivo no coincide con el nivel derivado de la jerarquía ({n.Nivel})."));
                    }
                }
            }
            if (deriva)
            {
                // P19: manda la jerarquía. Si el archivo trae un tipo explícito distinto, se avisa y se usa el derivado.
                var derivado = fila.Clase == ClaseCuenta.Rubro ? TipoCuenta.Titulo : CuentaContable.DerivarTipo(n.Nivel, hijos.ContainsKey(n.Codigo));
                if (fila.TipoTxt is not null && f.ParseTipo(fila.TipoTxt) is { } explicito && explicito != derivado)
                    fila.Errores.Add(Err(num, "tipo_cuenta", "CONTAB_IMPORT_TIPO_DERIVADO", "Advertencia",
                        $"El tipo del archivo no coincide con la jerarquía; se guarda «{TextoTipo(derivado)}»."));
                fila.Tipo = derivado;
            }
            else if (fila.Tipo == TipoCuenta.Afectable && hijos.ContainsKey(n.Codigo))
            {
                res.AfectablesConHijos++;
                fila.Errores.Add(Err(num, "tipo_cuenta", "CONTAB_CUENTA_AFECTABLE_CON_HIJAS", "Error", "Una cuenta afectable no puede tener hijas."));
            }
            else if (fila.Tipo == TipoCuenta.Titulo && !hijos.ContainsKey(n.Codigo)) res.TitulosSinHijos++;
            if (fila.Clase == ClaseCuenta.Rubro && fila.Control != CuentaControl.Ninguna)
                fila.Errores.Add(Err(num, "cuenta_control", "CONTAB_CUENTA_RUBRO_INVALIDO", "Error",
                    "Un rubro es una agrupación de reporte: no puede ser cuenta colectiva."));
            else if (fila.Control != CuentaControl.Ninguna && fila.Tipo != TipoCuenta.Afectable)
                fila.Errores.Add(Err(num, "cuenta_control", "CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE", "Error",
                    deriva ? "Una cuenta colectiva debe recibir movimientos: no puede ser de nivel 1 ni tener cuentas debajo."
                        : "Una cuenta de control debe estar marcada explícitamente como afectable."));

            // ── 5. Acción ────────────────────────────────────────────────────
            if (fila.TieneErrores) continue;
            var actual = fila.ExistenteId is null ? null : existentes[fila.Codigo!];
            if (actual is null) { fila.Accion = Accion.Crear; continue; }
            if (actual.Clase != fila.Clase)
            {
                fila.Errores.Add(Err(num, null, "CONTAB_CUENTA_RUBRO_INVALIDO", "Error",
                    "La cuenta ya existe con otra clase (cuenta o rubro); la importación no convierte una en otra."));
                continue;
            }
            var sensible = actual.PadreCodigo != fila.PadreCodigo || actual.Naturaleza != fila.Naturaleza || actual.Tipo != fila.Tipo;
            var distinto = sensible || actual.Nombre != fila.Nombre || actual.Control != fila.Control
                || actual.Agrupador != fila.Agrupador || actual.Grupo != fila.Grupo || actual.RubroCodigo != fila.RubroCodigo;
            if (sensible && usoSubarbol.Contains(actual.Codigo))
            {
                fila.Errores.Add(Err(num, null, "CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO", "Error",
                    $"La cuenta ya tiene movimientos (ella o sus hijas); cambiar naturaleza, tipo o padre alteraría la interpretación de saldos históricos."));
                continue;
            }
            fila.Accion = distinto || fila.OrigenNuevo ? Accion.Actualizar : Accion.SinCambios;
        }
        return res;
    }

    private static List<string>? Ciclo(Nodo n, Dictionary<string, Nodo> nodos)
    {
        var cadena = new List<string> { n.Codigo };
        var actual = n;
        while (actual.Padre is not null && nodos.TryGetValue(actual.Padre, out var p))
        {
            if (p.Codigo == n.Codigo) return cadena;
            if (cadena.Contains(p.Codigo)) return null; // el ciclo no pasa por n; lo reporta quien sí está en él
            cadena.Add(p.Codigo);
            actual = p;
        }
        return null;
    }

    /// <summary>Códigos de cuentas usadas y de todos sus ancestros (R8: "ella o sus descendientes").</summary>
    private static HashSet<string> SubarbolesConUso(ExistenteCatalogo ex)
    {
        var porCodigo = ex.Cuentas.ToDictionary(c => c.Codigo);
        var set = new HashSet<string>();
        foreach (var c in ex.Cuentas.Where(c => c.Usada))
        {
            for (var a = c; a is not null && set.Add(a.Codigo);
                 a = a.PadreCodigo is not null ? porCodigo.GetValueOrDefault(a.PadreCodigo) : null) { }
        }
        return set;
    }

    private static string TextoTipo(TipoCuenta t) => t == TipoCuenta.Titulo ? "Acumula (no recibe movimientos)" : "Afectable (recibe movimientos)";

    /// <summary>Nombre legible de una columna canónica para los mensajes al usuario.</summary>
    private static readonly Dictionary<string, string> NombresColumna = new()
    {
        ["codigo"] = "Código", ["nombre"] = "Nombre", ["codigo_padre"] = "Cuenta padre", ["naturaleza"] = "Naturaleza",
        ["tipo_cuenta"] = "Tipo (acumula o afectable)", ["cuenta_control"] = "Cuenta de control",
        ["codigo_agrupador"] = "Código agrupador", ["grupo_reporte"] = "Grupo de reporte",
        ["codigo_origen"] = "Código de origen", ["fuente"] = "Fuente",
    };

    private static string Columna(string c) => NombresColumna.TryGetValue(c, out var n) ? n : c;

    /// <summary>"Deudora o Acreedora (también se aceptan: deudor, d, …)" sin repetir valores que solo cambian en mayúsculas.</summary>
    private static string ValoresAceptados(Dictionary<string, List<string>> aliases)
    {
        var canonicos = aliases.Keys.ToList();
        var otros = aliases.Values.SelectMany(v => v)
            .Where(a => !canonicos.Contains(a, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var principal = string.Join(" o ", canonicos);
        return otros.Count == 0 ? principal : $"{principal} (también se aceptan: {string.Join(", ", otros)})";
    }

    private ErrorFila Err(int fila, string? columna, string codigo, string severidad, string mensaje) =>
        new(fila, columna, codigo, severidad, mensaje, Sugerencia(codigo, columna));

    /// <summary>Tabla código → texto (§20.4). Son textos del sistema, no valores de negocio.</summary>
    private string Sugerencia(string codigo, string? columna) => codigo switch
    {
        "CONTAB_IMPORT_COLUMNA_FALTANTE" => $"Agregue la columna «{Columna(columna ?? "codigo")}» al archivo (también se acepta con los encabezados: {string.Join(", ", f.AliasesColumna(columna ?? "codigo"))}).",
        "CONTAB_IMPORT_COLUMNA_IGNORADA" => "El sistema no reconoce esta columna y la ignora. Si necesita que se cargue, avise al administrador del sistema.",
        "CONTAB_IMPORT_COLUMNA_SIN_MAPEO" => "Columna informativa conocida: no se importa. Confirme con Contabilidad su significado antes de mapearla.",
        "CONTAB_IMPORT_CAMPO_PENDIENTE" => columna == "cuenta_control"
            ? "Si alguna cuenta es de clientes o proveedores, agregue la columna al archivo o márquela después en el catálogo."
            : "La cuenta se carga como pendiente de validación y no podrá recibir movimientos hasta que Contabilidad complete el dato.",
        "CONTAB_IMPORT_CODIGO_FORMATO" => "Corrija el código para que siga el formato de cuentas del catálogo. Si el formato del archivo es el correcto, avise al administrador del sistema.",
        "CONTAB_IMPORT_CODIGO_RELLENADO" => "Verifique que el archivo no perdió ceros a la izquierda (formato texto en Excel).",
        "CONTAB_IMPORT_NATURALEZA_DESCONOCIDA" => $"Escriba {ValoresAceptados(f.Opciones.Naturaleza.Aliases)}. Corrija la celda en el archivo.",
        "CONTAB_IMPORT_TIPO_DESCONOCIDO" => $"Escriba {ValoresAceptados(f.Opciones.Tipo.Aliases)}. Corrija la celda en el archivo.",
        "CONTAB_IMPORT_PADRE_INEXISTENTE" => "Agregue la cuenta padre al archivo o cárguela antes. El padre se obtiene quitando el último nivel del código (por ejemplo, el padre de 100.10.10.00 es 100.10.00.00).",
        "CONTAB_IMPORT_CODIGO_DUPLICADO_EN_ARCHIVO" => "Deje una sola fila con ese código en el archivo.",
        "CONTAB_IMPORT_CICLO" => "Rompa el ciclo corrigiendo el padre de alguna de las filas indicadas.",
        "CONTAB_IMPORT_CONTROL_CONFLICTO" => "Corrija la marca de control de esta fila para que coincida con las cuentas de control del sistema, o avise al administrador del sistema.",
        "CONTAB_IMPORT_CODIFICACION" => "Guarde el archivo como CSV UTF-8 y vuelva a cargarlo.",
        "CONTAB_IMPORT_FILA_TITULO" => "Nada: es un título de presentación del reporte. Si debía ser una cuenta, escriba su código.",
        "CONTAB_IMPORT_TIPO_DERIVADO" => "El sistema calcula si la cuenta acumula o recibe movimientos por su posición en el catálogo; no hace falta corregir el archivo.",
        "CONTAB_IMPORT_PADRE_CONVERTIDO" => "Confirme que la cuenta padre ya no debe recibir movimientos directos; los futuros irán a las cuentas de debajo.",
        "CONTAB_CUENTA_RUBRO_INVALIDO" => "Revise la fila: un rubro solo agrupa cuentas de nivel 1 para el reporte; no tiene padre, hijas ni movimientos.",
        "CONTAB_IMPORT_NIVEL_DISCREPANTE" => "Revise el código o el nivel contable del archivo; el nivel derivado de la jerarquía es el que se guarda.",
        "CONTAB_IMPORT_ORIGEN_CODIGO_DISTINTO" => "Use el código ya registrado para ese origen o cambie el código de origen.",
        "CONTAB_CUENTA_NIVEL_EXCEDIDO" => "La cuenta queda demasiado profunda en el árbol. Reduzca los niveles o avise al administrador del sistema.",
        "CONTAB_CUENTA_PADRE_NO_ES_TITULO" => "Marque el padre como título o cambie el padre de esta cuenta.",
        "CONTAB_CUENTA_PADRE_INVALIDO" => "Reactive el padre antes de importar sus hijas.",
        "CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO" => "Cree una cuenta nueva y desactive la anterior, o use el procedimiento de reclasificación aprobado por Contabilidad.",
        "CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE" => "Quite la marca de cuenta colectiva o elija una cuenta que reciba movimientos (nivel 2 o más, sin cuentas debajo).",
        "CONTAB_CUENTA_AFECTABLE_CON_HIJAS" => "Marque la cuenta como título o reubique sus hijas.",
        "CONTAB_CUENTA_NATURALEZA_INVALIDA" => "La naturaleza debe coincidir con la de la cuenta padre.",
        _ => "Revise el valor de la celda.",
    };
}
