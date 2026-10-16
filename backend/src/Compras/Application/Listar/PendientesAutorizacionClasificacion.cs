namespace Millet.Compras.Application.Listar;

/// <summary>
/// Reglas puras de clasificación de la bandeja de pendientes (S2). Sin I/O,
/// para poder probarlas en unitarias.
/// </summary>
public static class PendientesAutorizacionClasificacion
{
    /// <summary>Ventana (días) en la que se cuentan las RQ del requisitante.</summary>
    public const int VentanaRecurrenciaDias = 60;

    /// <summary>Mínimo de RQ en la ventana para marcar al requisitante como recurrente.</summary>
    public const int MinimoRecurrencia = 3;

    /// <summary>
    /// Clasifica la disponibilidad a partir del reparto estimado por línea
    /// (<c>Cantidad</c> solicitada y <c>DeAlmacen</c> cubierta por stock).
    /// </summary>
    public static DisponibilidadPendiente Disponibilidad(IReadOnlyList<(decimal Cantidad, decimal DeAlmacen)> lineas)
    {
        if (lineas.Count == 0)
        {
            return DisponibilidadPendiente.SinDato;
        }

        var deAlmacen = lineas.Sum(l => l.DeAlmacen);
        if (deAlmacen <= 0m)
        {
            return DisponibilidadPendiente.Nula;
        }

        return deAlmacen >= lineas.Sum(l => l.Cantidad)
            ? DisponibilidadPendiente.Total
            : DisponibilidadPendiente.Parcial;
    }

    /// <summary>Recurrente si el conteo de RQ en la ventana llega al mínimo.</summary>
    public static bool EsRecurrente(int rqsEnVentana) => rqsEnVentana >= MinimoRecurrencia;
}
