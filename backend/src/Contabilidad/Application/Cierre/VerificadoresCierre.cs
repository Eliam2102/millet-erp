using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Iva;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Application.Revaluacion;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Contabilidad.Application.Cierre;

/// <summary>Resultado de verificar un paso: Cumple, Pendiente (con qué falta) o NoAplica (no hubo nada que hacer en el mes).</summary>
public enum EstadoVerificacionPaso { Cumple = 0, Pendiente = 1, NoAplica = 2 }

public sealed record ResultadoVerificacionPaso(EstadoVerificacionPaso Estado, string Resumen, IReadOnlyList<string> Detalle)
{
    public static ResultadoVerificacionPaso Cumple(string resumen) => new(EstadoVerificacionPaso.Cumple, resumen, []);
    public static ResultadoVerificacionPaso NoAplica(string resumen) => new(EstadoVerificacionPaso.NoAplica, resumen, []);
    public static ResultadoVerificacionPaso Pendiente(string resumen, IEnumerable<string>? detalle = null) =>
        new(EstadoVerificacionPaso.Pendiente, resumen, detalle is null ? [] : [.. detalle.Take(MaximoDetalle)]);

    /// <summary>Tope de renglones de detalle por paso (el resto se ve en la pantalla del módulo dueño).</summary>
    public const int MaximoDetalle = 20;
}

/// <summary>
/// Periodo que se verifica. <see cref="Compartido{T}"/> evita recalcular lo mismo para varios pasos en la misma evaluación (la
/// liquidación de IVA alimenta REP, ingresos, pasivos e IVA).
/// </summary>
public sealed class ContextoVerificacionCierre(Guid periodoId, int anio, int numero, DateOnly desde, DateOnly hasta)
{
    private readonly Dictionary<string, object> _cache = [];

    public Guid PeriodoId { get; } = periodoId;
    public int Anio { get; } = anio;
    public int Numero { get; } = numero;
    public DateOnly Desde { get; } = desde;
    public DateOnly Hasta { get; } = hasta;
    public string Clave => PeriodoContable.Etiqueta(Anio, Numero);

    public async Task<T> Compartido<T>(string clave, Func<Task<T>> crear) where T : notnull
    {
        if (_cache.TryGetValue(clave, out var v)) return (T)v;
        var nuevo = await crear();
        _cache[clave] = nuevo;
        return nuevo;
    }
}

/// <summary>
/// Un paso automático del checklist (CON-12). La clave une la configuración guardada con el verificador; el nombre, la descripción,
/// el orden y si es obligatorio son los valores con los que nace el paso (después los edita Contabilidad).
/// </summary>
public interface IVerificadorPasoCierre
{
    string Clave { get; }
    string Nombre { get; }
    string Descripcion { get; }
    int Orden { get; }
    AlcancePasoCierre Alcance => AlcancePasoCierre.Mensual;
    /// <summary>Pantalla donde se atiende lo pendiente (ruta del frontend), o null si no hay.</summary>
    string? Ruta { get; }
    Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct);
}

/// <summary>Pasos con los que nace el checklist (P10.4 sobre R17) mientras Millet no mande el suyo (V13). Las claves no cambian.</summary>
public static class PasosCierre
{
    public const string ConciliacionBancaria = "CONCILIACION_BANCARIA";
    public const string RepConciliados = "REP_CONCILIADOS";
    public const string IngresosVsCfdi = "INGRESOS_VS_CFDI";
    public const string NominaVsCfdi = "NOMINA_VS_CFDI";
    public const string PasivosVsCfdi = "PASIVOS_VS_CFDI";
    public const string PolizasAutomaticas = "POLIZAS_AUTOMATICAS";
    public const string PolizasBorrador = "POLIZAS_BORRADOR";
    public const string InventarioCerrado = "INVENTARIO_CERRADO";
    public const string Depreciacion = "DEPRECIACION";
    public const string RevaluacionBancosClientes = "REVALUACION_BANCOS_CLIENTES";
    public const string RevaluacionProveedores = "REVALUACION_PROVEEDORES";
    public const string IvaLiquidado = "IVA_LIQUIDADO";
    public const string Auxiliares = "AUXILIARES";
    public const string BalanzaCuadrada = "BALANZA_CUADRADA";
    public const string CierreAnual = "CIERRE_ANUAL";

