using System.Text.Json;
using Millet.Almacen.Application.Integration;
using Millet.Compras.Application.Almacen;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Salida-por-línea C3: contrato del evento
/// <c>almacen.salida_requisicion.registrada.v1</c> tras RETIRAR SubAlmacenId.
/// El único consumidor (Compras) nunca lo leyó — su espejo
/// <see cref="SalidaRequisicionRegistradaAlmacenPayload"/> no tiene el campo.
///
/// <para>El round-trip reproduce el cable real: el evento se serializa en
/// PascalCase (el <c>OutboxSaveChangesInterceptor</c> no setea
/// <c>PropertyNamingPolicy</c>) y el espejo se deserializa con
/// <c>PropertyNameCaseInsensitive</c> (idéntico al
/// <c>AlmacenEventListenerWorker</c> de Compras). Sin BD ni host.</para>
/// </summary>
public class SalidaEventoContratoTests
{
    // Mismas opciones que el consumidor real (AlmacenEventListenerWorker.JsonOpts).
    private static readonly JsonSerializerOptions ConsumerOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Evento_sin_subalmacen_deserializa_en_el_espejo_de_compras()
    {
        var salidaId = Guid.NewGuid();
        var rqId = Guid.NewGuid();
        var lineaSalidaId = Guid.NewGuid();
        var lineaRqId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        var evento = new SalidaRequisicionRegistradaIntegrationEvent(
            EmpresaId: Guid.NewGuid(),
            OcurridoEn: DateTimeOffset.UtcNow,
            SalidaId: salidaId,
            FolioSalida: "SAL-2027-000123",
            FechaMovimiento: new DateOnly(2027, 3, 10),
            RqId: rqId,
            EsPorVale: false,
            PersonaDestinatariaId: null,
            ValeBlobRef: null,
            Lineas: new[]
            {
                new LineaSalidaPayload(
                    LineaSalidaId: lineaSalidaId,
                    ArticuloId: articuloId,
                    UnidadMedida: "PZA",
                    Cantidad: 4m,
                    CostoUnitarioMxn: 25.5m,
                    MontoTotalMxn: 102m,
                    CentroCostoId: null,
                    ProyectoId: null,
                    LineaRqId: lineaRqId),
            });

        // Publisher: PascalCase, como el OutboxSaveChangesInterceptor.
        var json = JsonSerializer.Serialize(evento);
        // El evento ya no lleva el campo retirado (mutación: re-agregar
        // SubAlmacenId al record → el JSON lo contiene → esta aserción falla).
        json.Should().NotContain("SubAlmacenId");

        // Consumer: el espejo de Compras (que nunca tuvo el campo) deserializa
        // sin throw y con los campos vivos intactos.
        var espejo = JsonSerializer.Deserialize<SalidaRequisicionRegistradaAlmacenPayload>(
            json, ConsumerOpts);

        espejo.Should().NotBeNull();
        espejo!.SalidaId.Should().Be(salidaId);
        espejo.FolioSalida.Should().Be("SAL-2027-000123");
        espejo.RqId.Should().Be(rqId);
        espejo.EsPorVale.Should().BeFalse();
        espejo.Lineas.Should().ContainSingle();
        espejo.Lineas[0].LineaSalidaId.Should().Be(lineaSalidaId);
        espejo.Lineas[0].LineaRqId.Should().Be(lineaRqId);
        espejo.Lineas[0].ArticuloId.Should().Be(articuloId);
    }
}
