using System.Globalization;
using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Millet.Catalogos.Domain;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using ValidationException = Millet.SharedKernel.Application.Exceptions.ValidationException;

namespace Millet.Contabilidad.Application.SaldosIniciales;

// M-166 (F1-CON-13) · parte contable de la carga de saldos iniciales: valida la balanza de cierre del sistema anterior ya
// normalizada por el lote común de cargas (M-047) y genera la póliza de apertura una sola vez. La pantalla, el mapeo de columnas, la
// autorización del lote por otra persona y su rechazo con motivo son del lote común (M-047, M-167). Contrato: el lote llama por
// MediatR a ValidarBalanzaAperturaQuery (vista previa) y a AplicarSaldosInicialesCommand (lote autorizado); Contabilidad no lee
// tablas del lote.

/// <summary>
/// Renglón de la balanza ya mapeado por el lote. Sin <see cref="TipoAuxiliar"/> es el renglón de la cuenta; con él es el detalle
/// de esa cuenta por cliente, proveedor o cuenta bancaria (la suma del detalle debe igualar el renglón).
/// </summary>
public sealed record FilaBalanzaApertura(
    int Fila, string Cuenta, decimal Cargo, decimal Abono, string? TipoAuxiliar = null, string? ClaveAuxiliar = null,
    string? Sucursal = null, string? Concepto = null);

public sealed record ErrorBalanzaApertura(int? Fila, string Campo, string Codigo, string Mensaje);

public sealed record AperturaAplicada(Guid LoteId, string Huella, DateOnly FechaCorte, Guid PolizaId, string Folio);

public sealed record ValidacionBalanzaApertura(
    bool PuedeAplicar, decimal TotalCargos, decimal TotalAbonos, decimal Diferencia, IReadOnlyList<ErrorBalanzaApertura> Errores,
    int Cuentas, int Auxiliares, AperturaAplicada? AperturaExistente);

/// <summary>Vista previa contable del lote: no guarda nada.</summary>
/// <param name="SucursalPorOmision">Clave de la sucursal para los renglones que no traen sucursal (la balanza del sistema anterior no siempre la lleva).</param>
/// <param name="TipoOperacionId">Tipo de póliza de la apertura; sin él, el tipo <c>SI</c>.</param>
public sealed record ValidarBalanzaAperturaQuery(
    DateOnly FechaCorte, IReadOnlyList<FilaBalanzaApertura> Filas, string? SucursalPorOmision = null, Guid? TipoOperacionId = null)
    : IRequest<ValidacionBalanzaApertura>;

/// <summary>
/// Aplica un lote ya autorizado. Con la misma huella y fecha de corte ya aplicadas regresa la póliza existente
/// (<see cref="AperturaAplicadaResponse.YaAplicada"/>); con otro archivo, si la empresa ya tiene apertura, 409.
/// </summary>
/// <param name="TipoOperacionId">Tipo de póliza (Diario); sin él se usa el tipo <c>SI</c>.</param>
public sealed record AplicarSaldosInicialesCommand(
    Guid LoteId, string Huella, DateOnly FechaCorte, Guid SubidoPorId, string SubidoPor, Guid AutorizadoPorId, string AutorizadoPor,
    IReadOnlyList<FilaBalanzaApertura> Filas, string? SucursalPorOmision = null, Guid? TipoOperacionId = null, string? Concepto = null)
    : IRequest<AperturaAplicadaResponse>;

public sealed record AperturaAplicadaResponse(Guid PolizaId, string Folio, bool YaAplicada);

public sealed class ValidarBalanzaAperturaValidator : AbstractValidator<ValidarBalanzaAperturaQuery>
{
    public ValidarBalanzaAperturaValidator()
    {
        RuleFor(q => q.Filas).NotNull().WithMessage("El lote no trae renglones.")
            .Must(f => f is { Count: > 0 and <= ReglasBalanzaApertura.FilasMaximo })
            .WithMessage($"El lote debe traer de 1 a {ReglasBalanzaApertura.FilasMaximo} renglones.");
    }
}

