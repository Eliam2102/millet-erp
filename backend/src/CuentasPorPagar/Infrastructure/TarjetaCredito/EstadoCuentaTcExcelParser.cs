using System.Globalization;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Ports.TarjetaCredito;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;

namespace Millet.CuentasPorPagar.Infrastructure.TarjetaCredito;

/// <summary>
/// Implementación de <see cref="IEstadoCuentaTcParserPort"/> para
/// estados de cuenta en formato Excel (.xlsx) — F7-PR5. ClosedXML lee
/// el archivo según el <see cref="PerfilParserBanco"/>: columnas,
/// formato fecha, signo de refund, locale.
///
/// <para>
/// <b>Casos manejados</b> (§6.2 anexo TC):
/// <list type="bullet">
///   <item>Headers en filas 1-N (skip vía <c>FilaInicioDatos</c>).</item>
///   <item>Filas de subtotal/saldo — detectar por columna fecha vacía
///   y capturar <c>TotalDeclaradoMxn</c> si la fila tiene palabra
///   clave SALDO/TOTAL.</item>
///   <item>Decimales con coma o punto vía <c>LocaleMontos</c>.</item>
///   <item>Refund por signo o por columna tipo (D11).</item>
/// </list>
/// </para>
///
/// <para>
/// El parser únicamente lee — no muta dominio. Los errores van a
/// <see cref="ParseResult.Errores"/> y el caller decide si rechazar
/// o continuar con advertencia.
/// </para>
/// </summary>
public sealed class EstadoCuentaTcExcelParser : IEstadoCuentaTcParserPort
{
    private readonly ILogger<EstadoCuentaTcExcelParser> _logger;

    public EstadoCuentaTcExcelParser(ILogger<EstadoCuentaTcExcelParser> logger)
    {
        _logger = logger;
    }

    public Task<ParseResult> ParseAsync(
        Stream archivoStream,
        PerfilParserBanco perfil,
        CancellationToken cancellationToken)
    {
        if (perfil.FormatoArchivo != "XLSX")
        {
            return Task.FromResult(new ParseResult(
                Lineas: [],
                Errores: [$"Formato '{perfil.FormatoArchivo}' no soportado por este parser (solo XLSX)."],
                TotalDeclaradoMxn: null));
        }

        var lineas = new List<LineaBancoParseada>();
        var errores = new List<string>();
        decimal? totalDeclarado = null;

        using var workbook = new XLWorkbook(archivoStream);
        var sheet = workbook.Worksheet(1);
        var locale = perfil.LocaleMontos == "es-MX" || perfil.LocaleMontos == "es-MX-PUNTO"
            ? CultureInfo.GetCultureInfo("es-MX")
            : CultureInfo.GetCultureInfo("en-US");

        var colFecha = ResolverColumna(perfil.ColumnaFecha);
        var colMonto = ResolverColumna(perfil.ColumnaMonto);
        var colMerchant = ResolverColumna(perfil.ColumnaMerchant);
        var colMoneda = perfil.ColumnaMoneda is { Length: > 0 } cm ? ResolverColumna(cm) : (int?)null;
        var colReferencia = perfil.ColumnaReferencia is { Length: > 0 } cr ? ResolverColumna(cr) : (int?)null;
        var colTipo = perfil.ColumnaTipo is { Length: > 0 } ct ? ResolverColumna(ct) : (int?)null;

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var rowNum = perfil.FilaInicioDatos; rowNum <= lastRow; rowNum++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = sheet.Row(rowNum);

            var fechaCell = row.Cell(colFecha);
            var montoCell = row.Cell(colMonto);
            var merchantCell = row.Cell(colMerchant);

            // Fila vacía o subtotal: detectar por columnas críticas vacías
            if (fechaCell.IsEmpty() || montoCell.IsEmpty() || merchantCell.IsEmpty())
            {
                var merchantPalabra = merchantCell.GetString().Trim().ToUpperInvariant();
                if ((merchantPalabra.Contains("TOTAL") || merchantPalabra.Contains("SALDO"))
                    && !montoCell.IsEmpty())
                {
                    if (TryParseMonto(montoCell.GetString(), locale, out var totalRow))
                        totalDeclarado = totalRow;
                }
                continue;
            }

            if (!TryParseFecha(fechaCell, perfil.FormatoFecha, out var fecha))
            {
                errores.Add($"Fila {rowNum}: fecha inválida ('{fechaCell.GetString()}').");
                continue;
            }

            if (!TryParseMonto(montoCell.GetString(), locale, out var monto))
            {
                errores.Add($"Fila {rowNum}: monto inválido ('{montoCell.GetString()}').");
                continue;
            }

            var merchantRaw = merchantCell.GetString().Trim();

            // Moneda: si hay columna, leerla; si no, default a MXN.
            var moneda = colMoneda is int cmIdx
                ? row.Cell(cmIdx).GetString().Trim().ToUpperInvariant()
                : "MXN";
            if (string.IsNullOrEmpty(moneda)) moneda = "MXN";

            var referencia = colReferencia is int crIdx
                ? row.Cell(crIdx).GetString().Trim()
                : null;
            if (string.IsNullOrEmpty(referencia)) referencia = null;

            var tipoSegunBanco = colTipo is int ctIdx
                ? row.Cell(ctIdx).GetString().Trim()
                : null;
            if (string.IsNullOrEmpty(tipoSegunBanco)) tipoSegunBanco = null;

            // Regla de signo: si negativo y la regla dice NegativoEsRefund,
            // marcamos tipo Refund (el dominio decide qué hacer con esa info).
            if (tipoSegunBanco is null)
            {
                tipoSegunBanco = perfil.ReglaSignoRefund switch
                {
                    "NegativoEsRefund" when monto < 0 => "Refund",
                    "PositivoEsRefund" when monto > 0 => "Refund",
                    _ => "Compra",
                };
            }

            // MontoMxn: si moneda == MXN, igual; si extranjera, el banco
            // siempre publica también el monto convertido — en MVP usamos
            // el mismo monto absoluto y asumimos el banco ya hizo la
            // conversión (perfil podría tener una columna dedicada en el
            // futuro; queda como TODO).
            var montoMxn = moneda == "MXN" ? Math.Abs(monto) : Math.Abs(monto);

            lineas.Add(new LineaBancoParseada(
                PosicionArchivo: rowNum,
                FechaAplicacion: fecha,
                Monto: monto,
                Moneda: moneda,
                MontoMxn: montoMxn,
                MerchantRaw: merchantRaw,
                ReferenciaBanco: referencia,
                TipoSegunBanco: tipoSegunBanco));
        }

