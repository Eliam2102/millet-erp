using Millet.Almacen.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.DevolucionesProveedor;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.UnitTests.DevolucionesProveedor;

/// <summary>
/// Almacén-por-línea PR6a (C1): el handler de registro de devolución a
/// proveedor (8.B) resuelve el bin de cada línea con <b>coalesce a la ÚNICA</b>
/// (es_default) del sub-almacén cuando <c>request.Bins</c> no lo trae, y
/// <b>falla ruidoso</b> si el sub no tiene ÚNICA.
///
/// <para>Es la pieza que hace seguro el <c>SET NOT NULL</c> de C2: 8.B es el
/// único flujo vivo que podía emitir <c>ubicacion_id</c> NULL (los bins son
/// opcionales) apoyándose en el fallback del trigger, que C2 retira. En vez de
/// un <c>23502</c> opaco, el handler resuelve la ÚNICA o levanta un error de
/// dominio legible.</para>
/// </summary>
public class RegistrarSalidaDevolucionAProveedorPr6aTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Sin_bin_y_sub_con_unica_coalesce_a_la_default()
    {
        await using var db = NuevaDb();
        var (subId, unicaId, _) = await SembrarSubConUnicaAsync(db, conUnica: true);
        var dev = await SembrarDevolucionAutorizadaAsync(db, subId);

        var resp = await Handler(db).Handle(
            // Bins vacío a propósito: la línea no trae bin explícito.
            new RegistrarSalidaDevolucionAProveedorCommand(
                DevolucionId: dev.Id,
                SubAlmacenId: subId,
                FechaMovimiento: new DateOnly(2027, 4, 1),
                Bins: Array.Empty<DevolucionProveedorSalidaLineaBin>()),
            CancellationToken.None);

        var linea = await LeerLineaUnicaAsync(db, resp.MovimientoSalidaId);
        linea.UbicacionId.Should().Be(unicaId);
    }

    [Fact]
    public async Task La_salida_hereda_la_medida_de_la_linea_de_recepcion_original()
    {
        await using var db = NuevaDb();
        var (subId, _, rackId) = await SembrarSubConUnicaAsync(db, conUnica: true);
        var variante = Guid.NewGuid();
        var dev = await SembrarDevolucionAutorizadaAsync(db, subId, varianteOrigen: variante);

        var resp = await Handler(db).Handle(
            new RegistrarSalidaDevolucionAProveedorCommand(dev.Id, subId, new DateOnly(2027, 4, 1),
                new[] { new DevolucionProveedorSalidaLineaBin(dev.Lineas.Single().Id, rackId) }),
            CancellationToken.None);

        var linea = await LeerLineaUnicaAsync(db, resp.MovimientoSalidaId);
        (linea.ProductoAwVarianteId, linea.AnchoMm, linea.AltoMm).Should().Be((variante, 2440m, 3660m));
    }

    [Fact]
    public async Task Con_bin_explicito_respeta_el_bin_y_no_usa_la_default()
    {
        await using var db = NuevaDb();
        var (subId, unicaId, rackId) = await SembrarSubConUnicaAsync(db, conUnica: true);
        var dev = await SembrarDevolucionAutorizadaAsync(db, subId);
        var lineaDevId = dev.Lineas.Single().Id;

        var resp = await Handler(db).Handle(
            new RegistrarSalidaDevolucionAProveedorCommand(
                DevolucionId: dev.Id,
                SubAlmacenId: subId,
                FechaMovimiento: new DateOnly(2027, 4, 1),
                Bins: new[] { new DevolucionProveedorSalidaLineaBin(lineaDevId, rackId) }),
            CancellationToken.None);

        var linea = await LeerLineaUnicaAsync(db, resp.MovimientoSalidaId);
        linea.UbicacionId.Should().Be(rackId);
        linea.UbicacionId.Should().NotBe(unicaId);
    }

    [Fact]
    public async Task Sin_bin_y_sub_sin_unica_falla_ruidoso()
    {
        await using var db = NuevaDb();
        var (subId, _, _) = await SembrarSubConUnicaAsync(db, conUnica: false);
        var dev = await SembrarDevolucionAutorizadaAsync(db, subId);

        var act = () => Handler(db).Handle(
            new RegistrarSalidaDevolucionAProveedorCommand(
                DevolucionId: dev.Id,
                SubAlmacenId: subId,
                FechaMovimiento: new DateOnly(2027, 4, 1),
                Bins: Array.Empty<DevolucionProveedorSalidaLineaBin>()),
            CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("SUBALMACEN_SIN_UBICACION_DEFAULT");
    }

    [Fact]
    public async Task Devolver_dejaria_recibido_bajo_facturado_se_bloquea()
    {
        // OC: recibido 110 de 100, facturado 110; devolver 5 (de las 10 de la recepción) → 105 < 110 facturado.
        await using var db = NuevaDb();
        var (subId, _, _) = await SembrarSubConUnicaAsync(db, conUnica: true);
        var lineaOcId = Guid.NewGuid();
        var dev = await SembrarDevolucionAutorizadaAsync(db, subId, lineaOcId);

        var act = () => Handler(db, new OcPortFacturada(lineaOcId, recibida: 110m, facturada: 110m)).Handle(
            new RegistrarSalidaDevolucionAProveedorCommand(
                DevolucionId: dev.Id, SubAlmacenId: subId,
                FechaMovimiento: new DateOnly(2027, 4, 1), Bins: Array.Empty<DevolucionProveedorSalidaLineaBin>()),
            CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("DEVOLUCION_EXCEDE_NO_FACTURADO");
        (await db.Movimientos.CountAsync(m => m.Tipo == TipoMovimiento.SalidaPorDevolucionAProveedor)).Should().Be(0);
    }

    [Fact]
    public async Task Devolver_sin_pasar_de_lo_no_facturado_se_permite()
    {
        await using var db = NuevaDb();
        var (subId, _, _) = await SembrarSubConUnicaAsync(db, conUnica: true);
        var lineaOcId = Guid.NewGuid();
        var dev = await SembrarDevolucionAutorizadaAsync(db, subId, lineaOcId);

        var resp = await Handler(db, new OcPortFacturada(lineaOcId, recibida: 110m, facturada: 100m)).Handle(
            new RegistrarSalidaDevolucionAProveedorCommand(
                DevolucionId: dev.Id, SubAlmacenId: subId,
                FechaMovimiento: new DateOnly(2027, 4, 1), Bins: Array.Empty<DevolucionProveedorSalidaLineaBin>()),
            CancellationToken.None);

        (await LeerLineaUnicaAsync(db, resp.MovimientoSalidaId)).Cantidad.Should().Be(5m);
    }

    [Fact]
    public async Task Devolver_lo_facturado_dentro_de_lo_pedido_se_permite()
    {
        // Pedido 100, recibido 100, facturado 100; devolver 5 deja 95 recibidas y 100 facturadas ≤ 100 pedidas:
        // la salida va primero y la NC 03 formaliza la nota de cargo después (caso OC-302 del 30-oct).
        await using var db = NuevaDb();
        var (subId, _, _) = await SembrarSubConUnicaAsync(db, conUnica: true);
        var lineaOcId = Guid.NewGuid();
        var dev = await SembrarDevolucionAutorizadaAsync(db, subId, lineaOcId);

        var resp = await Handler(db, new OcPortFacturada(lineaOcId, recibida: 100m, facturada: 100m)).Handle(
            new RegistrarSalidaDevolucionAProveedorCommand(
                DevolucionId: dev.Id, SubAlmacenId: subId,
                FechaMovimiento: new DateOnly(2027, 4, 1), Bins: Array.Empty<DevolucionProveedorSalidaLineaBin>()),
            CancellationToken.None);

        (await LeerLineaUnicaAsync(db, resp.MovimientoSalidaId)).Cantidad.Should().Be(5m);
    }

    [Fact]
    public async Task Dos_lineas_sobre_la_misma_linea_oc_se_acumulan_contra_lo_facturado()
    {
        // Recibido 110, facturado 105: devolver 3 deja 107 (ok) pero 3+3 deja 104 < 105 → bloquea.
        await using var db = NuevaDb();
        var (subId, _, _) = await SembrarSubConUnicaAsync(db, conUnica: true);
        var lineaOcId = Guid.NewGuid();
        var dev = await SembrarDevolucionAutorizadaAsync(db, subId, lineaOcId, lineasDev: 2, cantidadPorLinea: 3m);

        var act = () => Handler(db, new OcPortFacturada(lineaOcId, recibida: 110m, facturada: 105m)).Handle(
            new RegistrarSalidaDevolucionAProveedorCommand(
                DevolucionId: dev.Id, SubAlmacenId: subId,
                FechaMovimiento: new DateOnly(2027, 4, 1), Bins: Array.Empty<DevolucionProveedorSalidaLineaBin>()),
            CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("DEVOLUCION_EXCEDE_NO_FACTURADO");
    }

    // ─────────────────────────── Fixture ───────────────────────────

    private static RegistrarSalidaDevolucionAProveedorHandler Handler(AlmacenDbContext db, IComprasOcReadPort? oc = null) =>
        new(db, new NoOpEvents(), new FakeUser(), new FakeEmpresa(EmpresaId), oc ?? new OcPortNulo(), new PeriodoContableStub(true), new P7Support.Conversion());

    private static async Task<(Guid SubId, Guid UnicaId, Guid RackId)> SembrarSubConUnicaAsync(
        AlmacenDbContext db, bool conUnica)
    {
        var almacenId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var unicaId = Guid.NewGuid();

        db.SubAlmacenes.Add(new SubAlmacen(
            subId, almacenId, "SUB-P6A", "Sub PR6a", TipoSubAlmacen.Insumos));
        // Rack real siempre; la ÚNICA (es_default) solo cuando conUnica=true —
        // el caso false reproduce el sub sin default de dev.
        db.Ubicaciones.Add(new Ubicacion(rackId, subId, "RACK-P6A", "Rack PR6a"));
        if (conUnica)
        {
            db.Ubicaciones.Add(new Ubicacion(
                unicaId, subId, "UNICA-P6A", "Unica PR6a", esDefault: true));
        }
        await db.SaveChangesAsync();
        return (subId, unicaId, rackId);
    }

    private static async Task<DevolucionAProveedor> SembrarDevolucionAutorizadaAsync(
        AlmacenDbContext db, Guid _, Guid? lineaOcId = null, int lineasDev = 1, decimal cantidadPorLinea = 5m, Guid? varianteOrigen = null)
    {
        var dev = new DevolucionAProveedor(
            id: Guid.NewGuid(),
            empresaId: EmpresaId,
            proveedorId: EmpresaId,
            motivo: "Material no conforme",
            solicitadaPor: UserId,
            recepcionOrigenId: Guid.NewGuid(),
            ordenCompraOrigenId: lineaOcId is null ? null : Guid.NewGuid()); // sin OC → el port nulo no se consulta

        dev.AgregarLinea(new LineaDevolucionProveedor(
            id: Guid.NewGuid(), devolucionId: dev.Id, posicion: 1,
            articuloId: Guid.NewGuid(), cantidad: cantidadPorLinea, unidadMedida: "PZA",
            costoUnitarioMxn: 100m));
        var linea = dev.Lineas.Single();
        for (var i = 2; i <= lineasDev; i++)
            dev.AgregarLinea(new LineaDevolucionProveedor(
                id: Guid.NewGuid(), devolucionId: dev.Id, posicion: i,
                articuloId: linea.ArticuloId, cantidad: cantidadPorLinea, unidadMedida: "PZA",
                costoUnitarioMxn: 100m));
        var origen = new MovimientoInventario(dev.RecepcionOrigenId!.Value, TipoMovimiento.EntradaCompra, EmpresaId, new DateOnly(2027, 3, 1));
        origen.VincularRecepcionVarianteA(Guid.NewGuid(), null, Guid.NewGuid(), null);
        var recibida = new LineaMovimiento(Guid.NewGuid(), origen.Id, 1, linea.ArticuloId, 10, "PZA", 100, ubicacionId: Guid.NewGuid(), lineaOcId: lineaOcId,
            productoAwVarianteId: varianteOrigen, anchoMm: varianteOrigen is null ? null : 2440m, altoMm: varianteOrigen is null ? null : 3660m);
        origen.AgregarLinea(recibida);
        origen.Registrar(FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2027, 100), UserId);
        foreach (var l in dev.Lineas)
            typeof(LineaDevolucionProveedor).GetProperty(nameof(LineaDevolucionProveedor.LineaRecepcionOrigenId))!.SetValue(l, recibida.Id);
        db.Movimientos.Add(origen);
        dev.AgregarEvidencia(new EvidenciaDevolucionProveedor(
            id: Guid.NewGuid(), devolucionId: dev.Id, tipoEvidencia: "Foto",
            nombreArchivo: "e.jpg", blobRef: "blob://e.jpg"));
        dev.SolicitarAutorizacion();
        dev.Autorizar(Guid.NewGuid());

        db.Set<DevolucionAProveedor>().Add(dev);
        await db.SaveChangesAsync();
        return dev;
    }

    private static async Task<LineaMovimiento> LeerLineaUnicaAsync(
        AlmacenDbContext db, Guid movimientoId)
    {
        db.ChangeTracker.Clear();
        var mov = await db.Movimientos.AsNoTracking()
            .Include(m => m.Lineas)
            .FirstAsync(m => m.Id == movimientoId);
        return mov.Lineas.Single();
    }

    private static AlmacenDbContext NuevaDb()
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-pr6a-devprov-{Guid.NewGuid():N}")
            .Options;
        var db = new AlmacenDbContext(opts, new FakeEmpresa(EmpresaId));
        db.Database.EnsureCreated();
        return db;
    }

    // ─────────────────────────── Dobles ───────────────────────────

    private sealed class OcPortNulo : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) =>
            Task.FromResult<OcLectura?>(new(ocId, "OC-P1", EmpresaId, EmpresaId, "Cerrada", []));

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class OcPortFacturada(Guid lineaOcId, decimal recibida, decimal facturada) : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) =>
            Task.FromResult<OcLectura?>(new(ocId, "OC-P1", EmpresaId, EmpresaId, "Autorizada",
                [new OcLineaLectura(lineaOcId, Guid.NewGuid(), "PZA", 100m, recibida, 10m, CantidadFacturada: facturada)]));

        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(
            IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class NoOpEvents : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId => RegistrarSalidaDevolucionAProveedorPr6aTests.UserId;
        public string? UserName => "Test PR6a";
    }

    private sealed class FakeEmpresa(Guid current) : ICurrentEmpresaContext
    {
        public Guid? Current => current;
        public bool IsBypassed => true; // InMemory: sin filtro por empresa
        public IDisposable Bypass() => new NoOp();
        private sealed class NoOp : IDisposable { public void Dispose() { } }
    }
}