public sealed class AplicarSaldosInicialesValidator : AbstractValidator<AplicarSaldosInicialesCommand>
{
    public AplicarSaldosInicialesValidator()
    {
        RuleFor(c => c.LoteId).NotEmpty().WithMessage("Falta el lote.");
        RuleFor(c => c.Huella).Must(h => h is not null && Regex.IsMatch(h.Trim(), "^[0-9a-fA-F]{64}$"))
            .WithMessage("La huella del archivo debe ser un SHA-256 en hexadecimal (64 caracteres).");
        RuleFor(c => c.SubidoPorId).NotEmpty().WithMessage("Falta quién subió el lote.");
        RuleFor(c => c.AutorizadoPorId).NotEmpty().WithMessage("Falta quién autorizó el lote.");
        RuleFor(c => c.SubidoPor).NotEmpty().MaximumLength(256).WithMessage("Falta el nombre de quien subió el lote.");
        RuleFor(c => c.AutorizadoPor).NotEmpty().MaximumLength(256).WithMessage("Falta el nombre de quien autorizó el lote.");
        RuleFor(c => c.Concepto).MaximumLength(200);
        RuleFor(c => c.Filas).NotNull().WithMessage("El lote no trae renglones.")
            .Must(f => f is { Count: > 0 and <= ReglasBalanzaApertura.FilasMaximo })
            .WithMessage($"El lote debe traer de 1 a {ReglasBalanzaApertura.FilasMaximo} renglones.");
    }
}

/// <summary>Cuenta del catálogo, lo que necesita la validación de la balanza.</summary>
public sealed record CuentaApertura(Guid Id, string Codigo, string Nombre, bool Activa, TipoCuenta? Tipo, CuentaControl Control);

/// <summary>Partida que generaría la apertura, con la fila de la balanza de la que sale (para ubicar los errores).</summary>
public sealed record PartidaApertura(int Fila, PartidaRequest Partida);

/// <summary>
/// Reglas de la balanza de apertura sin base de datos (para probarlas directo): renglón por renglón, el detalle de las cuentas de
/// control y el cuadre del archivo.
/// </summary>
public static class ReglasBalanzaApertura
{
    public const int FilasMaximo = 20_000;
    public const string OrigenModulo = "contabilidad";
    public const string OrigenTipo = "apertura";
    public const string TipoPolizaPorOmision = "SI";

    public sealed record Resultado(
        IReadOnlyList<ErrorBalanzaApertura> Errores, IReadOnlyList<PartidaApertura> Partidas, decimal TotalCargos, decimal TotalAbonos,
        int Cuentas, int Auxiliares)
    {
        public decimal Diferencia => TotalCargos - TotalAbonos;
    }

    /// <summary>«Cliente», «Proveedor», «CuentaBancaria» (o «Banco»), sin distinguir mayúsculas ni espacios; null si no se reconoce.</summary>
    public static TipoAuxiliar? Auxiliar(string? texto) =>
        texto?.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant() switch
        {
            "CLIENTE" => TipoAuxiliar.Cliente,
            "PROVEEDOR" => TipoAuxiliar.Proveedor,
            "CUENTABANCARIA" or "BANCO" => TipoAuxiliar.Banco,
            _ => null,
        };

    /// <summary>Las cuentas de clientes y proveedores exigen el detalle; deudores y acreedores lo admiten.</summary>
    public static bool ExigeDetalle(CuentaControl control) => control is CuentaControl.Clientes or CuentaControl.Proveedores;

    /// <summary>Origen con el que se valida la partida: las cuentas colectivas, como su auxiliar (la captura manual no las toca, P23).</summary>
    public static OrigenMovimiento Origen(CuentaControl control) => control switch
    {
        CuentaControl.Clientes or CuentaControl.Deudores => OrigenMovimiento.AuxiliarCxC,
        CuentaControl.Proveedores or CuentaControl.Acreedores => OrigenMovimiento.AuxiliarCxP,
        _ => OrigenMovimiento.Automatica,
    };

