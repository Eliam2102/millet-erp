using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.Importacion;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Nomina;

// K10.11 (F1-CON-13): la nómina se calcula fuera del ERP y su póliza entra por archivo. Formato propuesto por VILO (supuesto
// «a validar» D10, INS-P1-40): un renglón por partida con cuenta, cargo, abono, sucursal, centro de costo, proyecto, concepto y
// empleado. El encabezado (tipo de póliza, fecha, referencia del lote, concepto y total de control) se captura en pantalla.
// La vista previa valida todo y no guarda nada; aplicar crea la póliza en borrador solo si no hay ningún error (K10.11-b).

/// <summary>Encabezado de la importación y el archivo (CSV en base64 o la tabla de una hoja .xlsx leída en el cliente).</summary>
public sealed record ImportacionNominaRequest(
    Guid TipoOperacionId, DateOnly Fecha, string Referencia, string Concepto, decimal? TotalControl, ImportacionRequest Archivo);

/// <summary>Error de un renglón del archivo (<c>Fila</c> = número de fila con la cabecera = 1) o del lote (<c>Fila</c> = null).</summary>
public sealed record ErrorImportacionNomina(int? Fila, string Campo, string Codigo, string Mensaje);

public sealed record RenglonNominaVista(
    int Fila, string Cuenta, string? CuentaNombre, decimal Cargo, decimal Abono, string? Sucursal, string? Centro, string? Concepto);

public sealed record VistaPreviaNominaResponse(
    bool Valida, int Renglones, decimal Cargos, decimal Abonos, bool Cuadra, IReadOnlyList<ErrorImportacionNomina> Errores,
    IReadOnlyList<RenglonNominaVista> Partidas, IReadOnlyList<string> ColumnasNoReconocidas);

public sealed class ImportacionNominaValidator : AbstractValidator<ImportacionNominaRequest>
{
    public ImportacionNominaValidator()
    {
        RuleFor(r => r.TipoOperacionId).NotEqual(Guid.Empty).WithMessage("Elija el tipo de póliza de nómina.");
        RuleFor(r => r.Fecha).NotEqual(default(DateOnly)).WithMessage("Indique la fecha contable.");
        RuleFor(r => r.Referencia).NotEmpty().MaximumLength(60)
            .WithMessage("Indique la referencia del lote (periodo y tipo de nómina, p. ej. «2026-Q22 ordinaria»), hasta 60 caracteres.");
        RuleFor(r => r.Concepto).NotEmpty().MaximumLength(Poliza.ConceptoMaximo);
        RuleFor(r => r.TotalControl).GreaterThan(0m).When(r => r.TotalControl is not null)
            .WithMessage("El total de control debe ser mayor a cero.");
        RuleFor(r => r.Archivo).NotNull().WithMessage("Elija el archivo de la nómina.");
    }
}

/// <summary>Vista previa (dry-run): valida el archivo completo y no guarda nada.</summary>
public sealed record VistaPreviaNominaQuery(ImportacionNominaRequest Importacion) : IRequest<VistaPreviaNominaResponse>;

public sealed class VistaPreviaNominaValidator : AbstractValidator<VistaPreviaNominaQuery>
{
    public VistaPreviaNominaValidator() => RuleFor(q => q.Importacion).SetValidator(new ImportacionNominaValidator());
}

/// <summary>Importa la póliza: si algo falla responde los errores por renglón y no crea nada; si no, nace en borrador y cuadrada.</summary>
public sealed record ImportarNominaCommand(ImportacionNominaRequest Importacion) : IRequest<PolizaResponse>;

public sealed class ImportarNominaValidator : AbstractValidator<ImportarNominaCommand>
{
    public ImportarNominaValidator() => RuleFor(c => c.Importacion).SetValidator(new ImportacionNominaValidator());
}

/// <summary>Columnas del formato y sus alias (sin acentos ni mayúsculas).</summary>
public static class FormatoNomina
{
    public const string OrigenModulo = "contabilidad";
    public const string OrigenTipo = "nomina_externa";

