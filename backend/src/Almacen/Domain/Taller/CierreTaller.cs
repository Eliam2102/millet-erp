using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Taller;

/// <summary>
/// Cierre de día del almacenista en el taller de una sucursal (PT5.10): hojas y retazos tomados, m² cortados, pedidos
/// servidos y cortadores. Tabla <c>almacen.cierres_taller</c>, uno por (empresa, sucursal, fecha); queda cerrado y solo
/// se corrige con el permiso de administrar almacenes. Los retazos se cuentan, no se valúan (F2-RET). Guarda la foto
/// de lo descargado por Serie 9000 ese día y el factor de optimización = m² descargados ÷ m² cortados.
/// </summary>
public sealed class CierreTaller : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid SucursalId { get; private set; }
    public DateOnly Fecha { get; private set; }
    public decimal HojasTomadas { get; private set; }
    public decimal RetazosTomados { get; private set; }
    public decimal M2Descargados { get; private set; }
    public decimal M2Cortados { get; private set; }
    public int PedidosServidos { get; private set; }
    public int PedidosSerie9000 { get; private set; }
    public int Cortadores { get; private set; }
    public decimal? FactorOptimizacion { get; private set; }
    public string? Notas { get; private set; }
    public DateTimeOffset? CorregidoAt { get; private set; }

    private CierreTaller() { }

    public CierreTaller(Guid sucursalId, DateOnly fecha, decimal hojasTomadas, decimal retazosTomados, decimal m2Descargados,
        decimal m2Cortados, int pedidosServidos, int pedidosSerie9000, int cortadores, string? notas) : base(Guid.CreateVersion7())
    {
        if (sucursalId == Guid.Empty)
            throw new BusinessRuleException("CIERRE_TALLER_SUCURSAL", "La sucursal es obligatoria.");
        SucursalId = sucursalId; Fecha = fecha;
        Capturar(hojasTomadas, retazosTomados, m2Descargados, m2Cortados, pedidosServidos, pedidosSerie9000, cortadores, notas);
    }

    /// <summary>Captura (o corrige) el cierre con lo descargado por Serie 9000 al momento; recalcula el factor.</summary>
    public void Capturar(decimal hojasTomadas, decimal retazosTomados, decimal m2Descargados, decimal m2Cortados, int pedidosServidos,
        int pedidosSerie9000, int cortadores, string? notas)
    {
        if (hojasTomadas < 0 || retazosTomados < 0 || m2Cortados < 0 || pedidosServidos < 0 || cortadores < 0)
            throw new BusinessRuleException("CIERRE_TALLER_NEGATIVO", "Las cantidades del cierre no pueden ser negativas.");
        HojasTomadas = hojasTomadas; RetazosTomados = retazosTomados; M2Descargados = m2Descargados; M2Cortados = m2Cortados;
        PedidosServidos = pedidosServidos; PedidosSerie9000 = pedidosSerie9000; Cortadores = cortadores;
        var texto = notas?.Trim();
        Notas = string.IsNullOrEmpty(texto) ? null : texto[..Math.Min(texto.Length, 500)];
        FactorOptimizacion = FactorOptimizacionTaller.Calcular(M2Descargados, M2Cortados);
    }

    public void MarcarCorregido() => CorregidoAt = DateTimeOffset.UtcNow;
}

/// <summary>Regla 4 de la ficha (Manual §4.4): factor = m² descargados por Serie 9000 ÷ m² vendidos o cortados del día.</summary>
public static class FactorOptimizacionTaller
{
    public const string Formula = "m² descargados por Serie 9000 ÷ m² cortados en el día";

    /// <summary>Sin m² cortados no hay factor (null): no se inventa un cero ni un infinito.</summary>
    public static decimal? Calcular(decimal m2Descargados, decimal m2Cortados) =>
        m2Cortados > 0 ? Math.Round(m2Descargados / m2Cortados, 4) : null;
}
