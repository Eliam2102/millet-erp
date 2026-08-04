using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Almacen.Domain;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.Workers;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Camino feliz del interruptor operativo del <c>ReordenWorker</c> contra
/// Postgres real (el advisory lock no es reproducible con InMemory; los
/// caminos de skip viven en <c>Almacen.UnitTests.Reorden.ReordenWorkerToggleTests</c>).
/// Con <c>AlmacenSettings.ReabastoAutomaticoActivo=true</c> para la empresa
/// del usuario de servicio, <c>EjecutarBarridoAsync</c> atraviesa lock+TX y
/// ejecuta el barrido (resultado no-null); con <c>false</c>, se salta (null).
/// </summary>
public class ReordenWorkerToggleTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _base;

    public ReordenWorkerToggleTests(WebApplicationFactory<Program> factory)
    {
        _base = factory;
    }

    [Fact]
    public async Task Flag_on_ejecuta_el_barrido_y_flag_off_lo_salta()
    {
        var recorder = new RecordingCrearRqPort();
        await using var factory = _base.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IComprasCrearRqSistemaPort>();
                s.AddSingleton<IComprasCrearRqSistemaPort>(recorder);
            }));

        using var scope = factory.Services.CreateScope();
        var usuarios = scope.ServiceProvider.GetRequiredService<IUsuarioServicioReadPort>();
        var usuarioServicio = await usuarios.ObtenerReordenAsync(CancellationToken.None);
        if (usuarioServicio is null)
        {
            // BD de dev sin el bootstrap del usuario de servicio del motor:
            // no hay forma de ejercitar el camino (misma fragilidad-por-datos
            // que el resto de esta suite). El gate queda cubierto por units.
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        using var bypass = scope.ServiceProvider
            .GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>().Bypass();

        var worker = new ReordenWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ReordenWorkerOptions { Disabled = false }),
            NullLogger<ReordenWorker>.Instance);

        try
        {
            await PonerFlagAsync(db, usuarioServicio.EmpresaId, activo: true);
            var conFlagOn = await worker.EjecutarBarridoAsync(CancellationToken.None);
            conFlagOn.Should().NotBeNull("con el flag encendido el ciclo atraviesa lock+TX y corre el barrido");

            await PonerFlagAsync(db, usuarioServicio.EmpresaId, activo: false);
            var conFlagOff = await worker.EjecutarBarridoAsync(CancellationToken.None);
            conFlagOff.Should().BeNull("con el flag apagado el ciclo se salta antes del lock");
        }
        finally
        {
            // El default operativo es apagado: dejar la BD como estaba.
            await PonerFlagAsync(db, usuarioServicio.EmpresaId, activo: false);
        }
    }

    private static async Task PonerFlagAsync(AlmacenDbContext db, Guid empresaId, bool activo)
    {
        var row = await db.AlmacenSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.EmpresaId == empresaId);
        if (row is null)
        {
            row = AlmacenSettings.CrearDefault(empresaId);
            db.AlmacenSettings.Add(row);
        }
        row.ActualizarReabastoAutomatico(activo);
        await db.SaveChangesAsync();
    }

    private sealed class RecordingCrearRqPort : IComprasCrearRqSistemaPort
    {
        public List<CrearRqSistemaSolicitud> Calls { get; } = [];

        public Task<Guid> CrearBorradorSistemaAsync(CrearRqSistemaSolicitud solicitud, CancellationToken ct)
        {
            Calls.Add(solicitud);
            return Task.FromResult(Guid.CreateVersion7());
        }
    }
}
