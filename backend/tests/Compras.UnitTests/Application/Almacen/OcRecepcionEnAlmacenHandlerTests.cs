using System.Text.Json;
using Millet.Compras.Application.Almacen;

namespace Millet.Compras.UnitTests.Application.Almacen;

/// <summary>
/// Tests del contrato espejo
/// <see cref="OcRecepcionRegistradaAlmacenPayload"/> que el
/// <c>AlmacenEventListenerWorker</c> de Compras deserializa al recibir
/// <c>almacen.oc_recepcion.registrada.v1</c> del topic
/// <c>almacen-events</c>.
///
/// <para>
/// Foco: verificar compatibilidad de schema. La lógica del handler EF
/// (incremento + dedupe) requiere Postgres real porque
/// <c>OrdenCompra</c> usa owned types (<c>DescuentoLinea</c>) que el
/// InMemory provider de EF Core no soporta. Esa cobertura vive en
/// integration tests; aquí blindamos solo el contrato.
/// </para>
///
/// <para>
/// La incrementalidad de <c>CantidadRecibida</c> queda cubierta a nivel
/// dominio en <c>OrdenCompraCancelarConRecepcionesTests</c>
/// (<c>RegistrarRecepcionLinea</c> con acumulado).
/// </para>
/// </summary>
public sealed class OcRecepcionRegistradaAlmacenPayloadTests
{
    // Mismas opciones que el worker productivo: case-insensitive para
    // aceptar tanto PascalCase (formato real del
    // OutboxSaveChangesInterceptor) como camelCase (formato que asumían
    // mis tests originales y que produce un serializer "convencional").
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Deserializa_payload_camelCase_como_lo_publica_Almacen()
    {
        // Body realista publicado por OutboxPublisherWorker<AlmacenDbContext>:
        // camelCase, DateOnly como "YYYY-MM-DD", decimal como número.
        var body = """
        {
          "empresaId": "00000000-0000-0000-0000-000000000001",
          "ocurridoEn": "2026-05-24T20:00:00Z",
          "recepcionId": "00000000-0000-0000-0000-000000000010",
          "folioRecepcion": "REC-MID2026-000010",
          "ordenCompraId": "00000000-0000-0000-0000-000000000020",
          "fechaMovimiento": "2026-05-24",
          "facturaPendiente": false,
          "cfdiRecibidoId": null,
          "observaciones": null,
          "lineas": [
            {
              "lineaRecepcionId": "00000000-0000-0000-0000-000000000041",
              "lineaOcId": "00000000-0000-0000-0000-000000000051",
              "articuloId": "00000000-0000-0000-0000-000000000061",
              "unidadMedida": "PZA",
              "cantidad": 3,
              "costoUnitarioMxn": 100,
              "montoTotalMxn": 300
            }
          ]
        }
        """;

        var p = JsonSerializer.Deserialize<OcRecepcionRegistradaAlmacenPayload>(body, JsonOpts);

        p.Should().NotBeNull();
        p!.RecepcionId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000010"));
        p.OrdenCompraId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000020"));
        p.FechaMovimiento.Should().Be(new DateOnly(2026, 5, 24));
        p.FacturaPendiente.Should().BeFalse();
        p.Lineas.Should().HaveCount(1);
        p.Lineas[0].LineaOcId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000051"));
        p.Lineas[0].Cantidad.Should().Be(3m);
        p.Lineas[0].CostoUnitarioMxn.Should().Be(100m);
    }

