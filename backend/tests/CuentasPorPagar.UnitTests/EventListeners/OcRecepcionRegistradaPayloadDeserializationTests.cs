using System.Text.Json;
using Millet.CuentasPorPagar.Application.EventListeners;

namespace Millet.CuentasPorPagar.UnitTests.EventListeners;

/// <summary>
/// Almacén-por-línea 6c: Almacén YA NO emite <c>SubAlmacenId</c> en
/// <c>almacen.oc_recepcion.registrada.v1</c> (se retiró del evento y de ambos
/// espejos). El espejo de CxP <see cref="OcRecepcionRegistradaPayload"/> debe
/// tolerar <b>las dos formas</b> del payload que pueden llegar durante y después
/// del swap, y estos tests blindan ambas:
///
/// <list type="bullet">
///   <item><b>Forma post-6c</b> (sin <c>SubAlmacenId</c>): un record posicional
///   pone el default en cualquier miembro ausente, así que la ausencia mapea el
///   resto sin fallar.</item>
///   <item><b>Forma legacy</b> (aún con <c>SubAlmacenId</c>): un mensaje en vuelo
///   emitido por el Almacén viejo durante el swap. El default de System.Text.Json
///   (unknown-member = Skip) lo ignora y mapea el resto.</item>
/// </list>
///
/// <para>El worker de CxP declara su <b>propio</b> <c>JsonOpts</c> — el guard
/// gemelo del lado Compras (<c>OcRecepcionRegistradaAlmacenPayloadTests</c>) NO
/// protege a CxP. Si alguien pone <c>UnmappedMemberHandling.Disallow</c> en el
/// worker de CxP, el caso legacy cae al DLQ y el test rojo lo atrapa antes.</para>
/// </summary>
public sealed class OcRecepcionRegistradaPayloadDeserializationTests
{
    // Réplica EXACTA de las opciones del AlmacenEventListenerWorker (CxP).
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Deserializa_payload_sin_SubAlmacenId_mapeando_el_resto()
    {
        var recepcionId = Guid.NewGuid();
        var ordenCompraId = Guid.NewGuid();
        var lineaOcId = Guid.NewGuid();

        // JSON tal como Almacén lo emite desde 6c: SIN SubAlmacenId. Property
        // names en camelCase — el worker usa PropertyNameCaseInsensitive.
        var json = $$"""
        {
          "empresaId": "{{Guid.NewGuid()}}",
          "ocurridoEn": "2026-07-23T12:00:00+00:00",
          "recepcionId": "{{recepcionId}}",
          "folioRecepcion": "REC-2026-000042",
          "ordenCompraId": "{{ordenCompraId}}",
          "fechaMovimiento": "2026-07-23",
          "facturaPendiente": true,
          "cfdiRecibidoId": null,
          "observaciones": "obs",
          "lineas": [
            {
              "lineaRecepcionId": "{{Guid.NewGuid()}}",
              "lineaOcId": "{{lineaOcId}}",
              "articuloId": "{{Guid.NewGuid()}}",
              "unidadMedida": "PZA",
              "cantidad": 5,
              "costoUnitarioMxn": 10.5,
              "montoTotalMxn": 52.5
            }
          ]
        }
        """;

        var payload = JsonSerializer.Deserialize<OcRecepcionRegistradaPayload>(json, JsonOpts);

        payload.Should().NotBeNull();
        payload!.RecepcionId.Should().Be(recepcionId);
        payload.OrdenCompraId.Should().Be(ordenCompraId);
        payload.FolioRecepcion.Should().Be("REC-2026-000042");
        payload.FechaMovimiento.Should().Be(new DateOnly(2026, 7, 23));
        payload.FacturaPendiente.Should().BeTrue();
        payload.Observaciones.Should().Be("obs");
        payload.Lineas.Should().ContainSingle();
        payload.Lineas[0].LineaOcId.Should().Be(lineaOcId);
        payload.Lineas[0].Cantidad.Should().Be(5m);
    }

    [Fact]
    public void Deserializa_payload_legacy_con_SubAlmacenId_ignorando_el_campo()
    {
        var recepcionId = Guid.NewGuid();
        var ordenCompraId = Guid.NewGuid();
        var lineaOcId = Guid.NewGuid();

        // Mensaje LEGACY en vuelo: emitido por el Almacén viejo (pre-6c) durante
        // el swap del deploy — AÚN trae subAlmacenId. El espejo ya no lo declara;
        // la propiedad desconocida debe ignorarse y el resto mapear. Si alguien
        // pone UnmappedMemberHandling.Disallow en el worker de CxP, este mensaje
        // caería al DLQ y este test se rompe en rojo antes.
        var json = $$"""
        {
          "empresaId": "{{Guid.NewGuid()}}",
          "ocurridoEn": "2026-07-23T12:00:00+00:00",
          "recepcionId": "{{recepcionId}}",
          "folioRecepcion": "REC-2026-000042",
          "ordenCompraId": "{{ordenCompraId}}",
          "subAlmacenId": "{{Guid.NewGuid()}}",
          "fechaMovimiento": "2026-07-23",
          "facturaPendiente": true,
          "cfdiRecibidoId": null,
          "observaciones": "obs",
          "lineas": [
            {
              "lineaRecepcionId": "{{Guid.NewGuid()}}",
              "lineaOcId": "{{lineaOcId}}",
              "articuloId": "{{Guid.NewGuid()}}",
              "unidadMedida": "PZA",
              "cantidad": 5,
              "costoUnitarioMxn": 10.5,
              "montoTotalMxn": 52.5
            }
          ]
        }
        """;

        var payload = JsonSerializer.Deserialize<OcRecepcionRegistradaPayload>(json, JsonOpts);

        payload.Should().NotBeNull();
        payload!.RecepcionId.Should().Be(recepcionId);
        payload.OrdenCompraId.Should().Be(ordenCompraId);
        payload.FolioRecepcion.Should().Be("REC-2026-000042");
        payload.FechaMovimiento.Should().Be(new DateOnly(2026, 7, 23));
        payload.FacturaPendiente.Should().BeTrue();
        payload.Observaciones.Should().Be("obs");
        payload.Lineas.Should().ContainSingle();
        payload.Lineas[0].LineaOcId.Should().Be(lineaOcId);
        payload.Lineas[0].Cantidad.Should().Be(5m);
    }
}
