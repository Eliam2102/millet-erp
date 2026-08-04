using Millet.Integraciones.Aw.Application.Queries.ListarCotizaciones;
using Millet.Integraciones.Aw.Application.Queries.ObtenerCotizacionDetalle;
using Millet.Integraciones.Aw.Application.Queries.ObtenerHistorialCotizacion;
using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.UnitTests.Application;

/// <summary>
/// Tests unit de los 3 query handlers de PR D. Usa EF InMemory.
/// </summary>
public sealed class QueriesHandlerTests
{
    [Fact]
    public async Task Detalle_NoExiste_RetornaNull()
    {
        var db = await InMemoryDb.CreateAsync();
        var handler = new ObtenerCotizacionDetalleHandler(db);

        var result = await handler.Handle(
            new ObtenerCotizacionDetalleQuery(Guid.NewGuid()),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Detalle_Correlated_RetornaAwDocIdYCorrelacionLegacyNull()
    {
        // PR #201: el flow callback per-EDI persiste AwDocId en la entidad
        // pero NO crea filas en `correlaciones` (tabla legacy). El detalle
        // sigue exponiendo AwDocId via la entidad; la propiedad Correlacion
        // del response solo se popula con datos legacy de antes de PR #201.
        var db = await InMemoryDb.CreateAsync();
        var entidad = new EntidadExterna(
            id: Guid.NewGuid(),
            tipoEntidad: TipoEntidad.Cotizacion,
            referenciaExterna: "Q-001",
            empresaId: Guid.NewGuid(),
            payloadOriginal: "{}",
            ediContent: "EDI",
            submittedBySpnId: Guid.NewGuid(),
            submittedAt: DateTimeOffset.UtcNow,
            sucursal: "CIR");
        entidad.MarcarCorrelacionadaDirectamente(42L, DateTimeOffset.UtcNow);
        db.EntidadesExternas.Add(entidad);
        await db.SaveChangesAsync();

        var handler = new ObtenerCotizacionDetalleHandler(db);

        var result = await handler.Handle(
            new ObtenerCotizacionDetalleQuery(entidad.Id),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(entidad.Id);
        result.Estado.Should().Be(EstadoEntidad.Correlated);
        result.AwDocId.Should().Be(42L);
        result.Correlacion.Should().BeNull();
    }

    [Fact]
    public async Task Listar_SinFiltros_RetornaTodosOrdenadosDescPorSubmittedAt()
    {
        var db = await InMemoryDb.CreateAsync();
        var empresaId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            db.EntidadesExternas.Add(NewCotizacion(empresaId, $"Q-{i:D3}",
                submittedAt: DateTimeOffset.UtcNow.AddMinutes(-i)));
        }
        await db.SaveChangesAsync();

        var handler = new ListarCotizacionesHandler(db);

        var result = await handler.Handle(
            new ListarCotizacionesQuery(),
            CancellationToken.None);

        result.Total.Should().Be(5);
        result.Items.Should().HaveCount(5);
        // Más reciente primero (Q-000 fue submitted hace 0 minutos).
        result.Items[0].ReferenciaExterna.Should().Be("Q-000");
    }

    [Fact]
    public async Task Listar_FiltroEstado_RetornaSoloEseEstado()
    {
        var db = await InMemoryDb.CreateAsync();
        var empresaId = Guid.NewGuid();
        var submitted = NewCotizacion(empresaId, "Q-S");
        var correlated = NewCotizacion(empresaId, "Q-C");
        correlated.MarcarCorrelacionadaDirectamente(1L, DateTimeOffset.UtcNow);
        db.EntidadesExternas.AddRange(submitted, correlated);
        await db.SaveChangesAsync();

        var handler = new ListarCotizacionesHandler(db);
        var result = await handler.Handle(
            new ListarCotizacionesQuery(Estado: EstadoEntidad.Correlated),
            CancellationToken.None);

        result.Total.Should().Be(1);
        result.Items[0].ReferenciaExterna.Should().Be("Q-C");
    }

    [Fact]
    public async Task Listar_LimitYOffset_PaginanCorrectamente()
    {
        var db = await InMemoryDb.CreateAsync();
        var empresaId = Guid.NewGuid();
        for (var i = 0; i < 10; i++)
        {
            db.EntidadesExternas.Add(NewCotizacion(empresaId, $"Q-{i:D3}",
                submittedAt: DateTimeOffset.UtcNow.AddMinutes(-i)));
        }
        await db.SaveChangesAsync();

        var handler = new ListarCotizacionesHandler(db);

        var page1 = await handler.Handle(
            new ListarCotizacionesQuery(Offset: 0, Limit: 3),
            CancellationToken.None);
        var page2 = await handler.Handle(
            new ListarCotizacionesQuery(Offset: 3, Limit: 3),
            CancellationToken.None);

        page1.Items.Should().HaveCount(3);
        page1.Total.Should().Be(10);
        page2.Items.Should().HaveCount(3);

        // Sin overlap.
        var page1Refs = page1.Items.Select(i => i.ReferenciaExterna).ToList();
        var page2Refs = page2.Items.Select(i => i.ReferenciaExterna).ToList();
        page1Refs.Should().NotIntersectWith(page2Refs);
    }

    [Fact]
    public async Task Historial_ConVariosEnvios_RetornaCronologicoAscendente()
    {
        var db = await InMemoryDb.CreateAsync();
        var entidad = NewCotizacion(Guid.NewGuid(), "Q-001");
        db.EntidadesExternas.Add(entidad);

        // Sembrar 3 envios con attempt_number 1, 2, 3.
        for (short n = 1; n <= 3; n++)
        {
            var envio = Envio.Empezar(entidad, n,
                DateTimeOffset.UtcNow.AddSeconds(-n * 10),
                "http://test/drop",
                filename: $"cot_CIR_Q-001-{n}.edi");
            db.Envios.Add(envio);
        }
        await db.SaveChangesAsync();

        var handler = new ObtenerHistorialCotizacionHandler(db);
        var result = await handler.Handle(
            new ObtenerHistorialCotizacionQuery(entidad.Id),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Envios.Should().HaveCount(3);
        result.Envios.Select(e => e.AttemptNumber).Should().BeInAscendingOrder();
        result.Envios.Should().AllSatisfy(e => e.Filename.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public async Task Historial_NoExisteCotizacion_RetornaNull()
    {
        var db = await InMemoryDb.CreateAsync();
        var handler = new ObtenerHistorialCotizacionHandler(db);

        var result = await handler.Handle(
            new ObtenerHistorialCotizacionQuery(Guid.NewGuid()),
            CancellationToken.None);

        result.Should().BeNull();
    }

    private static EntidadExterna NewCotizacion(
        Guid empresaId, string quoteRef, DateTimeOffset? submittedAt = null) =>
        new(
            id: Guid.NewGuid(),
            tipoEntidad: TipoEntidad.Cotizacion,
            referenciaExterna: quoteRef,
            empresaId: empresaId,
            payloadOriginal: "{}",
            ediContent: "EDI",
            submittedBySpnId: null,
            submittedAt: submittedAt ?? DateTimeOffset.UtcNow,
            sucursal: "CIR");
}
