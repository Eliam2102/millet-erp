using System.Globalization;
using MediatR;
using Millet.Contabilidad.Application.SaldosIniciales;
using Millet.SharedKernel.Application.Cargas;

namespace Millet.Contabilidad.Infrastructure.Cargas;

/// <summary>
/// M-166 ↔ M-047 (86e3jvvmf): adaptador del tipo de carga <c>contabilidad.saldos-iniciales</c>. Convierte las filas
/// del lote común a <see cref="FilaBalanzaApertura"/> y llama por MediatR a la validación y a la aplicación de la
/// apertura. Contabilidad no lee tablas del lote ni de usuarios: los nombres llegan en el contexto.
/// </summary>
public sealed class SaldosInicialesProcesadorCarga(ISender sender) : IProcesadorCarga
{
    public const string TipoCarga = "contabilidad.saldos-iniciales";
    public string Tipo => TipoCarga;
    public string Nombre => "Saldos iniciales contables";
    public IReadOnlyList<ColumnaLoteCarga> Columnas { get; } =
    [
        new("cuenta", "Cuenta del catálogo nuevo, p. ej. 105.01.00.00", "Cuenta contable", "No. cuenta", "Cta"),
        new("cargo", "Saldo deudor (cargo) al corte", "Debe", "Deudor"),
        new("abono", "Saldo acreedor (abono) al corte", "Haber", "Acreedor"),
        new("tipoAuxiliar", "Cliente, Proveedor o CuentaBancaria en los renglones de detalle", "Tipo auxiliar"),
        new("claveAuxiliar", "Clave del cliente o proveedor, o número de la cuenta bancaria", "Clave auxiliar", "Auxiliar"),
        new("sucursal", "Clave de la sucursal; vacía usa la sucursal por omisión", "Sucursal"),
        new("concepto", "Concepto del renglón", "Concepto", "Descripción"),
    ];
    public IReadOnlyList<string> ColumnasObligatorias { get; } = ["cuenta", "cargo", "abono"];
    public bool RequiereFechaCorte => true;
    public string PermisoSubir => "contabilidad.catalogo.importar";
    public string PermisoAutorizar => "contabilidad.polizas.autorizar";

    public Task<IReadOnlyList<ParametroCarga>> ParametrosAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ParametroCarga>>([
            new("sucursalPorOmision", "Sucursal por omisión", "Clave de la sucursal para los renglones que no traen sucursal.", false, null),
        ]);

    public async Task<ValidacionCarga> ValidarAsync(LoteCargaContexto lote, IReadOnlyList<FilaCarga> filas, CancellationToken cancellationToken)
    {
        var (convertidas, errores) = Convertir(filas);
        if (errores.Count > 0) return new(false, errores, new Dictionary<string, string>());
        var v = await sender.Send(new ValidarBalanzaAperturaQuery(lote.FechaCorte!.Value, convertidas, Sucursal(lote)), cancellationToken);
        var resumen = new Dictionary<string, string>
        {
            ["Total cargos"] = v.TotalCargos.ToString("N2", CultureInfo.GetCultureInfo("es-MX")),
            ["Total abonos"] = v.TotalAbonos.ToString("N2", CultureInfo.GetCultureInfo("es-MX")),
            ["Diferencia"] = v.Diferencia.ToString("N2", CultureInfo.GetCultureInfo("es-MX")),
            ["Cuentas"] = v.Cuentas.ToString(CultureInfo.InvariantCulture),
            ["Auxiliares"] = v.Auxiliares.ToString(CultureInfo.InvariantCulture),
        };
        if (v.AperturaExistente is { } a) resumen["Apertura existente"] = a.Folio;
        return new(v.PuedeAplicar, [.. v.Errores.Select(e => new ErrorCarga(e.Fila, e.Campo, e.Codigo, e.Mensaje))], resumen);
    }

    public async Task<AplicacionCarga> AplicarAsync(LoteCargaContexto lote, IReadOnlyList<FilaCarga> filas, CancellationToken cancellationToken)
    {
        var (convertidas, _) = Convertir(filas);
        var r = await sender.Send(new AplicarSaldosInicialesCommand(lote.LoteId, lote.Huella, lote.FechaCorte!.Value,
            lote.SubidoPorId, lote.SubidoPorNombre, lote.AutorizadoPorId!.Value, lote.AutorizadoPorNombre ?? string.Empty,
            convertidas, Sucursal(lote)), cancellationToken);
        return new(r.PolizaId.ToString(), r.Folio, r.YaAplicada, new Dictionary<string, string>());
    }

    private static string? Sucursal(LoteCargaContexto lote) =>
        lote.Parametros.TryGetValue("sucursalPorOmision", out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    private static (List<FilaBalanzaApertura> Filas, List<ErrorCarga> Errores) Convertir(IReadOnlyList<FilaCarga> filas)
    {
        var lista = new List<FilaBalanzaApertura>();
        var errores = new List<ErrorCarga>();
        foreach (var f in filas)
        {
            string? T(string c) => f.Valores.TryGetValue(c, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            decimal Importe(string c)
            {
                if (T(c) is not { } texto) return 0m;
                if (decimal.TryParse(texto.Replace(",", "", StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
                errores.Add(new(f.Fila, c, "AP_IMPORTE_INVALIDO", $"El {c} «{texto}» no es un importe."));
                return 0m;
            }
            lista.Add(new(f.Fila, T("cuenta") ?? string.Empty, Importe("cargo"), Importe("abono"),
                T("tipoAuxiliar"), T("claveAuxiliar"), T("sucursal"), T("concepto")));
        }
        return (lista, errores);
    }
}