    /// <summary>
    /// Pasos manuales con los que nace el checklist: no hay en el ERP de dónde verificarlos todavía. Nómina: la nómina es externa
    /// (K10.11) y su CFDI no llega al ERP; se confirma a mano hasta que exista la importación.
    /// </summary>
    public static readonly IReadOnlyList<(string Clave, string Nombre, string Descripcion, int Orden)> Manuales =
    [
        (NominaVsCfdi, "Nóminas contra CFDI emitidos",
            "La póliza de nómina del mes coincide con los CFDI de nómina timbrados (R17). Se confirma a mano: la nómina es externa (K10.11).", 40),
    ];

    internal static string Dinero(decimal v) => v.ToString("#,##0.00", CultureInfo.InvariantCulture);
}

// ─── Internos de Contabilidad ────────────────────────────────────────────────

/// <summary>R17: la balanza del periodo cuadra (cargos = abonos de las pólizas no borrador del periodo).</summary>
public sealed class VerificadorBalanzaCuadrada(ContabilidadDbContext db) : IVerificadorPasoCierre
{
    public string Clave => PasosCierre.BalanzaCuadrada;
    public string Nombre => "Balanza cuadrada";
    public string Descripcion => "Los cargos del periodo son iguales a los abonos (pólizas autorizadas y anuladas con su contrapartida).";
    public int Orden => 140;
    public string? Ruta => "/contabilidad/reportes/balanza";

    public async Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct)
    {
        var totales = await db.Polizas.AsNoTracking()
            .Where(p => p.PeriodoAnio == c.Anio && p.PeriodoNumero == c.Numero && p.Estado != EstadoPoliza.Borrador)
            .GroupBy(_ => 1).Select(g => new { Cargos = g.Sum(p => p.TotalCargos), Abonos = g.Sum(p => p.TotalAbonos), Polizas = g.Count() })
            .FirstOrDefaultAsync(ct);
        if (totales is null) return ResultadoVerificacionPaso.Cumple("Sin pólizas en el periodo: la balanza está en ceros.");
        var diferencia = totales.Cargos - totales.Abonos;
        return diferencia == 0m
            ? ResultadoVerificacionPaso.Cumple($"{totales.Polizas} pólizas; cargos y abonos por {PasosCierre.Dinero(totales.Cargos)}.")
            : ResultadoVerificacionPaso.Pendiente($"La balanza no cuadra por {PasosCierre.Dinero(diferencia)}.",
                [$"Cargos {PasosCierre.Dinero(totales.Cargos)} · abonos {PasosCierre.Dinero(totales.Abonos)}"]);
    }
}

/// <summary>Pólizas del periodo que siguen en borrador: no cuentan en la balanza hasta autorizarse o anularse.</summary>
public sealed class VerificadorPolizasBorrador(ContabilidadDbContext db) : IVerificadorPasoCierre
{
    public string Clave => PasosCierre.PolizasBorrador;
    public string Nombre => "Pólizas del periodo autorizadas";
    public string Descripcion => "No quedan pólizas en borrador con fecha del periodo.";
    public int Orden => 130;
    public string? Ruta => "/contabilidad/polizas";

    public async Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct)
    {
        var borradores = await db.Polizas.AsNoTracking()
            .Where(p => p.PeriodoAnio == c.Anio && p.PeriodoNumero == c.Numero && p.Estado == EstadoPoliza.Borrador)
            .OrderBy(p => p.Fecha).Select(p => new { p.TipoClave, p.Numero, p.Fecha, p.Concepto }).ToListAsync(ct);
        return borradores.Count == 0
            ? ResultadoVerificacionPaso.Cumple("Sin pólizas en borrador.")
            : ResultadoVerificacionPaso.Pendiente($"{borradores.Count} póliza(s) en borrador: autorícelas o anúlelas.",
                borradores.Select(p => $"{p.TipoClave} {p.Numero?.ToString(CultureInfo.InvariantCulture) ?? "s/n"} · {p.Fecha:dd/MM/yyyy} · {p.Concepto}"));
    }
}

