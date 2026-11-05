using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Oc.Events;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.AlmCompra;

/// <summary>86e3jvu83: cancelación de una OC ya mandada a Almacenes, condicionada a su acuse (Jorge 29-oct pt. C y 1-nov).</summary>
public sealed class CancelacionAlmacenesTests
{
    private static OrdenCompra OcAutorizada(decimal recibido = 0m)
    {
        var oc = new OrdenCompra(Guid.CreateVersion7(), Guid.CreateVersion7(), Folio.Parse("OC-MID2026-000001"), 2026, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            new DateOnly(2026, 11, 4), sinRequisicionPrevia: true, motivoSinRequisicion: "Test");
        oc.AgregarLineaManual(Guid.CreateVersion7(), Guid.CreateVersion7(), 10, "PZA", 100, Guid.CreateVersion7());
        typeof(OrdenCompra).GetProperty(nameof(OrdenCompra.Estado))!.SetValue(oc, EstadoOrdenCompra.Autorizada);
        var l = oc.Lineas.Single();
        typeof(LineaOrdenCompra).GetProperty(nameof(LineaOrdenCompra.CantidadRecibida))!.SetValue(l, recibido);
        typeof(LineaOrdenCompra).GetProperty(nameof(LineaOrdenCompra.RequisicionId))!.SetValue(l, Guid.CreateVersion7());
        return oc;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Solicitar_deja_la_OC_en_espera_sin_cancelar_y_nadie_la_resuelve_a_mano()
    {
        var oc = OcAutorizada();

        oc.SolicitarCancelacionAlmacenes(Guid.NewGuid(), Ahora, Guid.NewGuid(), "Cambio de proveedor");

        (oc.Estado, oc.CancelacionEsperaAlmacenes).Should().Be((EstadoOrdenCompra.CancelacionSolicitada, true));
        oc.MotivoCancelacionId.Should().BeNull();
        oc.Invoking(o => o.RechazarCancelacion(Guid.NewGuid(), Ahora, "no"))
            .Should().Throw<BusinessRuleException>().Where(e => e.Code == "OC_CANCELACION_ESPERA_ALMACENES");
    }

    [Fact]
    public void Acuse_PROCESADO_cancela_y_libera_lo_no_recibido_con_el_evento_de_siempre()
    {
        var oc = OcAutorizada();
        var usuario = Guid.NewGuid();
        oc.SolicitarCancelacionAlmacenes(usuario, Ahora, Guid.NewGuid(), "Cambio de proveedor");

        var r = oc.FinalizarCancelacionAlmacenes(Ahora.AddMinutes(1));

        (oc.Estado, oc.CancelacionEsperaAlmacenes, oc.MotivoCancelacion).Should().Be((EstadoOrdenCompra.Cancelada, false, "Cambio de proveedor"));
        r.EventoCancelada.Should().Match<OrdenCompraCanceladaEvent>(e => e.OrdenCompraId == oc.Id && e.EstadoPrevio == EstadoOrdenCompra.Autorizada && e.UsuarioCanceladorId == usuario);
        r.LiberacionesParciales.Single().CantidadLiberada.Should().Be(10m);
    }

    [Fact]
    public void Acuse_ERROR_revierte_al_estado_previo_y_deja_la_respuesta_para_que_se_vea_y_se_pueda_volver_a_pedir()
    {
        var oc = OcAutorizada();
        oc.SolicitarCancelacionAlmacenes(Guid.NewGuid(), Ahora, Guid.NewGuid(), "Cambio de proveedor");

        oc.RevertirCancelacionAlmacenes(Ahora.AddMinutes(1), "ya tiene entrada en Almacenes; lo sobrante sale como salida manual");

        (oc.Estado, oc.CancelacionEsperaAlmacenes).Should().Be((EstadoOrdenCompra.Autorizada, false));
        var s = oc.SolicitudesCancelacion.Single();
        (s.Confirmada, s.EsperaAlmacenes, s.FechaResolucion is not null).Should().Be((false, false, true));
        s.RespuestaAlmacenes.Should().Contain("salida manual");
        // Resuelta: ya no estorba otra solicitud (el índice único solo cuenta las pendientes).
        oc.Invoking(o => o.SolicitarCancelacionAlmacenes(Guid.NewGuid(), Ahora.AddHours(1), Guid.NewGuid(), "Otra vez")).Should().NotThrow();
    }

    [Fact]
    public void Finalizar_o_revertir_sin_solicitud_en_espera_es_error()
    {
        var oc = OcAutorizada();
        oc.Invoking(o => o.FinalizarCancelacionAlmacenes(Ahora)).Should().Throw<BusinessRuleException>().Where(e => e.Code == "OC_CANCELACION_NO_ESPERA_ALMACENES");
        oc.Invoking(o => o.RevertirCancelacionAlmacenes(Ahora, "x")).Should().Throw<BusinessRuleException>().Where(e => e.Code == "OC_CANCELACION_NO_ESPERA_ALMACENES");
    }

    [Fact]
    public void Con_material_recibido_la_segunda_firma_espera_a_Almacenes_y_al_aceptar_libera_solo_el_faltante()
    {
        var oc = OcAutorizada(recibido: 4m);
        var primera = Guid.NewGuid();
        oc.SolicitarCancelacionConRecepciones(primera, Ahora, Guid.NewGuid(), "Proveedor no surte el resto");

        oc.ConfirmarCancelacionConRecepcionesEsperaAlmacenes(Guid.NewGuid(), Ahora.AddMinutes(1), "De acuerdo");

        (oc.Estado, oc.CancelacionEsperaAlmacenes, oc.CancelacionAlmacenesEsDeDobleFirma).Should().Be((EstadoOrdenCompra.CancelacionSolicitada, true, true));
        var r = oc.FinalizarCancelacionAlmacenes(Ahora.AddMinutes(2));
        oc.Estado.Should().Be(EstadoOrdenCompra.Cancelada);
        r.LiberacionesParciales.Single().CantidadLiberada.Should().Be(6m);
    }
}
