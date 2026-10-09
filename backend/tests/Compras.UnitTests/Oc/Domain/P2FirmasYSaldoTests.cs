using Millet.Compras.Application.Oc;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

public sealed class P2FirmasYSaldoTests
{
    internal static OrdenCompra Crear(bool heredada = false)
    {
        var oc = new OrdenCompra(Guid.NewGuid(), Guid.NewGuid(),
            Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000987"), 2026,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 9),
            sinRequisicionPrevia: !heredada, motivoSinRequisicion: heredada ? null : "Prueba P2");
        if (heredada)
            oc.AgregarLineaDesdeRequisicion(Guid.NewGuid(), Guid.NewGuid(), 10, "PZA", 100,
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        else
            oc.AgregarLineaManual(Guid.NewGuid(), Guid.NewGuid(), 10, "PZA", 100, Guid.NewGuid());
        return oc;
    }

    [Theory]
    [InlineData(NivelAutorizacion.Nivel1)]
    [InlineData(NivelAutorizacion.Nivel2)]
    public void Capturista_NoPuedeFirmarNingunNivel(NivelAutorizacion nivel)
    {
        var oc = Crear();
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        if (nivel == NivelAutorizacion.Nivel2)
            oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.Autorizar(Guid.NewGuid(), nivel, oc.CompradorTitularId, DateTimeOffset.UtcNow));
        Assert.Equal("OC_AUTOAUTORIZACION", ex.Code);
    }

