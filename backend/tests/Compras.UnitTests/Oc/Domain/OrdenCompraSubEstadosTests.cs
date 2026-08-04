using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Oc.Events;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests F5-PR1 — sub-estados materializados + cierre automático.
/// Cubre <see cref="OrdenCompra.RegistrarRecepcionLinea"/>,
/// <see cref="OrdenCompra.RegistrarFacturacionLinea"/>,
/// <see cref="OrdenCompra.RegistrarPago"/>,
/// <see cref="OrdenCompra.RecalcularSubEstados"/> y la transición
/// automática a <see cref="EstadoOrdenCompra.Cerrada"/> cuando las 3
/// dimensiones cierran.
/// </summary>
public class OrdenCompraSubEstadosTests
{
    private static OrdenCompra NewOcAutorizadaConDosLineas()
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000020"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test");

        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 10m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7());

        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 4m,
            unidadMedida: "PZA",
            precioUnitario: 250m,
            departamentoSolicitanteId: Guid.CreateVersion7());

        // Atajo a Autorizada: enviar + N1 + N2.
        var ahora = DateTimeOffset.UtcNow;
        oc.EnviarAAutorizacion(ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Guid.CreateVersion7(), ahora);
        return oc;
    }

    [Fact]
    public void RegistrarRecepcionLinea_Parcial_SubEstadoParcial_SinCierre()
    {
        var oc = NewOcAutorizadaConDosLineas();
        var lineaA = oc.Lineas.First();

        var resultado = oc.RegistrarRecepcionLinea(lineaA.Id, 5m, DateTimeOffset.UtcNow);

        Assert.Null(resultado.Cerrada);
        Assert.Null(resultado.Reabrierta);
        Assert.Equal(SubEstadoRecepcion.Parcial, oc.SubEstadoRecepcion);
        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
    }

    [Fact]
    public void RegistrarRecepcionLinea_TodasLineasCompletas_SubEstadoCompleta()
    {
        var oc = NewOcAutorizadaConDosLineas();
        var lineas = oc.Lineas.ToList();

        oc.RegistrarRecepcionLinea(lineas[0].Id, lineas[0].Cantidad, DateTimeOffset.UtcNow);
        oc.RegistrarRecepcionLinea(lineas[1].Id, lineas[1].Cantidad, DateTimeOffset.UtcNow);

        Assert.Equal(SubEstadoRecepcion.Completa, oc.SubEstadoRecepcion);
        // Sin facturación + pago no cierra todavía.
        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
    }

    [Fact]
    public void RegistrarRecepcionLinea_Idempotente()
    {
        var oc = NewOcAutorizadaConDosLineas();
        var linea = oc.Lineas.First();

        oc.RegistrarRecepcionLinea(linea.Id, 5m, DateTimeOffset.UtcNow);
        oc.RegistrarRecepcionLinea(linea.Id, 5m, DateTimeOffset.UtcNow);
        oc.RegistrarRecepcionLinea(linea.Id, 5m, DateTimeOffset.UtcNow);

        Assert.Equal(5m, oc.Lineas.First(l => l.Id == linea.Id).CantidadRecibida);
    }

    [Fact]
    public void RegistrarRecepcionLinea_ExcedeCantidad_Lanza()
    {
        var oc = NewOcAutorizadaConDosLineas();
        var linea = oc.Lineas.First();

        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.RegistrarRecepcionLinea(linea.Id, linea.Cantidad + 1m, DateTimeOffset.UtcNow));
        Assert.Equal("LINEA_OC_CANTIDAD_RECIBIDA_EXCEDE", ex.Code);
    }

    [Fact]
    public void RegistrarRecepcionLinea_EstadoBorrador_Lanza()
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000021"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test");
        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 1m, unidadMedida: "PZA", precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7());
        var lineaId = oc.Lineas.First().Id;

        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.RegistrarRecepcionLinea(lineaId, 1m, DateTimeOffset.UtcNow));
        Assert.Equal("OC_MOVIMIENTO_ESTADO_INVALIDO", ex.Code);
    }

    [Fact]
    public void RegistrarFacturacionLinea_TodasCompletas_SubEstadoCompleta()
    {
        var oc = NewOcAutorizadaConDosLineas();
        var lineas = oc.Lineas.ToList();

        oc.RegistrarFacturacionLinea(lineas[0].Id, lineas[0].Cantidad, DateTimeOffset.UtcNow);
        oc.RegistrarFacturacionLinea(lineas[1].Id, lineas[1].Cantidad, DateTimeOffset.UtcNow);

        Assert.Equal(SubEstadoFacturacion.Completa, oc.SubEstadoFacturacion);
    }

    [Fact]
    public void RegistrarPago_MontoTotal_SubEstadoPagada()
    {
        var oc = NewOcAutorizadaConDosLineas();
        var totales = oc.CalcularTotales();

        oc.RegistrarPago(totales.TotalAPagar, DateTimeOffset.UtcNow);

        Assert.Equal(SubEstadoPago.Pagada, oc.SubEstadoPago);
    }

    [Fact]
    public void RegistrarPago_Parcial_SubEstadoParcial()
    {
        var oc = NewOcAutorizadaConDosLineas();
        var totales = oc.CalcularTotales();

        oc.RegistrarPago(totales.TotalAPagar / 2m, DateTimeOffset.UtcNow);

        Assert.Equal(SubEstadoPago.Parcial, oc.SubEstadoPago);
    }

    [Fact]
    public void TresDimensionesCompletas_TransicionaACerrada_EmiteEvento()
    {
        var oc = NewOcAutorizadaConDosLineas();
        var lineas = oc.Lineas.ToList();
        var totales = oc.CalcularTotales();
        var ahora = DateTimeOffset.UtcNow;

        // Recepción completa.
        oc.RegistrarRecepcionLinea(lineas[0].Id, lineas[0].Cantidad, ahora);
        oc.RegistrarRecepcionLinea(lineas[1].Id, lineas[1].Cantidad, ahora);
        // Facturación completa.
        oc.RegistrarFacturacionLinea(lineas[0].Id, lineas[0].Cantidad, ahora);
        oc.RegistrarFacturacionLinea(lineas[1].Id, lineas[1].Cantidad, ahora);
        // Último movimiento que cierra: pago.
        var resultado = oc.RegistrarPago(totales.TotalAPagar, ahora);

        Assert.NotNull(resultado.Cerrada);
        Assert.Null(resultado.Reabrierta);
        Assert.Equal(EstadoOrdenCompra.Cerrada, oc.Estado);
        Assert.Equal(ahora, oc.FechaCierre);
        Assert.Equal(oc.Id, resultado.Cerrada.OrdenCompraId);
        Assert.Equal(oc.Folio.Valor, resultado.Cerrada.Folio);
    }

    [Fact]
    public void CierrePuedeDispararseEnCualquierOrden()
    {
        var oc = NewOcAutorizadaConDosLineas();
        var lineas = oc.Lineas.ToList();
        var totales = oc.CalcularTotales();
        var ahora = DateTimeOffset.UtcNow;

        // Orden inverso: primero pago, luego facturación, luego recepción.
        Assert.Null(oc.RegistrarPago(totales.TotalAPagar, ahora).Cerrada);
        oc.RegistrarFacturacionLinea(lineas[0].Id, lineas[0].Cantidad, ahora);
        Assert.Null(oc.RegistrarFacturacionLinea(lineas[1].Id, lineas[1].Cantidad, ahora).Cerrada);
        oc.RegistrarRecepcionLinea(lineas[0].Id, lineas[0].Cantidad, ahora);
        var resultado = oc.RegistrarRecepcionLinea(lineas[1].Id, lineas[1].Cantidad, ahora);

        Assert.NotNull(resultado.Cerrada);
        Assert.Equal(EstadoOrdenCompra.Cerrada, oc.Estado);
    }

    [Fact]
    public void Devolucion_TrasCierre_ReabreOc_EmiteOrdenCompraReabriertaEvent()
    {
        var oc = NewOcAutorizadaConDosLineas();
        var lineas = oc.Lineas.ToList();
        var totales = oc.CalcularTotales();
        var ahora = DateTimeOffset.UtcNow;

        // Cerrar: las 3 dimensiones completas.
        oc.RegistrarRecepcionLinea(lineas[0].Id, lineas[0].Cantidad, ahora);
        oc.RegistrarRecepcionLinea(lineas[1].Id, lineas[1].Cantidad, ahora);
        oc.RegistrarFacturacionLinea(lineas[0].Id, lineas[0].Cantidad, ahora);
        oc.RegistrarFacturacionLinea(lineas[1].Id, lineas[1].Cantidad, ahora);
        var cierre = oc.RegistrarPago(totales.TotalAPagar, ahora);
        Assert.NotNull(cierre.Cerrada);
        Assert.Equal(EstadoOrdenCompra.Cerrada, oc.Estado);

        // Devolución: baja CantidadRecibida de la línea 0 → sub-recepción Parcial.
        var resultado = oc.RegistrarRecepcionLinea(lineas[0].Id, lineas[0].Cantidad - 1m, ahora);

        Assert.NotNull(resultado.Reabrierta);
        Assert.Null(resultado.Cerrada);
        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
        Assert.Equal(SubEstadoRecepcion.Parcial, oc.SubEstadoRecepcion);
        Assert.Null(oc.FechaCierre);
        Assert.Equal(oc.Id, resultado.Reabrierta.OrdenCompraId);
    }

    // --- GAP-9: líneas de servicio excluidas del sub-estado Recepción ---

    /// <summary>
    /// Helper GAP-9: OC Autorizada con líneas parametrizables en cantidad
    /// y naturaleza (esServicio). Mismo atajo a Autorizada que
    /// <see cref="NewOcAutorizadaConDosLineas"/>.
    /// </summary>
    private static OrdenCompra NewOcAutorizada(params (decimal Cantidad, bool EsServicio)[] lineasSpec)
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000030"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test");

        foreach (var (cantidad, esServicio) in lineasSpec)
        {
            oc.AgregarLineaManual(
                lineaId: Guid.CreateVersion7(),
                articuloId: Guid.CreateVersion7(),
                cantidad: cantidad,
                unidadMedida: "PZA",
                precioUnitario: 100m,
                departamentoSolicitanteId: Guid.CreateVersion7(),
                esServicio: esServicio);
        }

        var ahora = DateTimeOffset.UtcNow;
        oc.EnviarAAutorizacion(ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Guid.CreateVersion7(), ahora);
        return oc;
    }

    [Fact]
    public void OcSoloServicio_FacturadaYPagada_CierraSinRecepcion()
    {
        // Los servicios no se reciben en Almacén: la OC 100% servicio debe
        // cerrar con facturación + pago completos, sin recepción alguna.
        var oc = NewOcAutorizada((5m, true));
        var linea = oc.Lineas.Single();
        var totales = oc.CalcularTotales();
        var ahora = DateTimeOffset.UtcNow;

        oc.RegistrarFacturacionLinea(linea.Id, linea.Cantidad, ahora);
        var resultado = oc.RegistrarPago(totales.TotalAPagar, ahora);

        Assert.NotNull(resultado.Cerrada);
        Assert.Equal(EstadoOrdenCompra.Cerrada, oc.Estado);
        Assert.Equal(ahora, oc.FechaCierre);
        // La dimensión recepción queda SinRecepcion (no Completa): la regla
        // de cancelación sin recepciones depende de ese sub-estado.
        Assert.Equal(SubEstadoRecepcion.SinRecepcion, oc.SubEstadoRecepcion);
    }

    [Fact]
    public void OcMixta_RecepcionSoloDeLaFisica_FacturadaYPagada_Cierra()
    {
        var oc = NewOcAutorizada((10m, false), (3m, true));
        var lineaFisica = oc.Lineas.First(l => !l.EsServicio);
        var lineaServicio = oc.Lineas.First(l => l.EsServicio);
        var totales = oc.CalcularTotales();
        var ahora = DateTimeOffset.UtcNow;

        // Recepción SOLO de la línea física — el servicio nunca se recibe.
        oc.RegistrarRecepcionLinea(lineaFisica.Id, lineaFisica.Cantidad, ahora);
        Assert.Equal(SubEstadoRecepcion.Completa, oc.SubEstadoRecepcion);

        // Facturación de ambas líneas + pago total.
        oc.RegistrarFacturacionLinea(lineaFisica.Id, lineaFisica.Cantidad, ahora);
        oc.RegistrarFacturacionLinea(lineaServicio.Id, lineaServicio.Cantidad, ahora);
        var resultado = oc.RegistrarPago(totales.TotalAPagar, ahora);

        Assert.NotNull(resultado.Cerrada);
        Assert.Equal(EstadoOrdenCompra.Cerrada, oc.Estado);
    }

    [Fact]
    public void OcMixta_SinRecepcionDeLaFisica_NoCierra()
    {
        var oc = NewOcAutorizada((10m, false), (3m, true));
        var lineas = oc.Lineas.ToList();
        var totales = oc.CalcularTotales();
        var ahora = DateTimeOffset.UtcNow;

        // Facturación completa + pago total, pero la línea física NO se
        // ha recibido → la OC no puede cerrar.
        oc.RegistrarFacturacionLinea(lineas[0].Id, lineas[0].Cantidad, ahora);
        oc.RegistrarFacturacionLinea(lineas[1].Id, lineas[1].Cantidad, ahora);
        var resultado = oc.RegistrarPago(totales.TotalAPagar, ahora);

        Assert.Null(resultado.Cerrada);
        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
        Assert.Equal(SubEstadoRecepcion.SinRecepcion, oc.SubEstadoRecepcion);
    }

    // --- GAP-9: cierre manual ---

    [Fact]
    public void CerrarManual_DesdeAutorizada_CierraYEmiteEvento()
    {
        var oc = NewOcAutorizadaConDosLineas();
        var ahora = DateTimeOffset.UtcNow;

        var evento = oc.CerrarManual(ahora);

        Assert.Equal(EstadoOrdenCompra.Cerrada, oc.Estado);
        Assert.Equal(ahora, oc.FechaCierre);
        Assert.Equal(oc.Id, evento.OrdenCompraId);
        Assert.Equal(oc.EmpresaId, evento.EmpresaId);
        Assert.Equal(oc.Folio.Valor, evento.Folio);
        Assert.Equal(oc.CompradorTitularId, evento.CompradorTitularId);
        Assert.Equal(ahora, evento.OcurridoEn);
    }

    [Fact]
    public void CerrarManual_DesdeBorrador_Lanza()
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000031"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test");

        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.CerrarManual(DateTimeOffset.UtcNow));
        Assert.Equal("OC_CERRAR_MANUAL_ESTADO_INCORRECTO", ex.Code);
        Assert.Equal(EstadoOrdenCompra.Borrador, oc.Estado);
    }

    [Fact]
    public void RecalcularSubEstados_OcSinLineas_TodosSinX()
    {
        var oc = NewOcAutorizadaConDosLineas();
        // Eliminar las 2 líneas sería ilegal en Autorizada, así que usamos
        // una OC nueva sin pasar por la state machine.
        var ocVacia = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000022"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));

        ocVacia.RecalcularSubEstados();

        Assert.Equal(SubEstadoRecepcion.SinRecepcion, ocVacia.SubEstadoRecepcion);
        Assert.Equal(SubEstadoFacturacion.SinFactura, ocVacia.SubEstadoFacturacion);
        Assert.Equal(SubEstadoPago.SinPago, ocVacia.SubEstadoPago);
        // Solo nos importa que NewOcAutorizadaConDosLineas siga estable.
        Assert.Equal(2, oc.Lineas.Count);
    }
}
