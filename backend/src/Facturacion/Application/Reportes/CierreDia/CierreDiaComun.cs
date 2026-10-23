using System.Globalization;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Reportes.CierreDia;

/// <summary>
/// Piezas comunes del cierre del día por sucursal (F6.7; Manual de Sucursales
/// §2.4): monitor de estatus de CFDI y conciliación facturado contra ingresado.
/// El día se lee en la zona horaria de la sucursal (`[Decisión 12-8]`), igual
/// que el día de operación de las sesiones de caja.
/// </summary>
public static class CierreDia
{
    /// <summary>Bypass de sucursal (ADR-0051): corporativos ven el cierre de todas las sucursales.</summary>
    public const string PermisoTodasSucursales = "facturacion.reportes.leer-todas-sucursales";

    /// <summary>Timbrado y aún no cancelado ante el SAT: cuenta como facturado.</summary>
    public static readonly EstadoTimbrado[] EstadosVigentes =
        [EstadoTimbrado.Timbrado, EstadoTimbrado.CancelacionPendiente];

    /// <summary>Se intentó emitir y no tiene timbre (se lista, no suma).</summary>
    public static readonly EstadoTimbrado[] EstadosSinTimbrar =
    [
        EstadoTimbrado.Borrador, EstadoTimbrado.PendientePedimento,
        EstadoTimbrado.TimbradoEnProceso, EstadoTimbrado.TimbradoFallido,
    ];

    /// <summary>Formas de pago que pasan por la terminal bancaria (TPV).</summary>
    public static readonly HashSet<string> FormasTpv = new(StringComparer.Ordinal) { "04", "28", "29" };

    /// <summary>Del inicio (medianoche local) al fin del día, en UTC (Npgsql exige offset 0).</summary>
    public static (DateTimeOffset Desde, DateTimeOffset Hasta) Ventana(DateOnly dia, string? zona) =>
        (DiaOperacion.FinDelDia(dia.AddDays(-1), zona), DiaOperacion.FinDelDia(dia, zona));

    /// <summary>
    /// Defensa en el handler: el behavior de alcance (ADR-0051) ya validó la
    /// sucursal seleccionada; si llega una lista sin ella, no se responde.
    /// </summary>
    public static void ExigirSucursal(IReadOnlyList<Guid>? permitidas, Guid sucursalId)
    {
        if (permitidas is not null && !permitidas.Contains(sucursalId))
            throw new ForbiddenException(
                "SUCURSAL_FUERA_DE_ALCANCE",
                "No estás asociado a esta sucursal ni tienes permiso para consultar todas.");
    }

    public static string TipoDocumento(Comprobante c) => c switch
    {
        FacturaAnticipo => "Anticipo",
        NotaCredito => "Nota de crédito",
        _ when c.Tipo == TipoComprobante.Pago => "Complemento de pago",
        _ when c.Tipo == TipoComprobante.Traslado => "Carta Porte",
        _ => "Factura",
    };

    /// <summary>Estado del monitor (Manual §2.4: timbradas, canceladas con timbre, sin timbrar).</summary>
    public static string EstadoMonitor(EstadoTimbrado estado) => estado switch
    {
        EstadoTimbrado.Timbrado => "Timbrado",
        EstadoTimbrado.CancelacionPendiente => "Cancelación en trámite",
        EstadoTimbrado.Cancelado => "Cancelado",
        EstadoTimbrado.TimbradoFallido => "Fallido",
        _ => "Sin timbrar",
    };

    public static string Moneda(decimal importe) =>
        importe.ToString("C2", CultureInfo.GetCultureInfo("es-MX"));
}
