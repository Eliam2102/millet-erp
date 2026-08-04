using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.UnitTests.Domain;

/// <summary>
/// Tests F4-PR1 (OC) — métodos de compromiso/liberación de la
/// <see cref="Requisicion"/> con una OC activa.
/// </summary>
public class RequisicionCompromisoOcTests
{
    /// <summary>
    /// Crea una RQ y la lleva a Autorizada usando los métodos
    /// públicos del agregado.
    /// </summary>
    private static Requisicion NewRqAutorizada()
    {
        var rq = RequisicionTestsHelper.RequisicionEnBorrador();
        rq.AgregarLinea(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 5m,
            unidadMedida: "PZA",
            precioEstimado: Money.Mxn(100m));
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        rq.RegistrarAutorizacion(
            autorizacionId: Guid.CreateVersion7(),
            nivel: NivelAutorizacion.Nivel1,
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            requiereNivel: RequiereNivel.SoloN1);
        return rq;
    }

    [Fact]
    public void ComprometerEnOc_DesdeAutorizada_OK()
    {
        var rq = NewRqAutorizada();
        var ocId = Guid.CreateVersion7();

        rq.ComprometerEnOc(ocId);

        Assert.Equal(ocId, rq.ComprometidaEnOcId);
    }

    [Fact]
    public void ComprometerEnOc_DesdeBorrador_Lanza()
    {
        var rq = RequisicionTestsHelper.RequisicionEnBorrador();
        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.ComprometerEnOc(Guid.CreateVersion7()));
        Assert.Equal("RQ_COMPROMISO_ESTADO_INVALIDO", ex.Code);
    }

    /// <summary>
    /// ADR-0033 (2026-05-13): bajo el setting AutoGenerarOcAlAutorizar=false
    /// (default), el handler de Autorizar bifurca la RQ a EnSurtido sin
    /// generar OC. El comprador convierte manualmente; en ese punto la RQ
    /// está en EnSurtido y el agregado debe aceptar el compromiso.
    /// </summary>
    [Fact]
    public void ComprometerEnOc_DesdeEnSurtido_OK()
    {
        var rq = NewRqAutorizada();
        // Llevar la RQ a EnSurtido aplicando un cubrimiento con saldo de
        // compra > 0.
        rq.RegistrarCubrimiento(
            new[]
            {
                new CubrimientoLinea(
                    LineaId: rq.Lineas.Single().Id,
                    CantidadDeAlmacen: 0m,
                    CantidadDeCompra: 5m),
            },
            DateTimeOffset.UtcNow);
        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);

        var ocId = Guid.CreateVersion7();
        rq.ComprometerEnOc(ocId);

        Assert.Equal(ocId, rq.ComprometidaEnOcId);
    }

    [Fact]
    public void ComprometerEnOc_OcIdVacio_Lanza()
    {
        var rq = NewRqAutorizada();
        var ex = Assert.Throws<BusinessRuleException>(() => rq.ComprometerEnOc(Guid.Empty));
        Assert.Equal("RQ_COMPROMISO_OC_ID_VACIO", ex.Code);
    }

    [Fact]
    public void ComprometerEnOc_MismoOcId_Idempotente()
    {
        var rq = NewRqAutorizada();
        var ocId = Guid.CreateVersion7();
        rq.ComprometerEnOc(ocId);
        // Re-comprometer al mismo ocId no debe lanzar.
        rq.ComprometerEnOc(ocId);
        Assert.Equal(ocId, rq.ComprometidaEnOcId);
    }

    [Fact]
    public void ComprometerEnOc_OtroOcId_Lanza()
    {
        var rq = NewRqAutorizada();
        rq.ComprometerEnOc(Guid.CreateVersion7());
        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.ComprometerEnOc(Guid.CreateVersion7()));
        Assert.Equal("RQ_YA_COMPROMETIDA_EN_OTRA_OC", ex.Code);
    }

    [Fact]
    public void LiberarDeOc_Idempotente_SinComprometer_NoOp()
    {
        var rq = NewRqAutorizada();
        rq.LiberarDeOc();
        Assert.Null(rq.ComprometidaEnOcId);
    }

    [Fact]
    public void LiberarDeOc_TrasComprometer_LimpiaComprometida()
    {
        var rq = NewRqAutorizada();
        rq.ComprometerEnOc(Guid.CreateVersion7());

        rq.LiberarDeOc();

        Assert.Null(rq.ComprometidaEnOcId);
    }
}
