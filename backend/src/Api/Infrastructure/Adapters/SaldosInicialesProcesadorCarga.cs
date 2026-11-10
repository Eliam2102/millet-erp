using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Cargas.Lotes;
using Millet.Contabilidad.Application.SaldosIniciales;
using Millet.DatosMaestros.Application.Cargas;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// M-166 ↔ M-047: tipo de carga <c>contabilidad.saldos-iniciales</c> del lote común. Convierte las filas mapeadas del lote en la
/// balanza de apertura y llama por MediatR a la validación y a la aplicación de Contabilidad. Vive en Api (raíz de composición)
/// porque Contabilidad no puede referenciar Compartido: Compartido ya referencia Contabilidad para sus adaptadores.
/// </summary>
public sealed class SaldosInicialesProcesadorCarga(IMediator mediator, IdentidadDbContext identidad, ICurrentEmpresaContext empresa)
    : IProcesadorCarga
{
    public const string TipoCarga = "contabilidad.saldos-iniciales";

    public const string Cuenta = "cuenta", Cargo = "cargo", Abono = "abono", TipoAuxiliar = "tipoAuxiliar",
        ClaveAuxiliar = "claveAuxiliar", Sucursal = "sucursal", Concepto = "concepto";

    public string Tipo => TipoCarga;
    public string Nombre => "Saldos iniciales contables";
    public bool RequiereFechaCorte => true;
    public string PermisoSubir => PermisosCanonicos.ContabilidadPolizasCapturar;
    public string PermisoAutorizar => PermisosCanonicos.ContabilidadPolizasAutorizar;

    public IReadOnlyList<ColumnaCarga> Columnas { get; } =
    [
        new(Cuenta, "Código de la cuenta en el catálogo nuevo", "cuenta contable", "codigo cuenta", "num cuenta"),
        new(Cargo, "Saldo deudor al corte", "deudor", "saldo deudor", "debe", "debito"),
        new(Abono, "Saldo acreedor al corte", "acreedor", "saldo acreedor", "haber", "credito"),
        new(TipoAuxiliar, "Cliente, Proveedor o CuentaBancaria (solo en el detalle de la cuenta)", "tipo auxiliar", "auxiliar"),
        new(ClaveAuxiliar, "Clave del cliente o del proveedor, o número de la cuenta bancaria", "clave auxiliar", "cliente", "proveedor"),
        new(Sucursal, "Clave de la sucursal", "clave sucursal"),
        new(Concepto, "Concepto del movimiento (opcional)", "descripcion"),
    ];

    public IReadOnlyList<string> ColumnasObligatorias { get; } = [Cuenta, Cargo, Abono, Sucursal];

    public async Task<ValidacionCarga> ValidarAsync(LoteCargaContexto lote, IReadOnlyList<FilaCarga> filas, CancellationToken cancellationToken)
    {
        var (balanza, errores) = Convertir(filas);
        if (lote.FechaCorte is null)
            errores.Add(new ErrorCarga(null, "fechaCorte", "AP_FECHA_CORTE", "Indique la fecha de corte del lote."));
        if (errores.Count > 0 || lote.FechaCorte is null)
            return new ValidacionCarga(false, errores, new Dictionary<string, string>());

        var v = await mediator.Send(new ValidarBalanzaAperturaQuery(lote.FechaCorte.Value, balanza), cancellationToken);
        var resumen = new Dictionary<string, string>
        {
            ["totalCargos"] = Texto(v.TotalCargos),
            ["totalAbonos"] = Texto(v.TotalAbonos),
            ["diferencia"] = Texto(v.Diferencia),
            ["cuentas"] = v.Cuentas.ToString(CultureInfo.InvariantCulture),
            ["auxiliares"] = v.Auxiliares.ToString(CultureInfo.InvariantCulture),
        };
        if (v.AperturaExistente is { } ap) resumen["aperturaExistente"] = ap.Folio;
        return new ValidacionCarga(v.PuedeAplicar, [.. v.Errores.Select(e => new ErrorCarga(e.Fila, e.Campo, e.Codigo, e.Mensaje))], resumen);
    }

    public async Task<AplicacionCarga> AplicarAsync(LoteCargaContexto lote, IReadOnlyList<FilaCarga> filas, CancellationToken cancellationToken)
    {
        if (lote.FechaCorte is not { } corte)
            throw new BusinessRuleException("AP_FECHA_CORTE", "El lote no tiene fecha de corte.");
        if (lote.AutorizadoPorId is not { } autorizadoPorId)
            throw new BusinessRuleException("AP_AUTORIZA_OTRA_PERSONA", "El lote debe estar autorizado por otra persona antes de aplicarse.");
        var (balanza, errores) = Convertir(filas);
        if (errores.Count > 0)
            throw new BusinessRuleException("AP_VALIDACION", Resumir(errores.Select(e => (e.Fila, e.Mensaje))));

        var nombres = await NombresAsync([lote.SubidoPorId, autorizadoPorId], cancellationToken);
        try
        {
            var r = await mediator.Send(new AplicarSaldosInicialesCommand(lote.LoteId, lote.Huella, corte,
                lote.SubidoPorId, nombres.GetValueOrDefault(lote.SubidoPorId, "Usuario que subió el lote"),
                autorizadoPorId, nombres.GetValueOrDefault(autorizadoPorId, "Usuario que autorizó el lote"), balanza), cancellationToken);
            return new AplicacionCarga(r.PolizaId.ToString(), r.Folio, r.YaAplicada, new Dictionary<string, string> { ["poliza"] = r.Folio });
        }
        catch (ValidationException e)
        {
            // La balanza cambió de validez entre la vista previa y la aplicación: el lote queda autorizado con el motivo.
            throw new BusinessRuleException("AP_VALIDACION", Resumir(e.Errors.Select(x => ((int?)null, x.Message))));
        }
    }

    /// <summary>Filas del lote → balanza de apertura. Los importes admiten «1,234.56», «$1,234.56» y vacío (= 0).</summary>
    public static (List<FilaBalanzaApertura> Balanza, List<ErrorCarga> Errores) Convertir(IReadOnlyList<FilaCarga> filas)
    {
        var balanza = new List<FilaBalanzaApertura>(filas.Count);
        var errores = new List<ErrorCarga>();
        foreach (var f in filas)
        {
            string? Valor(string columna) => f.Valores.TryGetValue(columna, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            var cargo = Importe(Valor(Cargo));
            var abono = Importe(Valor(Abono));
            if (cargo is null) errores.Add(new ErrorCarga(f.Fila, Cargo, "AP_IMPORTE_INVALIDO", $"El cargo «{Valor(Cargo)}» no es un importe."));
            if (abono is null) errores.Add(new ErrorCarga(f.Fila, Abono, "AP_IMPORTE_INVALIDO", $"El abono «{Valor(Abono)}» no es un importe."));
            balanza.Add(new FilaBalanzaApertura(f.Fila, Valor(Cuenta) ?? string.Empty, cargo ?? 0m, abono ?? 0m,
                Valor(TipoAuxiliar), Valor(ClaveAuxiliar), Valor(Sucursal), Valor(Concepto)));
        }
        return (balanza, errores);
    }

    public static decimal? Importe(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0m;
        var t = texto.Replace("$", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)
            ? v : null;
    }

    private static string Texto(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Resumir(IEnumerable<(int? Fila, string Mensaje)> errores)
    {
        var lista = errores.ToList();
        var texto = string.Join("; ", lista.Take(5).Select(e => e.Fila is { } f ? $"Fila {f}: {e.Mensaje}" : e.Mensaje));
        return lista.Count > 5 ? $"{texto}; y {lista.Count - 5} más." : texto;
    }

    private async Task<Dictionary<Guid, string>> NombresAsync(Guid[] ids, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        return await identidad.Usuarios.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Nombre, ct);
    }
}

/// <summary>Registro de los tipos de carga que aporta Contabilidad al lote común.</summary>
public static class CargasContabilidadRegistro
{
    public static IServiceCollection AddCargasContabilidad(this IServiceCollection services) =>
        services.AddScoped<IProcesadorCarga, SaldosInicialesProcesadorCarga>();
}