    public static readonly IReadOnlyDictionary<string, string[]> Alias = new Dictionary<string, string[]>
    {
        ["cuenta"] = ["cuenta", "cuenta contable", "num cta", "numcta", "codigo cuenta"],
        ["cargo"] = ["cargo", "cargos", "debe"],
        ["abono"] = ["abono", "abonos", "haber"],
        ["sucursal"] = ["sucursal", "clave sucursal"],
        ["centro"] = ["centro", "centro de costo", "centro costo", "ceco", "cc"],
        ["proyecto"] = ["proyecto"],
        ["concepto"] = ["concepto", "descripcion"],
        ["empleado"] = ["empleado", "codigo empleado", "codigo de nomina", "no empleado", "num empleado"],
    };

    /// <summary>Id estable del lote: la misma referencia siempre da el mismo documento de origen (no se duplica la póliza).</summary>
    public static Guid IdLote(string referencia) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("nomina-externa:" + FormatoCatalogo.Clave(referencia)))[..16]);

    /// <summary>Importe del archivo: admite «1,234.56», «$1,234.56» y vacío (= 0); null si no es un número.</summary>
    public static decimal? Importe(string? texto)
    {
        var t = FormatoCatalogo.Texto(texto);
        if (t is null) return 0m;
        t = t.Replace("$", string.Empty).Replace(",", string.Empty).Replace(" ", string.Empty);
        return decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)
            ? v : null;
    }
}

