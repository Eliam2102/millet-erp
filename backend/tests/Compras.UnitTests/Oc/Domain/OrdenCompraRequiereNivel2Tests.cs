using Millet.Compras.Application.Oc;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Matriz de nivel de OC (menor cuantía): transición N1 directo a Autorizada
/// cuando <c>RequiereNivel2 = false</c>, y bloqueo de N2 en ese caso.
/// </summary>
public class OrdenCompraRequiereNivel2Tests
{
    private static OrdenCompra NewOcConLineaBorrador()
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000010"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test menor cuantía");
        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 1m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7());
        return oc;
    }

    [Fact]
    public void RequiereNivel2_PorDefecto_ConservaFlujoN1yN2()
    {
        var oc = NewOcConLineaBorrador();
        Assert.True(oc.RequiereNivel2);

        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        var resultado = oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        Assert.Equal(EstadoOrdenCompra.EnAutorizacionDireccion, oc.Estado);
        Assert.Null(resultado.OrdenCompraAutorizada);
    }

    [Fact]
    public void MenorCuantia_N1AutorizaDirecto_ConFechaContabilizacionYEvento()
    {
        var oc = NewOcConLineaBorrador();
        var ahora = DateTimeOffset.UtcNow;
        oc.EnviarAAutorizacion(ahora, requiereNivel2: false);
        Assert.False(oc.RequiereNivel2);

        var resultado = oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), ahora);

        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
        Assert.Equal(ahora, oc.FechaContabilizacion);
        Assert.NotNull(resultado.OrdenCompraAutorizada);
    }

    [Fact]
    public void MenorCuantia_N2_Lanza()
    {
        var oc = NewOcConLineaBorrador();
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow, requiereNivel2: false);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Guid.CreateVersion7(), DateTimeOffset.UtcNow));
        Assert.Equal("OC_AUTORIZAR_NIVEL2_NO_REQUERIDO", ex.Code);
    }

    [Fact]
    public void MontoMxn_MismaMoneda_RegresaTotal_ExtranjeraConvierte_SinTipoCambio_EsNull()
    {
        Assert.Equal(1500m, EvaluadorNivelOc.MontoMxn(1500m, "MXN", null));
        Assert.Equal(2000m, EvaluadorNivelOc.MontoMxn(100m, "USD", 20m));
        Assert.Null(EvaluadorNivelOc.MontoMxn(100m, "USD", null));
        Assert.Null(EvaluadorNivelOc.MontoMxn(100m, "USD", 0m));
    }
}
