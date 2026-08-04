using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.DevolucionesProveedor;

/// <summary>
/// Tests del agregado <see cref="DevolucionAProveedor"/> (F6-PR1).
/// Cubre invariantes del ctor + transiciones del estado +
/// requerimientos de evidencia.
/// </summary>
public class DevolucionAProveedorTests
{
    [Fact]
    public void Crear_arranca_en_borrador()
    {
        var d = NuevaDev();
        d.Estado.Should().Be(EstadoDevolucionProveedor.Borrador);
        d.Lineas.Should().BeEmpty();
        d.Evidencias.Should().BeEmpty();
        d.SolicitadaAt.Should().BeBefore(DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Crear_sin_proveedor_falla()
    {
        var act = () => new DevolucionAProveedor(
            Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "motivo", Guid.NewGuid());
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("DEV_PROV_SIN_PROVEEDOR");
    }

    [Fact]
    public void Solicitar_autorizacion_sin_lineas_falla()
    {
        var d = NuevaDev();
        var act = () => d.SolicitarAutorizacion();
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("DEV_PROV_SIN_LINEAS");
    }

    [Fact]
    public void Solicitar_autorizacion_con_lineas_pasa_a_en_autorizacion()
    {
        var d = NuevaDev();
        AgregarLinea(d);
        d.SolicitarAutorizacion();
        d.Estado.Should().Be(EstadoDevolucionProveedor.EnAutorizacion);
    }

    [Fact]
    public void Autorizar_sin_evidencia_falla()
    {
        var d = NuevaDev();
        AgregarLinea(d);
        d.SolicitarAutorizacion();

        var act = () => d.Autorizar(Guid.NewGuid());
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("DEV_PROV_AUTORIZAR_SIN_EVIDENCIA");
    }

    [Fact]
    public void Autorizar_con_evidencia_pasa_a_autorizada()
    {
        var d = NuevaDev();
        AgregarLinea(d);
        AgregarEvidencia(d);
        d.SolicitarAutorizacion();
        d.Autorizar(Guid.NewGuid());
        d.Estado.Should().Be(EstadoDevolucionProveedor.Autorizada);
        d.AutorizadaPor.Should().NotBeNull();
        d.AutorizadaAt.Should().NotBeNull();
    }

    [Fact]
    public void Rechazar_desde_en_autorizacion_pasa_a_rechazada()
    {
        var d = NuevaDev();
        AgregarLinea(d);
        d.SolicitarAutorizacion();
        d.Rechazar("motivo de rechazo");
        d.Estado.Should().Be(EstadoDevolucionProveedor.Rechazada);
        d.MotivoRechazo.Should().Be("motivo de rechazo");
    }

    [Fact]
    public void MarcarRegistrada_desde_autorizada_pasa_a_registrada()
    {
        var d = AutorizadaDev();
        var movId = Guid.NewGuid();
        d.MarcarRegistrada(movId, "M-DEV2026-000001");
        d.Estado.Should().Be(EstadoDevolucionProveedor.Registrada);
        d.MovimientoSalidaId.Should().Be(movId);
        d.FolioMovimientoSalida.Should().Be("M-DEV2026-000001");
    }

    [Fact]
    public void Conciliar_desde_registrada_pasa_a_conciliada()
    {
        var d = AutorizadaDev();
        d.MarcarRegistrada(Guid.NewGuid(), "M-DEV2026-000001");
        var ncId = Guid.NewGuid();
        d.ConciliarConNcFiscal(ncId);
        d.Estado.Should().Be(EstadoDevolucionProveedor.ConciliadaConNcFiscal);
        d.NotaCreditoFiscalId.Should().Be(ncId);
        d.ConciliadaConNcFiscalAt.Should().NotBeNull();
    }

    [Fact]
    public void Conciliar_desde_no_registrada_falla()
    {
        var d = AutorizadaDev();
        var act = () => d.ConciliarConNcFiscal(Guid.NewGuid());
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("DEV_PROV_NO_REGISTRADA");
    }

    [Fact]
    public void Agregar_linea_en_estado_no_borrador_falla()
    {
        var d = NuevaDev();
        AgregarLinea(d);
        d.SolicitarAutorizacion();
        var act = () => AgregarLinea(d);
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("DEV_PROV_NO_BORRADOR");
    }

    [Fact]
    public void Linea_con_cantidad_cero_falla()
    {
        var act = () => new LineaDevolucionProveedor(
            Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), 0, "PZA", 100m);
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("LINEA_DEV_PROV_CANT_NO_POSITIVA");
    }

    [Fact]
    public void Evidencia_con_blob_ref_vacio_falla()
    {
        var act = () => new EvidenciaDevolucionProveedor(
            Guid.NewGuid(), Guid.NewGuid(), "Foto", "archivo.jpg", "");
        act.Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("EVIDENCIA_BLOB_INVALIDO");
    }

    // ─── Helpers ───
    private static DevolucionAProveedor NuevaDev() =>
        new(
            id: Guid.NewGuid(),
            empresaId: Guid.NewGuid(),
            proveedorId: Guid.NewGuid(),
            motivo: "Material no conforme",
            solicitadaPor: Guid.NewGuid(),
            recepcionOrigenId: Guid.NewGuid(),
            ordenCompraOrigenId: Guid.NewGuid());

    private static DevolucionAProveedor AutorizadaDev()
    {
        var d = NuevaDev();
        AgregarLinea(d);
        AgregarEvidencia(d);
        d.SolicitarAutorizacion();
        d.Autorizar(Guid.NewGuid());
        return d;
    }

    private static void AgregarLinea(DevolucionAProveedor d, decimal cantidad = 5m, decimal costo = 100m)
    {
        d.AgregarLinea(new LineaDevolucionProveedor(
            id: Guid.NewGuid(),
            devolucionId: d.Id,
            posicion: d.Lineas.Count + 1,
            articuloId: Guid.NewGuid(),
            cantidad: cantidad,
            unidadMedida: "PZA",
            costoUnitarioMxn: costo));
    }

    private static void AgregarEvidencia(DevolucionAProveedor d)
    {
        d.AgregarEvidencia(new EvidenciaDevolucionProveedor(
            id: Guid.NewGuid(),
            devolucionId: d.Id,
            tipoEvidencia: "Foto",
            nombreArchivo: "evidencia.jpg",
            blobRef: "blob://evidencias/test.jpg"));
    }
}
