using System.Text.Json.Serialization;

namespace Millet.Contabilidad.Application.ConciliacionAuxiliares;

/// <summary>Controles que concilia CON-09.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ControlConciliacion { Clientes, Proveedores, Bancos }

/// <summary>
/// Resultado de un control. <c>SinCuenta</c>: no hay cuenta contable configurada, no se compara (nunca da un falso «Cuadra»).
/// <c>NoComparable</c>: hay cuenta, pero el auxiliar no se puede comparar con ese filtro o en esa moneda (se explica en las notas).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoConciliacion { Cuadra, Diferencia, SinCuenta, NoComparable }

/// <summary>Tipo de diferencia por documento del detalle de un control.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TipoDiferenciaAuxiliar { Proveedor, DocumentoSinPoliza, PartidaSinAuxiliar, CuentaBancaria }

/// <summary>Comparaciones puras de CON-09 (sin base de datos): estado de un control y diferencias por documento.</summary>
public static class ComparadorAuxiliares
{
    /// <summary>Diferencia que se considera redondeo (centavo).</summary>
    public const decimal Tolerancia = 0.01m;

    public static EstadoConciliacion Estado(bool tieneCuenta, decimal? contable, decimal? auxiliar)
    {
        if (!tieneCuenta) return EstadoConciliacion.SinCuenta;
        if (contable is null || auxiliar is null) return EstadoConciliacion.NoComparable;
        return Math.Abs(contable.Value - auxiliar.Value) <= Tolerancia ? EstadoConciliacion.Cuadra : EstadoConciliacion.Diferencia;
    }

    public static string Etiqueta(EstadoConciliacion e) => e switch
    {
        EstadoConciliacion.Cuadra => "Cuadra",
        EstadoConciliacion.Diferencia => "Diferencia",
        EstadoConciliacion.SinCuenta => "Sin cuenta configurada",
        _ => "No comparable",
    };

    public static string Etiqueta(ControlConciliacion c) => c switch
    {
        ControlConciliacion.Clientes => "Clientes",
        ControlConciliacion.Proveedores => "Proveedores",
        _ => "Bancos",
    };

    public static string Etiqueta(TipoDiferenciaAuxiliar t) => t switch
    {
        TipoDiferenciaAuxiliar.Proveedor => "Saldo del proveedor distinto",
        TipoDiferenciaAuxiliar.DocumentoSinPoliza => "Documento sin póliza",
        TipoDiferenciaAuxiliar.PartidaSinAuxiliar => "Movimiento sin auxiliar en la cuenta de control",
        _ => "Cuenta bancaria con diferencia",
    };

    /// <summary>Saldo contable de una cuenta acreedora (proveedores): abonos − cargos.</summary>
    public static decimal SaldoAcreedor(decimal cargoMenosAbono) => -cargoMenosAbono;

    /// <summary>
    /// Proveedores con saldo contable distinto del auxiliar. <paramref name="contablePorProveedor"/> = saldo acreedor de las cuentas
    /// de control por proveedor; <paramref name="auxiliarPorProveedor"/> = saldo de CxP en MXN. Solo devuelve los que difieren.
    /// </summary>
    public static IReadOnlyList<(Guid ProveedorId, decimal Contable, decimal Auxiliar)> DiferenciasPorTercero(
        IReadOnlyDictionary<Guid, decimal> contablePorProveedor, IReadOnlyDictionary<Guid, decimal> auxiliarPorProveedor) =>
        contablePorProveedor.Keys.Union(auxiliarPorProveedor.Keys)
            .Select(p => (p, contablePorProveedor.GetValueOrDefault(p), auxiliarPorProveedor.GetValueOrDefault(p)))
            .Where(x => Math.Abs(x.Item2 - x.Item3) > Tolerancia)
            .OrderByDescending(x => Math.Abs(x.Item2 - x.Item3))
            .ToList();

    /// <summary>
    /// Causa de un documento con saldo en el auxiliar que no tiene póliza al corte: el rechazo pendiente del motor si lo hay, o si su
    /// póliza quedó después del corte, o que el evento no generó póliza.
    /// </summary>
    public static string CausaSinPoliza(string? rechazoPendiente, DateOnly? fechaPolizaPosterior) =>
        rechazoPendiente is not null ? $"El evento no generó póliza: {rechazoPendiente}"
        : fechaPolizaPosterior is { } f ? $"La póliza tiene fecha posterior al corte ({f:yyyy-MM-dd})"
        : "El evento no generó póliza (sin rechazo pendiente en el motor)";

    /// <summary>Saldo de cartera de una factura de cliente al corte: total menos los movimientos vigentes al corte.</summary>
    public static decimal SaldoCarteraAlCorte(decimal total, IEnumerable<(decimal Importe, DateTimeOffset Fecha, bool Revertido, DateTimeOffset? RevertidoEn)> movimientos,
        DateTimeOffset finExclusivo) =>
        total - movimientos.Where(m => m.Fecha < finExclusivo && (!m.Revertido || (m.RevertidoEn is { } r && r >= finExclusivo)))
            .Sum(m => m.Importe);

    /// <summary>Saldo en libros de una cuenta bancaria al corte; null si el corte es anterior al corte de su saldo inicial.</summary>
    public static decimal? SaldoLibrosAlCorte(decimal? saldoInicial, DateOnly? fechaCorteSaldoInicial, DateOnly corte, decimal netoMovimientosAlCorte) =>
        fechaCorteSaldoInicial is { } f && f > corte ? null : (saldoInicial ?? 0m) + netoMovimientosAlCorte;
}