/// <summary>
/// Eventos de los módulos que el motor no pudo convertir en póliza (bandeja de rechazos de C1.4). Supuesto a validar: bloquean los
/// rechazos pendientes registrados hasta el último día del periodo; los posteriores se avisan (pueden ser de este mes o del siguiente).
/// </summary>
public sealed class VerificadorPolizasAutomaticas(ContabilidadDbContext db) : IVerificadorPasoCierre
{
    public string Clave => PasosCierre.PolizasAutomaticas;
    public string Nombre => "Documentos con su póliza";
    public string Descripcion => "Ningún evento de los módulos se quedó sin póliza en la bandeja de rechazos del motor.";
    public int Orden => 60;
    public string? Ruta => "/contabilidad/motor";

    public async Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct)
    {
        var limite = FechaContable.InicioDelDiaUtc(c.Hasta.AddDays(1));
        var pendientes = await db.RechazosContables.AsNoTracking().Where(r => r.Estado == EstadoRechazoContable.Pendiente)
            .OrderBy(r => r.CreadoEn).Select(r => new { r.TipoEvento, r.Mensaje, r.CreadoEn }).ToListAsync(ct);
        var delPeriodo = pendientes.Where(r => r.CreadoEn < limite).ToList();
        var posteriores = pendientes.Count - delPeriodo.Count;
        if (delPeriodo.Count == 0)
            return ResultadoVerificacionPaso.Cumple(posteriores == 0 ? "Sin rechazos pendientes en el motor."
                : $"Sin rechazos del periodo; hay {posteriores} posterior(es) al cierre: revise que no sean de este mes.");
        return ResultadoVerificacionPaso.Pendiente($"{delPeriodo.Count} evento(s) sin póliza en la bandeja de rechazos.",
            delPeriodo.Select(r => $"{r.TipoEvento} · {r.Mensaje}"));
    }
}

/// <summary>Revaluación de cierre de bancos y clientes en moneda extranjera (CON-11). No aplica si no hay saldos en otra moneda.</summary>
public sealed class VerificadorRevaluacionBancosClientes(IMediator mediator) : IVerificadorPasoCierre
{
    public string Clave => PasosCierre.RevaluacionBancosClientes;
    public string Nombre => "Revaluación de bancos y clientes";
    public string Descripcion => "Se corrió la revaluación de cierre de los saldos en moneda extranjera de bancos y clientes (CON-11).";
    public int Orden => 90;
    public string? Ruta => null;

    public async Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct)
    {
        var r = await mediator.Send(new ConsultarRevaluacionCierreQuery(c.Anio, c.Numero), ct);
        if (r.Ejecutada) return ResultadoVerificacionPaso.Cumple($"Ejecutada: {r.Polizas.Count(p => !p.EsReversa)} póliza(s) al {r.FechaCierre:dd/MM/yyyy}.");
        if (r.Avisos.Count > 0) return ResultadoVerificacionPaso.Pendiente("No se pudo calcular la revaluación.", r.Avisos);
        var conDiferencia = r.Detalles.Where(d => d.Diferencia != 0m).ToList();
        return conDiferencia.Count == 0
            ? ResultadoVerificacionPaso.NoAplica("Sin saldos en moneda extranjera que revaluar.")
            : ResultadoVerificacionPaso.Pendiente($"Falta correr la revaluación: {conDiferencia.Count} saldo(s) con diferencia.",
                conDiferencia.Select(d => $"{d.Control} · {d.CuentaCodigo} · {d.Moneda} {PasosCierre.Dinero(d.SaldoMoneda)} · diferencia {PasosCierre.Dinero(d.Diferencia)}"));
    }
}

/// <summary>Auxiliares contra cuentas de control al último día del periodo (CON-09).</summary>
public sealed class VerificadorAuxiliares(IConciliacionAuxiliaresResumenReadPort conciliacion) : IVerificadorPasoCierre
{
    public string Clave => PasosCierre.Auxiliares;
    public string Nombre => "Auxiliares contra cuentas de control";
    public string Descripcion => "Clientes, proveedores y bancos cuadran contra sus cuentas de control al cierre (CON-09).";
    public int Orden => 120;
    public string? Ruta => "/contabilidad/reportes/conciliacion-auxiliares";