    public static string Pesos(decimal importe) => importe.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>La fecha de corte es el último día de un mes: la apertura va en ese periodo y es el saldo inicial del siguiente.</summary>
    public static bool EsFinDeMes(DateOnly fecha) => fecha.AddDays(1).Day == 1;

    public static Resultado Revisar(
        IReadOnlyList<FilaBalanzaApertura> filas,
        IReadOnlyDictionary<string, CuentaApertura> cuentas,
        Func<TipoAuxiliar, string, AuxiliarContable?> auxiliar,
        IReadOnlyDictionary<string, SucursalContable> sucursales,
        string? sucursalPorOmision)
    {
        var errores = new List<ErrorBalanzaApertura>();
        void Error(int? fila, string campo, string codigo, string mensaje) => errores.Add(new(fila, campo, codigo, mensaje));

        // 1) Renglón por renglón.
        var validas = new List<(FilaBalanzaApertura F, CuentaApertura C, TipoAuxiliar? Tipo, AuxiliarContable? Aux, SucursalContable S)>();
        foreach (var f in filas)
        {
            var antes = errores.Count;
            var codigo = f.Cuenta?.Trim() ?? string.Empty;
            CuentaApertura? cuenta = null;
            if (codigo.Length == 0) Error(f.Fila, "cuenta", "AP_CUENTA_REQUERIDA", "Falta la cuenta.");
            else if (!cuentas.TryGetValue(codigo, out cuenta)) Error(f.Fila, "cuenta", "AP_CUENTA_NO_EXISTE", $"Cuenta no existe en el catálogo ({codigo}).");
            else if (!cuenta.Activa) Error(f.Fila, "cuenta", "AP_CUENTA_INACTIVA", $"La cuenta {codigo} está inactiva.");
            else if (cuenta.Tipo != TipoCuenta.Afectable)
                Error(f.Fila, "cuenta", "AP_CUENTA_ACUMULATIVA", $"La cuenta {codigo} es acumulativa: el saldo va en sus subcuentas.");

            if (f.Cargo < 0m || f.Abono < 0m) Error(f.Fila, "importe", "AP_IMPORTE_NEGATIVO", "El cargo y el abono no pueden ser negativos.");
            else if (f.Cargo > 0m && f.Abono > 0m) Error(f.Fila, "importe", "AP_CARGO_Y_ABONO", "El renglón trae cargo y abono a la vez; va uno solo.");
            if (decimal.Round(f.Cargo, 2) != f.Cargo || decimal.Round(f.Abono, 2) != f.Abono)
                Error(f.Fila, "importe", "AP_IMPORTE_DECIMALES", "Los importes van con dos decimales como máximo.");

            TipoAuxiliar? tipo = null;
            AuxiliarContable? aux = null;
            var hayTipo = !string.IsNullOrWhiteSpace(f.TipoAuxiliar);
            var clave = f.ClaveAuxiliar?.Trim();
            if (hayTipo && (tipo = Auxiliar(f.TipoAuxiliar)) is null)
                Error(f.Fila, "tipoAuxiliar", "AP_TIPO_AUXILIAR_INVALIDO", $"Tipo de auxiliar «{f.TipoAuxiliar!.Trim()}» no válido: Cliente, Proveedor o CuentaBancaria.");
            else if (hayTipo && string.IsNullOrEmpty(clave))
                Error(f.Fila, "claveAuxiliar", "AP_AUXILIAR_REQUERIDO", "Falta la clave del auxiliar.");
            else if (!hayTipo && !string.IsNullOrEmpty(clave))
                Error(f.Fila, "tipoAuxiliar", "AP_TIPO_AUXILIAR_REQUERIDO", "La clave del auxiliar viene sin su tipo (Cliente, Proveedor o CuentaBancaria).");
            else if (tipo is { } t)
            {
                aux = auxiliar(t, clave!);
                if (aux is null) Error(f.Fila, "claveAuxiliar", "AP_AUXILIAR_NO_EXISTE", $"{Nombre(t)} {clave} no existe.");
                else if (cuenta is not null && !Corresponde(t, cuenta.Control))
                    Error(f.Fila, "tipoAuxiliar", cuenta.Control == CuentaControl.Ninguna && t != TipoAuxiliar.Banco
                            ? "AP_AUXILIAR_EN_CUENTA_SIN_CONTROL" : "AP_AUXILIAR_NO_CORRESPONDE",
                        $"La cuenta {cuenta.Codigo} no lleva detalle por {Nombre(t).ToLowerInvariant()}.");
            }

            var claveSucursal = (string.IsNullOrWhiteSpace(f.Sucursal) ? sucursalPorOmision : f.Sucursal)?.Trim().ToUpperInvariant();
            SucursalContable? sucursal = null;
            if (string.IsNullOrEmpty(claveSucursal))
                Error(f.Fila, "sucursal", "AP_SUCURSAL_REQUERIDA", "Falta la sucursal (o la sucursal por omisión del lote).");
            else if (!sucursales.TryGetValue(claveSucursal, out sucursal))
                Error(f.Fila, "sucursal", "AP_SUCURSAL_NO_EXISTE", $"La sucursal {claveSucursal} no existe.");

            if (errores.Count == antes) validas.Add((f, cuenta!, tipo, aux, sucursal!));
        }

        // 2) Por cuenta: el renglón y su detalle.
        var partidas = new List<PartidaApertura>();
        var auxiliares = 0;
        foreach (var g in validas.GroupBy(v => v.C.Id))
        {
            var cuenta = g.First().C;
            var renglones = g.Where(v => v.Tipo is null).ToList();
            var detalle = g.Where(v => v.Tipo is not null).ToList();
            if (renglones.Count > 1)
            {
                foreach (var otro in renglones.Skip(1))
                    Error(otro.F.Fila, "cuenta", "AP_CUENTA_REPETIDA", $"La cuenta {cuenta.Codigo} ya viene en la fila {renglones[0].F.Fila}.");
                continue;
            }
            var renglon = renglones.SingleOrDefault();
            if (detalle.Count == 0)
            {
                if (ExigeDetalle(cuenta.Control) && renglon.F is { } sinDetalle && Saldo(sinDetalle) != 0m)
                {
                    Error(sinDetalle.Fila, "cuenta", "AP_DETALLE_REQUERIDO",
                        $"La cuenta {cuenta.Codigo} lleva el detalle por {(cuenta.Control == CuentaControl.Clientes ? "cliente" : "proveedor")}.");
                    continue;
                }
                if (renglon.F is { } unico && Saldo(unico) != 0m) partidas.Add(new(unico.Fila, Partida(unico, cuenta, renglon.S, null, null)));
                continue;
            }
            foreach (var repetido in detalle.GroupBy(d => (d.Tipo, d.Aux!.Id, d.S.Id)).Where(x => x.Count() > 1).SelectMany(x => x.Skip(1)))
                Error(repetido.F.Fila, "claveAuxiliar", "AP_AUXILIAR_REPETIDO",
                    $"{Nombre(repetido.Tipo!.Value)} {repetido.Aux!.Clave} ya viene en la cuenta {cuenta.Codigo} y la misma sucursal.");
            if (renglon.F is { } total)
            {
                var diferencia = Saldo(total) - detalle.Sum(d => Saldo(d.F));
                if (diferencia != 0m)
                {
                    Error(total.Fila, "detalle", "AP_DETALLE_NO_IGUALA",
                        $"El detalle de {Plural(detalle[0].Tipo!.Value)} no iguala el renglón (diferencia {Pesos(Math.Abs(diferencia))}).");
                    continue;
                }
            }
            foreach (var d in detalle.Where(d => Saldo(d.F) != 0m))
            {
                partidas.Add(new(d.F.Fila, Partida(d.F, cuenta, d.S, d.Tipo, d.Aux)));
                auxiliares++;
            }
        }

        // 3) Cuadre del archivo: los renglones y, de las cuentas que solo traen detalle, el detalle.
        var conRenglon = filas.Where(f => string.IsNullOrWhiteSpace(f.TipoAuxiliar)).Select(f => f.Cuenta?.Trim()).ToHashSet(StringComparer.Ordinal);
        var paraTotales = filas.Where(f => string.IsNullOrWhiteSpace(f.TipoAuxiliar) || !conRenglon.Contains(f.Cuenta?.Trim())).ToList();
        var cargos = paraTotales.Sum(f => f.Cargo);
        var abonos = paraTotales.Sum(f => f.Abono);
        if (cargos != abonos)
            Error(null, "archivo", "AP_NO_CUADRA", $"No cuadra por {Pesos(Math.Abs(cargos - abonos))} (cargos {Pesos(cargos)}, abonos {Pesos(abonos)}).");
        else if (cargos == 0m)
            Error(null, "archivo", "AP_SIN_SALDOS", "La balanza no trae saldos.");

        return new(errores.OrderBy(e => e.Fila ?? int.MaxValue).ToList(), partidas,
            cargos, abonos, partidas.Select(p => p.Partida.CuentaId).Distinct().Count(), auxiliares);
    }

