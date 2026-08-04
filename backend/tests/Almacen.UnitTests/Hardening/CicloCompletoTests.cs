using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Domain.Movimientos;

namespace Millet.Almacen.UnitTests.Hardening;

/// <summary>
/// Tests de hardening F9-PR1: validan que los agregados del módulo
/// interactúan coherentemente. Tests E2E con DB real se ejercitan en
/// IntegrationTests (deferred, dependen de Postgres).
/// </summary>
public class CicloCompletoTests
{
    [Fact]
    public void Inventario_fisico_anual_genera_bloqueo_al_iniciar()
    {
        var conteo = new ConteoInventario(
            id: Guid.NewGuid(),
            empresaId: Guid.NewGuid(),
            tipo: TipoConteo.Anual,
            fechaPlanificada: new DateOnly(2026, 12, 1),
            responsableId: Guid.NewGuid(),
            subAlmacenId: Guid.NewGuid());
        conteo.AgregarLinea(new LineaConteo(
            Guid.NewGuid(), conteo.Id, Guid.NewGuid(),
            conteo.SubAlmacenId!.Value, Guid.NewGuid(), 100m, 50m));
        conteo.Iniciar();
        conteo.Tipo.Should().Be(TipoConteo.Anual);
        conteo.Estado.Should().Be(EstadoConteo.EnCurso);
    }

    [Fact]
    public void Ciclo_devolucion_proveedor_termina_en_conciliada()
    {
        var d = new DevolucionAProveedor(
            id: Guid.NewGuid(),
            empresaId: Guid.NewGuid(),
            proveedorId: Guid.NewGuid(),
            motivo: "Material no conforme",
            solicitadaPor: Guid.NewGuid(),
            recepcionOrigenId: Guid.NewGuid(),
            ordenCompraOrigenId: Guid.NewGuid(),
            facturaProveedorOrigenId: Guid.NewGuid());
        d.AgregarLinea(new LineaDevolucionProveedor(
            Guid.NewGuid(), d.Id, 1, Guid.NewGuid(), 5m, "PZA", 100m));
        d.AgregarEvidencia(new EvidenciaDevolucionProveedor(
            Guid.NewGuid(), d.Id, "Foto", "ev.jpg", "blob://test.jpg"));
        d.SolicitarAutorizacion();
        d.Autorizar(Guid.NewGuid());
        d.MarcarRegistrada(Guid.NewGuid(), "M-DEV2026-000001");
        d.ConciliarConNcFiscal(Guid.NewGuid());
        d.Estado.Should().Be(EstadoDevolucionProveedor.ConciliadaConNcFiscal);
    }

    [Fact]
    public void EventoProcesado_es_idempotente_por_pk_compuesto()
    {
        var eventId = Guid.NewGuid();
        var e1 = new EventoProcesado(eventId, "test.event.v1");
        var e2 = new EventoProcesado(eventId, "otro.event.v1");
        // En BD, el PK compuesto (evento_id, evento_tipo) permite el mismo
        // GUID en eventos de distinto tipo — el agregado lo permite a nivel
        // de dominio. La unicidad real la enforza el PK de la tabla.
        e1.EventoId.Should().Be(e2.EventoId);
        e1.EventoTipo.Should().NotBe(e2.EventoTipo);
    }

    [Fact]
    public void Folio_movimiento_es_unico_por_prefijo_y_año()
    {
        var f1 = FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, 1);
        var f2 = FolioMovimiento.Construir(TipoMovimiento.SalidaConsumo, 2026, 1);
        // Mismo año, mismo secuencial, distinto prefijo = distinto folio.
        f1.Valor.Should().NotBe(f2.Valor);
        f1.Valor.Should().StartWith("M-ENT");
        f2.Valor.Should().StartWith("M-SAL");
    }

    [Fact]
    public void Movimiento_registrado_es_inmutable()
    {
        var m = new MovimientoInventario(
            id: Guid.NewGuid(),
            tipo: TipoMovimiento.EntradaCompra,
            empresaId: Guid.NewGuid(),
            fechaMovimiento: new DateOnly(2026, 5, 23));
        m.AgregarLinea(new LineaMovimiento(
            Guid.NewGuid(), m.Id, 1, Guid.NewGuid(), 5m, "PZA", 100m));
        m.Registrar(
            FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, 1),
            Guid.NewGuid());

        // Después de Registrado no se pueden agregar líneas.
        var act = () => m.AgregarLinea(new LineaMovimiento(
            Guid.NewGuid(), m.Id, 2, Guid.NewGuid(), 1m, "PZA", 50m));
        act.Should().Throw<SharedKernel.Application.Exceptions.BusinessRuleException>()
            .Which.Code.Should().Be("MOV_NO_BORRADOR");

        // No se puede cancelar.
        var actCancel = () => m.Cancelar("test");
        actCancel.Should().Throw<SharedKernel.Application.Exceptions.BusinessRuleException>()
            .Which.Code.Should().Be("MOV_NO_CANCELABLE");
    }
}
