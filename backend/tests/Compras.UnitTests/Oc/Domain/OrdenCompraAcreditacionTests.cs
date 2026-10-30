using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Devolución a proveedor acreditada con NC fiscal 03 (<c>almacen.oc_devolucion.conciliada.v1</c>): lo acreditado
/// deja de esperarse (decisión del owner, 30-oct) y la OC puede cerrarse sin reposición.
/// </summary>
public class OrdenCompraAcreditacionTests
{
    private static OrdenCompra OcAutorizadaDe20()
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(), empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000302"), folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(), sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(), usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(), encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true, motivoSinRequisicion: "Test", usoCfdiClave: "G03", metodoPago: "PPD", formaPagoClave: "99");
        oc.AgregarLineaManual(Guid.CreateVersion7(), Guid.CreateVersion7(), 20m, "PZA", 650m, Guid.CreateVersion7());
        var ahora = DateTimeOffset.UtcNow;
        oc.EnviarAAutorizacion(ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Guid.CreateVersion7(), ahora);
        return oc;
    }

    [Fact]
    public void Devolver_2_de_20_facturadas_y_acreditarlas_cierra_la_OC_pagada()
    {
        var oc = OcAutorizadaDe20();
        var linea = oc.Lineas.Single().Id;
        var ahora = DateTimeOffset.UtcNow;
        oc.RegistrarRecepcionLinea(linea, 20m, ahora);
        oc.RegistrarFacturacionLinea(linea, 20m, ahora);
        oc.RegistrarPago(oc.CalcularTotales().TotalAPagar, ahora);
        // Cerrada al pagar; la devolución de 2 baja lo recibido y la reabre.
        oc.RegistrarRecepcionLinea(linea, 18m, ahora);
        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
        Assert.Equal(SubEstadoRecepcion.Parcial, oc.SubEstadoRecepcion);

        var resultado = oc.RegistrarAcreditacionLinea(linea, 2m, ahora);

        Assert.NotNull(resultado.Cerrada);
        Assert.Equal(EstadoOrdenCompra.Cerrada, oc.Estado);
        Assert.Equal(SubEstadoRecepcion.Completa, oc.SubEstadoRecepcion);
        Assert.Equal(SubEstadoFacturacion.Completa, oc.SubEstadoFacturacion);
        Assert.Equal(18m, oc.Lineas.Single().CantidadFacturadaNeta);
    }

    [Fact]
    public void Acreditada_sin_pago_completo_no_cierra()
    {
        var oc = OcAutorizadaDe20();
        var linea = oc.Lineas.Single().Id;
        var ahora = DateTimeOffset.UtcNow;
        oc.RegistrarRecepcionLinea(linea, 18m, ahora);
        oc.RegistrarFacturacionLinea(linea, 20m, ahora);

        var resultado = oc.RegistrarAcreditacionLinea(linea, 2m, ahora);

        Assert.Null(resultado.Cerrada);
        Assert.Equal(SubEstadoRecepcion.Completa, oc.SubEstadoRecepcion);
        Assert.Equal(SubEstadoFacturacion.Completa, oc.SubEstadoFacturacion);
        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
    }

    [Fact]
    public void Reposicion_facturada_cabe_en_el_bruto()
    {
        var oc = OcAutorizadaDe20();
        var linea = oc.Lineas.Single().Id;
        var ahora = DateTimeOffset.UtcNow;
        oc.RegistrarRecepcionLinea(linea, 18m, ahora);
        oc.RegistrarFacturacionLinea(linea, 20m, ahora);
        oc.RegistrarAcreditacionLinea(linea, 2m, ahora);

        // El proveedor repone 2 y factura de nuevo: bruto 22 = 20 + 2 acreditadas.
        oc.RegistrarRecepcionLinea(linea, 20m, ahora);
        oc.RegistrarFacturacionLinea(linea, 22m, ahora);

        Assert.Equal(20m, oc.Lineas.Single().CantidadFacturadaNeta);
        Assert.Throws<BusinessRuleException>(() => oc.RegistrarFacturacionLinea(linea, 23m, ahora));
    }

    [Fact]
    public void Devolucion_total_acreditada_deja_recepcion_y_facturacion_completas()
    {
        var oc = OcAutorizadaDe20();
        var linea = oc.Lineas.Single().Id;
        var ahora = DateTimeOffset.UtcNow;
        oc.RegistrarRecepcionLinea(linea, 20m, ahora);
        oc.RegistrarFacturacionLinea(linea, 20m, ahora);
        oc.RegistrarRecepcionLinea(linea, 0m, ahora);

        oc.RegistrarAcreditacionLinea(linea, 20m, ahora);

        Assert.Equal(SubEstadoRecepcion.Completa, oc.SubEstadoRecepcion);
        Assert.Equal(SubEstadoFacturacion.Completa, oc.SubEstadoFacturacion);
    }

    [Fact]
    public void Acreditada_negativa_o_mayor_a_lo_facturado_se_rechaza()
    {
        var oc = OcAutorizadaDe20();
        var linea = oc.Lineas.Single().Id;
        Assert.Throws<BusinessRuleException>(() => oc.RegistrarAcreditacionLinea(linea, -1m, DateTimeOffset.UtcNow));
        Assert.Throws<BusinessRuleException>(() => oc.RegistrarAcreditacionLinea(linea, 21m, DateTimeOffset.UtcNow));
    }
}
