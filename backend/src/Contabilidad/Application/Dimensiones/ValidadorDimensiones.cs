using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.Dimensiones;

/// <summary>
/// Validación de dimensiones de un movimiento (F1-CON-02, plan §5). Orden: cuenta → tipo de documento → sucursal → centros
/// (existencia, nivel, jerarquía, estado) → sucursal del centro → reglas vigentes a la fecha contable. Junta todos los errores.
/// Resolución de reglas (D3): por dimensión gana la cuenta más cercana (la propia, luego el ancestro más cercano) y, a igual
/// cuenta, la regla del tipo de documento sobre la de todos los tipos. Un valor derivado por jerarquía cuenta como capturado
/// para "obligatorio", pero "no aplica" solo rechaza lo capturado directamente.
/// </summary>
public sealed class ValidadorDimensiones(
    ContabilidadDbContext db,
    ICuentaContableReadPort cuentas,
    ICentroCostoContabilidadPort centros,
    ISucursalContabilidadPort sucursales,
    IOptions<DimensionesOpciones> opciones) : IDimensionContableValidacionPort
{
    private DimensionesOpciones O => opciones.Value;

    public async Task<ValidacionDimensiones> ValidarAsync(MovimientoDimensionado movimiento, CancellationToken ct)
    {
        var m = movimiento;
        var errores = new List<ErrorDimension>();

        // 1. Cuenta (mismas reglas que el puerto de CON-01).
        var cuenta = await cuentas.ValidarParaMovimientoAsync(m.CuentaId, m.Origen, ct);
        if (!cuenta.Valida) errores.Add(new("CONTAB_DIM_CUENTA_NO_VALIDA", MensajeCuenta(cuenta), "cuentaId"));

        // 2. Tipo de documento.
        var tipo = await db.TiposDocumento.AsNoTracking().FirstOrDefaultAsync(t => t.Id == m.TipoDocumentoId, ct);
        if (tipo is not { Activo: true })
            errores.Add(new("CONTAB_DIM_TIPO_DOC_INVALIDO",
                tipo is null ? "El tipo de documento no existe." : $"El tipo de documento «{tipo.Nombre}» está inactivo.", "tipoDocumentoId"));

        // 3. Sucursal del movimiento (de la empresa actual).
        var sucursal = (await sucursales.ListarAsync(ct)).FirstOrDefault(s => s.Id == m.SucursalId);
        if (sucursal is not { Activa: true })
            errores.Add(new("CONTAB_DIM_SUCURSAL_INVALIDA",
                sucursal is null ? "La sucursal no existe." : $"La sucursal {sucursal.Nombre} está inactiva.", "sucursalId"));

        // 4–5. Centros.
        var efectivos = await ValidarCentrosAsync(m, sucursal, errores, ct);

        // 6. Reglas vigentes a la fecha contable.
        var requerimientos = cuenta.Cuenta is null
            ? []
            : await ResolverAsync(m.CuentaId, m.TipoDocumentoId, m.FechaContable, ct);
        if (cuenta.Cuenta is not null)
            errores.AddRange(EvaluarRequerimientos(requerimientos, m, efectivos,
                $"la cuenta {cuenta.Cuenta.Codigo}" + (tipo is null ? string.Empty : $" en «{tipo.Nombre}»")));

        return new(errores.Count == 0, errores, requerimientos, efectivos);
    }

    /// <summary>Requerimiento efectivo de cada dimensión para la cuenta y el tipo (null = solo reglas "todos los tipos").</summary>
    public async Task<IReadOnlyList<RequerimientoEfectivo>> ResolverAsync(Guid cuentaId, Guid? tipoDocumentoId, DateOnly fecha, CancellationToken ct)
    {
        var cadena = await CadenaAsync(cuentaId, ct);
        var ids = cadena.Select(c => c.Id).ToList();
        var reglas = await db.ReglasDimension.AsNoTracking()
            .Where(r => ids.Contains(r.CuentaId)
                && (r.TipoDocumentoId == null || r.TipoDocumentoId == tipoDocumentoId)
                && r.VigenteDesde <= fecha && (r.VigenteHasta == null || r.VigenteHasta >= fecha))
            .ToListAsync(ct);
        return Resolver(cadena, reglas, tipoDocumentoId, fecha, O);
    }

    /// <summary>
    /// D3 (puro): por dimensión gana la regla vigente de la cuenta más cercana (índice en <paramref name="cadena"/>, 0 = la
    /// propia) y, a igual cuenta, la del tipo de documento sobre la de todos los tipos. Sin regla ⇒ <c>SinReglaEs</c>.
    /// </summary>
    public static IReadOnlyList<RequerimientoEfectivo> Resolver(
        IReadOnlyList<(Guid Id, string Codigo)> cadena, IEnumerable<ReglaDimension> reglas, Guid? tipoDocumentoId, DateOnly fecha, DimensionesOpciones o)
    {
        var ids = cadena.Select(c => c.Id).ToList();
        var aplicables = reglas
            .Where(r => ids.Contains(r.CuentaId) && (r.TipoDocumentoId is null || r.TipoDocumentoId == tipoDocumentoId) && r.VigenteEn(fecha))
            .ToList();
        return [.. Enum.GetValues<DimensionContable>().Select(d =>
        {
            var regla = aplicables.Where(r => r.Dimension == d)
                .OrderBy(r => ids.IndexOf(r.CuentaId))
                .ThenBy(r => r.TipoDocumentoId is null ? 1 : 0)
                .FirstOrDefault();
            if (regla is null)
                return new RequerimientoEfectivo(d, o.Nombre(d), o.SinReglaEs, null, null, false, false, null, null, false);
            var indice = ids.IndexOf(regla.CuentaId);
            return new RequerimientoEfectivo(d, o.Nombre(d), regla.Requerimiento, regla.Id, cadena[indice].Codigo, indice > 0,
                regla.TipoDocumentoId is null, regla.VigenteDesde, regla.VigenteHasta, regla.EsPrueba);
        })];
    }

    /// <summary>
    /// Puro: "obligatorio" se cumple con el valor efectivo (capturado o derivado por jerarquía); "no aplica" solo rechaza
    /// lo capturado directamente. <paramref name="donde"/> describe cuenta y tipo para el mensaje.
    /// </summary>
    public static IEnumerable<ErrorDimension> EvaluarRequerimientos(
        IEnumerable<RequerimientoEfectivo> requerimientos, MovimientoDimensionado m, CentrosEfectivos efectivos, string donde)
    {
        foreach (var r in requerimientos)
        {
            var directo = Valor(r.Dimension, m.Dim1Id, m.Dim2Id, m.Dim3Id);
            var efectivo = Valor(r.Dimension, efectivos.Dim1Id, efectivos.Dim2Id, efectivos.Dim3Id);
            var origen = r.Heredada ? $" (regla definida en la cuenta {r.CuentaOrigenCodigo})" : string.Empty;
            if (r.Requerimiento == RequerimientoDimension.Obligatorio && efectivo is null)
                yield return new("CONTAB_DIM_OBLIGATORIA_FALTANTE",
                    $"Falta {r.NombreDimension}: es obligatoria para {donde}{origen}.", Campo(r.Dimension), r.Dimension);
            else if (r.Requerimiento == RequerimientoDimension.NoAplica && directo is not null)
                yield return new("CONTAB_DIM_NO_APLICA",
                    $"{r.NombreDimension} no aplica para {donde}{origen}: quítela del movimiento.", Campo(r.Dimension), r.Dimension);
        }
    }

    /// <summary>La cuenta y sus ancestros, de la más cercana a la raíz.</summary>
    private async Task<List<(Guid Id, string Codigo)>> CadenaAsync(Guid cuentaId, CancellationToken ct)
    {
        var cadena = new List<(Guid, string)>();
        Guid? actual = cuentaId;
        while (actual is { } id && cadena.Count < 32)
        {
            var c = await db.Cuentas.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.Id, x.Codigo, x.PadreId }).FirstOrDefaultAsync(ct);
            if (c is null) break;
            cadena.Add((c.Id, c.Codigo));
            actual = c.PadreId;
        }
        return cadena;
    }

    private async Task<CentrosEfectivos> ValidarCentrosAsync(
        MovimientoDimensionado m, SucursalContable? sucursal, List<ErrorDimension> errores, CancellationToken ct)
    {
        var capturados = new (DimensionContable Dim, Guid? Id)[] { (DimensionContable.Dim1, m.Dim1Id), (DimensionContable.Dim2, m.Dim2Id), (DimensionContable.Dim3, m.Dim3Id) };
        var ids = capturados.Where(c => c.Id is not null).Select(c => c.Id!.Value).ToList();
        if (ids.Count == 0) return new(null, null, null);

        var nodos = await centros.ObtenerAsync(ids, ct);
        var validos = new Dictionary<DimensionContable, CentroCostoNodo>();
        foreach (var (dim, id) in capturados)
        {
            if (id is not { } cid) continue;
            if (!nodos.TryGetValue(cid, out var nodo))
                errores.Add(new("CONTAB_DIM_CENTRO_NO_EXISTE", $"El centro indicado en {O.Nombre(dim)} no existe en el catálogo de centros de costo.", Campo(dim), dim));
            else if (nodo.Nivel != dim)
                errores.Add(new("CONTAB_DIM_CENTRO_NIVEL_INCORRECTO",
                    $"El centro {nodo.Clave} es de la {O.Nombre(nodo.Nivel)}, no de la {O.Nombre(dim)}.", Campo(dim), dim));
            else validos[dim] = nodo;
        }

        // Jerarquía: los capturados deben ser de la misma rama; los niveles superiores faltantes se derivan.
        validos.TryGetValue(DimensionContable.Dim1, out var d1);
        validos.TryGetValue(DimensionContable.Dim2, out var d2);
        validos.TryGetValue(DimensionContable.Dim3, out var d3);
        var congruente = true;
        if (d3 is not null && d2 is not null && d3.Dim2Id != d2.Id)
        {
            congruente = false;
            errores.Add(new("CONTAB_DIM_JERARQUIA_INCONGRUENTE",
                $"El centro {d3.Clave} no pertenece al centro {d2.Clave}: elija centros de la misma rama.", Campo(DimensionContable.Dim3), DimensionContable.Dim3));
        }
        var inferior = d3 ?? d2;
        if (inferior is not null && d1 is not null && inferior.Dim1Id != d1.Id)
        {
            congruente = false;
            errores.Add(new("CONTAB_DIM_JERARQUIA_INCONGRUENTE",
                $"El centro {inferior.Clave} no pertenece al centro {d1.Clave}: elija centros de la misma rama.", Campo(inferior.Nivel), inferior.Nivel));
        }

        // Estado: un centro dado de baja (o con un superior dado de baja) no se usa en movimientos nuevos.
        foreach (var n in validos.Values.Where(n => !n.ActivoEnCadena))
            errores.Add(new("CONTAB_DIM_CENTRO_INACTIVO",
                n.Activo
                    ? $"El centro {n.Clave} — {n.Nombre} pertenece a un centro superior dado de baja y no se puede usar en movimientos nuevos."
                    : $"El centro {n.Clave} — {n.Nombre} está dado de baja y no se puede usar en movimientos nuevos.",
                Campo(n.Nivel), n.Nivel));

        var efectivos = new CentrosEfectivos(
            d1?.Id ?? inferior?.Dim1Id,
            d2?.Id ?? d3?.Dim2Id,
            d3?.Id);

        // Sucursal del centro (D6): la decide el CeCo (Dim2); una Dim3 hereda la de su Dim2. Solo Dim1 no se liga a sucursal.
        if (O.ExigirSucursalDelCentro && congruente && efectivos.Dim2Id is { } dim2Id && sucursal is not null)
        {
            var asignadas = await db.CentrosSucursal.AsNoTracking().Where(x => x.Dim2Id == dim2Id).Select(x => x.SucursalId).ToListAsync(ct);
            var clave = d2?.Clave ?? d3?.Dim2Clave;
            var campo = Campo(inferior!.Nivel);
            if (asignadas.Count == 0)
                errores.Add(new("CONTAB_DIM_CENTRO_SIN_SUCURSAL",
                    $"El centro {clave} no tiene sucursales asignadas para movimientos contables; pida a Contabilidad que lo asigne.", campo, inferior.Nivel));
            else if (!asignadas.Contains(sucursal.Id))
                errores.Add(new("CONTAB_DIM_CENTRO_OTRA_SUCURSAL",
                    $"El centro {clave} no está asignado a la sucursal {sucursal.Nombre}.", campo, inferior.Nivel));
        }
        return efectivos;
    }

    private static Guid? Valor(DimensionContable d, Guid? dim1, Guid? dim2, Guid? dim3) =>
        d switch { DimensionContable.Dim1 => dim1, DimensionContable.Dim2 => dim2, _ => dim3 };

    public static string Campo(DimensionContable d) => d switch
    {
        DimensionContable.Dim1 => "dim1Id",
        DimensionContable.Dim2 => "dim2Id",
        _ => "dim3Id",
    };

    private static string MensajeCuenta(CuentaContableValidacion v)
    {
        var c = v.Cuenta?.Codigo;
        return v.Motivo switch
        {
            MotivoRechazoCuenta.NoExiste => "La cuenta contable no existe.",
            MotivoRechazoCuenta.Rubro => $"{c} es un rubro de reporte y no recibe movimientos.",
            MotivoRechazoCuenta.Inactiva => $"La cuenta {c} está dada de baja.",
            MotivoRechazoCuenta.PendienteValidacion => $"La cuenta {c} está pendiente de validación por Contabilidad (sin naturaleza).",
            MotivoRechazoCuenta.Titulo => $"La cuenta {c} acumula (tiene cuentas debajo) y no recibe movimientos.",
            MotivoRechazoCuenta.ControlSoloAuxiliar => $"La cuenta {c} es colectiva: solo se afecta desde su módulo.",
            _ => $"La cuenta {c} no acepta movimientos.",
        };
    }
}