    public async Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct)
    {
        var r = await conciliacion.ObtenerAsync(c.Hasta, ct);
        return r.TodoCuadra
            ? ResultadoVerificacionPaso.Cumple($"Cuadran {string.Join(", ", r.Controles.Select(x => x.Control))} al {r.Corte:dd/MM/yyyy}.")
            : ResultadoVerificacionPaso.Pendiente("Hay auxiliares que no cuadran con su cuenta de control.",
                r.Controles.Where(x => !x.Cuadra).Select(x => $"{x.Control}: {x.Estado}{(x.Diferencia is { } d ? $" · diferencia {PasosCierre.Dinero(d)}" : "")}"));
    }
}

/// <summary>Lecturas de la liquidación de IVA del mes (K10.7) compartidas por REP, ingresos, pasivos e IVA.</summary>
internal static class LiquidacionDelMes
{
    public static Task<LiquidacionIvaResponse> ObtenerAsync(ContextoVerificacionCierre c, IMediator mediator, CancellationToken ct) =>
        c.Compartido("liquidacion-iva", () => mediator.Send(new LiquidacionIvaQuery(c.Anio, c.Numero), ct));

    public static string Documento(DocumentoLiquidacionIva d) =>
        $"{d.OrigenTipo} {d.Folio ?? d.DocumentoId.ToString()} · {d.Fecha:dd/MM/yyyy} · {d.Moneda} {PasosCierre.Dinero(d.Iva)} de IVA";
}

/// <summary>R17: cada pago o cobro PPD del mes tiene su complemento de pago (REP emitido o recibido).</summary>
public sealed class VerificadorRepConciliados(IMediator mediator) : IVerificadorPasoCierre
{
    public const string MotivoSinRep = "PPD pagado sin REP";
    public string Clave => PasosCierre.RepConciliados;
    public string Nombre => "REP conciliados";
    public string Descripcion => "Cada pago y cobro PPD del mes tiene su REP (emitido a clientes o recibido de proveedores).";
    public int Orden => 20;
    public string? Ruta => "/contabilidad/reportes/liquidacion-iva";

    public async Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct)
    {
        var l = await LiquidacionDelMes.ObtenerAsync(c, mediator, ct);
        var ppd = l.Documentos.Where(d => d.MetodoPago == "PPD").ToList();
        if (ppd.Count == 0) return ResultadoVerificacionPaso.NoAplica("Sin pagos ni cobros PPD en el mes.");
        var sinRep = ppd.Where(d => d.Motivos.Contains(MotivoSinRep)).ToList();
        return sinRep.Count == 0
            ? ResultadoVerificacionPaso.Cumple($"{ppd.Count} documento(s) PPD con su REP.")
            : ResultadoVerificacionPaso.Pendiente($"{sinRep.Count} documento(s) PPD pagados o cobrados sin REP.",
                sinRep.Select(d => $"{(d.Lado == LadoIva.Trasladado ? "Cobro sin REP emitido" : "Pago sin REP del proveedor")} · {LiquidacionDelMes.Documento(d)}"));
    }
}

/// <summary>R17: los ingresos (cobros y ventas con IVA del mes) tienen su CFDI emitido.</summary>
public sealed class VerificadorIngresosVsCfdi(IMediator mediator) : IVerificadorPasoCierre
{
    public string Clave => PasosCierre.IngresosVsCfdi;
    public string Nombre => "Ingresos contra CFDI emitidos";
    public string Descripcion => "Las ventas y anticipos cobrados en el mes tienen su CFDI timbrado.";
    public int Orden => 30;
    public string? Ruta => "/contabilidad/reportes/liquidacion-iva";

    public Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct) =>
        SinCfdi.VerificarAsync(c, mediator, LadoIva.Trasladado, "venta(s) o anticipo(s) cobrados sin CFDI", "Sin cobros con IVA en el mes.", ct);
}

/// <summary>R17: los pasivos (facturas de proveedor pagadas en el mes) tienen su CFDI recibido.</summary>
public sealed class VerificadorPasivosVsCfdi(IMediator mediator) : IVerificadorPasoCierre
{
    public string Clave => PasosCierre.PasivosVsCfdi;
    public string Nombre => "Pasivos contra CFDI recibidos";
    public string Descripcion => "Las facturas de proveedor pagadas en el mes tienen su CFDI recibido.";
    public int Orden => 50;
    public string? Ruta => "/contabilidad/reportes/liquidacion-iva";

