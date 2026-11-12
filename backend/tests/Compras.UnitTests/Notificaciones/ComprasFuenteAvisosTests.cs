using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compras.Application.Listar;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Matriz;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Notificaciones;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Notificaciones;
using Millet.SharedKernel.Domain;
using RqFolio = Millet.Compras.Domain.Folio;
using OcFolio = Millet.Compras.Domain.Oc.Folio;

namespace Millet.Compras.UnitTests.Notificaciones;

public sealed class ComprasFuenteAvisosTests
{
    private readonly Guid empresa = Guid.NewGuid(), usuario = Guid.NewGuid(), sucursal = Guid.NewGuid();
    private static readonly DateTimeOffset Fecha = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Servicio_CE_no_avisa_firma_sin_facultad_del_nivel()
    {
        await using var db = Db();
        var servicio = Oc(40);
        typeof(OrdenCompra).GetProperty(nameof(OrdenCompra.EsServicioComercioExterior))!.SetValue(servicio, true);
        db.OrdenesCompra.Add(servicio); await db.SaveChangesAsync();
        var permisos = new Permisos(PermisosCanonicos.ComprasOrdenesLeer, PermisosCanonicos.ComprasOrdenesAutorizarNivel1);
        (await Fuente(db, permisos).ConsultarAsync(Contexto(), default)).Should().BeEmpty();
        permisos.Codigos.Add(PermisosCanonicos.ComercioExteriorServiciosAutorizarNivel1);
        (await Fuente(db, permisos).ConsultarAsync(Contexto(), default)).Select(a => a.DocumentoId).Should().Equal(servicio.Id);
    }

    [Fact]
    public async Task Oc_conjunto_completo_scope_y_permiso_no_duplican_ni_filtran_diez()
    {
        await using var db = Db();
        for (var i = 1; i <= 15; i++) db.OrdenesCompra.Add(Oc(i));
        db.OrdenesCompra.Add(Oc(16, comprador: usuario));
        db.OrdenesCompra.Add(Oc(17, otraSucursal: Guid.NewGuid()));
        db.OrdenesCompra.Add(Oc(18, otraEmpresa: Guid.NewGuid()));
        await db.SaveChangesAsync();
        var permisos = new Permisos(PermisosCanonicos.ComprasOrdenesLeer, PermisosCanonicos.ComprasOrdenesAutorizarNivel1);
        var fuente = Fuente(db, permisos);
        var avisos = await fuente.ConsultarAsync(Contexto(), default);
        avisos.Should().HaveCount(15).And.OnlyContain(a => a.Categoria == "Accion" && a.Modulo == "compras");
        (await fuente.ConsultarAsync(Contexto(), default)).Select(a => a.ClaveOcurrencia).Should().Equal(avisos.Select(a => a.ClaveOcurrencia));
        permisos.Codigos.Clear();
        (await fuente.ConsultarAsync(Contexto(), default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Oc_n2_excluye_firmante_n1_y_cancelacion_excluye_solicitante_y_espera_almacenes()
    {
        await using var db = Db();
        var n2 = Oc(1); FirmarN1(n2, Guid.NewGuid());
        var mismaPersona = Oc(2); FirmarN1(mismaPersona, usuario);
        var cancelar = Oc(3); Estado(cancelar, EstadoOrdenCompra.Autorizada);
        cancelar.SolicitarCancelacionAlmacenes(Guid.NewGuid(), Fecha, Guid.NewGuid(), "Cancelar");
        var solicitud = cancelar.SolicitudesCancelacion.Single();
        typeof(SolicitudCancelacionOc).GetProperty(nameof(SolicitudCancelacionOc.EsperaAlmacenes))!.SetValue(solicitud, false);
        var propia = Oc(4); Estado(propia, EstadoOrdenCompra.Autorizada);
        propia.SolicitarCancelacionAlmacenes(usuario, Fecha, Guid.NewGuid(), "Cancelar");
        typeof(SolicitudCancelacionOc).GetProperty(nameof(SolicitudCancelacionOc.EsperaAlmacenes))!.SetValue(propia.SolicitudesCancelacion.Single(), false);
        var espera = Oc(5); Estado(espera, EstadoOrdenCompra.Autorizada);
        espera.SolicitarCancelacionAlmacenes(Guid.NewGuid(), Fecha, Guid.NewGuid(), "Cancelar");
        db.OrdenesCompra.AddRange(n2, mismaPersona, cancelar, propia, espera);
        await db.SaveChangesAsync();
        var avisos = await Fuente(db, new Permisos(PermisosCanonicos.ComprasOrdenesLeer,
            PermisosCanonicos.ComprasOrdenesAutorizarNivel2)).ConsultarAsync(Contexto(), default);
        avisos.Select(a => a.DocumentoId).Should().BeEquivalentTo([n2.Id, cancelar.Id]);
    }

    [Fact]
    public void Rq_selector_respeta_segregacion_y_nivel_pendiente()
    {
        var n1 = Rq(1); Estado(n1, EstadoRequisicion.EnAutorizacion);
        var n2 = Rq(2); Estado(n2, EstadoRequisicion.EnAutorizacion);
        n2.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), Fecha, RequiereNivel.N1YN2);
        var propia = Rq(3, creador: usuario); Estado(propia, EstadoRequisicion.EnAutorizacion);
        var mismaPersona = Rq(4); Estado(mismaPersona, EstadoRequisicion.EnAutorizacion);
        mismaPersona.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, usuario, Fecha, RequiereNivel.N1YN2);
        var devuelta = Rq(5, creador: usuario); Estado(devuelta, EstadoRequisicion.EnAutorizacion);
        devuelta.Devolver(Guid.NewGuid(), "Completar datos", Fecha);
        var query = new[] { n1, n2, propia, mismaPersona, devuelta }.AsQueryable();
        PendientesAutorizacionLectura.RequisicionesAccionables(query, usuario, true, false).Select(r => r.Id).Should().Equal(n1.Id);
        PendientesAutorizacionLectura.RequisicionesAccionables(query, usuario, false, true).Select(r => r.Id).Should().Equal(n2.Id);
        PendientesAutorizacionLectura.RequisicionesAccionables(query, usuario, false, false).Should().BeEmpty();
        Estado(n2, EstadoRequisicion.Cancelada);
        PendientesAutorizacionLectura.RequisicionesAccionables(query, usuario, false, true).Should().BeEmpty();
    }

