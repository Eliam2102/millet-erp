using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorCobrar.Application.Conciliacion;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Conciliacion;
using Millet.CuentasPorCobrar.Domain.Ports.Conciliacion;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.CuentasPorCobrar.UnitTests.Conciliacion;

/// <summary>
/// M-236: cada diferencia de la conciliación abre una incidencia común con responsable; la siguiente corrida la cierra
/// sola cuando ya se refleja, y una resolución manual es definitiva para esa diferencia. Datos genéricos.
/// </summary>
public sealed class ConciliacionIncidenciasTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Ejecutor = Guid.NewGuid();
    private static readonly Guid Responsable = Guid.NewGuid();
    private static readonly DateOnly Dia = new(2026, 11, 2);
    private static readonly DateTimeOffset Timbrado = new(2026, 11, 2, 16, 0, 0, TimeSpan.Zero);

    internal static ConciliarCarteraHandler NuevoHandler(
        CuentasPorCobrarDbContext db, IConciliacionFacturacionReadPort puerto, ICurrentEmpresaContext empresa,
        ICurrentUserContext usuario, IClock reloj, IncidenciasFake incidencias, Guid? responsable) =>
        new(db, puerto, empresa, usuario, reloj, incidencias, incidencias, incidencias, new ResponsableFijo(responsable),
            NullLogger<ConciliarCarteraHandler>.Instance);

    private static CuentasPorCobrarDbContext Db() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>().UseInMemoryDatabase($"cxc_conc_inc_{Guid.NewGuid()}").Options,
            new ConciliacionCarteraTests.EmpresaContexto(null));

    private static FacturaFacturacion Factura(Guid id) =>
        new(Empresa, id, "FV-100", "UUID-FV", "XAXX010101000", "Cliente prueba", 1_160m, false);

    private static FacturaCartera EnCartera(Guid facturaVentaId) =>
        FacturaCartera.Crear(Empresa, facturaVentaId, null, "XAXX010101000", "Cliente prueba", "UUID-FV", "FV-100",
            1_160m, "MXN", "PPD", Timbrado, Timbrado.AddDays(30));

    private static ConciliarCarteraHandler Handler(
        CuentasPorCobrarDbContext db, PuertoPorDia puerto, IncidenciasFake incidencias, Guid? empresa = null,
        Guid? responsable = null, DateTimeOffset? ahora = null) =>
        NuevoHandler(db, puerto, new ConciliacionCarteraTests.EmpresaContexto(empresa ?? Empresa),
            new ConciliacionCarteraTests.UsuarioFijo(Ejecutor), new ConciliacionCarteraTests.Reloj(ahora ?? Timbrado.AddDays(40)),
            incidencias, responsable ?? Responsable);

    private static Task<IReadOnlyList<ConciliacionResumenResponse>> Conciliar(ConciliarCarteraHandler h, DateOnly fecha, OrigenConciliacion origen = OrigenConciliacion.Demanda) =>
        h.Handle(new ConciliarCarteraCommand(fecha, origen), CancellationToken.None);

    [Fact]
    public async Task Factura_timbrada_que_no_llego_a_cartera_abre_incidencia_asignada()
    {
        await using var db = Db();
        var factura = Guid.NewGuid();
        var incidencias = new IncidenciasFake();

        await Conciliar(Handler(db, new PuertoPorDia { [Dia] = new([Factura(factura)], []) }, incidencias), Dia);

        var i = incidencias.Registros.Should().ContainSingle().Subject;
        i.Solicitud.Paso.Should().Be(PasosIncidencia.CxcConciliacionDiferencia);
        i.Solicitud.DocumentoId.Should().Be(factura);
        i.Solicitud.DocumentoEtiqueta.Should().Be("Factura FV-100");
        i.Solicitud.ResponsableUsuarioId.Should().Be(Ejecutor); // a demanda: quien la ejecutó
        i.Solicitud.DetalleClave.Should().Be("2026-11-02|FacturaNoEnCartera");
        i.Solicitud.Motivo.Should().Contain("no está en cartera");
        i.Estado.Should().Be(EstadoFake.Abierta);
    }

    [Fact]
    public async Task La_corrida_programada_asigna_al_responsable_de_la_empresa()
    {
        await using var db = Db();
        var incidencias = new IncidenciasFake();
        var puerto = new PuertoPorDia { [Dia] = new([Factura(Guid.NewGuid())], []) };

        await Conciliar(Handler(db, puerto, incidencias, empresa: null), Dia, OrigenConciliacion.Programada);

        incidencias.Registros.Should().ContainSingle().Which.Solicitud.ResponsableUsuarioId.Should().Be(Responsable);
        incidencias.EmpresaDeApertura.Should().Be(Empresa);
    }

    [Fact]
    public async Task Sin_responsable_la_corrida_se_registra_igual_y_no_abre_incidencias()
    {
        await using var db = Db();
        var incidencias = new IncidenciasFake();
        var handler = NuevoHandler(db, new PuertoPorDia { [Dia] = new([Factura(Guid.NewGuid())], []) },
            new ConciliacionCarteraTests.EmpresaContexto(null), new ConciliacionCarteraTests.UsuarioFijo(Ejecutor),
            new ConciliacionCarteraTests.Reloj(Timbrado.AddDays(1)), incidencias, responsable: null);

        var r = await handler.Handle(new ConciliarCarteraCommand(Dia, OrigenConciliacion.Programada), CancellationToken.None);

        r.Should().OnlyContain(c => c.TotalDiferencias == 1);
        incidencias.Registros.Should().BeEmpty();
    }

    [Fact]
    public async Task Repetir_la_corrida_del_mismo_dia_no_duplica_la_incidencia()
    {
        await using var db = Db();
        var incidencias = new IncidenciasFake();
        var handler = Handler(db, new PuertoPorDia { [Dia] = new([Factura(Guid.NewGuid())], []) }, incidencias);

        await Conciliar(handler, Dia);
        await Conciliar(handler, Dia);

        incidencias.Registros.Should().ContainSingle();
    }

    [Fact]
    public async Task Al_reflejarse_en_cartera_la_siguiente_corrida_cierra_la_incidencia_sola()
    {
        await using var db = Db();
        var factura = Guid.NewGuid();
        var incidencias = new IncidenciasFake();
        var puerto = new PuertoPorDia { [Dia] = new([Factura(factura)], []), [Dia.AddDays(1)] = new([], []) };
        var handler = Handler(db, puerto, incidencias);
        await Conciliar(handler, Dia);

        db.FacturasCartera.Add(EnCartera(factura)); // el evento se procesó
        await db.SaveChangesAsync();
        await Conciliar(handler, Dia.AddDays(1), OrigenConciliacion.Programada);

        var i = incidencias.Registros.Should().ContainSingle().Subject;
        i.Estado.Should().Be(EstadoFake.ResueltaAutomatica);
        i.Resolucion.Should().Be(IncidenciasConciliacion.TextoCierreAutomatico(Dia));
    }

    [Fact]
    public async Task Si_la_diferencia_persiste_la_siguiente_corrida_la_deja_abierta()
    {
        await using var db = Db();
        var incidencias = new IncidenciasFake();
        var puerto = new PuertoPorDia { [Dia] = new([Factura(Guid.NewGuid())], []), [Dia.AddDays(1)] = new([], []) };
        var handler = Handler(db, puerto, incidencias);
        await Conciliar(handler, Dia);

        await Conciliar(handler, Dia.AddDays(1), OrigenConciliacion.Programada);

        incidencias.Registros.Should().ContainSingle().Which.Estado.Should().Be(EstadoFake.Abierta);
    }

    [Fact]
    public async Task Una_incidencia_resuelta_a_mano_no_se_reabre_ni_se_duplica_aunque_la_diferencia_persista()
    {
        await using var db = Db();
        var factura = Guid.NewGuid();
        var incidencias = new IncidenciasFake();
        var puerto = new PuertoPorDia { [Dia] = new([Factura(factura)], []) };
        var handler = Handler(db, puerto, incidencias);
        await Conciliar(handler, Dia);
        incidencias.ResolverAMano(factura);

        await Conciliar(handler, Dia); // misma fecha, la factura sigue sin cartera

        var i = incidencias.Registros.Should().ContainSingle().Subject;
        i.Estado.Should().Be(EstadoFake.ResueltaManual);
    }

    [Fact]
    public async Task Una_incidencia_cancelada_tampoco_se_reabre()
    {
        await using var db = Db();
        var factura = Guid.NewGuid();
        var incidencias = new IncidenciasFake();
        var handler = Handler(db, new PuertoPorDia { [Dia] = new([Factura(factura)], []) }, incidencias);
        await Conciliar(handler, Dia);
        incidencias.Cancelar(factura);

        await Conciliar(handler, Dia);

        incidencias.Registros.Should().ContainSingle().Which.Estado.Should().Be(EstadoFake.Cancelada);
    }

    [Fact]
    public async Task El_mismo_comprobante_con_diferencia_en_otro_dia_abre_su_propia_incidencia()
    {
        await using var db = Db();
        var factura = Guid.NewGuid();
        var incidencias = new IncidenciasFake();
        var puerto = new PuertoPorDia { [Dia] = new([Factura(factura)], []), [Dia.AddDays(1)] = new([Factura(factura)], []) };
        var handler = Handler(db, puerto, incidencias);

        await Conciliar(handler, Dia);
        incidencias.ResolverAMano(factura);
        await Conciliar(handler, Dia.AddDays(1));

        incidencias.Registros.Select(r => r.Solicitud.DetalleClave).Should()
            .BeEquivalentTo(["2026-11-02|FacturaNoEnCartera", "2026-11-03|FacturaNoEnCartera"]);
    }

    [Fact]
    public async Task La_revision_de_dias_anteriores_esta_acotada()
    {
        await using var db = Db();
        var incidencias = new IncidenciasFake();
        var puerto = new PuertoPorDia();
        const int dias = 25;
        for (var d = 0; d < dias; d++)
            puerto[Dia.AddDays(d)] = new([Factura(Guid.NewGuid())], []);
        var handler = Handler(db, puerto, incidencias);
        for (var d = 0; d < dias - 1; d++)
            await Conciliar(handler, Dia.AddDays(d));

        puerto.Consultas.Clear();
        await Conciliar(handler, Dia.AddDays(dias - 1));

        // El día de la corrida + como máximo MaxDiasRevisionPorCorrida días anteriores, cada uno una sola vez.
        puerto.Consultas.Should().HaveCountLessThanOrEqualTo(1 + IncidenciasConciliacion.MaxDiasRevisionPorCorrida);
        puerto.Consultas.Should().OnlyHaveUniqueItems();
        incidencias.Registros.Should().HaveCount(dias);
    }

    // ---------- dobles ----------

    internal enum EstadoFake { Abierta, ResueltaAutomatica, ResueltaManual, Cancelada }

    internal sealed class RegistroFake(AbrirIncidenciaSolicitud solicitud)
    {
        public AbrirIncidenciaSolicitud Solicitud { get; } = solicitud;
        public EstadoFake Estado { get; set; } = EstadoFake.Abierta;
        public string? Resolucion { get; set; }
    }

    private sealed class PuertoPorDia : IConciliacionFacturacionReadPort
    {
        private readonly Dictionary<DateOnly, ComprobantesDelDia> _dias = [];
        public List<DateOnly> Consultas { get; } = [];

        public ComprobantesDelDia this[DateOnly dia]
        {
            set => _dias[dia] = value;
        }

        public Task<ComprobantesDelDia> ConsultarDiaAsync(DateOnly dia, CancellationToken cancellationToken)
        {
            Consultas.Add(dia);
            return Task.FromResult(_dias.TryGetValue(dia, out var d) ? d : new ComprobantesDelDia([], []));
        }
    }

    private sealed class ResponsableFijo(Guid? responsable) : IResponsableConciliacionReadPort
    {
        public Task<Guid?> ObtenerAsync(Guid empresaId, CancellationToken cancellationToken) => Task.FromResult(responsable);
    }

    /// <summary>Incidencias en memoria: puerto común, historial y apertura por empresa en un solo doble.</summary>
    internal sealed class IncidenciasFake : IIncidenciasPort, IIncidenciasHistorialPort, IAperturaIncidenciasConciliacionPort
    {
        public List<RegistroFake> Registros { get; } = [];
        public Guid? EmpresaDeApertura { get; private set; }

        public Task<IncidenciaAbierta> AbrirAsync(AbrirIncidenciaSolicitud solicitud, CancellationToken cancellationToken)
        {
            var existente = Registros.FirstOrDefault(r => r.Estado == EstadoFake.Abierta && r.Solicitud.Paso == solicitud.Paso
                && r.Solicitud.DocumentoId == solicitud.DocumentoId && r.Solicitud.DetalleClave == solicitud.DetalleClave);
            if (existente is not null)
                return Task.FromResult(new IncidenciaAbierta(Guid.Empty, Registros.IndexOf(existente) + 1, true));
            Registros.Add(new RegistroFake(solicitud));
            return Task.FromResult(new IncidenciaAbierta(Guid.NewGuid(), Registros.Count, false));
        }

        public Task<IncidenciaAbierta> AbrirAsync(Guid empresaId, AbrirIncidenciaSolicitud solicitud, CancellationToken cancellationToken)
        {
            EmpresaDeApertura = empresaId;
            return AbrirAsync(solicitud, cancellationToken);
        }

        public Task<int> ResolverPorDocumentoAsync(string paso, Guid documentoId, string? detalleClave, string resolucion, CancellationToken cancellationToken)
        {
            var pendientes = Registros.Where(r => r.Estado == EstadoFake.Abierta && r.Solicitud.Paso == paso
                && r.Solicitud.DocumentoId == documentoId && (detalleClave is null || r.Solicitud.DetalleClave == detalleClave)).ToList();
            foreach (var r in pendientes)
            {
                r.Estado = EstadoFake.ResueltaAutomatica;
                r.Resolucion = resolucion;
            }
            return Task.FromResult(pendientes.Count);
        }

        public Task<bool> HayPendientesAsync(Guid documentoId, string? paso, CancellationToken cancellationToken) =>
            Task.FromResult(Registros.Any(r => r.Estado == EstadoFake.Abierta && r.Solicitud.DocumentoId == documentoId
                && (paso is null || r.Solicitud.Paso == paso)));

        public Task<bool> ExisteCerradaManualAsync(string paso, Guid documentoId, string detalleClave, string resolucionAutomatica, CancellationToken cancellationToken) =>
            Task.FromResult(Registros.Any(r => r.Solicitud.Paso == paso && r.Solicitud.DocumentoId == documentoId
                && r.Solicitud.DetalleClave == detalleClave
                && (r.Estado == EstadoFake.Cancelada || (r.Estado == EstadoFake.ResueltaManual && r.Resolucion != resolucionAutomatica))));

        public void ResolverAMano(Guid documentoId)
        {
            foreach (var r in Registros.Where(r => r.Estado == EstadoFake.Abierta && r.Solicitud.DocumentoId == documentoId))
            {
                r.Estado = EstadoFake.ResueltaManual;
                r.Resolucion = "Se corrigió a mano en cartera.";
            }
        }

        public void Cancelar(Guid documentoId)
        {
            foreach (var r in Registros.Where(r => r.Estado == EstadoFake.Abierta && r.Solicitud.DocumentoId == documentoId))
                r.Estado = EstadoFake.Cancelada;
        }
    }
}