/// <summary>Lee, resuelve y valida el archivo; lo comparten la vista previa y la importación.</summary>
public sealed class ImportadorNomina(
    ContabilidadDbContext db, RegistroPolizas registro, ISucursalContabilidadPort sucursales, ICentroCostoContabilidadPort centros)
{
    private static readonly DimensionContable[] Niveles = [DimensionContable.Dim3, DimensionContable.Dim2, DimensionContable.Dim1];

    public sealed record Resultado(
        VistaPreviaNominaResponse Vista, IReadOnlyList<PartidaRequest> Partidas, IReadOnlyList<int> Filas, TipoDocumentoContable? Tipo,
        (int Anio, int Numero) Periodo);

    public async Task<Resultado> AnalizarAsync(ImportacionNominaRequest r, CancellationToken ct)
    {
        var errores = new List<ErrorImportacionNomina>();
        var tabla = LectorTabla.Leer(r.Archivo);

        // Columnas: cada cabecera se reconoce por sus alias; las que faltan y son obligatorias se rechazan.
        var indice = new Dictionary<string, int>();
        var noReconocidas = new List<string>();
        for (var i = 0; i < tabla.Columnas.Count; i++)
        {
            var clave = FormatoCatalogo.Clave(tabla.Columnas[i]).Replace('_', ' ').Replace('.', ' ').Trim();
            var canonica = FormatoNomina.Alias.FirstOrDefault(a => a.Value.Contains(clave)).Key;
            if (canonica is null) { if (clave.Length > 0) noReconocidas.Add(tabla.Columnas[i]); }
            else indice.TryAdd(canonica, i);
        }
        foreach (var obligatoria in new[] { "cuenta", "cargo", "abono", "sucursal" })
            if (!indice.ContainsKey(obligatoria))
                errores.Add(new(null, obligatoria, "NOM_COLUMNA_FALTANTE", $"Falta la columna «{obligatoria}» en el archivo."));

        TipoDocumentoContable? tipo = null;
        try { tipo = await registro.TipoAsync(r.TipoOperacionId, ct); }
        catch (Millet.SharedKernel.Application.Exceptions.ValidationException ex)
        {
            errores.AddRange(ex.Errors.Select(e => new ErrorImportacionNomina(null, "tipoOperacionId", e.Code, e.Message)));
        }

        var periodo = RegistroPolizas.Periodo(r.Fecha, null);
        if (await registro.RechazoPeriodoAsync(periodo.Anio, periodo.Numero, OrigenPoliza.Manual, ct) is { } rechazo)
            errores.Add(new(null, "fecha", rechazo.Code, rechazo.Message));

        var referencia = r.Referencia.Trim();
        var idLote = FormatoNomina.IdLote(referencia);
        var duplicada = await db.Polizas.AsNoTracking()
            .Where(p => p.OrigenModulo == FormatoNomina.OrigenModulo && p.OrigenTipo == FormatoNomina.OrigenTipo
                && p.OrigenDocumentoId == idLote && p.Estado != EstadoPoliza.Anulada && p.PolizaAnuladaId == null)
            .Select(p => new { p.TipoClave, p.Numero, p.Estado }).FirstOrDefaultAsync(ct);
        if (duplicada is not null)
            errores.Add(new(null, "referencia", "NOM_LOTE_DUPLICADO",
                $"El lote «{referencia}» ya se importó ({(duplicada.Numero is { } n ? Poliza.FormatearFolio(duplicada.TipoClave, n) : "póliza en borrador")}). " +
                "Para corregirlo, anule o descarte esa póliza y vuelva a importar."));

        // Catálogos que el archivo referencia, en lote.
        string? Celda(string?[] celdas, string col) =>
            indice.TryGetValue(col, out var i) && i < celdas.Length ? FormatoCatalogo.Texto(celdas[i]) : null;
        var codigos = tabla.Filas.Select(f => FormatoCatalogo.Codigo(Celda(f.Celdas, "cuenta"))).OfType<string>().Distinct().ToList();
        var cuentas = (await db.Cuentas.AsNoTracking().Where(c => codigos.Contains(c.Codigo)).ToListAsync(ct))
            .GroupBy(c => c.Codigo.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First());
        var listaSucursales = (await sucursales.ListarAsync(ct)).GroupBy(s => s.Clave.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());
        var centrosPorClave = await CentrosAsync(tabla.Filas.Select(f => Celda(f.Celdas, "centro")).OfType<string>().Distinct().ToList(), ct);

        var partidas = new List<PartidaRequest>();
        var filas = new List<int>();
        var vista = new List<RenglonNominaVista>();
        if (!errores.Any(e => e.Codigo == "NOM_COLUMNA_FALTANTE"))
        {
            foreach (var (fila, celdas) in tabla.Filas)
            {
                var antes = errores.Count;
                var codigo = FormatoCatalogo.Codigo(Celda(celdas, "cuenta"));
                CuentaContable? cuenta = null;
                if (codigo is null) errores.Add(new(fila, "cuenta", "NOM_CUENTA_VACIA", "Falta la cuenta."));
                else if (!cuentas.TryGetValue(codigo, out cuenta))
                    errores.Add(new(fila, "cuenta", "NOM_CUENTA_NO_EXISTE", $"La cuenta {codigo} no existe en el catálogo."));
                else if (!cuenta.Activa)
                    errores.Add(new(fila, "cuenta", "NOM_CUENTA_INACTIVA", $"La cuenta {codigo} está inactiva."));
                else if (cuenta.EsRubro || cuenta.Tipo != TipoCuenta.Afectable)
                    errores.Add(new(fila, "cuenta", "NOM_CUENTA_NO_AFECTABLE", $"La cuenta {codigo} es de mayor (acumulativa): use una subcuenta afectable."));

                var cargo = FormatoNomina.Importe(Celda(celdas, "cargo"));
                var abono = FormatoNomina.Importe(Celda(celdas, "abono"));
                if (cargo is null) errores.Add(new(fila, "cargo", "NOM_IMPORTE_INVALIDO", $"El cargo «{Celda(celdas, "cargo")}» no es un número."));
                if (abono is null) errores.Add(new(fila, "abono", "NOM_IMPORTE_INVALIDO", $"El abono «{Celda(celdas, "abono")}» no es un número."));
                if (cargo < 0m || abono < 0m)
                    errores.Add(new(fila, "cargo", "NOM_IMPORTE_NEGATIVO", "Los importes no pueden ser negativos: un abono va en la columna de abono."));
                else if (cargo is { } c && abono is { } a && (c != 0m) == (a != 0m))
                    errores.Add(new(fila, "cargo", "NOM_CARGO_Y_ABONO", "Cada renglón lleva un cargo o un abono (solo uno, distinto de cero)."));
                if (cargo is { } c2 && decimal.Round(c2, 2) != c2 || abono is { } a2 && decimal.Round(a2, 2) != a2)
                    errores.Add(new(fila, "cargo", "NOM_IMPORTE_DECIMALES", "Los importes llevan como máximo dos decimales."));

                var claveSucursal = Celda(celdas, "sucursal")?.ToUpperInvariant();
                SucursalContable? sucursal = null;
                if (claveSucursal is null) errores.Add(new(fila, "sucursal", "NOM_SUCURSAL_VACIA", "Falta la sucursal."));
                else if (!listaSucursales.TryGetValue(claveSucursal, out sucursal))
                    errores.Add(new(fila, "sucursal", "NOM_SUCURSAL_NO_EXISTE", $"La sucursal {claveSucursal} no existe."));

                var claveCentro = Celda(celdas, "centro");
                CentroCostoNodo? centro = null;
                if (claveCentro is not null && !centrosPorClave.TryGetValue(claveCentro.ToUpperInvariant(), out centro))
                    errores.Add(new(fila, "centro", "NOM_CENTRO_NO_EXISTE", $"El centro de costo {claveCentro} no existe."));

                var concepto = Concepto(Celda(celdas, "concepto"), Celda(celdas, "empleado"));
                if (concepto?.Length > PolizaPartida.ConceptoMaximo) concepto = concepto[..PolizaPartida.ConceptoMaximo];
                vista.Add(new(fila, codigo ?? string.Empty, cuenta?.Nombre, cargo ?? 0m, abono ?? 0m, claveSucursal, claveCentro, concepto));
                if (errores.Count > antes) continue;

                partidas.Add(new PartidaRequest(cuenta!.Id, concepto, cargo!.Value, abono!.Value, sucursal!.Id,
                    centro is null ? null : centro.Dim1Id,
                    centro is null ? null : centro.Nivel == DimensionContable.Dim1 ? null : centro.Dim2Id ?? centro.Id,
                    centro is { Nivel: DimensionContable.Dim3 } ? centro.Id : null,
                    Celda(celdas, "proyecto")));
                filas.Add(fila);
            }
        }

        var cargos = vista.Sum(v => v.Cargo);
        var abonos = vista.Sum(v => v.Abono);
        if (vista.Count > 0 && cargos != abonos)
            errores.Add(new(null, "importes", "NOM_NO_CUADRA", $"La póliza no cuadra: cargos {cargos:N2} y abonos {abonos:N2} (diferencia {cargos - abonos:N2})."));
        if (r.TotalControl is { } control && control != cargos)
            errores.Add(new(null, "totalControl", "NOM_TOTAL_CONTROL",
                $"El total de cargos del archivo ({cargos:N2}) no coincide con el total de control ({control:N2})."));
        if (vista.Count > 500)
            errores.Add(new(null, "archivo", "NOM_DEMASIADAS_PARTIDAS", $"El archivo tiene {vista.Count} renglones; la póliza admite hasta 500."));

        // Reglas de dimensión K10.2 (obligatorias por cuenta, combinaciones válidas), con el error en su renglón.
        if (errores.Count == 0 && tipo is not null && partidas.Count >= 2)
        {
            try { await registro.ValidarPartidasAsync(partidas, tipo.Id, r.Fecha, OrigenMovimiento.Manual, ct); }
            catch (Millet.SharedKernel.Application.Exceptions.ValidationException ex)
            {
                foreach (var e in ex.Errors)
                    errores.Add(new(IndiceFila(e.Field, filas), e.Field, e.Code, e.Message));
            }
        }
        else if (errores.Count == 0 && partidas.Count < 2)
            errores.Add(new(null, "archivo", "NOM_POCAS_PARTIDAS", "La póliza lleva al menos dos renglones."));

        var vistaPrevia = new VistaPreviaNominaResponse(errores.Count == 0, vista.Count, cargos, abonos, vista.Count > 0 && cargos == abonos,
            [.. errores.OrderBy(e => e.Fila ?? 0)], vista, noReconocidas);
        return new(vistaPrevia, partidas, filas, tipo, periodo);
    }

    /// <summary>«partidas[3].dim2Id» → la fila del archivo de la cuarta partida.</summary>
    private static int? IndiceFila(string campo, List<int> filas)
    {
        var abre = campo.IndexOf('[');
        var cierra = campo.IndexOf(']');
        return abre >= 0 && cierra > abre && int.TryParse(campo[(abre + 1)..cierra], out var i) && i < filas.Count ? filas[i] : null;
    }

    private static string? Concepto(string? concepto, string? empleado) =>
        (concepto, empleado) switch
        {
            (null, null) => null,
            (_, null) => concepto,
            (null, _) => $"Empleado {empleado}",
            _ => $"{concepto} · Empleado {empleado}",
        };

    /// <summary>Centros por clave (cualquier nivel; gana el más específico). Las claves que no existen no aparecen.</summary>
    private async Task<Dictionary<string, CentroCostoNodo>> CentrosAsync(IReadOnlyList<string> claves, CancellationToken ct)
    {
        var res = new Dictionary<string, CentroCostoNodo>(StringComparer.OrdinalIgnoreCase);
        foreach (var clave in claves)
            foreach (var nivel in Niveles)
            {
                var encontrados = await centros.BuscarAsync(nivel, clave, null, incluirInactivos: true, 20, ct);
                var exacto = encontrados.FirstOrDefault(c => string.Equals(c.Clave.Trim(), clave.Trim(), StringComparison.OrdinalIgnoreCase));
                if (exacto is null) continue;
                res[clave.ToUpperInvariant()] = exacto;
                break;
            }
        return res;
    }
}

