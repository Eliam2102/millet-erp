using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests F5-PR4 — cancelar OC con recepciones parciales. Cobertura:
/// líneas totalmente recibidas no liberan; líneas parcialmente
/// recibidas liberan el saldo no recibido; líneas sin recepción liberan
/// la cantidad total; líneas manuales (sin RQ) nunca aparecen en la
/// lista de liberaciones.
/// </summary>
public class OrdenCompraCancelarConRecepcionesTests
{
    private static OrdenCompra NewOcAutorizadaDesdeRq()
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000060"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));

        oc.AgregarLineaDesdeRequisicion(
            lineaId: Guid.CreateVersion7(), articuloId: Guid.CreateVersion7(),
            cantidad: 10m, unidadMedida: "PZA", precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            requisicionId: Guid.CreateVersion7(),
            lineaRequisicionId: Guid.CreateVersion7());
        oc.AgregarLineaDesdeRequisicion(
            lineaId: Guid.CreateVersion7(), articuloId: Guid.CreateVersion7(),
            cantidad: 5m, unidadMedida: "PZA", precioUnitario: 200m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            requisicionId: Guid.CreateVersion7(),
            lineaRequisicionId: Guid.CreateVersion7());

        var ahora = DateTimeOffset.UtcNow;
        oc.EnviarAAutorizacion(ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Guid.CreateVersion7(), ahora);
        return oc;
    }

    [Fact]
    public void Cancelar_LineaSinRecepcion_LiberaCantidadTotal()
    {
        var oc = NewOcAutorizadaDesdeRq();
        var linea = oc.Lineas.First(); // cantidad = 10, recibida = 0

        var resultado = oc.CancelarConRecepcionesParciales(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoCancelacionId: Guid.CreateVersion7());

        var liberacion = resultado.LiberacionesParciales.First(l => l.LineaOrdenCompraId == linea.Id);
        Assert.Equal(10m, liberacion.CantidadLiberada);
    }

    [Fact]
    public void Cancelar_LineaParcial_LiberaSaldoNoRecibido()
    {
        var oc = NewOcAutorizadaDesdeRq();
        var lineas = oc.Lineas.ToList();
        var lineaA = lineas[0]; // cantidad 10
        oc.RegistrarRecepcionLinea(lineaA.Id, 4m, DateTimeOffset.UtcNow);

        var resultado = oc.CancelarConRecepcionesParciales(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoCancelacionId: Guid.CreateVersion7());

        var liberacionA = resultado.LiberacionesParciales.First(l => l.LineaOrdenCompraId == lineaA.Id);
        Assert.Equal(6m, liberacionA.CantidadLiberada);
        Assert.Equal(4m, oc.Lineas.First(l => l.Id == lineaA.Id).CantidadRecibida);
    }

    [Fact]
    public void Cancelar_LineaCompletamenteRecibida_NoApareceEnLiberaciones()
    {
        var oc = NewOcAutorizadaDesdeRq();
        var lineas = oc.Lineas.ToList();
        var lineaA = lineas[0]; // cantidad 10
        oc.RegistrarRecepcionLinea(lineaA.Id, 10m, DateTimeOffset.UtcNow);

        var resultado = oc.CancelarConRecepcionesParciales(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoCancelacionId: Guid.CreateVersion7());

        // La línea totalmente recibida no genera liberación.
        Assert.DoesNotContain(resultado.LiberacionesParciales,
            l => l.LineaOrdenCompraId == lineaA.Id);
        // La otra línea (sin recepción) sí libera.
        Assert.Contains(resultado.LiberacionesParciales,
            l => l.LineaOrdenCompraId == lineas[1].Id && l.CantidadLiberada == 5m);
    }

    [Fact]
    public void Cancelar_LineaManualSinRq_NoApareceEnLiberaciones()
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000061"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Manual");
        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(), articuloId: Guid.CreateVersion7(),
            cantidad: 3m, unidadMedida: "PZA", precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7());
        var ahora = DateTimeOffset.UtcNow;
        oc.EnviarAAutorizacion(ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Guid.CreateVersion7(), ahora);
        oc.RegistrarRecepcionLinea(oc.Lineas.First().Id, 1m, ahora);

        var resultado = oc.CancelarConRecepcionesParciales(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoCancelacionId: Guid.CreateVersion7());

        Assert.Empty(resultado.LiberacionesParciales);
        Assert.Equal(EstadoOrdenCompra.Cancelada, oc.Estado);
    }

    [Fact]
    public void Cancelar_CantidadesRecibidasPermanecen()
    {
        var oc = NewOcAutorizadaDesdeRq();
        var lineas = oc.Lineas.ToList();
        oc.RegistrarRecepcionLinea(lineas[0].Id, 4m, DateTimeOffset.UtcNow);
        oc.RegistrarRecepcionLinea(lineas[1].Id, 5m, DateTimeOffset.UtcNow); // 100% recibido

        oc.CancelarConRecepcionesParciales(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoCancelacionId: Guid.CreateVersion7());

        // Trazabilidad contable: las cantidades recibidas NO se decrementan.
        Assert.Equal(4m, oc.Lineas.First(l => l.Id == lineas[0].Id).CantidadRecibida);
        Assert.Equal(5m, oc.Lineas.First(l => l.Id == lineas[1].Id).CantidadRecibida);
    }

    [Fact]
    public void Cancelar_EstadoTerminal_Lanza()
    {
        var oc = NewOcAutorizadaDesdeRq();
        oc.CancelarConRecepcionesParciales(
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoCancelacionId: Guid.CreateVersion7());

        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.CancelarConRecepcionesParciales(
                usuarioId: Guid.CreateVersion7(),
                fechaHora: DateTimeOffset.UtcNow,
                motivoCancelacionId: Guid.CreateVersion7()));
        Assert.Equal("OC_CANCELAR_ESTADO_TERMINAL", ex.Code);
    }

    [Fact]
    public void Cancelar_MotivoVacio_Lanza()
    {
        var oc = NewOcAutorizadaDesdeRq();
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.CancelarConRecepcionesParciales(
                usuarioId: Guid.CreateVersion7(),
                fechaHora: DateTimeOffset.UtcNow,
                motivoCancelacionId: Guid.Empty));
        Assert.Equal("OC_CANCELAR_MOTIVO_REQUERIDO", ex.Code);
    }
}
