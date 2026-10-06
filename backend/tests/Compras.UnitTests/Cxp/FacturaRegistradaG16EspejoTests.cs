using System.Text.Json;
using Millet.Compras.Application.Oc.Eventos.Cxp;

namespace Millet.Compras.UnitTests.Cxp;

/// <summary>
/// G1.6: el JSON NUEVO de <c>factura.registrada.v1</c> (con bloque contable)
/// se sigue leyendo igual en el espejo de Compras, que no se modifica.
/// </summary>
public sealed class FacturaRegistradaG16EspejoTests
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Espejo_lee_json_nuevo_igual_que_antes()
    {
        const string json = """
{
  "EmpresaId": "11111111-1111-1111-1111-111111111111",
  "OcurridoEn": "2026-01-01T00:00:00+00:00",
  "FacturaProveedorId": "22222222-2222-2222-2222-222222222222",
  "OrdenCompraId": "33333333-3333-3333-3333-333333333333",
  "TotalFactura": 1150,
  "Lineas": [{ "LineaFacturaId": "44444444-4444-4444-4444-444444444444", "LineaOcId": "55555555-5555-5555-5555-555555555555", "Cantidad": 2, "Importe": 1000, "CentroCostoId": "66666666-6666-6666-6666-666666666666" }],
  "LineasAcumuladasOc": [{ "LineaOcId": "55555555-5555-5555-5555-555555555555", "CantidadAcumulada": 5 }],
  "ProveedorId": "77777777-7777-7777-7777-777777777777",
  "Uuid": "UUID-1", "Subtotal": 1000, "Iva": 160, "Retenciones": 10,
  "RetencionesDetalle": [{ "Impuesto": "001", "Tasa": null, "Importe": 10 }],
  "Moneda": "USD", "TipoCambio": 17.5,
  "SucursalId": "88888888-8888-8888-8888-888888888888",
  "CentroCostoId": "66666666-6666-6666-6666-666666666666"
}
""";

        var p = JsonSerializer.Deserialize<FacturaProveedorRegistradaPayload>(json, Opts)!;

        p.TotalFactura.Should().Be(1150m);
        p.Lineas.Should().ContainSingle().Which.Cantidad.Should().Be(2m);
        p.LineasAcumuladasOc.Should().ContainSingle().Which.CantidadAcumulada.Should().Be(5m);
    }
}