public sealed class VistaPreviaNominaHandler(ImportadorNomina importador) : IRequestHandler<VistaPreviaNominaQuery, VistaPreviaNominaResponse>
{
    public async Task<VistaPreviaNominaResponse> Handle(VistaPreviaNominaQuery request, CancellationToken cancellationToken) =>
        (await importador.AnalizarAsync(request.Importacion, cancellationToken)).Vista;
}

public sealed class ImportarNominaHandler(
    ContabilidadDbContext db, ImportadorNomina importador, RegistroPolizas registro, AlcanceSucursalContable alcance,
    ICurrentUserContext usuario, LecturaPolizas lectura) : IRequestHandler<ImportarNominaCommand, PolizaResponse>
{
    public async Task<PolizaResponse> Handle(ImportarNominaCommand request, CancellationToken cancellationToken)
    {
        var r = request.Importacion;
        Poliza? poliza = null;
        await registro.EnTransaccionAsync(async ct =>
        {
            var analisis = await importador.AnalizarAsync(r, ct);
            if (!analisis.Vista.Valida)
                throw new Millet.SharedKernel.Application.Exceptions.ValidationException(analisis.Vista.Errores.Select(e =>
                    new ValidationError(e.Fila is { } f ? $"fila[{f}].{e.Campo}" : e.Campo, e.Codigo,
                        e.Fila is { } fila ? $"Fila {fila}: {e.Mensaje}" : e.Mensaje)));
            foreach (var s in analisis.Partidas.Select(p => p.SucursalId).Distinct()) await alcance.VerificarAsync(s, ct);
            var validadas = await registro.ValidarPartidasAsync(analisis.Partidas, analisis.Tipo!.Id, r.Fecha, OrigenMovimiento.Manual, ct);
            var referencia = r.Referencia.Trim();
            poliza = Poliza.CrearManual(Guid.CreateVersion7(), analisis.Tipo, r.Fecha, analisis.Periodo.Anio, analisis.Periodo.Numero,
                r.Concepto.Trim(), new DocumentoOrigenPoliza(FormatoNomina.OrigenModulo, FormatoNomina.OrigenTipo, FormatoNomina.IdLote(referencia),
                    referencia, null), validadas.Partidas, usuario.UserId, PoliticaPolizas.Usuario(usuario));
            poliza.ValidarCuadre();
            db.Polizas.Add(poliza);
            await db.SaveChangesAsync(ct);
        }, cancellationToken);
        return await lectura.ObtenerAsync(poliza!.Id, cancellationToken);
    }
}