    [Fact]
    public void Deserializa_variante_B_con_LineaOcId_null_y_facturaPendiente_true()
    {
        var body = """
        {
          "empresaId": "00000000-0000-0000-0000-000000000001",
          "ocurridoEn": "2026-05-24T20:00:00Z",
          "recepcionId": "00000000-0000-0000-0000-000000000011",
          "folioRecepcion": "REC-MID2026-000011",
          "ordenCompraId": "00000000-0000-0000-0000-000000000021",
          "fechaMovimiento": "2026-05-24",
          "facturaPendiente": true,
          "cfdiRecibidoId": null,
          "observaciones": "Materiales directos no-vidrio",
          "lineas": [
            {
              "lineaRecepcionId": "00000000-0000-0000-0000-000000000042",
              "lineaOcId": null,
              "articuloId": "00000000-0000-0000-0000-000000000062",
              "unidadMedida": "KG",
              "cantidad": 500.5,
              "costoUnitarioMxn": 12.75,
              "montoTotalMxn": 6381.375
            }
          ]
        }
        """;

        var p = JsonSerializer.Deserialize<OcRecepcionRegistradaAlmacenPayload>(body, JsonOpts);

        p.Should().NotBeNull();
        p!.FacturaPendiente.Should().BeTrue();
        p.Lineas[0].LineaOcId.Should().BeNull();
        p.Lineas[0].Cantidad.Should().Be(500.5m);
        p.Lineas[0].UnidadMedida.Should().Be("KG");
    }

    [Fact]
    public void Deserializa_payload_PascalCase_real_con_SubAlmacenId_legacy_ignorado()
    {
        // Formato real que produce el OutboxSaveChangesInterceptor:
        // JsonSerializer.Serialize(ev, ev.GetType(), { WriteIndented = false })
        // sin PropertyNamingPolicy → PascalCase. Antes del fix
        // <c>PropertyNameCaseInsensitive=true</c>, este body se
        // deserializaba a Guids vacíos y el handler explotaba con
        // EntityNotFoundException al buscar OC '00000000-...'.
        //
        // Almacén-por-línea 6c: este body CONSERVA "SubAlmacenId" a propósito.
        // El campo se retiró del evento y del espejo, pero un mensaje legacy en
        // vuelo (emitido por el Almacén viejo durante el swap) aún lo trae. El
        // espejo (sin UnmappedMemberHandling.Disallow) debe ignorarlo y mapear el
        // resto — este test es el guard de esa tolerancia; si alguien pone
        // Disallow, se rompe aquí antes de que un mensaje legacy caiga al DLQ.
        var body = """
        {
          "EmpresaId": "00000000-0000-0000-0000-000000000001",
          "OcurridoEn": "2026-05-24T20:00:00Z",
          "RecepcionId": "00000000-0000-0000-0000-000000000010",
          "FolioRecepcion": "REC-MID2026-000010",
          "OrdenCompraId": "00000000-0000-0000-0000-000000000020",
          "SubAlmacenId": "00000000-0000-0000-0000-000000000030",
          "FechaMovimiento": "2026-05-24",
          "FacturaPendiente": false,
          "CfdiRecibidoId": null,
          "Observaciones": null,
          "Lineas": [
            {
              "LineaRecepcionId": "00000000-0000-0000-0000-000000000041",
              "LineaOcId": "00000000-0000-0000-0000-000000000051",
              "ArticuloId": "00000000-0000-0000-0000-000000000061",
              "UnidadMedida": "PZA",
              "Cantidad": 3,
              "CostoUnitarioMxn": 100,
              "MontoTotalMxn": 300
            }
          ]
        }
        """;

        var p = JsonSerializer.Deserialize<OcRecepcionRegistradaAlmacenPayload>(body, JsonOpts);

        p.Should().NotBeNull();
        p!.RecepcionId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000010"));
        p.OrdenCompraId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000020"));
        p.Lineas[0].LineaOcId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000051"));
        p.Lineas[0].Cantidad.Should().Be(3m);
    }

    [Fact]
    public void EventType_constante_match_la_convencion_del_topic_almacen()
    {
        // Defensa contra typos al renombrar; ambos lados (Almacén
        // emisor + Compras consumidor) deben coincidir en el literal.
        OcRecepcionEnAlmacenHandler.EventType
            .Should().Be("almacen.oc_recepcion.registrada.v1");
    }
}
