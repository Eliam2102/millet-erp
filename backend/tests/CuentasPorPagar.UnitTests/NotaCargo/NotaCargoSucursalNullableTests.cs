using Millet.CuentasPorPagar.Domain.NotaCargo;

namespace Millet.CuentasPorPagar.UnitTests.NotaCargo;

/// <summary>
/// F6-PR3: <c>NotaCargo.SucursalId</c> es nullable para soportar notas
/// auto-generadas desde devoluciones a proveedor (Almacén sub-flujo 8.B)
/// donde el payload del evento no carga sucursal.
/// </summary>
public sealed class NotaCargoSucursalNullableTests
{
    private static Domain.NotaCargo.NotaCargo CrearSinSucursal() =>
        Domain.NotaCargo.NotaCargo.Crear(
            empresaId: Guid.NewGuid(),
            folio: FolioInternoNotaCargo.FromAnioSecuencial(2026, 7),
            proveedorId: Guid.NewGuid(),
            sucursalId: null,
            concepto: "Devolución a proveedor — REC-2026-001",
            conceptoContableId: null,
            monto: 850m,
            moneda: "MXN",
            tipoCambio: null,
            facturaOrigenId: Guid.NewGuid(),
            devolucionAProveedorId: Guid.NewGuid(),
            creadoPor: null,
            ahora: DateTimeOffset.UtcNow);

    [Fact]
    public void Crear_acepta_sucursal_nula_para_auto_generadas()
    {
        var n = CrearSinSucursal();
        n.SucursalId.Should().BeNull();
        n.DevolucionAProveedorId.Should().NotBeNull();
        n.Estado.Should().Be(EstadoNotaCargo.Borrador);
    }

    [Fact]
    public void Ciclo_completo_funciona_con_sucursal_nula()
    {
        var n = CrearSinSucursal();
        n.Autorizar(usuarioId: null, DateTimeOffset.UtcNow);
        n.Aplicar(usuarioId: null, DateTimeOffset.UtcNow);
        n.Formalizar(notaCreditoProveedorId: Guid.NewGuid(), DateTimeOffset.UtcNow);
        n.Estado.Should().Be(EstadoNotaCargo.Formalizada);
        n.SucursalId.Should().BeNull();
    }
}