    public Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct) =>
        SinCfdi.VerificarAsync(c, mediator, LadoIva.Acreditable, "factura(s) de proveedor pagadas sin CFDI", "Sin pagos con IVA en el mes.", ct);
}

internal static class SinCfdi
{
    public const string Motivo = "Sin CFDI";

    public static async Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, IMediator mediator, LadoIva lado,
        string pendiente, string sinMovimiento, CancellationToken ct)
    {
        var l = await LiquidacionDelMes.ObtenerAsync(c, mediator, ct);
        var docs = l.Documentos.Where(d => d.Lado == lado).ToList();
        if (docs.Count == 0) return ResultadoVerificacionPaso.NoAplica(sinMovimiento);
        var faltan = docs.Where(d => d.Motivos.Contains(Motivo)).ToList();
        return faltan.Count == 0
            ? ResultadoVerificacionPaso.Cumple($"{docs.Count} documento(s) con su CFDI.")
            : ResultadoVerificacionPaso.Pendiente($"{faltan.Count} {pendiente}.", faltan.Select(LiquidacionDelMes.Documento));
    }
}

/// <summary>K10.7: el IVA por documentos cuadra con las cuentas y la póliza de liquidación del mes está autorizada.</summary>
public sealed class VerificadorIvaLiquidado(IMediator mediator, ContabilidadDbContext db) : IVerificadorPasoCierre
{
    public string Clave => PasosCierre.IvaLiquidado;
    public string Nombre => "IVA del mes liquidado";
    public string Descripcion => "El IVA por documentos cuadra con sus cuentas y la póliza de liquidación del mes está autorizada (K10.7).";
    public int Orden => 110;
    public string? Ruta => "/contabilidad/reportes/liquidacion-iva";

    public async Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct)
    {
        var l = await LiquidacionDelMes.ObtenerAsync(c, mediator, ct);
        var r = l.Resumen;
        if (l.Documentos.Count == 0 && r.IvaTrasladadoCobrado == 0m && r.IvaAcreditablePagado == 0m && r.IvaRetenidoPorClientes == 0m)
            return ResultadoVerificacionPaso.NoAplica("Sin IVA cobrado ni pagado en el mes.");
        var faltas = new List<string>();
        faltas.AddRange(l.Cuadre.Where(x => !x.Cuadra).Select(x => $"{x.Descripcion}: documentos {PasosCierre.Dinero(x.PorDocumentos)} · cuentas {PasosCierre.Dinero(x.EnCuentas)} · diferencia {PasosCierre.Dinero(x.Diferencia)}"));
        var folio = $"IVA-{c.Anio:D4}-{c.Numero:D2}";
        var estados = await db.Polizas.AsNoTracking()
            .Where(p => p.OrigenModulo == LiquidacionIvaHandlers.DocLiquidacion.Modulo && p.OrigenTipo == LiquidacionIvaHandlers.DocLiquidacion.Tipo
                && p.OrigenFolio == folio && p.Estado != EstadoPoliza.Anulada)
            .Select(p => p.Estado).ToListAsync(ct);
        if (estados.Count == 0) faltas.Add($"Falta generar la póliza de liquidación {folio}.");
        else if (!estados.Contains(EstadoPoliza.Autorizada)) faltas.Add($"La póliza de liquidación {folio} sigue en borrador: autorícela.");
        return faltas.Count == 0
            ? ResultadoVerificacionPaso.Cumple($"IVA a cargo {PasosCierre.Dinero(r.IvaACargo)} · a favor {PasosCierre.Dinero(r.IvaAFavor)}; póliza {folio} autorizada.")
            : ResultadoVerificacionPaso.Pendiente("El IVA del mes no está liquidado.", faltas);
    }
}

// ─── Con datos de otros módulos (puertos de consumidor) ──────────────────────

