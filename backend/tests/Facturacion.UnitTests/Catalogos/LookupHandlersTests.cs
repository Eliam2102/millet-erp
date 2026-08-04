using Millet.Facturacion.Application.Catalogos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Catalogos;

/// <summary>
/// FAC-UX-PR1 — lookups de clientes/productos para los pickers del
/// formulario de emisión (sin GUIDs capturados a mano).
/// </summary>
public sealed class LookupHandlersTests
{
    [Fact]
    public async Task Clientes_lookup_marca_datos_fiscales_completos()
    {
        var completo = new ClienteBusquedaItem(
            Guid.NewGuid(), "CLI-1", "Cliente SA", "AAA010101AAA", "601", "97000",
            "G03", "03", "PUE", "MXN", false,
            NumRegIdTrib: "US123456789", PaisResidencia: "USA",
            DomicilioExtranjeroCalle: "123 Main St", DomicilioExtranjeroEstado: "Texas",
            DomicilioExtranjeroCodigoPostal: "75001");
        var incompleto = new ClienteBusquedaItem(
            Guid.NewGuid(), "CLI-2", "Otro SA", null, null, null,
            null, null, null, "MXN", false,
            NumRegIdTrib: null, PaisResidencia: null, DomicilioExtranjeroCalle: null,
            DomicilioExtranjeroEstado: null, DomicilioExtranjeroCodigoPostal: null);
        var handler = new ClientesLookupHandler(
            new FakeClientesReadPort(busqueda: [completo, incompleto]));

        var items = await handler.Handle(new ClientesLookupQuery(), CancellationToken.None);

        items.Should().HaveCount(2);
        items[0].DatosFiscalesCompletos.Should().BeTrue();
        items[1].DatosFiscalesCompletos.Should().BeFalse();
        // El receptor extranjero viaja al FE para prellenar el CCE (Fase 2b).
        items[0].NumRegIdTrib.Should().Be("US123456789");
        items[0].PaisResidencia.Should().Be("USA");
        items[0].DomicilioExtranjeroEstado.Should().Be("Texas");
    }

    [Fact]
    public async Task Productos_lookup_marca_datos_fiscales_completos()
    {
        var completo = new ProductoAwBusquedaItem(
            Guid.NewGuid(), "PROD-1", "Vidrio templado", "M2", "43211701", "MTK", "02", 0.16m, null, null,
            FraccionArancelaria: "70071100", UnidadAduana: "06", PesoUnitarioKg: 12.5m);
        var incompleto = new ProductoAwBusquedaItem(
            Guid.NewGuid(), "PROD-2", "Sin claves", "PZA", null, null, null, null, null, null,
            FraccionArancelaria: null, UnidadAduana: null, PesoUnitarioKg: null);
        var handler = new ProductosAwLookupHandler(
            new FakeProductosReadPort(busqueda: [completo, incompleto]));

        var items = await handler.Handle(new ProductosAwLookupQuery(), CancellationToken.None);

        items.Should().HaveCount(2);
        items[0].DatosFiscalesCompletos.Should().BeTrue();
        items[1].DatosFiscalesCompletos.Should().BeFalse();
        // Los datos de aduana viajan al FE para prellenar la línea CCE (Fase 1b).
        items[0].FraccionArancelaria.Should().Be("70071100");
        items[0].UnidadAduana.Should().Be("06");
        items[0].PesoUnitarioKg.Should().Be(12.5m);
    }
}
