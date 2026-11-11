using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.EventListeners;
using Millet.Almacen.Application.PublicPorts;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.Seed;
using Millet.Catalogos.Domain;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Contabilidad.Application.Motor;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.Api.IntegrationTests.ComercioExterior;

/// <summary>
/// M-169 (Contabilidad, 10-nov) · cadena Comercio Exterior → Almacén → Contabilidad con la base aislada del escenario: un expediente
/// con honorarios del agente aduanal $12,000 (proveedor persona física en el maestro) y flete internacional $8,000 se distribuye
/// con los conceptos del catálogo; el evento de CE lleva el desglose por grupo, Almacén lo aplica sin tocar el costo promedio y
/// publica el costo aplicado con el mismo desglose, que Contabilidad lee en un campo por grupo. La póliza, la cuenta y el rechazo
/// por rol sin cuenta se prueban en <c>ListenersContabilidadTests.M169</c>. Usa la recepción e importación sembradas del escenario.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class M169GastoPorConceptoTests
{
    [Fact]
    public async Task Honorarios_y_flete_viajan_por_grupo_hasta_contabilidad_y_el_costo_promedio_no_cambia()
    {
        await using var escenario = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var scope = escenario.Scope(); var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var empresa = DemoSesionSeedHostedService.EmpresaId;
        var ce = sp.GetRequiredService<ComercioExteriorDbContext>();
        var almacen = sp.GetRequiredService<AlmacenDbContext>();
        var maestros = sp.GetRequiredService<CompartidoDbContext>();

        // El agente aduanal es persona física en el maestro de proveedores; el transportista, persona moral.
        var proveedores = await maestros.Proveedores.OrderBy(p => p.Clave).Take(2).ToListAsync();
        var (agente, transportista) = (proveedores[0], proveedores[1]);
        maestros.Entry(agente).Property(p => p.TipoPersona).CurrentValue = TipoPersonaProveedor.Fisica;
        maestros.Entry(transportista).Property(p => p.TipoPersona).CurrentValue = TipoPersonaProveedor.Moral;
        await maestros.SaveChangesAsync();

        var conceptos = await ce.ConceptosGasto.Where(c => c.EmpresaId == empresa).ToListAsync();
        var honorarios = conceptos.Single(c => c.Codigo == "HONORARIOS"); var flete = conceptos.Single(c => c.Codigo == "FLETE_INT");
        honorarios.GrupoContable.Should().Be(GrupoContableGasto.HonorariosAduanales);
        flete.GrupoContable.Should().Be(GrupoContableGasto.FletesManiobras);
        honorarios.Configurar(honorarios.BaseImportacion, honorarios.BaseExportacion, requiereOrdenServicio: false);

        var oc = DemoSesionSeedHostedService.Id("DEMO-CE-P1-OC");
        var recibidas = await sp.GetRequiredService<IRecepcionesCostoReadPort>().ListarAsync(empresa, escenario.SucursalId, oc, CancellationToken.None);
        recibidas.Should().HaveCount(2);
        var bases = recibidas.Select((r, i) => new BasePartidaCosto(r.PartidaId, null, r.Descripcion, i == 0 ? 200000 : 300000, 1000, r.Cantidad,
            null, r.OcId, r.RecepcionId, r.ArticuloId, r.UbicacionId, r.Remanente, r.CostoPromedioMxn, r.ExistenciaTotal)).ToArray();
        var saldosAntes = await almacen.SaldosInventario.AsNoTracking().Where(s => recibidas.Select(r => r.UbicacionId).Contains(s.UbicacionId)).ToListAsync();

        var expediente = new Expediente(Guid.NewGuid(), empresa, "CE-IMP-2026-M169", TipoExpediente.Importacion, escenario.SucursalId,
            "Vidrio flotado importado · octubre", oc, escenario.Incoterm, "USD", escenario.Responsable, []);
        expediente.GuardarGasto(Guid.NewGuid(), new(honorarios.Id, agente.Id, "A-15873", "MXN", 12000, new(2026, 10, 28)), honorarios, 1, "MXN");
        expediente.GuardarGasto(Guid.NewGuid(), new(flete.Id, transportista.Id, "F-2291", "MXN", 8000, new(2026, 10, 28)), flete, 1, "MXN");

        // CE: la distribución separa los honorarios por el tipo de persona que lee el puerto de catálogos.
        var personasFisicas = await sp.GetRequiredService<ICatalogosCeReadPort>().ProveedoresPersonaFisicaAsync([agente.Id, transportista.Id], CancellationToken.None);
        personasFisicas.Should().BeEquivalentTo([agente.Id]);
        var distribucion = expediente.PrepararDistribucion(conceptos, bases, escenario.Responsable, escenario.Ahora, incrementablesAlInventario: false, personasFisicas);
        expediente.ConfirmarDistribucion(distribucion);
        distribucion.GastosPorGrupo.Should().BeEquivalentTo(new[]
        {
            new GastoGrupoDistribuido(GrupoGastoImportacion.FletesManiobras, 8000),
            new GastoGrupoDistribuido(GrupoGastoImportacion.HonorariosPersonaFisica, 12000),
        });

        // CE → Almacén: mismo JSON del outbox, leído con el contrato espejo de Almacén.
        var distribuido = new ExpedienteCostoDistribuidoEvent(empresa, escenario.Ahora, expediente.Id, escenario.SucursalId, distribucion.NumeroVersion,
            distribucion.Partidas) { GastosPorGrupo = distribucion.GastosPorGrupo };
        var payload = JsonSerializer.Deserialize<CostoExpedientePayload>(JsonSerializer.Serialize(distribuido), JsonSerializerOptions.Web)!;
        payload.GastosPorGrupo.Should().HaveCount(2);
        await sp.GetRequiredService<IMediator>().Send(new AplicarCostoExpedienteCommand(payload));

        almacen.ChangeTracker.Clear();
        var saldosDespues = await almacen.SaldosInventario.AsNoTracking().Where(s => recibidas.Select(r => r.UbicacionId).Contains(s.UbicacionId)).ToListAsync();
        saldosDespues.Select(s => (s.Id, s.CostoPromedioMxn, s.ValorInventarioMxn))
            .Should().BeEquivalentTo(saldosAntes.Select(s => (s.Id, s.CostoPromedioMxn, s.ValorInventarioMxn)), "Almacén no cambia el costo promedio");

        // Almacén → Contabilidad: el costo aplicado lleva el mismo desglose y el normalizador lo pone en un campo por grupo.
        var salida = (await almacen.Set<IntegrationEventOutboxEntry>().AsNoTracking()
                .Where(x => x.EventType == NormalizadorEventos.CostoImportacionAplicado).ToListAsync())
            .Single(x => x.Payload.Contains(expediente.Id.ToString(), StringComparison.Ordinal));
        var aplicado = JsonSerializer.Deserialize<ExpedienteCostoAplicadoEvent>(salida.Payload, JsonSerializerOptions.Web)!;
        aplicado.Partidas.Sum(p => p.GastoImportacionMxn).Should().Be(20000);
        aplicado.Partidas.Sum(p => p.InventarioMxn).Should().Be(0);
        aplicado.GastosPorGrupo.Should().BeEquivalentTo(new[]
        {
            new GastoGrupoAplicado(GrupoGastoImportacion.FletesManiobras, 8000),
            new GastoGrupoAplicado(GrupoGastoImportacion.HonorariosPersonaFisica, 12000),
        });
        var evento = NormalizadorEventos.Normalizar(NormalizadorEventos.CostoImportacionAplicado, salida.Id, salida.Payload);
        (evento.Importes["GastoHonorariosPf"], evento.Importes["GastoFletesManiobras"], evento.Importes["GastoImportacion"], evento.Importes["Total"])
            .Should().Be((12000m, 8000m, 0m, 20000m));
    }
}