/// <summary>R17: conciliación bancaria del mes cerrada en cada cuenta (Tesorería, T9.2).</summary>
public sealed class VerificadorConciliacionBancaria(IConciliacionBancariaCierreReadPort tesoreria) : IVerificadorPasoCierre
{
    public string Clave => PasosCierre.ConciliacionBancaria;
    public string Nombre => "Conciliación bancaria";
    public string Descripcion => "La conciliación del mes de cada cuenta bancaria está cerrada en Tesorería, sin movimientos por conciliar.";
    public int Orden => 10;
    public string? Ruta => "/tesoreria/conciliacion";

    public async Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct)
    {
        var pendientes = await tesoreria.PendientesAsync(c.Anio, c.Numero, ct);
        return pendientes.Count == 0
            ? ResultadoVerificacionPaso.Cumple("Todas las cuentas con movimientos están conciliadas.")
            : ResultadoVerificacionPaso.Pendiente($"{pendientes.Count} cuenta(s) sin conciliar.",
                pendientes.Select(p => $"{p.Cuenta}: {p.Motivo}{(p.MovimientosSinConciliar > 0 ? $" · {p.MovimientosSinConciliar} movimiento(s) sin conciliar" : "")}"));
    }
}

/// <summary>Inventario del mes cerrado en Almacén (su cierre es independiente: reabrir Contabilidad no lo reabre, D18).</summary>
public sealed class VerificadorInventarioCerrado(IInventarioCierreReadPort almacen) : IVerificadorPasoCierre
{
    public string Clave => PasosCierre.InventarioCerrado;
    public string Nombre => "Inventario cerrado";
    public string Descripcion => "Almacén cerró el mes (foto de existencias y costo).";
    public int Orden => 70;
    public string? Ruta => "/almacen/cierre-mes";

    public async Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct) =>
        await almacen.MesCerradoAsync(c.Anio, c.Numero, ct)
            ? ResultadoVerificacionPaso.Cumple("Almacén cerró el mes.")
            : ResultadoVerificacionPaso.Pendiente($"Almacén no ha cerrado {c.Clave}.");
}

/// <summary>Depreciación del mes aplicada en Activos Fijos (AF11.4).</summary>
public sealed class VerificadorDepreciacion(IDepreciacionCierreReadPort activos) : IVerificadorPasoCierre
{
    public string Clave => PasosCierre.Depreciacion;
    public string Nombre => "Depreciación del mes";
    public string Descripcion => "La corrida de depreciación del mes está aplicada en Activos Fijos.";
    public int Orden => 80;
    public string? Ruta => "/activos-fijos/depreciacion";

    public async Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct)
    {
        var e = await activos.EstadoAsync(c.Anio, c.Numero, ct);
        if (e.Ejecutada) return ResultadoVerificacionPaso.Cumple("Corrida del mes aplicada.");
        return e.HayActivosDepreciables
            ? ResultadoVerificacionPaso.Pendiente($"Falta correr la depreciación de {c.Clave}.")
            : ResultadoVerificacionPaso.NoAplica("Sin activos que depreciar en el mes.");
    }
}

/// <summary>Revaluación de cierre de proveedores en moneda extranjera (CxP, CXP-12).</summary>
public sealed class VerificadorRevaluacionProveedores(IRevaluacionProveedoresCierreReadPort cxp) : IVerificadorPasoCierre
{
    public string Clave => PasosCierre.RevaluacionProveedores;
    public string Nombre => "Revaluación de proveedores";
    public string Descripcion => "Se corrió en CxP la revaluación de cierre de las facturas de proveedor en moneda extranjera (CXP-12).";
    public int Orden => 100;
    public string? Ruta => "/cxp/revaluacion";

    public async Task<ResultadoVerificacionPaso> VerificarAsync(ContextoVerificacionCierre c, CancellationToken ct)
    {
        var e = await cxp.EstadoAsync(c.Anio, c.Numero, ct);
        if (e.Ejecutada) return ResultadoVerificacionPaso.Cumple("Revaluación del mes ejecutada en CxP.");
        return e.FacturasMonedaExtranjera == 0
            ? ResultadoVerificacionPaso.NoAplica("Sin facturas de proveedor en moneda extranjera con saldo al cierre.")
            : ResultadoVerificacionPaso.Pendiente($"Falta correr la revaluación en CxP: {e.FacturasMonedaExtranjera} factura(s) en moneda extranjera con saldo.");
    }
}
