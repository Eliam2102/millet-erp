using Millet.Compras.Application.Listar;

namespace Millet.Compras.UnitTests.Application;

/// <summary>
/// Reglas puras de la bandeja de pendientes (S2), sin DB ni puertos:
/// <list type="bullet">
///   <item>disponibilidad Total / Parcial / Nula / SinDato según el reparto por línea;</item>
///   <item>recurrencia a partir de 3 RQ en la ventana;</item>
///   <item>menor cuantía solo con umbral configurado y monto ≤ umbral.</item>
/// </list>
/// </summary>
public class PendientesAutorizacionClasificacionTests
{
    [Fact]
    public void Disponibilidad_SinLineas_EsSinDato()
    {
        Assert.Equal(DisponibilidadPendiente.SinDato,
            PendientesAutorizacionClasificacion.Disponibilidad(Array.Empty<(decimal, decimal)>()));
    }

    [Fact]
    public void Disponibilidad_TodasCubiertasDeAlmacen_EsTotal()
    {
        var lineas = new[] { (10m, 10m), (5m, 5m) };

        Assert.Equal(DisponibilidadPendiente.Total,
            PendientesAutorizacionClasificacion.Disponibilidad(lineas));
    }

    [Fact]
    public void Disponibilidad_AlgunaCubiertaDeAlmacen_EsParcial()
    {
        var lineas = new[] { (10m, 10m), (5m, 0m) };

        Assert.Equal(DisponibilidadPendiente.Parcial,
            PendientesAutorizacionClasificacion.Disponibilidad(lineas));
    }

    [Fact]
    public void Disponibilidad_NingunaCubiertaDeAlmacen_EsNula()
    {
        var lineas = new[] { (10m, 0m), (5m, 0m) };

        Assert.Equal(DisponibilidadPendiente.Nula,
            PendientesAutorizacionClasificacion.Disponibilidad(lineas));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(7, true)]
    public void EsRecurrente_UmbralDeTresRq(int rqsEnVentana, bool esperado)
    {
        Assert.Equal(esperado, PendientesAutorizacionClasificacion.EsRecurrente(rqsEnVentana));
    }
}