    private static decimal Saldo(FilaBalanzaApertura f) => f.Cargo - f.Abono;

    private static bool Corresponde(TipoAuxiliar tipo, CuentaControl control) => tipo switch
    {
        TipoAuxiliar.Cliente => control is CuentaControl.Clientes or CuentaControl.Deudores,
        TipoAuxiliar.Proveedor => control is CuentaControl.Proveedores or CuentaControl.Acreedores,
        _ => control == CuentaControl.Ninguna,
    };

    private static string Nombre(TipoAuxiliar t) => t switch
    {
        TipoAuxiliar.Cliente => "El cliente",
        TipoAuxiliar.Proveedor => "El proveedor",
        _ => "La cuenta bancaria",
    };

    private static string Plural(TipoAuxiliar t) => t switch
    {
        TipoAuxiliar.Cliente => "clientes",
        TipoAuxiliar.Proveedor => "proveedores",
        _ => "cuentas bancarias",
    };

    private static PartidaRequest Partida(FilaBalanzaApertura f, CuentaApertura cuenta, SucursalContable sucursal, TipoAuxiliar? tipo,
        AuxiliarContable? aux)
    {
        var saldo = Saldo(f);
        var concepto = string.IsNullOrWhiteSpace(f.Concepto) ? "Saldo inicial" : f.Concepto.Trim();
        if (concepto.Length > 200) concepto = concepto[..200];
        return new PartidaRequest(cuenta.Id, concepto, saldo > 0m ? saldo : 0m, saldo < 0m ? -saldo : 0m, sucursal.Id,
            ClienteId: tipo == TipoAuxiliar.Cliente ? aux!.Id : null,
            ProveedorId: tipo == TipoAuxiliar.Proveedor ? aux!.Id : null,
            CuentaBancariaId: tipo == TipoAuxiliar.Banco ? aux!.Id : null,
            Origen: Origen(cuenta.Control));
    }
}

