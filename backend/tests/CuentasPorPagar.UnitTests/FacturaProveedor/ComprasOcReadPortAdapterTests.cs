using FluentAssertions;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.CuentasPorPagar.Infrastructure.Compras;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

/// <summary>
/// Regresión del incidente 2026-07-15 (verificación e2e): el adapter
/// exponía <c>Total</c> = Σ(cantidad×precio) SIN IVA, mientras la
/// conciliación 3-way lo compara contra el total de la factura CON IVA
/// — toda factura gravada excedía la tolerancia default ($0.99) y se
/// cancelaba como <c>RechazadaPorTolerancia</c>. El total correcto es
/// <see cref="OrdenCompra.CalcularTotales"/>.TotalAPagar (diseño §4.9).
///
/// <para>
/// Se testea <see cref="ComprasOcReadPortAdapter.MapToDto"/> directo
/// sobre el agregado (sin EF): el provider InMemory no materializa
/// complex types (<c>DescuentoLinea</c>) y el query del adapter es un
/// Include trivial sin lógica propia.
/// </para>
/// </summary>
public sealed class ComprasOcReadPortAdapterTests
{
    [Fact]
    public void MapToDto_expone_total_a_pagar_con_iva()
    {
        // 9 × 150 = 1350 subtotal + 216 IVA16 = 1566 — igual que
        // CalcularTotales() y que el total del CFDI del proveedor.
        var oc = NewOcAutorizadaConUnaLinea(cantidad: 9m, precioUnitario: 150m);

        var dto = ComprasOcReadPortAdapter.MapToDto(oc);

        dto.Total.Should().Be(oc.CalcularTotales().TotalAPagar);
        dto.Total.Should().Be(1566m);
        dto.Estado.Should().Be("Autorizada");
        dto.Lineas.Should().ContainSingle()
            .Which.PrecioUnitario.Should().Be(150m);
    }

    [Fact]
    public void MapToDto_total_refleja_descuento_de_linea()
    {
        // 40 × 25 = 1000 bruto − 10% descuento = 900 + 144 IVA16 = 1044.
        var oc = NewOcAutorizadaConUnaLinea(
            cantidad: 40m,
            precioUnitario: 25m,
            descuento: new DescuentoLinea(DescuentoTipo.Porcentaje, 10m));

        var dto = ComprasOcReadPortAdapter.MapToDto(oc);

        dto.Total.Should().Be(oc.CalcularTotales().TotalAPagar);
        dto.Total.Should().Be(1044m);
    }

    private static OrdenCompra NewOcAutorizadaConUnaLinea(
        decimal cantidad, decimal precioUnitario, DescuentoLinea? descuento = null)
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
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test adapter");

        oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: cantidad,
            unidadMedida: "PZA",
            precioUnitario: precioUnitario,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            descuento: descuento);

        // Atajo a Autorizada: enviar + N1 + N2 (las invariantes de
        // adjuntos viven en Application, no en el agregado).
        var ahora = DateTimeOffset.UtcNow;
        oc.EnviarAAutorizacion(ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel1, Guid.CreateVersion7(), ahora);
        oc.Autorizar(Guid.CreateVersion7(), NivelAutorizacion.Nivel2, Guid.CreateVersion7(), ahora);
        return oc;
    }
}
