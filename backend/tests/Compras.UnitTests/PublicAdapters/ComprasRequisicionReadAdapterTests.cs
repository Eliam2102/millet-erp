using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.UnitTests.PublicAdapters;

/// <summary>
/// Tests del <see cref="ComprasRequisicionReadAdapter"/> — en particular que la
/// lectura de folio para presentación (<c>ObtenerFoliosAsync</c>) es
/// state-agnostic: resuelve el folio aunque la RQ ya haya avanzado a un estado
/// que <c>ObtenerAsync</c> (lectura operativa, gateada por estado) rechaza.
/// ADR-0042. Cubre el caso de una salida histórica cuya RQ ya está Cerrada.
/// </summary>
public class ComprasRequisicionReadAdapterTests
{
    [Fact]
    public async Task ObtenerFolios_resuelve_aunque_la_RQ_este_cerrada()
    {
        await using var db = await NuevaDbAsync();
        var rq = CrearRqCerrada(folio: "MID2026-000777");
        db.Requisiciones.Add(rq);
        await db.SaveChangesAsync();

        var adapter = new ComprasRequisicionReadAdapter(db);

        // Lectura de presentación: state-agnostic. La RQ está Cerrada —un estado
        // que la lectura operativa ObtenerAsync rechaza por su filtro (solo
        // Autorizada/EnSurtido)— y aun así el folio resuelve.
        //
        // No ejercitamos ObtenerAsync aquí: su Include(Lineas) materializa el VO
        // Money, que el provider EF InMemory no sabe shapear (limitación del
        // provider de test, no del código; en PostgreSQL funciona). Su filtro de
        // estado está cubierto por los tests de surtido del flujo de salida.
        var folios = await adapter.ObtenerFoliosAsync(new[] { rq.Id }, CancellationToken.None);
        folios.Should().ContainKey(rq.Id);
        folios[rq.Id].Should().Be("MID2026-000777");
    }

    [Fact]
    public async Task ObtenerFolios_ignora_ids_inexistentes()
    {
        await using var db = await NuevaDbAsync();
        var adapter = new ComprasRequisicionReadAdapter(db);

        var folios = await adapter.ObtenerFoliosAsync(new[] { Guid.NewGuid() }, CancellationToken.None);

        folios.Should().BeEmpty();
    }

    private static Requisicion CrearRqCerrada(string folio)
    {
        var rq = new Requisicion(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Folio.Parse(folio),
            folioAnio: 2026,
            clasificacion: Clasificacion.MateriaPrima,
            sucursalId: Guid.CreateVersion7(),
            departamentoId: Guid.CreateVersion7(),
            almacenDestinoId: Guid.CreateVersion7(),
            requisitanteId: Guid.CreateVersion7(),
            creadorId: Guid.CreateVersion7(),
            prioridad: Prioridad.Normal,
            fechaSolicitud: DateTimeOffset.UtcNow,
            descripcion: null);

        rq.AgregarLinea(Guid.CreateVersion7(), Guid.CreateVersion7(), 10m, "PZA", Money.Mxn(15m));
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        rq.RegistrarAutorizacion(
            autorizacionId: Guid.CreateVersion7(),
            nivel: NivelAutorizacion.Nivel1,
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            requiereNivel: RequiereNivel.SoloN1);

        var lineaId = rq.Lineas.Single().Id;
        rq.RegistrarCubrimiento(
            new[] { new CubrimientoLinea(lineaId, CantidadDeAlmacen: 10m, CantidadDeCompra: 0m) },
            DateTimeOffset.UtcNow);
        // ADR-0043 #3: el cubrimiento ya no cierra (queda EnSurtido). La RQ
        // llega a Cerrada por ENTREGA total (el cierre nuevo, único auto-cierre).
        rq.RegistrarEntrega(lineaId, cantidadEntregada: 10m, ocurridoEn: DateTimeOffset.UtcNow);

        rq.Estado.Should().Be(EstadoRequisicion.Cerrada);
        return rq;
    }

    private static async Task<ComprasDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<ComprasDbContext>()
            .UseInMemoryDatabase($"compras-rq-read-adapter-{Guid.NewGuid():N}")
            .Options;
        var db = new ComprasDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
