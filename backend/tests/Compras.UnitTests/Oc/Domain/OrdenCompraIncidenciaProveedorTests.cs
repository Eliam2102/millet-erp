using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

public sealed class OrdenCompraIncidenciaProveedorTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 21);
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid Responsable = Guid.NewGuid();

    private static OrdenCompra Autorizada()
    {
        var oc = P2FirmasYSaldoTests.Crear(true);
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, Guid.NewGuid(), DateTimeOffset.UtcNow);
        return oc;
    }

    private static Guid Linea(OrdenCompra oc) => oc.Lineas.Single().Id;

    private static IncidenciaProveedorOc Registrar(OrdenCompra oc, TipoIncidenciaProveedorOc tipo, decimal cant, int dia = 25) =>
        oc.RegistrarIncidenciaProveedor(Linea(oc), tipo, cant, new DateOnly(2026, 10, dia), Responsable,
            "Proveedor avisó por correo", Hoy, Usuario, DateTimeOffset.UtcNow);

    private static string Codigo(Action a) => Assert.Throws<BusinessRuleException>(a).Code;

    [Fact]
    public void Recepcion_de_6_de_10_deja_4_pendientes_con_su_compromiso_vigente()
    {
        var oc = Autorizada();
        oc.RegistrarRecepcionLinea(Linea(oc), 6, DateTimeOffset.UtcNow);
        var i = Registrar(oc, TipoIncidenciaProveedorOc.Faltante, 4);

        Assert.True(i.Abierta);
        Assert.Equal(1, i.Numero);
        Assert.Equal(new DateOnly(2026, 10, 25), i.FechaCompromisoVigente);
        Assert.Equal(Responsable, i.ResponsableId);
        Assert.Equal(4, oc.Lineas.Single().Cantidad - oc.Lineas.Single().CantidadRecibida);
        // Regla 4: la incidencia no acredita factura ni pago.
        Assert.Equal(SubEstadoFacturacion.SinFactura, oc.SubEstadoFacturacion);
        Assert.Equal(SubEstadoPago.SinPago, oc.SubEstadoPago);
    }

    [Fact]
    public void Reprogramar_conserva_el_compromiso_anterior()
    {
        var oc = Autorizada();
        var i = Registrar(oc, TipoIncidenciaProveedorOc.Atraso, 10);
        oc.ReprogramarIncidenciaProveedor(i.Id, new DateOnly(2026, 10, 30), "Proveedor sin material", Hoy, Usuario, DateTimeOffset.UtcNow);

        Assert.Equal(2, i.Compromisos.Count);
        Assert.Equal(new DateOnly(2026, 10, 25), i.Compromisos.Single(c => c.Numero == 1).FechaCompromiso);
        Assert.Equal(new DateOnly(2026, 10, 30), i.FechaCompromisoVigente);
    }

    [Fact]
    public void Recibir_el_remanente_cierra_las_incidencias_de_la_linea()
    {
        var oc = Autorizada();
        oc.RegistrarRecepcionLinea(Linea(oc), 6, DateTimeOffset.UtcNow);
        var i = Registrar(oc, TipoIncidenciaProveedorOc.Faltante, 4);

        oc.RegistrarRecepcionLinea(Linea(oc), 10, DateTimeOffset.UtcNow);

        Assert.False(i.Abierta);
        Assert.Null(i.CerradaPorId);
        Assert.Contains(i, oc.Incidencias);
    }

    [Fact]
    public void Recepcion_parcial_no_cierra_la_incidencia()
    {
        var oc = Autorizada();
        var i = Registrar(oc, TipoIncidenciaProveedorOc.Faltante, 10);
        oc.RegistrarRecepcionLinea(Linea(oc), 6, DateTimeOffset.UtcNow);
        Assert.True(i.Abierta);
    }

    [Fact]
    public void Danado_se_cierra_al_recibir_la_reposicion_tras_la_devolucion()
    {
        var oc = Autorizada();
        oc.RegistrarRecepcionLinea(Linea(oc), 10, DateTimeOffset.UtcNow);
        var i = Registrar(oc, TipoIncidenciaProveedorOc.Danado, 2);

        oc.RegistrarRecepcionLinea(Linea(oc), 8, DateTimeOffset.UtcNow); // devolución 8.B
        Assert.True(i.Abierta);
        oc.RegistrarRecepcionLinea(Linea(oc), 10, DateTimeOffset.UtcNow); // reposición
        Assert.False(i.Abierta);
    }

    [Fact]
    public void Cierre_manual_exige_motivo_y_no_se_cierra_dos_veces()
    {
        var oc = Autorizada();
        var i = Registrar(oc, TipoIncidenciaProveedorOc.Faltante, 2);
        Assert.Equal("OC_INCIDENCIA_MOTIVO_REQUERIDO", Codigo(() => oc.CerrarIncidenciaProveedor(i.Id, " ", Usuario, DateTimeOffset.UtcNow)));

        oc.CerrarIncidenciaProveedor(i.Id, "El proveedor no surtirá", Usuario, DateTimeOffset.UtcNow);
        Assert.Equal(Usuario, i.CerradaPorId);
        Assert.Equal("OC_INCIDENCIA_CERRADA", Codigo(() => oc.CerrarIncidenciaProveedor(i.Id, "otra vez", Usuario, DateTimeOffset.UtcNow)));
        Assert.Equal("OC_INCIDENCIA_CERRADA", Codigo(() =>
            oc.ReprogramarIncidenciaProveedor(i.Id, new DateOnly(2026, 10, 28), "x", Hoy, Usuario, DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void Rechazos_de_registro()
    {
        var oc = Autorizada();
        Assert.Equal("OC_INCIDENCIA_EXCEDE_PENDIENTE", Codigo(() => Registrar(oc, TipoIncidenciaProveedorOc.Faltante, 11)));
        Assert.Equal("OC_INCIDENCIA_EXCEDE_RECIBIDO", Codigo(() => Registrar(oc, TipoIncidenciaProveedorOc.Danado, 1)));
        Assert.Equal("OC_INCIDENCIA_CANTIDAD_INVALIDA", Codigo(() => Registrar(oc, TipoIncidenciaProveedorOc.Faltante, 0)));
        Assert.Equal("OC_INCIDENCIA_FECHA_PASADA", Codigo(() => Registrar(oc, TipoIncidenciaProveedorOc.Faltante, 2, 20)));
        Assert.Equal("OC_INCIDENCIA_RESPONSABLE_REQUERIDO", Codigo(() => oc.RegistrarIncidenciaProveedor(Linea(oc),
            TipoIncidenciaProveedorOc.Faltante, 2, Hoy, Guid.Empty, null, Hoy, Usuario, DateTimeOffset.UtcNow)));
        Assert.Equal("OC_LINEA_NO_ENCONTRADA", Codigo(() => oc.RegistrarIncidenciaProveedor(Guid.NewGuid(),
            TipoIncidenciaProveedorOc.Faltante, 2, Hoy, Responsable, null, Hoy, Usuario, DateTimeOffset.UtcNow)));
        Assert.Empty(oc.Incidencias);
    }

    [Fact]
    public void Solo_en_oc_autorizada()
    {
        var oc = P2FirmasYSaldoTests.Crear(true);
        Assert.Equal("OC_INCIDENCIA_ESTADO_INVALIDO", Codigo(() => Registrar(oc, TipoIncidenciaProveedorOc.Faltante, 2)));
    }

    [Fact]
    public void Reprogramar_rechaza_misma_fecha_y_sin_motivo()
    {
        var oc = Autorizada();
        var i = Registrar(oc, TipoIncidenciaProveedorOc.Atraso, 10);
        Assert.Equal("OC_INCIDENCIA_MISMA_FECHA", Codigo(() =>
            oc.ReprogramarIncidenciaProveedor(i.Id, new DateOnly(2026, 10, 25), "x", Hoy, Usuario, DateTimeOffset.UtcNow)));
        Assert.Equal("OC_INCIDENCIA_MOTIVO_REQUERIDO", Codigo(() =>
            oc.ReprogramarIncidenciaProveedor(i.Id, new DateOnly(2026, 10, 28), "", Hoy, Usuario, DateTimeOffset.UtcNow)));
        Assert.Single(i.Compromisos);
    }
}