/// <summary>
/// Servicio <c>ValidadorBalanzaApertura</c> (M-166): carga el catálogo, las sucursales y los auxiliares que menciona el lote,
/// aplica <see cref="ReglasBalanzaApertura"/> y, si no hay errores de renglón, valida las partidas con las mismas reglas de las
/// pólizas (dimensiones K10.2, periodo) y devuelve esos errores en su fila.
/// </summary>
public sealed class ValidadorBalanzaApertura(
    ContabilidadDbContext db, RegistroPolizas registro, ISucursalContabilidadPort sucursales, ITerceroContabilidadPort terceros,
    ICuentaBancariaContabilidadPort bancos)
{
    public sealed record Analisis(ValidacionBalanzaApertura Vista, IReadOnlyList<PartidaApertura> Partidas, (int Anio, int Numero) Periodo);

    public async Task<Analisis> AnalizarAsync(DateOnly fechaCorte, IReadOnlyList<FilaBalanzaApertura> filas, string? sucursalPorOmision,
        Guid? tipoOperacionId, CancellationToken ct)
    {
        var codigos = filas.Select(f => f.Cuenta?.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
        var cuentas = await db.Cuentas.AsNoTracking().Where(c => codigos.Contains(c.Codigo) && c.Clase != ClaseCuenta.Rubro)
            .Select(c => new CuentaApertura(c.Id, c.Codigo, c.Nombre, c.Estatus == EstatusCatalogo.Activo, c.Tipo, c.CuentaControl))
            .ToListAsync(ct);
        var porCodigo = cuentas.GroupBy(c => c.Codigo, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var listaSucursales = (await sucursales.ListarAsync(ct)).GroupBy(s => s.Clave.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var auxiliares = new Dictionary<TipoAuxiliar, IReadOnlyDictionary<string, AuxiliarContable>>();
        foreach (var g in filas.Select(f => (Tipo: ReglasBalanzaApertura.Auxiliar(f.TipoAuxiliar), Clave: f.ClaveAuxiliar?.Trim()))
                     .Where(x => x.Tipo is not null && !string.IsNullOrEmpty(x.Clave)).GroupBy(x => x.Tipo!.Value))
        {
            var claves = g.Select(x => x.Clave!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            auxiliares[g.Key] = g.Key == TipoAuxiliar.Banco
                ? await bancos.ObtenerPorNumerosAsync(claves, ct)
                : await terceros.ObtenerPorClavesAsync(g.Key, claves, ct);
        }
        AuxiliarContable? Buscar(TipoAuxiliar t, string clave) =>
            auxiliares.TryGetValue(t, out var d) && d.TryGetValue(clave.Trim().ToUpperInvariant(), out var a) ? a : null;

        var r = ReglasBalanzaApertura.Revisar(filas, porCodigo, Buscar, listaSucursales, sucursalPorOmision);
        var errores = r.Errores.ToList();
        var periodo = RegistroPolizas.Periodo(fechaCorte, null);
        if (!ReglasBalanzaApertura.EsFinDeMes(fechaCorte))
            errores.Add(new(null, "fechaCorte", "AP_FECHA_CORTE", "La fecha de corte es el último día de un mes."));
        else if (await registro.RechazoPeriodoAsync(periodo.Anio, periodo.Numero, OrigenPoliza.Apertura, ct) is { } rechazo)
            errores.Add(new(null, "fechaCorte", "AP_PERIODO_NO_DISPONIBLE",
                $"El periodo {periodo.Numero:D2}/{periodo.Anio} de la apertura no admite pólizas: {rechazo.Message}"));

        // Mismas reglas que cualquier póliza (dimensiones obligatorias por cuenta, K10.2), con el error en la fila de la balanza.
        if (errores.Count == 0 && r.Partidas.Count > 0 && tipoOperacionId is { } tipoId)
        {
            try
            {
                await registro.ValidarPartidasAsync([.. r.Partidas.Select(p => p.Partida)], tipoId, fechaCorte,
                    OrigenMovimiento.Automatica, ct);
            }
            catch (ValidationException e)
            {
                errores.AddRange(e.Errors.Select(x => new ErrorBalanzaApertura(FilaDe(x.Field, r.Partidas), Campo(x.Field), x.Code,
                    Regex.Replace(x.Message, @"^Partida \d+(?: \(cuenta [^)]*\))?: ", string.Empty))));
            }
        }

        var existente = await db.Aperturas.AsNoTracking()
            .Select(a => new AperturaAplicada(a.LoteId, a.Huella, a.FechaCorte, a.PolizaId, a.Folio)).FirstOrDefaultAsync(ct);
        var vista = new ValidacionBalanzaApertura(errores.Count == 0 && existente is null, r.TotalCargos, r.TotalAbonos, r.Diferencia,
            [.. errores.OrderBy(e => e.Fila ?? int.MaxValue)], r.Cuentas, r.Auxiliares, existente);
        return new(vista, r.Partidas, periodo);
    }

    private static int? FilaDe(string campo, IReadOnlyList<PartidaApertura> partidas)
    {
        var m = Regex.Match(campo, @"^partidas\[(\d+)\]");
        return m.Success && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is var i && i < partidas.Count ? partidas[i].Fila : null;
    }

    private static string Campo(string campo)
    {
        var m = Regex.Match(campo, @"^partidas\[\d+\]\.?(.*)$");
        return m.Success ? (m.Groups[1].Value.Length == 0 ? "cuenta" : m.Groups[1].Value) : campo;
    }
}

public sealed class ValidarBalanzaAperturaHandler(ValidadorBalanzaApertura validador, ContabilidadDbContext db)
    : IRequestHandler<ValidarBalanzaAperturaQuery, ValidacionBalanzaApertura>
{
    public async Task<ValidacionBalanzaApertura> Handle(ValidarBalanzaAperturaQuery request, CancellationToken cancellationToken)
    {
        var tipo = await AperturaTipos.PorOmisionAsync(db, request.TipoOperacionId, cancellationToken);
        var analisis = await validador.AnalizarAsync(request.FechaCorte, request.Filas, request.SucursalPorOmision, tipo?.Id,
            cancellationToken);
        if (tipo is not null) return analisis.Vista;
        var errores = analisis.Vista.Errores.Append(AperturaTipos.ErrorSinTipo).ToList();
        return analisis.Vista with { PuedeAplicar = false, Errores = errores };
    }
}

internal static class AperturaTipos
{
    public static readonly ErrorBalanzaApertura ErrorSinTipo = new(null, "tipoOperacionId", "AP_TIPO_POLIZA",
        $"Dé de alta el tipo de póliza {ReglasBalanzaApertura.TipoPolizaPorOmision} (Diario) o indique el tipo de la apertura.");

    /// <summary>El tipo indicado o, sin él, el tipo <c>SI</c> activo; null si no existe.</summary>
    public static async Task<TipoDocumentoContable?> PorOmisionAsync(ContabilidadDbContext db, Guid? tipoOperacionId, CancellationToken ct) =>
        tipoOperacionId is { } id
            ? await db.TiposDocumento.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct)
            : await db.TiposDocumento.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Clave == ReglasBalanzaApertura.TipoPolizaPorOmision && t.Estatus == EstatusCatalogo.Activo, ct);
}

public sealed class AplicarSaldosInicialesHandler(
    ContabilidadDbContext db, ValidadorBalanzaApertura validador, RegistroPolizas registro, IClock clock)
    : IRequestHandler<AplicarSaldosInicialesCommand, AperturaAplicadaResponse>
{
    public async Task<AperturaAplicadaResponse> Handle(AplicarSaldosInicialesCommand request, CancellationToken cancellationToken)
    {
        var r = request;
        if (r.SubidoPorId == r.AutorizadoPorId)
            throw new BusinessRuleException("AP_AUTORIZA_OTRA_PERSONA", "Debe autorizar otra persona.");
        var huella = AperturaContable.NormalizarHuella(r.Huella);
        if (await RepetidoAsync(huella, r.FechaCorte, cancellationToken) is { } previa) return previa;

        var tipo = await AperturaTipos.PorOmisionAsync(db, r.TipoOperacionId, cancellationToken);
        if (tipo is null) throw new BusinessRuleException(AperturaTipos.ErrorSinTipo.Codigo, AperturaTipos.ErrorSinTipo.Mensaje);
        if (tipo.TipoSat != TipoPolizaSat.Diario)
            throw new BusinessRuleException("AP_TIPO_POLIZA", $"La apertura va en una póliza de Diario; el tipo {tipo.Clave} no lo es.");

        Poliza? poliza = null;
        try
        {
            await registro.EnTransaccionAsync(async ct =>
            {
                var analisis = await validador.AnalizarAsync(r.FechaCorte, r.Filas, r.SucursalPorOmision, tipo.Id, ct);
                if (analisis.Vista.Errores.Count > 0)
                    throw new ValidationException(analisis.Vista.Errores.Select(e => new ValidationError(
                        e.Fila is { } f ? $"filas[{f}].{e.Campo}" : e.Campo, e.Codigo, e.Fila is { } fila ? $"Fila {fila}: {e.Mensaje}" : e.Mensaje)));
                var (anio, numero) = analisis.Periodo;
                var partidas = await registro.ValidarPartidasAsync([.. analisis.Partidas.Select(p => p.Partida)], tipo.Id, r.FechaCorte,
                    OrigenMovimiento.Automatica, ct);
                var folio = await registro.ReservarNumeroAsync(tipo.Id, anio, ct);
                var corte = r.FechaCorte.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var concepto = string.IsNullOrWhiteSpace(r.Concepto) ? $"Apertura: saldos iniciales al {corte}" : r.Concepto.Trim();
                poliza = Poliza.CrearApertura(Guid.CreateVersion7(), tipo, r.FechaCorte, anio, numero, concepto,
                    new DocumentoOrigenPoliza(ReglasBalanzaApertura.OrigenModulo, ReglasBalanzaApertura.OrigenTipo, r.LoteId, $"Corte {corte}", null),
                    $"apertura:{r.LoteId:N}", partidas.Partidas, folio, r.SubidoPorId, r.SubidoPor.Trim(), r.AutorizadoPorId, r.AutorizadoPor.Trim(),
                    clock.UtcNow);
                db.Polizas.Add(poliza);
                db.Aperturas.Add(new AperturaContable(Guid.CreateVersion7(), r.LoteId, huella, r.FechaCorte, poliza.Id, poliza.Folio!,
                    r.SubidoPorId, r.AutorizadoPorId, r.AutorizadoPor.Trim(), clock.UtcNow));
                await db.SaveChangesAsync(ct);
                await registro.RegistrarUsosAsync(poliza, partidas.ReglasAplicadas, ct);
                await db.SaveChangesAsync(ct);
            }, cancellationToken);
        }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException
                                          {
                                              SqlState: Npgsql.PostgresErrorCodes.UniqueViolation,
                                              ConstraintName: "ux_aperturas_empresa" or "ux_polizas_llave_idempotencia",
                                          })
        {
            // Otra petición aplicó la apertura al mismo tiempo: el mismo archivo responde lo ya aplicado; otro, 409.
            db.ChangeTracker.Clear();
            return await RepetidoAsync(huella, r.FechaCorte, cancellationToken)
                ?? throw new InvalidOperationException("Apertura duplicada sin registro.");
        }
        return new(poliza!.Id, poliza.Folio!, false);
    }

    /// <summary>Misma huella y corte ⇒ la apertura ya aplicada; otro archivo con apertura existente ⇒ 409.</summary>
    private async Task<AperturaAplicadaResponse?> RepetidoAsync(string huella, DateOnly fechaCorte, CancellationToken ct)
    {
        var existente = await db.Aperturas.AsNoTracking().FirstOrDefaultAsync(ct);
        if (existente is null) return null;
        if (existente.EsElMismoArchivo(huella, fechaCorte)) return new(existente.PolizaId, existente.Folio, true);
        throw new ConflictException("AP_YA_HAY_APERTURA",
            $"Ya hay apertura (póliza {existente.Folio}, corte {existente.FechaCorte:yyyy-MM-dd}); use el ajuste único.");
    }
}
