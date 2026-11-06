using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compartido.Application.Incidencias;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Incidencias;
using Millet.SharedKernel.Domain.Incidencias;

namespace Millet.Compartido.UnitTests.Incidencias;

/// <summary>U-4 (86e3yzemw): alta sin duplicar, resolución al retomar el paso, «mis incidencias» y alcance por sucursal.</summary>
public sealed class IncidenciasServicioTests
{
    private static readonly Guid Responsable = Guid.NewGuid();
    private static readonly Guid Sucursal = Guid.NewGuid();

    private sealed class FakeUsuarios : IIncidenciaUsuariosReadPort
    {
        public Dictionary<Guid, string> Nombres { get; } = new() { [Responsable] = "Jefe de almacén" };
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(Nombres.Where(n => ids.Contains(n.Key)).ToDictionary());
    }

    private sealed class FakeSucursales : IUsuarioSucursalReadPort
    {
        public HashSet<(Guid, Guid)> Asociaciones { get; } = [];
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid u, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(Asociaciones.Where(a => a.Item1 == u).Select(a => a.Item2).ToList());
        public Task<bool> EstaAsociadoAsync(Guid u, Guid s, CancellationToken ct) => Task.FromResult(Asociaciones.Contains((u, s)));
    }

    private sealed class Entorno
    {
        public FakeEmpresa Empresa { get; } = new();
        public FakeUser Usuario { get; } = new();
        public FakePermisos Permisos { get; } = new();
        public FakeSucursales Sucursales { get; } = new();
        public FakeUsuarios Usuarios { get; } = new();
        public FakeClock Clock { get; } = new();
        public CompartidoDbContext Db { get; }
        public IncidenciasServicio Servicio { get; }
        public IncidenciasAdapter Puerto { get; }