    [Fact]
    public void SegundaFirma_NoPuedeSerLaPrimera()
    {
        var oc = Crear();
        var firmante = Guid.NewGuid();
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, firmante, DateTimeOffset.UtcNow);
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, firmante, DateTimeOffset.UtcNow));
        Assert.Equal("OC_FIRMA_MISMA_PERSONA", ex.Code);
        Assert.Single(oc.Autorizaciones);
    }

    internal static OrdenCompra ConRecepcion()
    {
        var oc = Crear(true);
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, Guid.NewGuid(), DateTimeOffset.UtcNow);
        oc.RegistrarRecepcionLinea(oc.Lineas.Single().Id, 4, DateTimeOffset.UtcNow);
        return oc;
    }

    [Theory]
    [InlineData(NivelAutorizacion.Nivel1)]
    [InlineData(NivelAutorizacion.Nivel2)]
    public void RechazarCorregirYReenviar_AbreCicloSinFirmasVigentes_YConservaHistorial(NivelAutorizacion nivelRechazo)
    {
        var oc = Crear(true);
        var jefe = Guid.NewGuid();
        var direccion = Guid.NewGuid();
        var fecha = DateTimeOffset.UtcNow;
        var motivo = Guid.NewGuid();
        oc.EnviarAAutorizacion(fecha);
        if (nivelRechazo == NivelAutorizacion.Nivel2)
            oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, jefe, fecha);
        oc.Rechazar(Guid.NewGuid(), direccion, fecha, motivo, "Corregir precio", "Primer ciclo");
        oc.ActualizarLinea(oc.Lineas.Single().Id, precioUnitario: 90);
        Assert.Equal(1, oc.CicloAutorizacion);

        // Misma fecha para demostrar que la pertenencia no depende de timestamps.
        oc.EnviarAAutorizacion(fecha);
        Assert.Equal(2, oc.CicloAutorizacion);
        Assert.Null(oc.MotivoRechazoId);
        Assert.Null(oc.MotivoRechazoTexto);
        Assert.DoesNotContain(oc.Autorizaciones, a => a.Ciclo == oc.CicloAutorizacion);
        var auto = Assert.Throws<BusinessRuleException>(() =>
            oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, oc.CompradorTitularId, fecha));
        Assert.Equal("OC_AUTOAUTORIZACION", auto.Code);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, jefe, fecha, "Corregida");
        var misma = Assert.Throws<BusinessRuleException>(() =>
            oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, jefe, fecha));
        Assert.Equal("OC_FIRMA_MISMA_PERSONA", misma.Code);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, direccion, fecha);

        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
        var rechazo = Assert.Single(oc.Autorizaciones, a => a.Resultado == ResultadoAutorizacionOc.Rechazado);
        Assert.Equal(1, rechazo.Ciclo);
        Assert.Equal(nivelRechazo, rechazo.Nivel);
        Assert.Equal(direccion, rechazo.UsuarioId);
        Assert.Equal(motivo, rechazo.MotivoRechazoId);
        Assert.Equal("Corregir precio", rechazo.MotivoRechazoTexto);
        Assert.Equal("Primer ciclo", rechazo.Notas);
        Assert.Equal(fecha, rechazo.FechaHora);
        Assert.Equal(2, oc.Autorizaciones.Count(a => a.Ciclo == 2));
        Assert.Equal(nivelRechazo == NivelAutorizacion.Nivel2 ? 4 : 3, oc.Autorizaciones.Count);
    }

    [Fact]
    public void FirmasDeCiclosAnteriores_NoRestringenLaIdentidadDeN2DelCicloNuevo()
    {
        var oc = Crear();
        var jefeAnterior = Guid.NewGuid();
        var fecha = DateTimeOffset.UtcNow;
        oc.EnviarAAutorizacion(fecha);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, jefeAnterior, fecha);
        oc.Rechazar(Guid.NewGuid(), Guid.NewGuid(), fecha, Guid.NewGuid(), "Corregir");
        oc.EnviarAAutorizacion(fecha);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), fecha);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, jefeAnterior, fecha);
        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
    }

    [Fact]
    public void Solicitud_BloqueaRecepcionYFacturacion_YRechazoRestauraEstado()
    {
        var oc = ConRecepcion();
        oc.SolicitarCancelacionConRecepciones(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), "Motivo N1");
        Assert.Equal(EstadoOrdenCompra.CancelacionSolicitada, oc.Estado);
        Assert.Throws<BusinessRuleException>(() => oc.RegistrarRecepcionLinea(oc.Lineas.Single().Id, 5, DateTimeOffset.UtcNow));
        Assert.Throws<BusinessRuleException>(() => oc.RegistrarFacturacionLinea(oc.Lineas.Single().Id, 4, DateTimeOffset.UtcNow));
        oc.RechazarCancelacion(Guid.NewGuid(), DateTimeOffset.UtcNow, "Continuar compra");
        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
        Assert.False(oc.SolicitudesCancelacion.Single().Confirmada);
        oc.RegistrarRecepcionLinea(oc.Lineas.Single().Id, 5, DateTimeOffset.UtcNow);
        oc.SolicitarCancelacionConRecepciones(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), "Segunda solicitud");
        Assert.Equal(2, oc.SolicitudesCancelacion.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Cancelacion_MismaPersonaNoPuedeResolver(bool confirmar)
    {
        var oc = ConRecepcion();
        var usuario = Guid.NewGuid();
        oc.SolicitarCancelacionConRecepciones(usuario, DateTimeOffset.UtcNow, Guid.NewGuid(), "Motivo N1");
        var ex = Assert.Throws<BusinessRuleException>(() => {
            if (confirmar) oc.ConfirmarCancelacionConRecepciones(usuario, DateTimeOffset.UtcNow, "Confirmo");
            else oc.RechazarCancelacion(usuario, DateTimeOffset.UtcNow, "Rechazo");
        });
        Assert.Equal("OC_CANCELACION_MISMA_PERSONA", ex.Code);
        Assert.Null(oc.SolicitudesCancelacion.Single().ResolutorId);
    }

    [Fact]
    public void Cancelacion_DosFirmas_LiberaSoloSeisDeDiez_YConservaCuatroRecibidas()
    {
        var oc = ConRecepcion();
        var solicitante = Guid.NewGuid();
        var direccion = Guid.NewGuid();
        var fecha = DateTimeOffset.UtcNow;
        var motivoId = Guid.NewGuid();
        oc.SolicitarCancelacionConRecepciones(solicitante, fecha, motivoId, "Faltante innecesario");
        var resultado = oc.ConfirmarCancelacionConRecepciones(direccion, fecha.AddMinutes(1), "Confirmo saldo");
        Assert.Equal(6, resultado.LiberacionesParciales.Single().CantidadLiberada);
        Assert.Equal(4, oc.Lineas.Single().CantidadRecibida);
        Assert.Equal(EstadoOrdenCompra.Cancelada, oc.Estado);
        var firma = oc.SolicitudesCancelacion.Single();
        Assert.Equal(solicitante, firma.SolicitanteId);
        Assert.Equal(direccion, firma.ResolutorId);
        Assert.Equal("Faltante innecesario", firma.MotivoSolicitud);
        Assert.Equal("Confirmo saldo", firma.MotivoResolucion);
        Assert.Equal(fecha.AddMinutes(1), firma.FechaResolucion);
    }

    [Fact]
    public void LineaRq_NoPermiteCambiarArticulo()
    {
        var oc = Crear(true);
        var ex = Assert.Throws<BusinessRuleException>(() => oc.ActualizarLinea(oc.Lineas.Single().Id, articuloId: Guid.NewGuid()));
        Assert.Equal("OC_LINEA_RQ_ARTICULO_FIJO", ex.Code);
    }

    [Theory]
    [InlineData(10, 0, 4, 6)]
    [InlineData(10, 6, 4, 0)]
    [InlineData(10, 3, 4, 3)]
    [InlineData(10, 11, 4, 0)]
    public void Saldo_DescuentaVivasYRecibidoCancelado(decimal compra, decimal vivo, decimal recibido, decimal esperado)
        => Assert.Equal(esperado, SaldoCompraRq.Calcular(compra, vivo, recibido));
}
