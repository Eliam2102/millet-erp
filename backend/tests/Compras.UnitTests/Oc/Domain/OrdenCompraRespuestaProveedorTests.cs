using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

public sealed class OrdenCompraRespuestaProveedorTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 17);

    private static OrdenCompra Autorizada()
    {
        var oc = P2FirmasYSaldoTests.Crear(true);
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, Guid.NewGuid(), DateTimeOffset.UtcNow);
        return oc;
    }

    private static CompromisoEntregaPropuesto C(OrdenCompra oc, decimal cant, int dia) =>
        new(oc.Lineas.Single().Id, cant, new DateOnly(2026, 10, dia));

    private static RespuestaProveedorOc Confirmar(OrdenCompra oc, params CompromisoEntregaPropuesto[] c) =>
        oc.RegistrarRespuestaProveedor(TipoRespuestaProveedorOc.Confirmacion, Hoy, Hoy, "Correo P-881",
            null, null, c, Guid.NewGuid(), DateTimeOffset.UtcNow);

    private static string Codigo(Action a) => Assert.Throws<BusinessRuleException>(a).Code;

    [Fact]
    public void Confirmacion_completa_no_toca_la_orden()
    {
        var oc = Autorizada();
        var r = Confirmar(oc, C(oc, 10, 20));
        Assert.Equal(EstadoComercialProveedorOc.Confirmada, oc.EstadoComercialProveedor);
        Assert.Equal(new DateOnly(2026, 10, 20), oc.FechaCompromisoProveedor);
        Assert.False(r.TieneDiferencias);
        Assert.Equal(10, oc.Lineas.Single().Cantidad);
        Assert.Equal(0, oc.Lineas.Single().CantidadRecibida);
        Assert.Equal(SubEstadoRecepcion.SinRecepcion, oc.SubEstadoRecepcion);
        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
    }

    [Fact]
    public void Parcialidades_sin_diferencias_y_compromiso_es_la_fecha_mayor()
    {
        var oc = Autorizada();
        var r = Confirmar(oc, C(oc, 6, 20), C(oc, 4, 25));
        Assert.False(r.TieneDiferencias);
        Assert.Equal(2, r.Compromisos.Count);
        Assert.Equal(new DateOnly(2026, 10, 25), oc.FechaCompromisoProveedor);
    }

    [Fact]
    public void Confirmar_menos_marca_diferencias_sin_cambiar_cantidad()
    {
        var oc = Autorizada();
        var r = Confirmar(oc, C(oc, 8, 20));
        Assert.Equal(EstadoComercialProveedorOc.ConfirmadaConDiferencias, oc.EstadoComercialProveedor);
        Assert.True(r.TieneDiferencias);
        Assert.Equal(10, oc.Lineas.Single().Cantidad);
    }

    [Fact]
    public void Rechazo_con_motivo()
    {
        var oc = Autorizada();
        oc.RegistrarRespuestaProveedor(TipoRespuestaProveedorOc.Rechazo, Hoy, Hoy, "Correo", null, "Sin stock",
            [], Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Equal(EstadoComercialProveedorOc.Rechazada, oc.EstadoComercialProveedor);
        Assert.Null(oc.FechaCompromisoProveedor);
        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
    }

    [Fact]
    public void Nueva_respuesta_conserva_la_anterior()
    {
        var oc = Autorizada();
        Confirmar(oc, C(oc, 10, 20));
        Confirmar(oc, C(oc, 10, 22));
        Assert.Equal([1, 2], oc.RespuestasProveedor.Select(r => r.Numero).ToArray());
        Assert.Equal(new DateOnly(2026, 10, 20), oc.RespuestasProveedor.First().Compromisos.Single().FechaPrometida);
        Assert.Equal(new DateOnly(2026, 10, 22), oc.FechaCompromisoProveedor);
    }

    [Fact]
    public void Rechazo_sin_motivo()
    {
        var oc = Autorizada();
        var respuesta = oc.RegistrarRespuestaProveedor(
            TipoRespuestaProveedorOc.Rechazo, Hoy, Hoy, "Correo", null, " ", [], Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Null(respuesta.Motivo);
    }

    [Fact]
    public void Oc_no_autorizada()
    {
        var oc = P2FirmasYSaldoTests.Crear(true);
        Assert.Equal("OC_RESPUESTA_ESTADO_INVALIDO", Codigo(() => Confirmar(oc, C(oc, 10, 20))));
    }

    [Fact]
    public void Fecha_de_respuesta_futura()
    {
        var oc = Autorizada();
        Assert.Equal("OC_RESPUESTA_FECHA_FUTURA", Codigo(() => oc.RegistrarRespuestaProveedor(
            TipoRespuestaProveedorOc.Confirmacion, Hoy.AddDays(1), Hoy, "Correo", null, null,
            [C(oc, 10, 20)], Guid.NewGuid(), DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void Fecha_prometida_anterior_a_la_respuesta()
    {
        var oc = Autorizada();
        Assert.Equal("OC_RESPUESTA_FECHA_PROMETIDA_INVALIDA", Codigo(() => Confirmar(oc, C(oc, 10, 16))));
    }

    [Fact]
    public void Linea_ajena()
    {
        var oc = Autorizada();
        Assert.Equal("OC_RESPUESTA_LINEA_INVALIDA", Codigo(() =>
            Confirmar(oc, new CompromisoEntregaPropuesto(Guid.NewGuid(), 10, new DateOnly(2026, 10, 20)))));
    }

    [Fact]
    public void Evidencia_en_blanco()
    {
        var oc = Autorizada();
        var respuesta = oc.RegistrarRespuestaProveedor(null, null, Hoy, null, null, null,
            [], Guid.NewGuid(), DateTimeOffset.UtcNow);
        Assert.Null(respuesta.Tipo);
        Assert.Null(respuesta.FechaRespuesta);
        Assert.Equal(EstadoComercialProveedorOc.RespuestaRegistrada, oc.EstadoComercialProveedor);
        Assert.Null(oc.FechaCompromisoProveedor);
    }

    [Fact]
    public void Con_recepcion_parcial_el_pendiente_es_la_referencia()
    {
        var oc = Autorizada();
        oc.RegistrarRecepcionLinea(oc.Lineas.Single().Id, 4, DateTimeOffset.UtcNow);
        var r = Confirmar(oc, C(oc, 6, 20));
        Assert.False(r.TieneDiferencias);
        Assert.Equal(4, oc.Lineas.Single().CantidadRecibida);
    }
}