        _logger.LogInformation(
            "[EstadoCuentaTcExcelParser] Perfil={Perfil} líneas={Count} errores={Errors} totalDeclarado={Total}",
            perfil.Codigo, lineas.Count, errores.Count, totalDeclarado);

        return Task.FromResult(new ParseResult(lineas, errores, totalDeclarado));
    }

    /// <summary>
    /// Resuelve la referencia de columna: 'A','B','AA' (letra Excel) o
    /// '1','2' (índice 1-based). Lanza si no se puede parsear.
    /// </summary>
    internal static int ResolverColumna(string referencia)
    {
        var trimmed = referencia.Trim();
        if (int.TryParse(trimmed, out var indice) && indice >= 1)
            return indice;

        // Letra columna (A=1, B=2, ..., Z=26, AA=27).
        var upper = trimmed.ToUpperInvariant();
        var result = 0;
        foreach (var c in upper)
        {
            if (c < 'A' || c > 'Z')
                throw new InvalidOperationException(
                    $"Referencia de columna inválida: '{referencia}'. Use letra Excel (A-ZZ) o índice 1-based.");
            result = result * 26 + (c - 'A' + 1);
        }
        if (result < 1)
            throw new InvalidOperationException($"Referencia de columna vacía o inválida: '{referencia}'.");
        return result;
    }

    private static bool TryParseFecha(IXLCell cell, string formato, out DateOnly fecha)
    {
        // ClosedXML reconoce nativamente las fechas Excel — preferimos esa ruta.
        if (cell.DataType == XLDataType.DateTime)
        {
            fecha = DateOnly.FromDateTime(cell.GetDateTime());
            return true;
        }

        var raw = cell.GetString().Trim();
        if (DateOnly.TryParseExact(raw, formato, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha))
            return true;
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha))
            return true;

        fecha = default;
        return false;
    }

    private static bool TryParseMonto(string raw, CultureInfo locale, out decimal monto)
    {
        raw = raw.Trim().Replace("$", "").Replace(" ", "");
        return decimal.TryParse(raw, NumberStyles.Number | NumberStyles.AllowParentheses, locale, out monto);
    }
}
