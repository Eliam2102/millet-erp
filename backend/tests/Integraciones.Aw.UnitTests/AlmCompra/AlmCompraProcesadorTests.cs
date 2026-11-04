using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Compras.Application.Integration.AlmCompra;
using Millet.Compras.Domain.AlmCompra;
using Millet.Compras.Infrastructure;
using Millet.Integraciones.Aw.Infrastructure.AlmCompra;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.UnitTests.AlmCompra;

/// <summary>86e3jvu83 (C3.2): bandeja ERP → Almacenes contra el transporte simulado: feliz, duplicado, falla, lento, orden y freno por error.</summary>
public sealed class AlmCompraProcesadorTests
{
    private static readonly Guid Empresa = Guid.CreateVersion7();

    private sealed class Reloj(DateTimeOffset ahora) : IClock { public DateTimeOffset UtcNow { get; set; } = ahora; }
    private sealed class BypassEmpresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Nada();
        private sealed class Nada : IDisposable { public void Dispose() { } }
    }

    private sealed class Entorno
    {
        public ComprasDbContext Db { get; }
        public SimuladoAlmCompraTransporte Sim { get; } = new();
        public Reloj Reloj { get; } = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
        public AlmCompraProcesador Procesador { get; }

        public Entorno(int maxIntentos = 8)
        {
            var opts = new DbContextOptionsBuilder<ComprasDbContext>().UseInMemoryDatabase($"alm-compra-{Guid.NewGuid():N}").Options;
            Db = new ComprasDbContext(opts, new BypassEmpresa());
            Db.Database.EnsureCreated();
            Procesador = new AlmCompraProcesador(Db, Sim, Options.Create(new AlmCompraOptions { MaxIntentos = maxIntentos }),
                Reloj, new BypassEmpresa(), NullLogger<AlmCompraProcesador>.Instance);
        }

        /// <summary>Encola un mensaje de la OC <paramref name="folio"/> con el ordinal siguiente de su tipo.</summary>
        public AlmCompraEnvio Encolar(string folio, string tipo)
        {
            var ordinal = Db.AlmCompraEnvios.Local.Count(e => e.Referencia == folio && e.Tipo == tipo) + 1;
            var ocId = Guid.NewGuid();
            var id = AlmCompraSobre.MensajeId(folio, ocId, tipo, ordinal);
            object datos = tipo == AlmCompraEnvio.TipoCancelada
                ? AlmCompraSobre.DatosCancelada(folio, "Cancelada")
                : AlmCompraSobre.DatosCompra(new AlmCompraDatos(folio, 164, "3M", "MXN", null, new DateOnly(2026, 11, 4),
                    [new AlmCompraLinea("1", "RQ-00000003", 1, 100006, 501, 3300, 6100, 10, null, null, null, 210.5m, 4237.8m)]));
            var envio = new AlmCompraEnvio(Guid.CreateVersion7(), Empresa, ocId, folio, tipo, ordinal, id,
                AlmCompraSobre.Armar(id, tipo, Reloj.UtcNow, folio, datos));
            Db.AlmCompraEnvios.Add(envio);
            Db.SaveChanges();
            return envio;
        }

        public async Task<string> EstadoAsync(AlmCompraEnvio e) =>
            (await Db.AlmCompraEnvios.AsNoTracking().SingleAsync(x => x.Id == e.Id)).Estado;
    }

    [Fact]
    public async Task Feliz_la_compra_se_escribe_y_queda_PROCESADA_con_el_id_de_Almacenes()
    {
        var t = new Entorno();
        var r = t.Encolar("OC-1", AlmCompraEnvio.TipoRecibida);

        var tick = await t.Procesador.ProcesarAsync(default);

        tick.Should().Be(new AlmCompraTick(1, 1, 0));
        var fila = await t.Db.AlmCompraEnvios.AsNoTracking().SingleAsync(x => x.Id == r.Id);
        (fila.Estado, fila.CompraIdAlmacenes, fila.UltimoError).Should().Be((AlmCompraEnvio.Procesado, "1", null));
        t.Sim.Escrituras.Should().Equal(r.MensajeId);
    }

    [Fact]
    public async Task Duplicado_un_reintento_del_mismo_mensaje_no_duplica_y_el_mismo_folio_da_DUPLICADO_con_el_primer_compraId()
    {
        var t = new Entorno();
        var r = t.Encolar("OC-1", AlmCompraEnvio.TipoRecibida);
        await t.Procesador.ProcesarAsync(default);

        // Reintento del mismo mensajeId (p. ej. el acuse no alcanzó a regresar): el destino no lo escribe dos veces.
        await t.Sim.EscribirAsync(new AlmCompraMensaje(r.MensajeId, r.Tipo, 1, t.Reloj.UtcNow, r.Referencia, r.Sobre), default);
        t.Sim.Escrituras.Should().ContainSingle();

        // Otro mensaje (otro mensajeId) para el mismo folio de compra: Almacenes contesta DUPLICADO con el compraId de la primera vez.
        var otro = new AlmCompraEnvio(Guid.CreateVersion7(), Empresa, r.OrdenCompraId, "OC-1", AlmCompraEnvio.TipoRecibida, 2, "ERP-OC-1-R2", r.Sobre);
        t.Db.AlmCompraEnvios.Add(otro);
        await t.Db.SaveChangesAsync();
        await t.Procesador.ProcesarAsync(default);

        var fila = await t.Db.AlmCompraEnvios.AsNoTracking().SingleAsync(x => x.Id == otro.Id);
        (fila.Estado, fila.CompraIdAlmacenes).Should().Be((AlmCompraEnvio.Duplicado, "1"));
    }

    [Fact]
    public async Task Orden_estricto_y_freno_por_error_no_avanza_la_referencia_hasta_que_Almacenes_reprocesa()
    {
        var t = new Entorno();
        var r = t.Encolar("OC-1", AlmCompraEnvio.TipoRecibida);
        var m1 = t.Encolar("OC-1", AlmCompraEnvio.TipoModificada);
        var m2 = t.Encolar("OC-1", AlmCompraEnvio.TipoModificada);
        var otra = t.Encolar("OC-2", AlmCompraEnvio.TipoRecibida); // otra referencia: no la frena
        t.Sim.Forzar(r.MensajeId, "ERROR", "RQ-00000003 línea 1: solo faltan 3 piezas por comprar");

        await t.Procesador.ProcesarAsync(default);
        await t.Procesador.ProcesarAsync(default);

        (await t.EstadoAsync(r)).Should().Be(AlmCompraEnvio.Error);
        (await t.EstadoAsync(m1)).Should().Be(AlmCompraEnvio.Pendiente);
        (await t.EstadoAsync(m2)).Should().Be(AlmCompraEnvio.Pendiente);
        (await t.EstadoAsync(otra)).Should().Be(AlmCompraEnvio.Procesado);
        t.Sim.Escrituras.Should().NotContain([m1.MensajeId, m2.MensajeId]);
        (await t.Db.AlmCompraEnvios.AsNoTracking().SingleAsync(x => x.Id == r.Id)).UltimoError.Should().Contain("solo faltan 3 piezas");

        // Almacenes corrige y reprocesa desde su aplicación: el ERP relee el acuse y la referencia sigue, en orden.
        t.Sim.Reprocesar(r.MensajeId);
        await t.Procesador.ProcesarAsync(default);

        (await t.EstadoAsync(r)).Should().Be(AlmCompraEnvio.Procesado);
        (await t.EstadoAsync(m1)).Should().Be(AlmCompraEnvio.Procesado);
        (await t.EstadoAsync(m2)).Should().Be(AlmCompraEnvio.Procesado);
        t.Sim.Escrituras.Where(x => x.StartsWith("ERP-OC-1-", StringComparison.Ordinal)).Should().Equal(r.MensajeId, m1.MensajeId, m2.MensajeId);
        t.Sim.ViolacionesDeOrden.Should().BeEmpty();
    }

    [Fact]
    public async Task Falla_del_transporte_reintenta_con_espera_creciente_y_agotados_los_intentos_pasa_a_ERROR_y_detiene_la_referencia()
    {
        var t = new Entorno(maxIntentos: 3);
        var r = t.Encolar("OC-1", AlmCompraEnvio.TipoRecibida);
        var m = t.Encolar("OC-1", AlmCompraEnvio.TipoModificada);
        t.Sim.FallasPendientes = 10;

        (await t.Procesador.ProcesarAsync(default)).Fallas.Should().Be(1);
        var f1 = await t.Db.AlmCompraEnvios.AsNoTracking().SingleAsync(x => x.Id == r.Id);
        (f1.Estado, f1.Intentos, f1.ProximoIntentoEn).Should().Be((AlmCompraEnvio.Pendiente, 1, t.Reloj.UtcNow.AddSeconds(5)));

        // Aún en espera: no se toca el transporte.
        t.Sim.FallasPendientes.Should().Be(9);
        (await t.Procesador.ProcesarAsync(default)).Fallas.Should().Be(0);
        t.Sim.FallasPendientes.Should().Be(9);

        t.Reloj.UtcNow = t.Reloj.UtcNow.AddSeconds(6);
        await t.Procesador.ProcesarAsync(default);
        var f2 = await t.Db.AlmCompraEnvios.AsNoTracking().SingleAsync(x => x.Id == r.Id);
        (f2.Intentos, f2.ProximoIntentoEn).Should().Be((2, t.Reloj.UtcNow.AddSeconds(10)));

        t.Reloj.UtcNow = t.Reloj.UtcNow.AddSeconds(11);
        await t.Procesador.ProcesarAsync(default);
        var f3 = await t.Db.AlmCompraEnvios.AsNoTracking().SingleAsync(x => x.Id == r.Id);
        (f3.Estado, f3.Intentos).Should().Be((AlmCompraEnvio.Error, 3));
        f3.UltimoError.Should().Contain("IOException").And.NotContain("sobre");
        (await t.EstadoAsync(m)).Should().Be(AlmCompraEnvio.Pendiente);
        t.Sim.Escrituras.Should().BeEmpty();

        // Reintento manual cuando el destino ya responde: escribe y la referencia sigue en orden.
        t.Sim.FallasPendientes = 0;
        var envio = await t.Db.AlmCompraEnvios.SingleAsync(x => x.Id == r.Id);
        envio.Reintentar();
        await t.Db.SaveChangesAsync();
        await t.Procesador.ProcesarAsync(default);
        t.Sim.Escrituras.Should().Equal(r.MensajeId, m.MensajeId);
    }

    [Fact]
    public async Task Lento_sin_acuse_la_fila_queda_ENVIADA_y_el_siguiente_mensaje_espera()
    {
        var t = new Entorno();
        t.Sim.LecturasAntesDeAcuse = 2;
        var r = t.Encolar("OC-1", AlmCompraEnvio.TipoRecibida);
        var m = t.Encolar("OC-1", AlmCompraEnvio.TipoModificada);

        await t.Procesador.ProcesarAsync(default);
        (await t.EstadoAsync(r)).Should().Be(AlmCompraEnvio.Enviado);
        await t.Procesador.ProcesarAsync(default);
        (await t.EstadoAsync(r)).Should().Be(AlmCompraEnvio.Enviado);
        (await t.EstadoAsync(m)).Should().Be(AlmCompraEnvio.Pendiente);
        t.Sim.Escrituras.Should().Equal(r.MensajeId); // no se escribe M1 sin acuse de R

        await t.Procesador.ProcesarAsync(default);
        (await t.EstadoAsync(r)).Should().Be(AlmCompraEnvio.Procesado);
        await t.Procesador.ProcesarAsync(default);
        await t.Procesador.ProcesarAsync(default);
        await t.Procesador.ProcesarAsync(default);
        (await t.EstadoAsync(m)).Should().Be(AlmCompraEnvio.Procesado);
        t.Sim.ViolacionesDeOrden.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelar_o_modificar_con_entrada_cerrada_es_ERROR_de_Almacenes_y_detiene_la_referencia()
    {
        var t = new Entorno();
        t.Encolar("OC-1", AlmCompraEnvio.TipoRecibida);
        await t.Procesador.ProcesarAsync(default);
        t.Sim.MarcarEntradaCerrada("OC-1");
        var c = t.Encolar("OC-1", AlmCompraEnvio.TipoCancelada);

        await t.Procesador.ProcesarAsync(default);

        var fila = await t.Db.AlmCompraEnvios.AsNoTracking().SingleAsync(x => x.Id == c.Id);
        (fila.Estado, fila.UltimoError).Should().Be((AlmCompraEnvio.Error, "La compra OC-1 ya tiene entrada cerrada."));
    }

    [Fact]
    public async Task Un_error_local_sin_escribir_no_se_envia_ni_se_reintenta_solo_y_Reintentar_solo_aplica_si_no_se_escribio()
    {
        var t = new Entorno();
        var local = AlmCompraEnvio.ConErrorLocal(Guid.CreateVersion7(), Empresa, Guid.NewGuid(), "OC-9", AlmCompraEnvio.TipoRecibida, 1, "ERP-OC-9-R1", "ALM_COMPRA_SIN_TIPO_CAMBIO: falta");
        t.Db.AlmCompraEnvios.Add(local);
        await t.Db.SaveChangesAsync();

        (await t.Procesador.ProcesarAsync(default)).Should().Be(new AlmCompraTick(0, 0, 0));
        t.Sim.Escrituras.Should().BeEmpty();

        var enviado = t.Encolar("OC-8", AlmCompraEnvio.TipoRecibida);
        t.Sim.Forzar(enviado.MensajeId, "ERROR", "x");
        await t.Procesador.ProcesarAsync(default);
        var rechazado = await t.Db.AlmCompraEnvios.SingleAsync(x => x.Id == enviado.Id);
        rechazado.Invoking(e => e.Reintentar()).Should().Throw<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>()
            .Where(e => e.Code == "ALM_COMPRA_NO_REINTENTABLE");
    }
}
