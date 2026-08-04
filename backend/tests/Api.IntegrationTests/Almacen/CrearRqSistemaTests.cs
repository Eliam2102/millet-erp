using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Test de integración del path de creación de RQ por el sistema (ADR-0047 PR5.C):
/// <c>IComprasCrearRqSistemaPort</c>. Dado sucursal/almacén/artículo/cantidad crea una
/// RQ en Borrador con <c>Origen=Sistema</c>, creador/requisitante = usuario de servicio,
/// empresa del SP, depto SIS-REAB, folio con el código de la sucursal, y precio/UM del
/// maestro. Corre SIN empresa en contexto (como el worker real) y sin fila
/// <c>sucursal_departamentos</c> (prueba de que OMITE opera-en-sucursal).
///
/// <para>Fakea los puertos de resolución (SP/sucursal/artículo) para no depender del
/// seed de millet_dev; siembra un almacén real (lo valida IAlmacenReadPort). El folio
/// y la escritura de la RQ son reales.</para>
/// </summary>
public class CrearRqSistemaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _base;

    public CrearRqSistemaTests(WebApplicationFactory<Program> factory)
    {
        _base = factory;
    }

    [Fact]
    public async Task Crea_RQ_de_sistema_con_las_4_fuentes_y_omite_opera_en_sucursal()
    {
        var empresa = Guid.NewGuid();
        var spId = Guid.NewGuid();
        var suc = Guid.NewGuid();
        var alm = Guid.NewGuid();
        var art = Guid.NewGuid();
        var anio = DateTimeOffset.UtcNow.Year;
        var clave = $"T{Guid.NewGuid():N}".Substring(0, 6).ToUpperInvariant();   // clave única del almacén
        var sucCodigo = "RSU";   // código de sucursal para el folio: debe ser [A-Z]{2,4}

        await using var factory = _base.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IArticuloReadPort>();
                s.AddScoped<IArticuloReadPort>(_ => new FakeArticulo());
                s.RemoveAll<ISucursalReadPort>();
                s.AddScoped<ISucursalReadPort>(_ => new FakeSucursal(sucCodigo));
                s.RemoveAll<IUsuarioServicioReadPort>();
                s.AddScoped<IUsuarioServicioReadPort>(_ => new FakeSp(spId, empresa));
            }));

        using var scope = factory.Services.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IComprasCrearRqSistemaPort>();
        var comprasDb = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var almacenDb = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var rqId = Guid.Empty;
        try
        {
            // Almacén real bajo la sucursal (IAlmacenReadPort valida pertenencia).
            await almacenDb.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
                VALUES ({alm}, {clave}, 'RA', {suc}, 0, 0, NOW(), NOW())");

            rqId = await port.CrearBorradorSistemaAsync(
                new CrearRqSistemaSolicitud(suc, alm, new[] { new LineaRqSistema(art, 25m) }),
                CancellationToken.None);

            rqId.Should().NotBe(Guid.Empty);

            var rq = await comprasDb.Requisiciones
                .IgnoreQueryFilters().AsNoTracking()
                .Include(r => r.Lineas)
                .FirstAsync(r => r.Id == rqId);

            rq.Origen.Should().Be(OrigenRequisicion.Sistema);
            rq.Estado.Should().Be(EstadoRequisicion.Borrador);
            rq.CreadorId.Should().Be(spId);            // usuario de servicio
            rq.RequisitanteId.Should().Be(spId);
            rq.EmpresaId.Should().Be(empresa);         // del SP (no de la sucursal)
            rq.DepartamentoId.Should().Be(DepartamentosSistema.ReabastecimientoAutomatico);
            rq.AlmacenDestinoId.Should().Be(alm);
            rq.Clasificacion.Should().Be(Clasificacion.OrdenCompra);   // default neutro del sistema
            rq.Prioridad.Should().Be(Prioridad.Normal);
            rq.Folio.Valor.Should().StartWith($"{sucCodigo}{anio}");   // código de sucursal + año

            rq.Lineas.Should().ContainSingle();
            var linea = rq.Lineas.Single();
            linea.ArticuloId.Should().Be(art);
            linea.Cantidad.Should().Be(25m);
            linea.UnidadMedida.Should().Be("PZA");             // del maestro
            linea.PrecioEstimado.Amount.Should().Be(15m);      // PrecioReferenciaMonto del maestro
            linea.PrecioEstimado.Currency.Should().Be("MXN");
        }
        finally
        {
            if (rqId != Guid.Empty)
            {
                await comprasDb.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM compras.requisicion_lineas WHERE requisicion_id = {rqId}");
                await comprasDb.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM compras.requisiciones WHERE id = {rqId}");
            }
            await comprasDb.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM compras.folio_secuencias WHERE empresa_id = {empresa}");
            await almacenDb.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.almacenes WHERE id = {alm}");
        }
    }

    private sealed class FakeArticulo : IArticuloReadPort
    {
        private static ArticuloLectura Art(Guid id) =>
            new(id, "ACC1", "Artículo", "PZA", null, null, EsActivo: true,
                PrecioReferenciaMonto: 15m, PrecioReferenciaMoneda: "MXN");

        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken ct) =>
            Task.FromResult<ArticuloLectura?>(Art(articuloId));

        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> articuloIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
                articuloIds.ToDictionary(id => id, Art));
    }

    private sealed class FakeSucursal(string clave) : ISucursalReadPort
    {
        public Task<SucursalLectura?> ObtenerAsync(Guid sucursalId, CancellationToken ct) =>
            Task.FromResult<SucursalLectura?>(
                new SucursalLectura(sucursalId, clave, "Sucursal", Guid.NewGuid(), EsActiva: true));
    }

    private sealed class FakeSp(Guid id, Guid empresaId) : IUsuarioServicioReadPort
    {
        public Task<UsuarioServicioLectura?> ObtenerReordenAsync(CancellationToken ct) =>
            Task.FromResult<UsuarioServicioLectura?>(
                new UsuarioServicioLectura(id, empresaId, Activo: true));
    }
}
