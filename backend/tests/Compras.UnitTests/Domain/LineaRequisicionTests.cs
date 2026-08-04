using Millet.Compras.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.UnitTests.Domain;

public class LineaRequisicionTests
{
    private static LineaRequisicion CrearValida(
        decimal cantidad = 10m,
        string unidadMedida = "PZA",
        Money? precio = null,
        string? notas = null)
    {
        // Las líneas se crean SIEMPRE vía Requisicion.AgregarLinea, pero
        // exponemos el ctor internal a través del agregado para los tests.
        // Aquí construimos el agregado y agregamos una línea para luego
        // testear los métodos de la línea expuestos via Requisicion.
        var rq = RequisicionTestsHelper.RequisicionEnBorrador();
        return rq.AgregarLinea(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: cantidad,
            unidadMedida: unidadMedida,
            precioEstimado: precio ?? Money.Mxn(15.50m),
            notas: notas);
    }

    [Fact]
    public void Should_Create_WithCubrimientoInicialEnCero()
    {
        var linea = CrearValida();

        Assert.Equal(0m, linea.CantidadDeAlmacen);
        Assert.Equal(0m, linea.CantidadDeCompra);
        Assert.Equal(0m, linea.CantidadRecibida);
        Assert.False(linea.Cubrimiento.TieneCubrimiento);
        Assert.Equal(linea.Cantidad, linea.Cubrimiento.CantidadPendiente);
    }

    [Fact]
    public void Should_AssignAllFields()
    {
        var linea = CrearValida(cantidad: 25.5m, unidadMedida: "KG", notas: "Urgente");

        Assert.Equal(25.5m, linea.Cantidad);
        Assert.Equal("KG", linea.UnidadMedida);
        Assert.Equal("Urgente", linea.Notas);
        Assert.Equal("MXN", linea.PrecioEstimado.Currency);
    }

    [Fact]
    public void Cubrimiento_Should_Be_Computed_From_Cantidades()
    {
        var linea = CrearValida(cantidad: 10m);

        var cub = linea.Cubrimiento;

        Assert.Equal(10m, cub.CantidadOriginal);
        Assert.Equal(linea.CantidadDeAlmacen, cub.CantidadDeAlmacen);
        Assert.Equal(linea.CantidadDeCompra, cub.CantidadDeCompra);
        Assert.Equal(linea.CantidadRecibida, cub.CantidadRecibida);
    }
}

/// <summary>
/// Helper compartido entre tests para construir agregados Requisicion
/// listos para usar.
/// </summary>
internal static class RequisicionTestsHelper
{
    public static Requisicion RequisicionEnBorrador()
    {
        return new Requisicion(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Folio.Parse("MID2026-000001"),
            folioAnio: 2026,
            clasificacion: Clasificacion.MateriaPrima,
            sucursalId: Guid.CreateVersion7(),
            departamentoId: Guid.CreateVersion7(),
            almacenDestinoId: Guid.CreateVersion7(),
            requisitanteId: Guid.CreateVersion7(),
            creadorId: Guid.CreateVersion7(),
            prioridad: Prioridad.Normal,
            fechaSolicitud: DateTimeOffset.UtcNow);
    }
}
