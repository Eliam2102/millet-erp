using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Facturas.Queries;
using Millet.Facturacion.Application.Pedidos.Queries;
using Millet.Facturacion.Application.Repp.Queries;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cajas;

/// <summary>
/// Capa A de Cajas (CAJAS-PR2, 12-cajas.md §4/§9): resolución dinámica del
/// alcance (cajas activas ∪ usuario_alcance ∪ leer-todas), predicados sobre
/// Comprobante/PedidoFacturable y bucket "Sin asignar".
/// </summary>
public sealed class AlcanceCajaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Usuario = Guid.NewGuid();
    private static readonly Guid SucursalA = Guid.NewGuid();
    private static readonly Guid SucursalB = Guid.NewGuid();

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(Empresa));

    private static AlcanceCajaEvaluator Evaluador(FacturacionDbContext db, params string[] permisos) =>
        new(db, new FakeUserContext(Usuario), new FakeCurrentUserPermissions(permisos));

    private static DatosFiscalesReceptor Receptor() =>
        new("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);

    private static DatosFiscalesEmisor Emisor() =>
        new("BBB010101BBB", "Millet", "601", "76120");

    private static FacturaVenta Factura(Guid sucursalId, short canal, string folio, long numero) =>
        FacturaVenta.CrearBorrador(Empresa, folio, numero, sucursalId, null, null, Receptor(), Emisor(),
            "PUE", "01", "MXN", null, 2026, 7, canal, ComportamientoFiscal.MostradorInmediato,
            null, null, null, false);

    private static ReciboPago Repp(Guid sucursalId, short? canal, string folio, long numero) =>
        ReciboPago.CrearBorrador(Empresa, folio, numero, sucursalId, null, null, Receptor(), Emisor(),
            2026, 7, new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero), "MXN", canal);

    private static Caja CajaCon(Guid[] sucursales, short[] canales, Guid[] usuarios, string nombre = "Caja")
    {
        var caja = Caja.Crear(Empresa, nombre, null);
        if (sucursales.Length > 0) caja.ReemplazarSucursales(sucursales);
        if (canales.Length > 0) caja.ReemplazarCanales(canales);
        if (usuarios.Length > 0) caja.ReemplazarUsuarios(usuarios);
        return caja;
    }

    // ---- Resolución del alcance ----

    [Fact]
    public async Task LeerTodas_resuelve_alcance_total()
    {
        using var db = NewDb();
        var alcance = await Evaluador(db, AlcanceCajaEvaluator.PermisoLeerTodas).ResolverAsync(CancellationToken.None);
        alcance.Tipo.Should().Be(TipoAlcanceCaja.Total);
        alcance.EsTotal.Should().BeTrue();
    }

    [Fact]
    public async Task Sin_cajas_ni_concesiones_resuelve_ninguno_y_no_ve_documentos()
    {
        using var db = NewDb();
        db.FacturasVenta.Add(Factura(SucursalA, 1, "F-1", 1));
        await db.SaveChangesAsync();

        var alcance = await Evaluador(db).ResolverAsync(CancellationToken.None);

        alcance.Tipo.Should().Be(TipoAlcanceCaja.Ninguno);
        (await alcance.AplicarA(db.FacturasVenta.AsNoTracking()).ToListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Caja_activa_del_usuario_da_el_producto_sucursales_x_canales()
    {
        using var db = NewDb();
        db.Cajas.Add(CajaCon([SucursalA, SucursalB], [1, 2], [Usuario]));
        await db.SaveChangesAsync();

        var alcance = await Evaluador(db).ResolverAsync(CancellationToken.None);

        alcance.Tipo.Should().Be(TipoAlcanceCaja.Combinaciones);
        alcance.Combinaciones.Should().BeEquivalentTo(new[]
        {
            new CombinacionAlcance(SucursalA, 1), new CombinacionAlcance(SucursalA, 2),
            new CombinacionAlcance(SucursalB, 1), new CombinacionAlcance(SucursalB, 2),
        });
    }

    [Fact]
    public async Task Caja_inactiva_no_aporta_combinaciones()
    {
        using var db = NewDb();
        var caja = CajaCon([SucursalA], [1], [Usuario]);
        caja.Desactivar();
        db.Cajas.Add(caja);
        await db.SaveChangesAsync();

        var alcance = await Evaluador(db).ResolverAsync(CancellationToken.None);
        alcance.Tipo.Should().Be(TipoAlcanceCaja.Ninguno);
    }

    [Fact]
    public async Task UsuarioAlcance_se_une_a_las_cajas_del_usuario()
    {
        using var db = NewDb();
        db.Cajas.Add(CajaCon([SucursalA], [1], [Usuario]));
        db.UsuariosAlcance.Add(UsuarioAlcance.Crear(Empresa, Usuario, SucursalB, null));
        await db.SaveChangesAsync();

        var alcance = await Evaluador(db).ResolverAsync(CancellationToken.None);

        alcance.Combinaciones.Should().BeEquivalentTo(new[]
        {
            new CombinacionAlcance(SucursalA, 1),
            new CombinacionAlcance(SucursalB, null),
        });
    }

    // ---- Predicados sobre documentos ----

    [Fact]
    public async Task AplicarA_filtra_facturas_por_par_sucursal_canal()
    {
        using var db = NewDb();
        db.Cajas.Add(CajaCon([SucursalA], [1], [Usuario]));
        db.FacturasVenta.AddRange(
            Factura(SucursalA, 1, "F-1", 1),   // en alcance
            Factura(SucursalA, 2, "F-2", 2),   // otro canal
            Factura(SucursalB, 1, "F-3", 3));  // otra sucursal
        await db.SaveChangesAsync();

        var alcance = await Evaluador(db).ResolverAsync(CancellationToken.None);
        var visibles = await alcance.AplicarA(db.FacturasVenta.AsNoTracking()).ToListAsync();

        visibles.Should().ContainSingle().Which.Folio.Should().Be("F-1");
    }

    [Fact]
    public async Task Comodin_de_canal_no_alcanza_documentos_con_canal_null()
    {
        using var db = NewDb();
        // Caja con sucursal A y SIN canales = todos los canales del catálogo.
        db.Cajas.Add(CajaCon([SucursalA], [], [Usuario]));
        db.RecibosPago.AddRange(
            Repp(SucursalA, 1, "P-1", 1),      // canal concreto → visible
            Repp(SucursalA, null, "P-2", 2));  // canal NULL → Sin asignar ([Decisión 12-D])
        await db.SaveChangesAsync();

        var alcance = await Evaluador(db).ResolverAsync(CancellationToken.None);
        var visibles = await alcance.AplicarA(db.RecibosPago.AsNoTracking()).ToListAsync();

        visibles.Should().ContainSingle().Which.Folio.Should().Be("P-1");
    }

    [Fact]
    public async Task AplicarA_filtra_pedidos_facturables()
    {
        using var db = NewDb();
        db.Cajas.Add(CajaCon([SucursalA], [1], [Usuario]));
        db.PedidosFacturables.AddRange(
            PedidoFacturable.CrearManual(Empresa, "PED-1", SucursalA, Guid.NewGuid(), "Cliente", 1,
                ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, null),
            PedidoFacturable.CrearManual(Empresa, "PED-2", SucursalB, Guid.NewGuid(), "Cliente", 1,
                ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, null));
        await db.SaveChangesAsync();

        var alcance = await Evaluador(db).ResolverAsync(CancellationToken.None);
        var visibles = await alcance.AplicarA(db.PedidosFacturables.AsNoTracking()).ToListAsync();

        visibles.Should().ContainSingle().Which.NumeroPedido.Should().Be("PED-1");
    }

    // ---- Bucket "Sin asignar" ----

    [Fact]
    public async Task SinAsignar_contiene_combinaciones_sin_caja_activa_y_canal_null()
    {
        using var db = NewDb();
        db.Cajas.Add(CajaCon([SucursalA], [1], []));  // caja activa cubre (A,1); sin usuarios — irrelevante aquí
        db.FacturasVenta.AddRange(
            Factura(SucursalA, 1, "F-1", 1),   // cubierta → NO sin-asignar
            Factura(SucursalA, 2, "F-2", 2),   // canal sin caja → sin-asignar
            Factura(SucursalB, 1, "F-3", 3));  // sucursal sin caja → sin-asignar
        db.RecibosPago.Add(Repp(SucursalA, null, "P-1", 4)); // canal NULL → sin-asignar
        await db.SaveChangesAsync();

        var sinAsignar = await Evaluador(db).ResolverSinAsignarAsync(CancellationToken.None);

        var facturas = await sinAsignar.AplicarA(db.FacturasVenta.AsNoTracking()).ToListAsync();
        facturas.Select(f => f.Folio).Should().BeEquivalentTo("F-2", "F-3");

        var repps = await sinAsignar.AplicarA(db.RecibosPago.AsNoTracking()).ToListAsync();
        repps.Should().ContainSingle().Which.Folio.Should().Be("P-1");
    }

    // ---- Handlers de bandeja/detalle bajo Capa A ----

    [Fact]
    public async Task Bandeja_facturas_filtra_por_alcance_y_omite_el_contador()
    {
        using var db = NewDb();
        db.Cajas.Add(CajaCon([SucursalA], [1], [Usuario]));
        db.FacturasVenta.AddRange(Factura(SucursalA, 1, "F-1", 1), Factura(SucursalB, 1, "F-2", 2));
        await db.SaveChangesAsync();

        var response = await new BandejaFacturasHandler(db, Evaluador(db))
            .Handle(new BandejaFacturasQuery(null, 0, 50), CancellationToken.None);

        response.Items.Should().ContainSingle().Which.Folio.Should().Be("F-1");
        response.SinAsignarCount.Should().BeNull(); // solo leer-todas ve el contador
    }

    [Fact]
    public async Task Bandeja_facturas_con_leer_todas_expone_contador_y_bucket()
    {
        using var db = NewDb();
        db.Cajas.Add(CajaCon([SucursalA], [1], []));
        db.FacturasVenta.AddRange(Factura(SucursalA, 1, "F-1", 1), Factura(SucursalB, 9, "F-2", 2));
        await db.SaveChangesAsync();

        var evaluador = Evaluador(db, AlcanceCajaEvaluator.PermisoLeerTodas);

        var todo = await new BandejaFacturasHandler(db, evaluador)
            .Handle(new BandejaFacturasQuery(null, 0, 50), CancellationToken.None);
        todo.Items.Should().HaveCount(2); // alcance total: ve todo
        todo.SinAsignarCount.Should().Be(1);

        var bucket = await new BandejaFacturasHandler(db, evaluador)
            .Handle(new BandejaFacturasQuery(null, 0, 50, SoloSinAsignar: true), CancellationToken.None);
        bucket.Items.Should().ContainSingle().Which.Folio.Should().Be("F-2");
    }

    [Fact]
    public async Task Bandeja_sin_asignar_sin_leer_todas_es_prohibida()
    {
        using var db = NewDb();
        db.Cajas.Add(CajaCon([SucursalA], [1], [Usuario]));
        await db.SaveChangesAsync();

        var act = () => new BandejaFacturasHandler(db, Evaluador(db))
            .Handle(new BandejaFacturasQuery(null, 0, 50, SoloSinAsignar: true), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>().Where(e => e.Code == "CAJA_SIN_ASIGNAR_PROHIBIDO");
    }

    [Fact]
    public async Task Detalle_fuera_de_alcance_responde_404()
    {
        using var db = NewDb();
        db.Cajas.Add(CajaCon([SucursalA], [1], [Usuario]));
        var fuera = Factura(SucursalB, 1, "F-1", 1);
        db.FacturasVenta.Add(fuera);
        await db.SaveChangesAsync();

        var act = () => new ComprobanteDetalleHandler(db, Evaluador(db))
            .Handle(new ComprobanteDetalleQuery(fuera.Id), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "FACTURA_NO_ENCONTRADA");
    }

    [Fact]
    public async Task Bandeja_repp_respeta_el_alcance()
    {
        using var db = NewDb();
        db.Cajas.Add(CajaCon([SucursalA], [1], [Usuario]));
        db.RecibosPago.AddRange(Repp(SucursalA, 1, "P-1", 1), Repp(SucursalB, 1, "P-2", 2));
        await db.SaveChangesAsync();

        var response = await new BandejaReppHandler(db, Evaluador(db))
            .Handle(new BandejaReppQuery(null, 0, 50), CancellationToken.None);

        response.Items.Should().ContainSingle().Which.Folio.Should().Be("P-1");
    }

    [Fact]
    public async Task Bandeja_pedidos_respeta_el_alcance_y_expone_contador_para_leer_todas()
    {
        using var db = NewDb();
        db.Cajas.Add(CajaCon([SucursalA], [1], [Usuario]));
        db.PedidosFacturables.AddRange(
            PedidoFacturable.CrearManual(Empresa, "PED-1", SucursalA, Guid.NewGuid(), "Cliente", 1,
                ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, null),
            PedidoFacturable.CrearManual(Empresa, "PED-2", SucursalB, Guid.NewGuid(), "Cliente", 5,
                ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, null));
        await db.SaveChangesAsync();

        var propio = await new BandejaPedidosFacturablesHandler(db, Evaluador(db))
            .Handle(new BandejaPedidosFacturablesQuery(null, null, 0, 50), CancellationToken.None);
        propio.Items.Should().ContainSingle().Which.NumeroPedido.Should().Be("PED-1");

        var admin = await new BandejaPedidosFacturablesHandler(db, Evaluador(db, AlcanceCajaEvaluator.PermisoLeerTodas))
            .Handle(new BandejaPedidosFacturablesQuery(null, null, 0, 50), CancellationToken.None);
        admin.Items.Should().HaveCount(2);
        admin.SinAsignarCount.Should().Be(1); // PED-2: (SucursalB, 5) sin caja activa
    }
}
