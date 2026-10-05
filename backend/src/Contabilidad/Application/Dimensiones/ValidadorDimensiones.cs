using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.Dimensiones;

/// <summary>
/// Validación de dimensiones de un movimiento (F1-CON-02, ficha K10.2). Orden: cuenta → tipo de documento → sucursal → centros
/// (existencia, nivel, jerarquía, estado) → ubicación del centro contra la sucursal (salvo centro corporativo) → auxiliares
/// (cliente, proveedor, banco) → reglas vigentes a la fecha contable. Junta todos los errores.
/// Resolución de reglas (D3): por dimensión gana la cuenta más cercana (la propia, luego el ancestro más cercano) y, a igual
/// cuenta, la regla del tipo de documento sobre la de todos los tipos. Un valor derivado por jerarquía cuenta como capturado
/// para "obligatorio", pero "no aplica" solo rechaza lo capturado directamente.
/// </summary>
public sealed class ValidadorDimensiones(
    ContabilidadDbContext db,
    ICuentaContableReadPort cuentas,
    ICentroCostoContabilidadPort centros,
    ISucursalContabilidadPort sucursales,
    ITerceroContabilidadPort terceros,
    ICuentaBancariaContabilidadPort bancos,
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
        var catalogoSucursales = await sucursales.ListarAsync(ct);
        var sucursal = catalogoSucursales.FirstOrDefault(s => s.Id == m.SucursalId);
        if (sucursal is not { Activa: true })
            errores.Add(new("CONTAB_DIM_SUCURSAL_INVALIDA",
                sucursal is null ? "La sucursal no existe." : $"La sucursal {sucursal.Nombre} está inactiva.", "sucursalId"));

        // 4–5. Centros y su ubicación.
        var efectivos = await ValidarCentrosAsync(m, sucursal, catalogoSucursales, errores, ct);

        // 6. Auxiliares capturados: existen y están activos.
        await ValidarAuxiliarAsync(TipoAuxiliar.Cliente, m.ClienteId, DimensionContable.Cliente, errores, ct);
        await ValidarAuxiliarAsync(TipoAuxiliar.Proveedor, m.ProveedorId, DimensionContable.Proveedor, errores, ct);
        await ValidarAuxiliarAsync(TipoAuxiliar.Banco, m.CuentaBancariaId, DimensionContable.Banco, errores, ct);

        // 7. Reglas vigentes a la fecha contable.
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
            var directo = Capturada(r.Dimension, m);
            var efectivo = directo || Derivada(r.Dimension, efectivos);
            var origen = r.Heredada ? $" (regla definida en la cuenta {r.CuentaOrigenCodigo})" : string.Empty;
            if (r.Requerimiento == RequerimientoDimension.Obligatorio && !efectivo)
                yield return new("CONTAB_DIM_OBLIGATORIA_FALTANTE",
                    $"Falta {r.NombreDimension}: es obligatoria para {donde}{origen}.", Campo(r.Dimension), r.Dimension);
            else if (r.Requerimiento == RequerimientoDimension.NoAplica && directo)
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

    private async Task ValidarAuxiliarAsync(TipoAuxiliar tipo, Guid? id, DimensionContable dim, List<ErrorDimension> errores, CancellationToken ct)
    {
        if (id is not { } aid) return;
        var encontrados = tipo == TipoAuxiliar.Banco
            ? await bancos.ObtenerAsync([aid], ct)
            : await terceros.ObtenerAsync(tipo, [aid], ct);
        if (!encontrados.TryGetValue(aid, out var aux))
            errores.Add(new("CONTAB_DIM_AUXILIAR_NO_EXISTE", $"{O.Nombre(dim)}: el registro indicado no existe.", Campo(dim), dim));
        else if (!aux.Activo)
            errores.Add(new("CONTAB_DIM_AUXILIAR_INACTIVO",
                $"{O.Nombre(dim)} {aux.Clave} — {aux.Nombre} está dado de baja y no se puede usar en movimientos nuevos.", Campo(dim), dim));
    }

    private async Task<CentrosEfectivos> ValidarCentrosAsync(
        MovimientoDimensionado m, SucursalContable? sucursal, IReadOnlyList<SucursalContable> catalogoSucursales,
        List<ErrorDimension> errores, CancellationToken ct)
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

        // Ubicación → sucursal (K10.2, V49): la ubicación (Dim1) del centro debe ser de la sucursal del movimiento, salvo que el
        // CeCo (Dim2) sea corporativo. Una Dim3 hereda la ubicación y el carácter corporativo de su CeCo.
        if (O.ExigirSucursalDelCentro && congruente && sucursal is not null && efectivos.Dim1Id is { } dim1Id)
        {
            var corporativo = efectivos.Dim2Id is { } d2Id && await db.CentrosCorporativos.AsNoTracking().AnyAsync(x => x.Dim2Id == d2Id, ct);
            if (!corporativo)
            {
                var nodo = (d3 ?? d2 ?? d1)!;
                var ubicacion = d1?.Clave ?? nodo.Dim1Clave;
                var ligada = await db.UbicacionesSucursal.AsNoTracking().FirstOrDefaultAsync(x => x.Dim1Id == dim1Id, ct);
                if (ligada is null)
                    errores.Add(new("CONTAB_DIM_UBICACION_SIN_SUCURSAL",
                        $"La ubicación {ubicacion} del centro {nodo.Clave} no está ligada a ninguna sucursal; pida a Contabilidad que la configure.",
                        Campo(nodo.Nivel), nodo.Nivel));
                else if (ligada.SucursalId != sucursal.Id)
                {
                    var suya = catalogoSucursales.FirstOrDefault(s => s.Id == ligada.SucursalId)?.Nombre ?? "otra sucursal";
                    errores.Add(new("CONTAB_DIM_CENTRO_OTRA_SUCURSAL",
                        $"El centro {nodo.Clave} es de la ubicación {ubicacion} ({suya}) y no se puede usar en la sucursal {sucursal.Nombre}.",
                        Campo(nodo.Nivel), nodo.Nivel));
                }
            }
        }
        return efectivos;
    }

    /// <summary>La dimensión viene capturada directamente en la partida.</summary>
    private static bool Capturada(DimensionContable d, MovimientoDimensionado m) => d switch
    {
        DimensionContable.Dim1 => m.Dim1Id is not null,
        DimensionContable.Dim2 => m.Dim2Id is not null,
        DimensionContable.Dim3 => m.Dim3Id is not null,
        DimensionContable.Proyecto => !string.IsNullOrWhiteSpace(m.Proyecto),
        DimensionContable.Cliente => m.ClienteId is not null,
        DimensionContable.Proveedor => m.ProveedorId is not null,
        _ => m.CuentaBancariaId is not null,
    };

    /// <summary>Solo los centros se derivan (del nivel inferior capturado); proyecto y auxiliares no.</summary>
    private static bool Derivada(DimensionContable d, CentrosEfectivos e) => d switch
    {
        DimensionContable.Dim1 => e.Dim1Id is not null,
        DimensionContable.Dim2 => e.Dim2Id is not null,
        DimensionContable.Dim3 => e.Dim3Id is not null,
        _ => false,
    };

    public static string Campo(DimensionContable d) => d switch
    {
        DimensionContable.Dim1 => "dim1Id",
        DimensionContable.Dim2 => "dim2Id",
        DimensionContable.Dim3 => "dim3Id",
        DimensionContable.Proyecto => "proyecto",
        DimensionContable.Cliente => "clienteId",
        DimensionContable.Proveedor => "proveedorId",
        _ => "cuentaBancariaId",
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