    [Fact]
    public async Task Cambio_de_sucursal_o_asociacion_revocada_no_muestra_acciones()
    {
        await using var db = Db(); db.OrdenesCompra.Add(Oc(1)); await db.SaveChangesAsync();
        var permisos = new Permisos(PermisosCanonicos.ComprasOrdenesLeer, PermisosCanonicos.ComprasOrdenesAutorizarNivel1);
        (await Fuente(db, permisos).ConsultarAsync(new(usuario, empresa, Guid.NewGuid()), default)).Should().BeEmpty();
        var fuente = new ComprasFuenteAvisos(db, permisos, new Sucursales([]), new Autorizadores(null), new Niveles());
        (await fuente.ConsultarAsync(Contexto(), default)).Should().BeEmpty();
    }

    [Fact]
    public void Rq_ciclo_cambia_solo_al_reenviar_no_al_firmar_n1()
    {
        var rq = Rq(1);
        rq.AgregarLinea(Guid.NewGuid(), Guid.NewGuid(), 1, "PZA", Money.Mxn(10));
        rq.EnviarAAutorizacion(Fecha);
        var clave = ClaveAviso.Crear(empresa, "compras", "requisicion", rq.Id, "autorizar-n1", rq.CicloAutorizacion.ToString());
        rq.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), Fecha, RequiereNivel.N1YN2);
        rq.CicloAutorizacion.Should().Be(1);
        rq.Devolver(Guid.NewGuid(), "Corregir", Fecha);
        rq.EnviarAAutorizacion(Fecha.AddDays(1));
        rq.CicloAutorizacion.Should().Be(2);
        ClaveAviso.Crear(empresa, "compras", "requisicion", rq.Id, "autorizar-n1", rq.CicloAutorizacion.ToString()).Should().NotBe(clave);
        rq.Autorizaciones.Should().HaveCount(1);
    }

    private ContextoAvisos Contexto() => new(usuario, empresa, sucursal);
    private ComprasFuenteAvisos Fuente(ComprasDbContext db, Permisos permisos, Autorizadores? resolver = null) =>
        new(db, permisos, new Sucursales([sucursal]), resolver ?? new Autorizadores(null), new Niveles());
    private static ComprasDbContext Db() => new(new DbContextOptionsBuilder<ComprasDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());
    private Requisicion Rq(int n, Guid? creador = null) => new(Guid.NewGuid(), empresa, RqFolio.Parse($"MID2026-{n:D6}"), 2026,
        Clasificacion.MateriaPrima, sucursal, Guid.NewGuid(), null, Guid.NewGuid(), creador ?? Guid.NewGuid(), Prioridad.Normal, Fecha, null);
    private OrdenCompra Oc(int n, Guid? comprador = null, Guid? otraSucursal = null, Guid? otraEmpresa = null)
    {
        var oc = new OrdenCompra(Guid.NewGuid(), otraEmpresa ?? empresa, OcFolio.Parse($"OC-MID2026-{n:D6}"), 2026, Guid.NewGuid(),
            otraSucursal ?? sucursal, Guid.NewGuid(), Guid.NewGuid(), comprador ?? Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 11, 11),
            usoCfdiClave: "G03", metodoPago: "PPD", formaPagoClave: "99");
        Estado(oc, EstadoOrdenCompra.EnAutorizacionJefeCompras);
        return oc;
    }
    private static void FirmarN1(OrdenCompra oc, Guid firmante)
    {
        typeof(OrdenCompra).GetProperty(nameof(OrdenCompra.RequiereNivel2))!.SetValue(oc, true);
        oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, firmante, Fecha);
    }
    private static void Estado<T>(T entidad, object estado) => typeof(T).GetProperty("Estado")!.SetValue(entidad, estado);
    private sealed class Permisos(params string[] codigos) : ICurrentUserPermissions
    {
        public HashSet<string> Codigos { get; } = [.. codigos];
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) => ValueTask.FromResult(Codigos.Contains(permiso));
    }
    private sealed class Sucursales(IReadOnlyList<Guid> ids) : IUsuarioSucursalReadPort
    {
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid usuarioId, CancellationToken ct) => Task.FromResult(ids);
        public Task<bool> EstaAsociadoAsync(Guid usuarioId, Guid sucursalId, CancellationToken ct) => Task.FromResult(ids.Contains(sucursalId));
    }
    private sealed class Autorizadores(Guid? usuarioId) : IResolverAutorizadorPort
    {
        public Guid? Usuario { get; set; } = usuarioId;
        public Task<Guid?> ResolverN1Async(Requisicion r, CancellationToken ct = default) => Task.FromResult(Usuario);
        public Task<Guid?> ResolverN2Async(Requisicion r, CancellationToken ct = default) => Task.FromResult(Usuario);
    }
    private sealed class Niveles : IRequiereNivelEvaluator
    {
        public Task<RequiereNivel> EvaluarAsync(Requisicion rq, CancellationToken ct = default) => Task.FromResult(RequiereNivel.N1YN2);
    }
    private sealed class EmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
