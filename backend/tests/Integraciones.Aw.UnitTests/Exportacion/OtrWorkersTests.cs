using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Infrastructure.Exportacion;
namespace Millet.Integraciones.Aw.UnitTests.Exportacion;
public sealed class OtrWorkersTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task Apagado_o_sin_empresa_no_resuelve_servicios_ni_abre_conexiones(bool entrada, bool salida, bool empresa)
    {
        var options = Options.Create(new OtrOptions { EntradaHabilitada = entrada, SalidaHabilitada = salida, EmpresaId = empresa ? Guid.NewGuid() : null });
        var scopes = new Prohibido();
        using var lector = new OtrEntradaWorker(scopes, options, NullLogger<OtrEntradaWorker>.Instance);
        using var escritor = new OtrSalidaWorker(scopes, options, NullLogger<OtrSalidaWorker>.Instance);
        Assert.Equal(0, await lector.CorrerUnaVezAsync(CancellationToken.None)); Assert.Equal(0, await escritor.CorrerUnaVezAsync(CancellationToken.None));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task Banderas_independientes_no_arrancan_la_direccion_apagada(bool probarEntrada)
    {
        var options = Options.Create(new OtrOptions { EntradaHabilitada = !probarEntrada, SalidaHabilitada = probarEntrada, EmpresaId = Guid.NewGuid() });
        using OtrWorker worker = probarEntrada
            ? new OtrEntradaWorker(new Prohibido(), options, NullLogger<OtrEntradaWorker>.Instance)
            : new OtrSalidaWorker(new Prohibido(), options, NullLogger<OtrSalidaWorker>.Instance);
        Assert.Equal(0, await worker.CorrerUnaVezAsync(CancellationToken.None));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Error_por_fila_no_detiene_lote_y_control_local_limita_reintentos(bool fallaWriteback)
    {
        using var db = new ComercioExteriorDbContext(new DbContextOptionsBuilder<ComercioExteriorDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
        var reader = new EntradaMemoria(fallaWriteback);
        using var sp = new ServiceCollection().AddMediatR(c => c.RegisterServicesFromAssemblyContaining<AplicarEnvioOtr>())
            .AddScoped<IRequestHandler<AplicarEnvioOtr, bool>, AplicadorPrueba>().AddSingleton<IOtrEntradaReader>(reader).BuildServiceProvider();
        using var worker = new OtrEntradaWorker(sp.GetRequiredService<IServiceScopeFactory>(), Options.Create(new OtrOptions { EmpresaId = Guid.NewGuid(), MaxIntentos = 2 }), NullLogger<OtrEntradaWorker>.Instance);
        Assert.Equal(1, await worker.EntradaAsync(sp, db, CancellationToken.None));
        Assert.Equal("Aplicada", reader.Resultados["OK"].Resultado);
        var control = await db.OtrEntradasControl.SingleAsync(c => c.IdOrigen == "ERROR");
        Assert.Equal(1, control.Intentos); Assert.False(control.Rechazada); Assert.Equal(500, control.UltimoError!.Length); Assert.StartsWith("CE_OTR_ERROR_TECNICO", control.UltimoError);
        if (!fallaWriteback) Assert.Equal("Error", reader.Resultados["ERROR"].Resultado);
        await worker.EntradaAsync(sp, db, CancellationToken.None);
        control = await db.OtrEntradasControl.SingleAsync(c => c.IdOrigen == "ERROR");
        Assert.Equal(2, control.Intentos); Assert.True(control.Rechazada);
        if (!fallaWriteback) Assert.Equal("Rechazada", reader.Resultados["ERROR"].Resultado);
        await worker.EntradaAsync(sp, db, CancellationToken.None);
        Assert.Equal(2, (await db.OtrEntradasControl.SingleAsync(c => c.IdOrigen == "ERROR")).Intentos);
    }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
    public sealed class AplicadorPrueba : IRequestHandler<AplicarEnvioOtr, bool>
    {
        public Task<bool> Handle(AplicarEnvioOtr request, CancellationToken cancellationToken) => request.Entrada.IdOrigen == "ERROR"
            ? throw new InvalidOperationException(new string('x', 600)) : Task.FromResult(true);
    }
    private sealed class EntradaMemoria(bool fallaWriteback) : IOtrEntradaReader
    {
        public Dictionary<string, (string Resultado, string? Motivo)> Resultados { get; } = [];
        public Task<IReadOnlyList<OtrEntrada>> LeerPendientesAsync(int max, CancellationToken ct)
        {
            var contenido = new OtrContenido(Guid.NewGuid(), Guid.NewGuid(), "DEMO", "DEMO", null, new(null, null, null, null, null, null, null, null), []);
            return Task.FromResult<IReadOnlyList<OtrEntrada>>(new[] { new OtrEntrada("ERROR", 1, contenido), new OtrEntrada("OK", 1, contenido) }
                .Where(f => !Resultados.TryGetValue(f.IdOrigen, out var r) || r.Resultado == "Error").ToArray());
        }
        public Task MarcarResultadoAsync(string idOrigen, long version, string resultado, string? motivo, CancellationToken ct)
        {
            if (fallaWriteback && idOrigen == "ERROR") throw new IOException("DEMO writeback fallido");
            Resultados[idOrigen] = (resultado, motivo); return Task.CompletedTask;
        }
    }
    private sealed class Prohibido : IServiceScopeFactory
    { public IServiceScope CreateScope() => throw new InvalidOperationException("Un worker apagado no debe resolver ni abrir conexiones."); }
}
