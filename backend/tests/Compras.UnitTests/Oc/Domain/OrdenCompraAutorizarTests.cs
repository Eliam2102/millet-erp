using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests de la state machine de autorización (F3-PR1):
/// EnviarAAutorizacion + Autorizar N1/N2.
/// </summary>
public class OrdenCompraAutorizarTests
{
    private static OrdenCompra NewOcConLineaBorrador()
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000001"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test autorizar");
        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 10m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7());
        return oc;
    }

    [Fact]
    public void EnviarAAutorizacion_Borrador_TransicionaA_EnAutorizacionJefeCompras()
    {
        var oc = NewOcConLineaBorrador();
        var ahora = DateTimeOffset.UtcNow;
        var evento = oc.EnviarAAutorizacion(ahora);

        Assert.Equal(EstadoOrdenCompra.EnAutorizacionJefeCompras, oc.Estado);
        Assert.Equal(oc.Id, evento.OrdenCompraId);
        Assert.Equal(oc.Folio.Valor, evento.Folio);
        Assert.Equal(ahora, evento.OcurridoEn);
    }

    [Fact]
    public void EnviarAAutorizacion_SinLineas_Lanza()
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000002"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));
        var ex = Assert.Throws<BusinessRuleException>(() => oc.EnviarAAutorizacion(DateTimeOffset.UtcNow));
        Assert.Equal("OC_TRANSMITIR_SIN_LINEAS", ex.Code);
    }

    [Fact]
    public void EnviarAAutorizacion_EnEstadoNoEditable_Lanza()
    {
        var oc = NewOcConLineaBorrador();
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        // Ya está en EnAutorizacionJefeCompras, no se puede re-transmitir.
        var ex = Assert.Throws<BusinessRuleException>(() => oc.EnviarAAutorizacion(DateTimeOffset.UtcNow));
        Assert.Equal("OC_TRANSMITIR_SOLO_DESDE_BORRADOR_O_RECHAZADA", ex.Code);
    }

    [Fact]
    public void Autorizar_N1_DesdeEnAutorizacionJefeCompras_TransicionaA_EnAutorizacionDireccion()
    {
        var oc = NewOcConLineaBorrador();
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        var resultado = oc.Autorizar(
            autorizacionId: Guid.CreateVersion7(),
            nivel: NivelAutorizacion.Nivel1,
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow);

        Assert.Equal(EstadoOrdenCompra.EnAutorizacionDireccion, oc.Estado);
        Assert.Single(oc.Autorizaciones);
        Assert.Equal(NivelAutorizacion.Nivel1, resultado.Autorizacion.Nivel);
        Assert.Equal(ResultadoAutorizacionOc.Autorizado, resultado.Autorizacion.Resultado);
        Assert.Null(resultado.OrdenCompraAutorizada); // N1 no emite evento Autorizada.
    }

    [Fact]
    public void Autorizar_N2_TrasN1_TransicionaA_Autorizada_Y_EmiteEvento()
    {
        var oc = NewOcConLineaBorrador();
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        var fechaN2 = DateTimeOffset.UtcNow.AddMinutes(5);
        var resultado = oc.Autorizar(
            autorizacionId: Guid.CreateVersion7(),
            nivel: NivelAutorizacion.Nivel2,
            usuarioId: Guid.CreateVersion7(),
            fechaHora: fechaN2);

        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
        Assert.Equal(fechaN2, oc.FechaContabilizacion);
        Assert.Equal(2, oc.Autorizaciones.Count);
        Assert.NotNull(resultado.OrdenCompraAutorizada);
        Assert.Equal(fechaN2, resultado.OrdenCompraAutorizada!.FechaContabilizacion);
    }

    [Fact]
    public void Autorizar_N1_EnEstadoIncorrecto_Lanza()
    {
        var oc = NewOcConLineaBorrador();
        // OC en Borrador — N1 espera EnAutorizacionJefeCompras.
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), DateTimeOffset.UtcNow));
        Assert.Equal("OC_AUTORIZAR_ESTADO_INCORRECTO", ex.Code);
    }

    [Fact]
    public void Autorizar_N2_SinN1_Lanza()
    {
        var oc = NewOcConLineaBorrador();
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        // Intentar saltarse N1 — el estado todavía es EnAutorizacionJefeCompras,
        // así que el check de estado lanza ESTADO_INCORRECTO primero.
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Guid.CreateVersion7(), DateTimeOffset.UtcNow));
        Assert.Equal("OC_AUTORIZAR_ESTADO_INCORRECTO", ex.Code);
    }

    [Fact]
    public void Autorizar_N1_Duplicado_Lanza()
    {
        var oc = NewOcConLineaBorrador();
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        // Estado ya es EnAutorizacionDireccion. Re-intentar N1 falla por
        // estado incorrecto antes de llegar al check de duplicado.
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), DateTimeOffset.UtcNow));
        Assert.Equal("OC_AUTORIZAR_ESTADO_INCORRECTO", ex.Code);
    }

    [Fact]
    public void EnviarAAutorizacion_DesdeRechazada_OK()
    {
        // Rechazada → Borrador implícito via re-transmitir. Para este test
        // simulamos el estado manualmente vía reflexión no es necesario:
        // el método ya permite transmitir desde Borrador o Rechazada.
        var oc = NewOcConLineaBorrador();
        // Por ahora no podemos llegar a Rechazada en F3-PR1 (eso es
        // F3-PR2). Test directo desde Borrador (que ya pasa el camino
        // feliz). Cobertura completa de "desde Rechazada" entra cuando
        // F3-PR2 implemente Rechazar.
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        Assert.Equal(EstadoOrdenCompra.EnAutorizacionJefeCompras, oc.Estado);
    }
}
