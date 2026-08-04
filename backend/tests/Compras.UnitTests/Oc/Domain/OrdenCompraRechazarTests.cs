using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests del método <see cref="OrdenCompra.Rechazar"/> (F3-PR2).
/// </summary>
public class OrdenCompraRechazarTests
{
    private static OrdenCompra NewOcEnAutorizacionN1()
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
            motivoSinRequisicion: "Test rechazar");
        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 5m,
            unidadMedida: "PZA",
            precioUnitario: 50m,
            departamentoSolicitanteId: Guid.CreateVersion7());
        oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        return oc;
    }

    [Fact]
    public void Rechazar_DesdeN1_TransicionaA_Rechazada_Y_EmiteEvento()
    {
        var oc = NewOcEnAutorizacionN1();
        var motivoId = Guid.CreateVersion7();
        var ahora = DateTimeOffset.UtcNow;

        var evento = oc.Rechazar(
            autorizacionId: Guid.CreateVersion7(),
            usuarioId: Guid.CreateVersion7(),
            fechaHora: ahora,
            motivoRechazoId: motivoId,
            motivoRechazoTexto: "duplicada",
            notas: null);

        Assert.Equal(EstadoOrdenCompra.Rechazada, oc.Estado);
        Assert.Equal(motivoId, oc.MotivoRechazoId);
        Assert.Equal("duplicada", oc.MotivoRechazoTexto);
        Assert.Single(oc.Autorizaciones);
        Assert.Equal(ResultadoAutorizacionOc.Rechazado, oc.Autorizaciones.First().Resultado);
        Assert.Equal(NivelAutorizacion.Nivel1, oc.Autorizaciones.First().Nivel);
        Assert.Equal(motivoId, evento.MotivoRechazoId);
        Assert.Equal(NivelAutorizacion.Nivel1, evento.NivelRechazo);
    }

    [Fact]
    public void Rechazar_DesdeN2_TransicionaA_Rechazada_NivelN2()
    {
        var oc = NewOcEnAutorizacionN1();
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        var motivoId = Guid.CreateVersion7();
        var evento = oc.Rechazar(
            autorizacionId: Guid.CreateVersion7(),
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoRechazoId: motivoId,
            motivoRechazoTexto: null);

        Assert.Equal(EstadoOrdenCompra.Rechazada, oc.Estado);
        Assert.Equal(NivelAutorizacion.Nivel2, evento.NivelRechazo);
        // N1 + rechazo N2 = 2 filas en autorizaciones.
        Assert.Equal(2, oc.Autorizaciones.Count);
    }

    [Fact]
    public void Rechazar_DesdeBorrador_Lanza()
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000099"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));

        var ex = Assert.Throws<BusinessRuleException>(() => oc.Rechazar(
            autorizacionId: Guid.CreateVersion7(),
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoRechazoId: Guid.CreateVersion7()));
        Assert.Equal("OC_RECHAZAR_ESTADO_INVALIDO", ex.Code);
    }

    [Fact]
    public void Rechazar_MotivoVacio_Lanza()
    {
        var oc = NewOcEnAutorizacionN1();
        var ex = Assert.Throws<BusinessRuleException>(() => oc.Rechazar(
            autorizacionId: Guid.CreateVersion7(),
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoRechazoId: Guid.Empty));
        Assert.Equal("OC_RECHAZAR_MOTIVO_REQUERIDO", ex.Code);
    }

    [Fact]
    public void Rechazar_ReTransmitir_CambiaEstadoARechazadaYTransmite()
    {
        var oc = NewOcEnAutorizacionN1();
        oc.Rechazar(
            autorizacionId: Guid.CreateVersion7(),
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            motivoRechazoId: Guid.CreateVersion7());

        // Desde Rechazada se puede re-transmitir (mismo método EnviarAAutorizacion).
        var evento = oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        Assert.Equal(EstadoOrdenCompra.EnAutorizacionJefeCompras, oc.Estado);
        Assert.NotNull(evento);
    }
}
