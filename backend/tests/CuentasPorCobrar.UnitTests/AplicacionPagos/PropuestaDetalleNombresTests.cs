using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Ports.Administracion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.AplicacionPagos;

/// <summary>H-U41: el detalle de la propuesta muestra quién la propuso y quién la resolvió (nombres ficticios).</summary>
public sealed class PropuestaDetalleNombresTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid Proponente = Guid.NewGuid();
    private static readonly Guid Tesoreria = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Detalle_incluye_nombre_de_quien_propuso_y_de_quien_confirmo()
    {
        using var db = new CuentasPorCobrarDbContext(
            new DbContextOptionsBuilder<CuentasPorCobrarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(EmpresaId));
        var cartera = FacturaCartera.Crear(EmpresaId, Guid.NewGuid(), ClienteId, "AAA010101AAA", "Cliente Prueba",
            "UUID-1", "FX-000001", 1100m, "MXN", "PUE", Ahora, Ahora.AddDays(30));
        db.FacturasCartera.Add(cartera);
        var propuesta = PropuestaAplicacionPago.Crear(EmpresaId, ClienteId, "DEP-1", 1100m, "MXN", "REM-1",
            [("UUID-1", cartera.Id, 1100m, null)], 0m, propuestoPor: Proponente, ahora: Ahora);
        db.PropuestasAplicacionPago.Add(propuesta);
        await db.SaveChangesAsync();

        var nombres = new FakeNombres(new Dictionary<Guid, string> { [Proponente] = "Ana Analista", [Tesoreria] = "Tere Tesorera" });
        var handler = new ObtenerPropuestaAplicacionHandler(db, usuarios: nombres);

        var abierta = await handler.Handle(new ObtenerPropuestaAplicacionQuery(propuesta.Id), default);
        abierta.PropuestoPorNombre.Should().Be("Ana Analista");
        abierta.ResueltaPorNombre.Should().BeNull();

        propuesta.Confirmar(Tesoreria, Ahora, Guid.NewGuid());
        await db.SaveChangesAsync();
        var confirmada = await handler.Handle(new ObtenerPropuestaAplicacionQuery(propuesta.Id), default);
        confirmada.PropuestoPorNombre.Should().Be("Ana Analista");
        confirmada.ResueltaPorNombre.Should().Be("Tere Tesorera");
    }

    private sealed class FakeNombres(IReadOnlyDictionary<Guid, string> nombres) : IUsuarioNombreReadPort
    {
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(
            IReadOnlyCollection<Guid> usuarioIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(
                usuarioIds.Where(nombres.ContainsKey).ToDictionary(id => id, id => nombres[id]));
    }

    private sealed class FakeEmpresaContext(Guid empresaId) : ICurrentEmpresaContext
    {
        public Guid? Current => empresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