        public Entorno()
        {
            Db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Empresa);
            Usuarios.Nombres[Usuario.UserId!.Value] = "Ana Prueba";
            Servicio = new IncidenciasServicio(Db, Usuario, Permisos, Empresa, Sucursales, Usuarios, Clock);
            Puerto = new IncidenciasAdapter(Db, Servicio, Usuario);
        }
    }

    private static AbrirIncidenciaSolicitud Solicitud(Guid documento, string? detalle = null, Guid? sucursal = null) =>
        new("almacen", "recepcion", documento, "REC-000123", PasosIncidencia.AlmRecepcionDiferenciaOc,
            "Llegaron 12 piezas y la OC pide 10", Responsable, sucursal ?? Sucursal, "RECEPCION_EXCEDE_PENDIENTE",
            DetalleClave: detalle, PasoReanudacion: PasosIncidencia.AlmRecepcionRegistrar, RutaReanudacion: "/almacen/recepciones");

    [Fact]
    public async Task Abrir_dos_veces_lo_mismo_devuelve_la_misma_pendiente()
    {
        var e = new Entorno();
        var doc = Guid.NewGuid();

        var a = await e.Puerto.AbrirAsync(Solicitud(doc), default);
        var b = await e.Puerto.AbrirAsync(Solicitud(doc), default);
        var otraLinea = await e.Puerto.AbrirAsync(Solicitud(doc, detalle: "linea-2"), default);

        a.YaExistia.Should().BeFalse();
        b.YaExistia.Should().BeTrue();
        b.Id.Should().Be(a.Id);
        otraLinea.Id.Should().NotBe(a.Id);
        (await e.Db.Incidencias.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Abrir_guarda_empresa_responsable_y_actor()
    {
        var e = new Entorno();
        var r = await e.Puerto.AbrirAsync(Solicitud(Guid.NewGuid()), default);

        var i = await e.Db.Incidencias.SingleAsync(x => x.Id == r.Id);
        i.EmpresaId.Should().Be(e.Empresa.Current!.Value);
        i.ResponsableNombre.Should().Be("Jefe de almacén");
        i.AbiertaPorNombre.Should().Be("Ana Prueba");
        i.Automatica.Should().BeFalse();
    }

    [Fact]
    public async Task Sin_usuario_la_abre_el_sistema()
    {
        var e = new Entorno();
        e.Usuario.UserId = null;

        var r = await e.Puerto.AbrirAsync(Solicitud(Guid.NewGuid()), default);

        var i = await e.Db.Incidencias.SingleAsync(x => x.Id == r.Id);
        i.Automatica.Should().BeTrue();
        i.AbiertaPorNombre.Should().Be(Incidencia.ActorSistema);
    }

    [Fact]
    public async Task Responsable_inexistente_se_rechaza()
    {
        var e = new Entorno();
        var act = () => e.Puerto.AbrirAsync(Solicitud(Guid.NewGuid()) with { ResponsableUsuarioId = Guid.NewGuid() }, default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("INCIDENCIA_RESPONSABLE_INVALIDO");
    }

    [Fact]
    public async Task Retomar_el_paso_resuelve_solo_las_pendientes_de_ese_documento()
    {
        var e = new Entorno();
        var doc = Guid.NewGuid();
        await e.Puerto.AbrirAsync(Solicitud(doc, "linea-1"), default);
        await e.Puerto.AbrirAsync(Solicitud(doc, "linea-2"), default);
        var otro = await e.Puerto.AbrirAsync(Solicitud(Guid.NewGuid()), default);

        var n = await e.Puerto.ResolverPorDocumentoAsync(PasosIncidencia.AlmRecepcionDiferenciaOc, doc, "linea-1", "Se recibió el pendiente", default);
        n.Should().Be(1);
        (await e.Puerto.HayPendientesAsync(doc, null, default)).Should().BeTrue();

        n = await e.Puerto.ResolverPorDocumentoAsync(PasosIncidencia.AlmRecepcionDiferenciaOc, doc, null, "Se recibió el pendiente", default);
        n.Should().Be(1);
        (await e.Puerto.HayPendientesAsync(doc, null, default)).Should().BeFalse();
        (await e.Db.Incidencias.SingleAsync(x => x.Id == otro.Id)).Estado.Should().Be(EstadoIncidencia.Abierta);
    }

    [Fact]
    public async Task Mis_incidencias_lista_las_pendientes_del_responsable()
    {
        var e = new Entorno();
        var r = await e.Puerto.AbrirAsync(Solicitud(Guid.NewGuid()), default);
        await e.Puerto.AbrirAsync(Solicitud(Guid.NewGuid()), default);
        await e.Puerto.ResolverPorDocumentoAsync(PasosIncidencia.AlmRecepcionDiferenciaOc,
            (await e.Db.Incidencias.SingleAsync(x => x.Id == r.Id)).DocumentoId, null, "Listo, resuelta", default);

        e.Usuario.UserId = Responsable;
        var pagina = await new ListarMisIncidenciasHandler(e.Db, e.Usuario).Handle(new ListarMisIncidenciasQuery(null, null, null, null), default);
        var todas = await new ListarMisIncidenciasHandler(e.Db, e.Usuario).Handle(new ListarMisIncidenciasQuery(["Abierta", "Resuelta"], null, null, null), default);
        var resumen = await new ResumenMisIncidenciasHandler(e.Db, e.Usuario).Handle(new ResumenMisIncidenciasQuery(), default);

        pagina.Total.Should().Be(1);
        pagina.Items.Single().PasoNombre.Should().Be("Diferencia de recepción contra la orden de compra");
        todas.Total.Should().Be(2);
        resumen.Should().Be(new ResumenMisIncidencias(1, 0));
    }

    [Fact]
    public async Task Estado_desconocido_en_el_filtro_se_rechaza()
    {
        var e = new Entorno();
        var act = () => new ListarMisIncidenciasHandler(e.Db, e.Usuario).Handle(new ListarMisIncidenciasQuery(["Cerrada"], null, null, null), default);
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("INCIDENCIA_ESTADO_INVALIDO");
    }

    [Fact]
    public async Task Consulta_por_documento_respeta_el_alcance_de_sucursal()
    {
        var e = new Entorno();
        var doc = Guid.NewGuid();
        await e.Puerto.AbrirAsync(Solicitud(doc), default);
        await e.Puerto.AbrirAsync(Solicitud(doc, "sin-sucursal", sucursal: Guid.Empty) with { SucursalId = null }, default);
        e.Permisos.Concedidos.Add(IncidenciasPermisos.Ver);
        var handler = new ListarIncidenciasHandler(e.Db, e.Servicio, e.Permisos);
        var q = new ListarIncidenciasQuery(null, null, doc, null, null, null, null, null);

        (await handler.Handle(q, default)).Total.Should().Be(1, "sin asociación solo ve la que no tiene sucursal");

        e.Sucursales.Asociaciones.Add((e.Usuario.UserId!.Value, Sucursal));
        (await handler.Handle(q, default)).Total.Should().Be(2);
    }

    [Fact]
    public async Task Sin_permiso_ni_ser_responsable_no_ve_el_detalle()
    {
        var e = new Entorno();
        var r = await e.Puerto.AbrirAsync(Solicitud(Guid.NewGuid()), default);

        var act = () => new ObtenerIncidenciaHandler(e.Servicio).Handle(new ObtenerIncidenciaQuery(r.Id), default);
        await act.Should().ThrowAsync<ForbiddenException>();

        e.Usuario.UserId = Responsable;
        var detalle = await new ObtenerIncidenciaHandler(e.Servicio).Handle(new ObtenerIncidenciaQuery(r.Id), default);
        detalle.PuedeAtender.Should().BeTrue();
        detalle.PuedeResolver.Should().BeTrue();
        detalle.PuedeCancelar.Should().BeFalse("ni la abrió ni gestiona");
        detalle.Historial.Should().ContainSingle();
    }

    [Fact]
    public async Task El_responsable_atiende_y_resuelve_con_version()
    {
        var e = new Entorno();
        var r = await e.Puerto.AbrirAsync(Solicitud(Guid.NewGuid()), default);
        e.Usuario.UserId = Responsable;
        var version = (await e.Db.Incidencias.SingleAsync(x => x.Id == r.Id)).Version;

        var atendida = await new AtenderIncidenciaHandler(e.Servicio, e.Db).Handle(new AtenderIncidenciaCommand(r.Id, version, "Lo reviso"), default);
        atendida.Estado.Should().Be("EnAtencion");

        var vieja = () => new ResolverIncidenciaHandler(e.Servicio, e.Db).Handle(new ResolverIncidenciaCommand(r.Id, version + 99, "Ya quedó"), default);
        await vieja.Should().ThrowAsync<ConcurrencyException>();

        var resuelta = await new ResolverIncidenciaHandler(e.Servicio, e.Db).Handle(new ResolverIncidenciaCommand(r.Id, atendida.Version, "Ya quedó resuelta"), default);
        resuelta.Estado.Should().Be("Resuelta");
        resuelta.ResueltaPorNombre.Should().Be("Jefe de almacén");
    }

    [Fact]
    public async Task Otro_usuario_sin_gestionar_no_resuelve()
    {
        var e = new Entorno();
        var r = await e.Puerto.AbrirAsync(Solicitud(Guid.NewGuid()), default);
        e.Permisos.Concedidos.Add(IncidenciasPermisos.Ver);
        e.Sucursales.Asociaciones.Add((e.Usuario.UserId!.Value, Sucursal));
        var version = (await e.Db.Incidencias.SingleAsync(x => x.Id == r.Id)).Version;

        var act = () => new ResolverIncidenciaHandler(e.Servicio, e.Db).Handle(new ResolverIncidenciaCommand(r.Id, version, "La resuelvo yo"), default);
        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Code.Should().Be("INCIDENCIA_SIN_PERMISO");

        e.Permisos.Concedidos.Add(IncidenciasPermisos.Gestionar);
        var ok = await new ResolverIncidenciaHandler(e.Servicio, e.Db).Handle(new ResolverIncidenciaCommand(r.Id, version, "La resuelvo yo"), default);
        ok.Estado.Should().Be("Resuelta");
    }

    [Fact]
    public async Task Alta_manual_exige_permiso_registrar_y_sucursal()
    {
        var e = new Entorno();
        var cmd = new AbrirIncidenciaCommand("almacen", "articulo", Guid.NewGuid(), "ART-1", Sucursal, PasosIncidencia.AlmUbicacionesAsignacion,
            "ENTRADA_SIN_ASIGNACION", "El artículo no está asignado a la ubicación", null, null, Responsable, null, PasosIncidencia.AlmUbicacionesAsignar, "/almacen/ubicaciones");
        var handler = new AbrirIncidenciaHandler(e.Servicio, e.Permisos);

        await ((Func<Task>)(() => handler.Handle(cmd, default))).Should().ThrowAsync<ForbiddenException>();

        e.Permisos.Concedidos.Add(IncidenciasPermisos.Registrar);
        (await ((Func<Task>)(() => handler.Handle(cmd, default))).Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("SUCURSAL_NO_ASOCIADA");

        e.Sucursales.Asociaciones.Add((e.Usuario.UserId!.Value, Sucursal));
        var r = await handler.Handle(cmd, default);
        r.YaExistia.Should().BeFalse();
        r.Detalle.PuedeCancelar.Should().BeTrue("quien la abrió puede cancelarla");
    }
}
