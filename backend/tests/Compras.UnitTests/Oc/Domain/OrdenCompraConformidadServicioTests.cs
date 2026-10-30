using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>86e3tacbn: conformidad del servicio por OC (una por OC; solo el departamento solicitante).</summary>
public class OrdenCompraConformidadServicioTests
{
    private static readonly Guid DeptoSolicitante = Guid.CreateVersion7();
    private static readonly Guid Usuario = Guid.CreateVersion7();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static OrdenCompra OcAutorizada(bool conServicio = true)
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(), empresaId: Guid.CreateVersion7(), folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000030"),
            folioAnio: 2026, proveedorId: Guid.CreateVersion7(), sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(), usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(), encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(Ahora.UtcDateTime), sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test", usoCfdiClave: "G03", metodoPago: "PPD", formaPagoClave: "99");
        // Dos líneas de servicio de departamentos distintos + una física: una sola confirmación por OC.
        oc.AgregarLineaManual(Guid.CreateVersion7(), Guid.CreateVersion7(), 1m, "SRV", 500m, DeptoSolicitante, esServicio: conServicio);
        oc.AgregarLineaManual(Guid.CreateVersion7(), Guid.CreateVersion7(), 1m, "SRV", 300m, Guid.CreateVersion7(), esServicio: conServicio);
        oc.AgregarLineaManual(Guid.CreateVersion7(), Guid.CreateVersion7(), 5m, "PZA", 10m, Guid.CreateVersion7());
        oc.EnviarAAutorizacion(Ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), Ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Guid.CreateVersion7(), Ahora);
        return oc;
    }

    [Fact]
    public void Confirmar_registra_fecha_usuario_comentario_y_emite_evento()
    {
        var oc = OcAutorizada();

        var evento = oc.ConfirmarServicio(Usuario, DeptoSolicitante, "  Todo conforme  ", Ahora);

        oc.ServicioConfirmadoEn.Should().Be(Ahora);
        oc.ServicioConfirmadoPorId.Should().Be(Usuario);
        oc.ServicioConfirmadoComentario.Should().Be("Todo conforme");
        evento.Confirmado.Should().BeTrue();
        evento.OrdenCompraId.Should().Be(oc.Id);
    }

    [Fact]
    public void Confirmar_de_otro_departamento_se_rechaza_con_403()
    {
        var oc = OcAutorizada();

        var otro = () => oc.ConfirmarServicio(Usuario, Guid.CreateVersion7(), null, Ahora);
        var sinDepto = () => oc.ConfirmarServicio(Usuario, null, null, Ahora);

        otro.Should().Throw<ForbiddenException>();
        sinDepto.Should().Throw<ForbiddenException>();
        oc.ServicioConfirmadoEn.Should().BeNull();
    }

    [Fact]
    public void Confirmar_una_oc_sin_servicios_o_ya_confirmada_se_rechaza()
    {
        OcAutorizada(conServicio: false).Invoking(o => o.ConfirmarServicio(Usuario, DeptoSolicitante, null, Ahora))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("OC_SERVICIO_SIN_LINEAS_SERVICIO");

        var oc = OcAutorizada();
        oc.ConfirmarServicio(Usuario, DeptoSolicitante, null, Ahora);
        oc.Invoking(o => o.ConfirmarServicio(Usuario, DeptoSolicitante, null, Ahora))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("OC_SERVICIO_YA_CONFIRMADO");
    }

    [Fact]
    public void Deshacer_exige_motivo_y_limpia_la_confirmacion()
    {
        var oc = OcAutorizada();
        oc.ConfirmarServicio(Usuario, DeptoSolicitante, null, Ahora);

        oc.Invoking(o => o.RevocarConfirmacionServicio(Usuario, DeptoSolicitante, " ", Ahora))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("OC_SERVICIO_MOTIVO_REQUERIDO");
        var evento = oc.RevocarConfirmacionServicio(Usuario, DeptoSolicitante, "Faltó entregar el reporte", Ahora);

        oc.ServicioConfirmadoEn.Should().BeNull();
        oc.ServicioConfirmacionRevocadaMotivo.Should().Be("Faltó entregar el reporte");
        evento.Confirmado.Should().BeFalse();
    }

    [Fact]
    public void Deshacer_con_una_factura_pagada_se_rechaza()
    {
        var oc = OcAutorizada();
        oc.ConfirmarServicio(Usuario, DeptoSolicitante, null, Ahora);
        oc.RegistrarPago(100m, Ahora);

        oc.Invoking(o => o.RevocarConfirmacionServicio(Usuario, DeptoSolicitante, "Error", Ahora))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("OC_SERVICIO_CONFIRMACION_CON_PAGO");
        oc.ServicioConfirmadoEn.Should().NotBeNull();
    }

    [Fact]
    public void No_conforme_guarda_el_motivo_y_una_confirmacion_posterior_lo_limpia()
    {
        var oc = OcAutorizada();

        oc.Invoking(o => o.MarcarServicioNoConforme(Usuario, DeptoSolicitante, "", Ahora))
            .Should().Throw<BusinessRuleException>();
        oc.MarcarServicioNoConforme(Usuario, DeptoSolicitante, "No entregaron el informe", Ahora);
        oc.ServicioNoConformeMotivo.Should().Be("No entregaron el informe");

        oc.ConfirmarServicio(Usuario, DeptoSolicitante, null, Ahora);
        oc.ServicioNoConformeEn.Should().BeNull();
        oc.Invoking(o => o.MarcarServicioNoConforme(Usuario, DeptoSolicitante, "otra", Ahora))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("OC_SERVICIO_YA_CONFIRMADO");
    }
}
