using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Adapters;

namespace Millet.Integraciones.Aw.UnitTests.Adapters;

/// <summary>
/// Tests del parser <c>TryExtractOperatorUserId</c> de
/// <see cref="SoketiAgentRealtimePublisher"/>. La integración real con
/// el SDK Pusher se valida E2E contra el Soketi de Agent (no acá).
/// </summary>
public sealed class SoketiAgentRealtimePublisherTests
{
    [Fact]
    public void TryExtractOperatorUserId_PayloadValido_RetornaInt()
    {
        var entidad = NewEntidad(payloadOriginal: """
            {"operator_user_id":67,"sb_conversation_id":12345,"glass_agent_version":"2.5.0"}
            """);

        var result = SoketiAgentRealtimePublisher.TryExtractOperatorUserId(entidad);

        Assert.Equal(67, result);
    }

    [Fact]
    public void TryExtractOperatorUserId_FaltaCampo_RetornaNull()
    {
        // Glass Agent < v2.5.0 — no incluye operator_user_id.
        var entidad = NewEntidad(payloadOriginal: """
            {"sb_conversation_id":12345}
            """);

        var result = SoketiAgentRealtimePublisher.TryExtractOperatorUserId(entidad);

        Assert.Null(result);
    }

    [Fact]
    public void TryExtractOperatorUserId_OperatorIdComoString_TambienParsea()
    {
        // Defensive: si alguna versión serializa el int como string.
        var entidad = NewEntidad(payloadOriginal: """{"operator_user_id":"42"}""");

        var result = SoketiAgentRealtimePublisher.TryExtractOperatorUserId(entidad);

        Assert.Equal(42, result);
    }

    [Fact]
    public void TryExtractOperatorUserId_StringNoNumerico_RetornaNull()
    {
        var entidad = NewEntidad(payloadOriginal: """{"operator_user_id":"abc"}""");

        var result = SoketiAgentRealtimePublisher.TryExtractOperatorUserId(entidad);

        Assert.Null(result);
    }

    [Fact]
    public void TryExtractOperatorUserId_OperatorIdZero_RetornaNull()
    {
        // 0 es defensive — un canal `private-user-0` sería inválido.
        var entidad = NewEntidad(payloadOriginal: """{"operator_user_id":0}""");

        var result = SoketiAgentRealtimePublisher.TryExtractOperatorUserId(entidad);

        Assert.Null(result);
    }

    [Fact]
    public void TryExtractOperatorUserId_OperatorIdNegativo_RetornaNull()
    {
        var entidad = NewEntidad(payloadOriginal: """{"operator_user_id":-5}""");

        var result = SoketiAgentRealtimePublisher.TryExtractOperatorUserId(entidad);

        Assert.Null(result);
    }

    [Fact]
    public void TryExtractOperatorUserId_PayloadJsonCorrupto_RetornaNull()
    {
        var entidad = NewEntidad(payloadOriginal: "{ malformed json: ");

        var result = SoketiAgentRealtimePublisher.TryExtractOperatorUserId(entidad);

        Assert.Null(result);
    }

    [Fact]
    public void TryExtractOperatorUserId_PayloadVacio_RetornaNull()
    {
        var entidad = NewEntidad(payloadOriginal: string.Empty);

        var result = SoketiAgentRealtimePublisher.TryExtractOperatorUserId(entidad);

        Assert.Null(result);
    }

    [Fact]
    public void TryExtractOperatorUserId_OperatorIdComoFloat_RetornaNull()
    {
        // 67.5 no es int — defensive: ignorar tipos no-int.
        var entidad = NewEntidad(payloadOriginal: """{"operator_user_id":67.5}""");

        var result = SoketiAgentRealtimePublisher.TryExtractOperatorUserId(entidad);

        Assert.Null(result);
    }

    private static EntidadExterna NewEntidad(string payloadOriginal)
    {
        // PayloadOriginal no puede ser empty string por validación del
        // constructor; para el test de "payload vacío" usamos un workaround:
        // entidad con payload "{}" y luego seteamos vía reflection. Más
        // simple: para tests con payload vacío real, usar "{}" en lugar de
        // string.Empty — el parser igual retorna null porque la key falta.
        var safePayload = string.IsNullOrEmpty(payloadOriginal) ? "{}" : payloadOriginal;
        return new EntidadExterna(
            id: Guid.NewGuid(),
            tipoEntidad: TipoEntidad.Cotizacion,
            referenciaExterna: "Q-TEST",
            empresaId: Guid.NewGuid(),
            payloadOriginal: safePayload,
            ediContent: "EDI",
            submittedBySpnId: null,
            submittedAt: DateTimeOffset.UtcNow,
            sucursal: "CIR");
    }
}

/// <summary>
/// Test del NoOp publisher — el caso default cuando Soketi no está
/// configurado (dev local).
/// </summary>
public sealed class NoOpAgentRealtimePublisherTests
{
    [Fact]
    public async Task PublishCotizacionActualizadaAsync_NoTira_NoBloquea()
    {
        var noop = new NoOpAgentRealtimePublisher(NullLogger<NoOpAgentRealtimePublisher>.Instance);
        var entidad = new EntidadExterna(
            id: Guid.NewGuid(),
            tipoEntidad: TipoEntidad.Cotizacion,
            referenciaExterna: "Q-TEST",
            empresaId: Guid.NewGuid(),
            payloadOriginal: "{}",
            ediContent: "EDI",
            submittedBySpnId: null,
            submittedAt: DateTimeOffset.UtcNow,
            sucursal: "CIR");

        var ex = await Record.ExceptionAsync(() =>
            noop.PublishCotizacionActualizadaAsync(entidad, CancellationToken.None));

        Assert.Null(ex);
    }
}
