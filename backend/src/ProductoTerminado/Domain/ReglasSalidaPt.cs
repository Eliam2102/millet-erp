using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.Domain;

/// <summary>
/// Reglas de la hoja de salida confirmadas por Millet (Guadalupe Polanco, 9-nov-2026):
/// 1) no se emite si un pedido no tiene factura timbrada del mismo día o anterior; 2) excepción: Dirección de Administración, y en su
/// ausencia Planeación o Contraloría (con motivo de suplencia); 3) exportación: la factura en PendientePedimento vale (el pedimento aún no existe
/// al 71); 4) salida parcial: la autoriza Crédito y Cobranza, en su ausencia Gerencia de Ventas; 5) liquidación del chofer: sucursal hasta el
/// siguiente día hábil (lunes a sábado) a las 13:00, Conkal hasta 2 días; alerta a las 12 horas hábiles; las cajas cierran a las 18:00 locales.
/// </summary>
public static class ReglasSalidaPt
{
    public const int HoraLimiteLiquidacion = 13;
    public const int HoraCierreCaja = 18;
    public const int HorasAlertaLiquidacion = 12;

    /// <summary>Niveles en orden de suplencia (igual que CE): los que no son el primero de su regla piden por qué no autorizó el anterior.</summary>
    public static (bool Parcial, bool Suplencia)? Nivel(string? nivel) => nivel switch
    {
        "administracion" => (false, false),
        "planeacion" => (false, true),
        "contraloria" => (false, true),
        "credito" => (true, false),
        "ventas" => (true, true),
        _ => null,
    };

    public static string MotivoValido(string? motivo)
    {
        var limpio = motivo?.Trim();
        if (limpio is null || limpio.Length is < 5 or > 500)
            throw new BusinessRuleException("PT_MOTIVO_INVALIDO", "El motivo debe tener entre 5 y 500 caracteres.");
        return limpio;
    }

    /// <summary>Regla 1 y 3: alguna factura timbrada con fecha (día local) de hoy o anterior; en exportación también una en PendientePedimento.</summary>
    public static bool TieneFactura(IEnumerable<(bool PendientePedimento, bool Exportacion, DateOnly? FechaTimbrado)> facturas, DateOnly hoy) =>
        facturas.Any(f => f.PendientePedimento ? f.Exportacion : f.FechaTimbrado <= hoy);

    /// <summary>Regla 4: alguna posición sale con menos piezas de las que faltan por entregar (pedidas menos las que ya salieron antes).</summary>
    public static bool EsParcial(IEnumerable<(int Esperadas, int Cargadas, int YaSalieron)> posiciones) =>
        posiciones.Any(p => p.Cargadas + p.YaSalieron < p.Esperadas);

    /// <summary>Lunes a sábado sin festivo (Millet liquida en sábado; el calendario compartido cuenta lunes a viernes).</summary>
    public static bool DiaHabil(DateOnly dia, IReadOnlySet<DateOnly> festivos) => dia.DayOfWeek != DayOfWeek.Sunday && !festivos.Contains(dia);

    /// <summary>
    /// Regla 5: límite para liquidar la hoja, a las 13:00 de la sucursal, <paramref name="diasHabiles"/> días hábiles después del día de la salida
    /// (1 en sucursal, 2 en Conkal). Lo emitido después del cierre de caja (18:00 locales) cuenta como del día siguiente.
    /// </summary>
    public static DateTimeOffset LimiteLiquidacion(DateTimeOffset emitida, TimeZoneInfo zona, int diasHabiles, IReadOnlySet<DateOnly> festivos)
    {
        var local = TimeZoneInfo.ConvertTime(emitida, zona);
        var dia = DateOnly.FromDateTime(local.DateTime);
        if (local.Hour >= HoraCierreCaja) dia = dia.AddDays(1);
        for (var n = 0; n < diasHabiles;)
        {
            dia = dia.AddDays(1);
            if (DiaHabil(dia, festivos)) n++;
        }
        return AUtc(dia.ToDateTime(new TimeOnly(HoraLimiteLiquidacion, 0)), zona);
    }

    /// <summary>Regla 5: alerta a las 12 horas hábiles de la salida sin liquidar (horas de días hábiles en la zona de la sucursal).</summary>
    public static DateTimeOffset AlertaLiquidacion(DateTimeOffset emitida, TimeZoneInfo zona, IReadOnlySet<DateOnly> festivos)
    {
        var cursor = TimeZoneInfo.ConvertTime(emitida, zona).DateTime;
        var restante = TimeSpan.FromHours(HorasAlertaLiquidacion);
        while (restante > TimeSpan.Zero)
        {
            var finDia = cursor.Date.AddDays(1);
            if (DiaHabil(DateOnly.FromDateTime(cursor), festivos))
            {
                var tramo = finDia - cursor < restante ? finDia - cursor : restante;
                restante -= tramo;
                cursor += tramo;
            }
            else cursor = finDia;
        }
        return AUtc(cursor, zona);
    }

    /// <summary>EnPlazo, Alerta (pasaron 12 horas hábiles) o Vencida (pasó el límite).</summary>
    public static string EstadoPlazo(DateTimeOffset ahora, DateTimeOffset alerta, DateTimeOffset limite) =>
        ahora >= limite ? "Vencida" : ahora >= alerta ? "Alerta" : "EnPlazo";

    private static DateTimeOffset AUtc(DateTime local, TimeZoneInfo zona)
    {
        var sinZona = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(sinZona, zona.GetUtcOffset(sinZona)).ToUniversalTime();
    }
}
