using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Application.Escaneos;
using Millet.ProductoTerminado.Application.Liberacion;
using Millet.ProductoTerminado.Application.Ports;
using Millet.ProductoTerminado.Application.Salidas;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.UnitTests.Application;

public sealed class AwVpnFactAttribute : FactAttribute
{
    public const string Host = "192.168.1.8";
    public AwVpnFactAttribute()
    {
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            var ar = tcp.BeginConnect(Host, 1433, null, null);
            var success = ar.AsyncWaitHandle.WaitOne(1000);
            if (!success || !tcp.Connected)
            {
                Skip = "Prueba de integración local con A+W Production: requiere VPN activa a 192.168.1.8.";
            }
        }
        catch
        {
            Skip = "Prueba de integración local con A+W Production: requiere VPN activa a 192.168.1.8.";
        }
    }
}

public sealed class ValidacionRealAwTests
{
    private sealed class UsuarioContext(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "DevGeo Local";
    }

    private sealed class PermisosFull : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken ct = default) => ValueTask.FromResult(true);
    }

    private sealed class SucursalesMock(Guid sucursalId) : IUsuarioSucursalReadPort
    {
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid usuarioId, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([sucursalId]);
        public Task<bool> EstaAsociadoAsync(Guid usuarioId, Guid sucursalIdParam, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class FacturacionMock : IFacturacionSalidaPort
    {
        public List<(Guid PedidoId, string Referencia, IReadOnlyList<(int Posicion, decimal Cantidad)> Lineas)> Salidas { get; } = [];
        public Task RegistrarSalidaAsync(Guid pedidoFacturableId, string referencia, IReadOnlyList<(int Posicion, decimal Cantidad)> lineas, CancellationToken ct)
        {
            Salidas.Add((pedidoFacturableId, referencia, lineas));
            return Task.CompletedTask;
        }
        public Task<CancelacionSalidaPt> CancelarSalidaAsync(string referencia, string motivo, CancellationToken ct) =>
            Task.FromResult(new CancelacionSalidaPt(false, false, false));
        public Task<CartaPorteSalida> EmitirCartaPorteAsync(SolicitudCartaPorte solicitud, CancellationToken ct) =>
            Task.FromResult(new CartaPorteSalida(Guid.NewGuid(), "CP-LOCAL-001"));

        public Task<IReadOnlyDictionary<string, FacturaAlbaranRuta>> FacturasDeAlbaranesAsync(IReadOnlyList<string> albaranFolios, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, FacturaAlbaranRuta>>(new Dictionary<string, FacturaAlbaranRuta>());
        public Task<IReadOnlyList<CobroRutaRegistrado>> RegistrarCobrosRutaAsync(Guid sucursalId, IReadOnlyList<CobroRutaAlbaran> cobros, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CobroRutaRegistrado>>([]);
        public Task<IReadOnlyList<FacturaPedidoSalida>> FacturasDePedidosAsync(IReadOnlyList<Guid> pedidoFacturableIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FacturaPedidoSalida>>([]);
    }

    private sealed record AwScanRow(string Auftnr, short Pos, int TeileNr, int Esnr, string Etikett, DateTime Zeit);

    [AwVpnFact]
    public async Task Validar_flujo_completo_con_datos_y_escaneos_reales_de_aw_via_vpn()
    {
        // 1. Obtener escaneos de A+W Production vía VPN
        string csAw = "Server=192.168.1.8,1433;Database=MILMAIN;User ID=millet_erp_lector_aw;Password=Pa$$w0rd138**;Encrypt=True;TrustServerCertificate=True;Connection Timeout=15;";
        var scansReales = new List<AwScanRow>();
        using (var conn = new SqlConnection(csAw))
        {
            await conn.OpenAsync();
            string sql = @"
                SELECT * FROM OPENQUERY([AWPROD_ERP], '
                    SELECT b.auftnr, b.pos, b.teile_nr, b.esnr_ist, b.etikettnr, b.bearb_zeit
                    FROM alcimdb.dbo.awbar_bearbeit b
                    WHERE b.auftnr IN (''10449822'', ''10449764'')
                      AND b.fertig = 1
                      AND (b.esnr_ist BETWEEN 3201 AND 3315 OR b.esnr_ist BETWEEN 2821 AND 2831)
                    ORDER BY b.bearb_zeit
                ')";
            using var cmd = new SqlCommand(sql, conn);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                scansReales.Add(new AwScanRow(
                    rdr["auftnr"].ToString()!,
                    Convert.ToInt16(rdr["pos"]),
                    Convert.ToInt32(rdr["teile_nr"]),
                    Convert.ToInt32(rdr["esnr_ist"]),
                    rdr["etikettnr"].ToString()!,
                    Convert.ToDateTime(rdr["bearb_zeit"])
                ));
            }
        }

        // Verificamos que existen los escaneos de producción
        scansReales.Should().NotBeEmpty();
        scansReales.Should().Contain(s => s.Auftnr == "10449822" && s.Etikett == "031813724" && s.TeileNr == 0);
        scansReales.Should().Contain(s => s.Auftnr == "10449764" && s.Etikett == "031815712" && s.TeileNr == 0);
        scansReales.Should().Contain(s => s.Auftnr == "10449764" && s.Etikett == "031815713" && s.TeileNr == 0);

        // 2. Configurar entorno del ERP
        var empresaId = Guid.Parse("0193132e-4b68-71e9-9bc8-812e9606ba30");
        var sucursalConkal = Guid.Parse("0193132e-4b68-71e9-9bc8-812e9606ba31");
        var usuarioId = Guid.NewGuid();

        var dbOptions = new DbContextOptionsBuilder<ProductoTerminadoDbContext>()
            .UseInMemoryDatabase("millet_pt_real_validation_" + Guid.NewGuid())
            .Options;

        var empresaContext = new EmpresaFija(empresaId);
        using var db = new ProductoTerminadoDbContext(dbOptions, empresaContext);
        var reloj = new RelojFijo(new DateTimeOffset(2026, 11, 8, 18, 0, 0, TimeSpan.Zero));
        var events = new CapturaEventos();
        var facturacion = new FacturacionMock();
        var pedidosPort = new FakePedidosPort();
        var sucursalesPort = new FakeSucursalPort();
        sucursalesPort.Claves["CON"] = sucursalConkal;
        var saldosPort = new FakeSaldoPort();
        saldosPort.Saldos["10449822"] = 0m;
        saldosPort.Saldos["10449764"] = 0m;
        var userContext = new UsuarioContext(usuarioId);
        var permissions = new PermisosFull();
        var usuarioSucursalPort = new SucursalesMock(sucursalConkal);
        var ptAcceso = new PtAcceso(userContext, permissions, usuarioSucursalPort);
        var liberacion = new LiberacionPedido(db, saldosPort, events, reloj);

        // Pedido 10449822: Construcciones Industriales RCO (1 pieza)
        var facturable1 = Guid.NewGuid();
        pedidosPort.Pedidos["10449822"] = new PedidoPtOrigen(
            facturable1, "10449822", sucursalConkal, "Abierto", "Credito", 1,
            [new LineaPtOrigen(1, null, "Cristal Claro 6mm Templado", 1m, null)]
        );

        // Pedido 10449764: David Daniel Ruelas Nuñez (2 piezas)
        var facturable2 = Guid.NewGuid();
        pedidosPort.Pedidos["10449764"] = new PedidoPtOrigen(
            facturable2, "10449764", sucursalConkal, "Abierto", "Contado", 2,
            [
                new LineaPtOrigen(1, null, "Cristal Filtrasol 6mm Templado", 1m, null),
                new LineaPtOrigen(2, null, "Cristal Filtrasol 6mm Templado", 1m, null)
            ]
        );

        var procesador = new EscaneoProcesador(db, pedidosPort, sucursalesPort, events, reloj, liberacion);
        var escaneoHandler = new RegistrarEscaneoPiezaHandler(db, procesador, reloj);

        // 3. Procesar los escaneos de A+W donde teile_nr = 0
        foreach (var scan in scansReales)
        {
            var cmd = new RegistrarEscaneoPiezaCommand(
                empresaId, "Real", scan.Etikett, scan.Auftnr, scan.Pos, null,
                scan.TeileNr.ToString(), // "0"
                scan.Esnr, null, scan.Zeit, "CON", 5
            );
            var res = await escaneoHandler.Handle(cmd, CancellationToken.None);
            res.Resultado.Should().Be("Alta");
            res.Motivo.Should().BeNull();
        }

        // Verificar que las 3 piezas reales fueron dadas de alta en DB
        var piezas = await db.Piezas.OrderBy(p => p.Etiqueta).ToListAsync();
        piezas.Should().HaveCount(3);
        piezas.All(p => p.Estado == EstadoPieza.EnPT).Should().BeTrue();

        // Verificar ranura asignada automáticamente (NumeroPieza = 1)
        var p1 = piezas.Single(p => p.Etiqueta == "031813724");
        p1.Posicion.Should().Be(1);
        p1.NumeroPieza.Should().Be(1);

        var p2 = piezas.Single(p => p.Etiqueta == "031815712");
        p2.Posicion.Should().Be(1);
        p2.NumeroPieza.Should().Be(1);

        var p3 = piezas.Single(p => p.Etiqueta == "031815713");
        p3.Posicion.Should().Be(2);
        p3.NumeroPieza.Should().Be(1);

        // 4. Crear Hoja de Salida para el Pedido 10449822
        var crearHandler = new CrearHojaSalidaHandler(db, ptAcceso, pedidosPort, empresaContext, reloj);
        var crearCmd = new CrearHojaSalidaCommand(
            sucursalConkal, "Obra Conkal - CONSTRUCCIONES INDUSTRIALES RCO", false,
            null, null, null, null, null, null, null, null,
            [new PedidoCargadoDto("10449822", ["031813724"])]
        );
        var hoja1 = await crearHandler.Handle(crearCmd, CancellationToken.None);
        hoja1.Estado.Should().Be("Borrador");
        hoja1.Albaranes.Should().HaveCount(1);
        hoja1.Albaranes[0].NumeroPedido.Should().Be("10449822");

        // 5. Emitir Hoja de Salida (descuento de inventario, cambio a Embarcada y notificación a Facturación)
        var emitirHandler = new EmisorAutorizado(db, pedidosPort, facturacion, new EmitirHojaSalidaHandler(db, ptAcceso, pedidosPort, facturacion, events, empresaContext, reloj), reloj);
        var hojaEmitida = await emitirHandler.Handle(new EmitirHojaSalidaCommand(hoja1.Id), CancellationToken.None);
        hojaEmitida.Estado.Should().Be("Emitida");
        hojaEmitida.YaEmitida.Should().BeFalse();

        // Verificar que la pieza cambió a Embarcada
        var piezaEmbarcada = await db.Piezas.FirstAsync(p => p.Etiqueta == "031813724");
        piezaEmbarcada.Estado.Should().Be(EstadoPieza.Embarcada);

        // Verificar que Facturación recibió la salida
        facturacion.Salidas.Should().ContainSingle();
        facturacion.Salidas[0].PedidoId.Should().Be(facturable1);
        facturacion.Salidas[0].Referencia.Should().Be(hoja1.Albaranes[0].Folio);

        // Verificar evento publicado PedidoEnRutaEvent
        var evt = events.Eventos.OfType<PedidoEnRutaEvent>().Single();
        evt.NumeroPedido.Should().Be("10449822");
        evt.AlbaranFolio.Should().Be(hoja1.Albaranes[0].Folio);
        evt.HojaSalidaFolio.Should().Be(hoja1.Folio);

        // 6. Idempotencia: re-emitir la misma hoja no vuelve a descontar ni re-enviar
        var hojaReemitida = await emitirHandler.Handle(new EmitirHojaSalidaCommand(hoja1.Id), CancellationToken.None);
        hojaReemitida.YaEmitida.Should().BeTrue();
        facturacion.Salidas.Should().HaveCount(1); // No duplicó

        // 7. Flujo completo para Pedido 10449764 (DAVID DANIEL RUELAS NUÑEZ - 2 posiciones, 2 piezas)
        var crearCmd2 = new CrearHojaSalidaCommand(
            sucursalConkal, "Obra Conkal - DAVID DANIEL RUELAS NUÑEZ", false,
            null, null, null, null, null, null, null, null,
            [new PedidoCargadoDto("10449764", ["031815712", "031815713"])]
        );
        var hoja2 = await crearHandler.Handle(crearCmd2, CancellationToken.None);
        hoja2.Estado.Should().Be("Borrador");
        hoja2.Albaranes.Should().HaveCount(1);
        hoja2.Albaranes[0].NumeroPedido.Should().Be("10449764");
        hoja2.Albaranes[0].Total.Should().Be(2);
        hoja2.Albaranes[0].Etiquetas.Should().HaveCount(2);

        var hoja2Emitida = await emitirHandler.Handle(new EmitirHojaSalidaCommand(hoja2.Id), CancellationToken.None);
        hoja2Emitida.Estado.Should().Be("Emitida");
        hoja2Emitida.YaEmitida.Should().BeFalse();

        // Verificar que ambas piezas del pedido 10449764 pasaron a Embarcada
        var piezas2 = await db.Piezas.Where(p => p.PedidoPtId == facturable2 || p.Etiqueta == "031815712" || p.Etiqueta == "031815713").ToListAsync();
        piezas2.Should().HaveCount(2);
        piezas2.All(p => p.Estado == EstadoPieza.Embarcada).Should().BeTrue();

        // Verificar registro de salida en facturación para pedido 10449764
        facturacion.Salidas.Should().HaveCount(2);
        var salida2 = facturacion.Salidas.Single(s => s.PedidoId == facturable2);
        salida2.Referencia.Should().Be(hoja2.Albaranes[0].Folio);
        salida2.Lineas.Sum(l => l.Cantidad).Should().Be(2m);

        // Verificar evento PedidoEnRutaEvent para 10449764
        var evt2 = events.Eventos.OfType<PedidoEnRutaEvent>().Single(e => e.NumeroPedido == "10449764");
        evt2.AlbaranFolio.Should().Be(hoja2.Albaranes[0].Folio);
        evt2.HojaSalidaFolio.Should().Be(hoja2.Folio);
    }
}
