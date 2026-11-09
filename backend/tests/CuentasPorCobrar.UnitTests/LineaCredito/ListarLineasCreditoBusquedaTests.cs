using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.LineasCredito;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.LineaCredito;

using LineaCreditoAggregate = Millet.CuentasPorCobrar.Domain.LineaCredito.LineaCredito;

/// <summary>
/// M1 (prueba S3-4): la búsqueda de texto de la bandeja de líneas de crédito
/// se resuelve en el servidor; el total y la paginación reflejan lo filtrado
/// y no solo la página visible.
/// </summary>
public sealed class ListarLineasCreditoBusquedaTests
{
    [Fact]
    public async Task M1_con_texto_filtra_todas_las_lineas_y_el_total_refleja_lo_filtrado()
    {
        using var db = CrearDbContext();
        var buscado = Guid.NewGuid();
        // 60 líneas de otros clientes antes del buscado: con paginación de 10
        // y filtro en el navegador, el cliente no aparecía en la primera página.
        for (var i = 0; i < 60; i++)
            db.LineasCredito.Add(CrearLinea(Guid.NewGuid(), "MXN"));
        db.LineasCredito.Add(CrearLinea(buscado, "MXN"));
        db.LineasCredito.Add(CrearLinea(buscado, "USD"));
        await db.SaveChangesAsync();
        var clientes = new FakeClientes(("grupo comercial", buscado));

        var r = await new ListarLineasCreditoHandler(db, clientes)
            .Handle(new ListarLineasCreditoQuery(Offset: 0, Limit: 10, Q: "  Grupo Comercial "), default);

        r.Total.Should().Be(2);
        r.Items.Should().HaveCount(2).And.OnlyContain(l => l.ClienteId == buscado);
        clientes.UltimoTexto.Should().Be("Grupo Comercial");
    }

    [Fact]
    public async Task M1_texto_sin_coincidencias_devuelve_cero_y_combina_con_moneda()
    {
        using var db = CrearDbContext();
        var cliente = Guid.NewGuid();
        db.LineasCredito.Add(CrearLinea(cliente, "MXN"));
        db.LineasCredito.Add(CrearLinea(cliente, "USD"));
        await db.SaveChangesAsync();
        var clientes = new FakeClientes(("fahe", cliente));
        var handler = new ListarLineasCreditoHandler(db, clientes);

        var sinCoincidencia = await handler.Handle(new ListarLineasCreditoQuery(Q: "zzz"), default);
        var conMoneda = await handler.Handle(new ListarLineasCreditoQuery(Moneda: "USD", Q: "fahe"), default);

        sinCoincidencia.Total.Should().Be(0);
        sinCoincidencia.Items.Should().BeEmpty();
        conMoneda.Total.Should().Be(1);
        conMoneda.Items.Single().Moneda.Should().Be("USD");
    }

    [Fact]
    public async Task M1_sin_texto_no_consulta_clientes_y_pagina_todo()
    {
        using var db = CrearDbContext();
        for (var i = 0; i < 15; i++)
            db.LineasCredito.Add(CrearLinea(Guid.NewGuid(), "MXN"));
        await db.SaveChangesAsync();
        var clientes = new FakeClientes();

        var r = await new ListarLineasCreditoHandler(db, clientes)
            .Handle(new ListarLineasCreditoQuery(Offset: 10, Limit: 10, Q: "   "), default);

        r.Total.Should().Be(15);
        r.Items.Should().HaveCount(5);
        clientes.UltimoTexto.Should().BeNull();
    }

    private static LineaCreditoAggregate CrearLinea(Guid clienteId, string moneda) =>
        LineaCreditoAggregate.Crear(
            empresaId: Guid.NewGuid(),
            clienteId: clienteId,
            moneda: moneda,
            limite: 100_000m,
            origen: OrigenLineaCredito.Solunion,
            plazoDias: 45);

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_m1_{Guid.NewGuid()}")
            .Options;
        return new CuentasPorCobrarDbContext(options, new FakeEmpresaContext());
    }

    /// <summary>Doble del lookup: devuelve los clientes cuya «razón social» contiene el texto.</summary>
    private sealed class FakeClientes(params (string RazonSocial, Guid Id)[] catalogo) : IClienteReadPort
    {
        public string? UltimoTexto { get; private set; }

        public Task<ClienteRefDto?> ObtenerPorRfcAsync(string rfc, CancellationToken ct) =>
            Task.FromResult<ClienteRefDto?>(null);
        public Task<ClienteRefDto?> ObtenerAsync(Guid clienteId, CancellationToken ct) =>
            Task.FromResult<ClienteRefDto?>(null);
        public Task<IReadOnlyList<ClienteLookupCxcDto>> BuscarAsync(
            string? rfc, string? razonSocial, IReadOnlyCollection<Guid>? ids, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClienteLookupCxcDto>>([]);
        public Task<IReadOnlyList<ClienteLookupCxcDto>> BuscarPorPalabrasAsync(
            string texto, int limit, CancellationToken ct)
        {
            UltimoTexto = texto;
            IReadOnlyList<ClienteLookupCxcDto> r = catalogo
                .Where(c => c.RazonSocial.Contains(texto, StringComparison.OrdinalIgnoreCase))
                .Select(c => new ClienteLookupCxcDto(c.Id, "C-1", null, c.RazonSocial))
                .ToList();
            return Task.FromResult(r);
        }
    }

    private sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
