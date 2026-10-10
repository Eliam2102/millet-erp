using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Millet.Compras.Infrastructure.Migrations;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Application.Oc.Autorizar;
using Millet.Compras.Application.Oc.CancelarConRecepciones;
using Millet.Compras.Application.Oc.CrearOrdenCompraDesdeRequisicion;
using Millet.Compras.Application.Oc.DuplicarOrdenCompra;
using Millet.Compras.Application.Oc.Lineas.AgregarLineaDesdeRequisicion;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Millet.SharedKernel.Domain;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// Usa únicamente catálogos seed; crea documentos propios en PostgreSQL desechable.
public partial class OrdenesCompraEndpointsTests
{
    private static readonly int[] P2CiclosOcEsperados = [1, 2, 2];
    private static readonly int[] P2CiclosFirmaEsperados = [1, 1, 1, 1, 2];
    [Fact]
    public async Task P2_MigracionCiclos_ConservaYClasificaFirmasHistoricas()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        await using var conexion = new NpgsqlConnection(db.Database.GetConnectionString());
        await conexion.OpenAsync();
        // Tablas temporales de esta conexión: no modifica esquema ni catálogos compartidos.
        await using var preparar = new NpgsqlCommand("""
            CREATE TEMP TABLE ordenes_compra (id uuid PRIMARY KEY, estado smallint, ciclo_autorizacion integer DEFAULT 1);
            CREATE TEMP TABLE orden_compra_autorizaciones (id uuid PRIMARY KEY, orden_compra_id uuid,
                nivel smallint, resultado smallint, fecha_hora timestamptz, created_at timestamptz, ciclo integer DEFAULT 1);
            INSERT INTO ordenes_compra (id, estado) VALUES
                ('00000000-0000-0000-0000-000000000001', 6),
                ('00000000-0000-0000-0000-000000000002', 1),
                ('00000000-0000-0000-0000-000000000003', 2);
            INSERT INTO orden_compra_autorizaciones (id, orden_compra_id, nivel, resultado, fecha_hora, created_at) VALUES
                ('00000000-0000-0000-0000-000000000011', '00000000-0000-0000-0000-000000000001', 1, 1, '2026-10-09 12:00Z', '2026-10-09 12:00Z'),
                ('00000000-0000-0000-0000-000000000012', '00000000-0000-0000-0000-000000000001', 2, 2, '2026-10-09 13:00Z', '2026-10-09 13:00Z'),
                ('00000000-0000-0000-0000-000000000021', '00000000-0000-0000-0000-000000000002', 1, 2, '2026-10-09 12:00Z', '2026-10-09 12:00Z'),
                ('00000000-0000-0000-0000-000000000031', '00000000-0000-0000-0000-000000000003', 1, 2, '2026-10-09 12:00Z', '2026-10-09 12:00Z'),
                ('00000000-0000-0000-0000-000000000032', '00000000-0000-0000-0000-000000000003', 1, 1, '2026-10-09 13:00Z', '2026-10-09 13:00Z');
            """, conexion);
        await preparar.ExecuteNonQueryAsync();
        var sql = Assert.Single(new P2CiclosAutorizacionOc().UpOperations.OfType<SqlOperation>()).Sql;
        await using var migrar = new NpgsqlCommand(sql.Replace("compras.", "pg_temp.", StringComparison.Ordinal), conexion);
        await migrar.ExecuteNonQueryAsync();
        await using var consultar = new NpgsqlCommand("""
            SELECT ciclo_autorizacion FROM pg_temp.ordenes_compra ORDER BY id;
            SELECT ciclo FROM pg_temp.orden_compra_autorizaciones ORDER BY id;
            """, conexion);
        await using var filas = await consultar.ExecuteReaderAsync();
        var ciclosOc = new List<int>();
        while (await filas.ReadAsync()) ciclosOc.Add(filas.GetInt32(0));
        Assert.Equal(P2CiclosOcEsperados, ciclosOc);
        await filas.NextResultAsync();
        var ciclosFirma = new List<int>();
        while (await filas.ReadAsync()) ciclosFirma.Add(filas.GetInt32(0));
        Assert.Equal(P2CiclosFirmaEsperados, ciclosFirma);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task P2_Endpoint_NivelDenegado_403(int nivel)
    {
        // Una sesión sin empresa se rechaza antes del permiso por nivel.
        // Asignación real a empresa con el permiso del nivel opuesto.
        var client = await CreateClientConPermisosAsync(nivel == 1
            ? "compras.ordenes.autorizar-nivel2" : "compras.ordenes.autorizar-nivel1");
        var sesion = await client.GetAsync("/api/auth/me");
        sesion.EnsureSuccessStatusCode();
        var usuarioId = (await ReadJsonAsync(sesion)).GetProperty("userId").GetGuid();
        try
        {
            var response = await client.PostAsJsonAsync($"{EndpointBase}/{Guid.NewGuid()}/autorizaciones", new { Nivel = nivel });
            await AssertCodigoP2(response, HttpStatusCode.Forbidden, "AUTORIZAR_NIVEL_DENEGADO");
        }
        finally
        {
            // No dejar usuarios/roles de esta prueba en los catálogos compartidos.
            using var scope = _factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            var rolId = await db.UsuarioEmpresaRoles.Where(a => a.UsuarioId == usuarioId).Select(a => a.RolId).SingleAsync();
            await db.Usuarios.Where(u => u.Id == usuarioId).ExecuteDeleteAsync();
            await db.Roles.Where(r => r.Id == rolId).ExecuteDeleteAsync();
        }
    }

    [Theory]
    [InlineData("cancelar-con-recepciones")]
    [InlineData("resolver-cancelacion")]
    public async Task P2_Cancelacion_RequierePermisoDelPaso_403(string paso)
    {
        var client = _factory.CreateClientWithIdempotency();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos"));
        var response = await client.PostAsJsonAsync($"{EndpointBase}/{Guid.NewGuid()}/{paso}",
            new { MotivoCancelacionId = Guid.NewGuid(), MotivoCancelacionTexto = "Solicitud P2", Confirmar = true, Motivo = "Decisión P2" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task P2_SuperAdmin_NoAutoautoriza_422(int nivel)
    {
        var client = await CreateSuperAdminClientAsync();
        var creada = await client.PostAsJsonAsync(EndpointBase, ValidBody() with { SinRequisicionPrevia = true, MotivoSinRequisicion = "Prueba P2 de autoautorización" });
        creada.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(creada)).GetProperty("id").GetGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == id);
            AgregarLineaP2(oc);
            oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
            if (nivel == 2) oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        var response = await client.PostAsJsonAsync($"{EndpointBase}/{id}/autorizaciones", new { Nivel = nivel });
        await AssertCodigoP2(response, HttpStatusCode.UnprocessableEntity, "OC_AUTOAUTORIZACION");
    }

    [Fact]
    public async Task P2_N2IgualN1_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var (id, _) = await SembrarP2(client, recepcion: false, autorizar: false);
        (await client.PostAsJsonAsync($"{EndpointBase}/{id}/autorizaciones", new { Nivel = 1 })).EnsureSuccessStatusCode();
        await AssertCodigoP2(await client.PostAsJsonAsync($"{EndpointBase}/{id}/autorizaciones", new { Nivel = 2 }),
            HttpStatusCode.UnprocessableEntity, "OC_FIRMA_MISMA_PERSONA");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task P2_RechazarCorregirReenviar_PersisteDosCiclos_YPermiteRepetirJefe(int nivelRechazo)
    {
        var client = await CreateSuperAdminClientAsync();
        var (id, _) = await SembrarP2(client, recepcion: false, autorizar: false);
        Guid motivoId;
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            motivoId = (await db.MotivosRechazo.FirstAsync(m => m.Activo && (m.AplicaA & MotivoRechazoAplicaA.OrdenCompra) != 0)).Id;
            var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == id);
            var tipoId = (await db.TiposDocumentoOc.SingleAsync(t => t.Clave == "cotizacion")).Id;
            oc.AdjuntarDocumento(Guid.NewGuid(), tipoId, "p2-cotizacion.pdf", "p2-cotizacion", "application/pdf", 10,
                DateTimeOffset.UtcNow, Guid.NewGuid());
            await db.SaveChangesAsync();
        }
        if (nivelRechazo == 2)
            (await client.PostAsJsonAsync($"{EndpointBase}/{id}/autorizaciones", new { Nivel = 1 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"{EndpointBase}/{id}/rechazar",
            new { MotivoRechazoId = motivoId, MotivoRechazoTexto = "Corregir precio P2" })).EnsureSuccessStatusCode();
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == id);
            oc.ActualizarLinea(oc.Lineas.Single().Id, precioUnitario: 90);
            await db.SaveChangesAsync();
        }
        (await client.PostAsync($"{EndpointBase}/{id}/transmitir", null)).EnsureSuccessStatusCode();
        var reenviada = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{id}"));
        Assert.Equal(2, reenviada.GetProperty("cicloAutorizacion").GetInt32());
        Assert.Null(reenviada.GetProperty("motivoRechazoId").GetString());
        (await client.PostAsJsonAsync($"{EndpointBase}/{id}/autorizaciones", new { Nivel = 1 })).EnsureSuccessStatusCode();
        await AssertCodigoP2(await client.PostAsJsonAsync($"{EndpointBase}/{id}/autorizaciones", new { Nivel = 2 }),
            HttpStatusCode.UnprocessableEntity, "OC_FIRMA_MISMA_PERSONA");
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var sp = scope.ServiceProvider;
            await new AutorizarOrdenCompraHandler(sp.GetRequiredService<ComprasDbContext>(),
                sp.GetRequiredService<CompartidoDbContext>(), new UsuarioP2(Guid.NewGuid()),
                sp.GetRequiredService<IClock>(), sp.GetRequiredService<IPublisher>())
                .Handle(new(id, NivelAutorizacion.Nivel2, null), default);
        }
        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{id}"));
        Assert.Equal((int)EstadoOrdenCompra.Autorizada, detalle.GetProperty("estado").GetInt32());
        var firmas = detalle.GetProperty("autorizaciones").EnumerateArray().ToList();
        Assert.Equal(nivelRechazo == 2 ? 4 : 3, firmas.Count);
        Assert.Equal(2, firmas.Count(f => f.GetProperty("ciclo").GetInt32() == 2));
        var rechazo = Assert.Single(firmas, f => f.GetProperty("resultado").GetInt32() == 2);
        Assert.Equal(1, rechazo.GetProperty("ciclo").GetInt32());
        Assert.Equal(nivelRechazo, rechazo.GetProperty("nivel").GetInt32());
        Assert.Equal("Corregir precio P2", rechazo.GetProperty("motivoRechazoTexto").GetString());
        Assert.False(string.IsNullOrWhiteSpace(rechazo.GetProperty("motivoRechazoNombre").GetString()));
        Assert.True(rechazo.GetProperty("fechaHora").GetDateTimeOffset() > DateTimeOffset.MinValue);
        if (nivelRechazo == 2)
            Assert.Single(firmas.Where(f => f.GetProperty("nivel").GetInt32() == 1)
                .Select(f => f.GetProperty("usuarioId").GetGuid()).Distinct());
    }

    [Fact]
    public async Task P2_Cancelacion_DosUsuarios_SoloDevuelveNoRecibido_YNuevaOcSoloFaltante()
    {
        var client = await CreateSuperAdminClientAsync();
        var (id, rqId) = await SembrarP2(client, recepcion: true);
        await SolicitarP2(client, id);
        await AssertCodigoP2(await client.PostAsJsonAsync($"{EndpointBase}/{id}/resolver-cancelacion",
            new { Confirmar = true, Motivo = "Misma persona no debe confirmar" }),
            HttpStatusCode.UnprocessableEntity, "OC_CANCELACION_MISMA_PERSONA");
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.Include(o => o.SolicitudesCancelacion).SingleAsync(o => o.Id == id);
            Assert.Equal(EstadoOrdenCompra.CancelacionSolicitada, oc.Estado);
            Assert.Equal(id, (await db.Requisiciones.SingleAsync(r => r.Id == rqId)).ComprometidaEnOcId);
            // Segunda sesión simulada en el handler; las comprobaciones de permiso se prueban por HTTP.
            var handler = new ResolverCancelacionOcHandler(db, new UsuarioP2(Guid.NewGuid()),
                scope.ServiceProvider.GetRequiredService<IClock>(), scope.ServiceProvider.GetRequiredService<IPublisher>());
            await handler.Handle(new(id, true, "Dirección confirma el faltante"), default);
        }
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.Include(o => o.Lineas).Include(o => o.SolicitudesCancelacion).SingleAsync(o => o.Id == id);
            Assert.Equal(EstadoOrdenCompra.Cancelada, oc.Estado);
            Assert.Equal(4m, oc.Lineas.Single().CantidadRecibida);
            Assert.Null((await db.Requisiciones.SingleAsync(r => r.Id == rqId)).ComprometidaEnOcId);
            Assert.NotEqual(oc.SolicitudesCancelacion.Single().SolicitanteId, oc.SolicitudesCancelacion.Single().ResolutorId);
        }
        var nueva = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion", DesdeRqP2(rqId));
        nueva.EnsureSuccessStatusCode();
        var nuevoId = (await ReadJsonAsync(nueva)).GetProperty("ordenCompraId").GetGuid();
        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{nuevoId}"));
        Assert.Equal(6m, detalle.GetProperty("lineas")[0].GetProperty("cantidad").GetDecimal());
    }

    [Fact]
    public async Task P2_Solicitud_Rechazo_RestauraEstado_YSeVeEnDetalleYBandeja()
    {
        var client = await CreateSuperAdminClientAsync();
        var (id, _) = await SembrarP2(client, recepcion: true);
        await SolicitarP2(client, id);
        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{id}"));
        Assert.Single(detalle.GetProperty("solicitudesCancelacion").EnumerateArray());
        var bandeja = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/pendientes-autorizacion?nivel=Nivel2&pageSize=200"));
        Assert.Contains(bandeja.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        await new ResolverCancelacionOcHandler(db, new UsuarioP2(Guid.NewGuid()),
            scope.ServiceProvider.GetRequiredService<IClock>(), scope.ServiceProvider.GetRequiredService<IPublisher>())
            .Handle(new(id, false, "La compra debe continuar"), default);
        db.ChangeTracker.Clear();
        var oc = await db.OrdenesCompra.Include(o => o.SolicitudesCancelacion).SingleAsync(o => o.Id == id);
        Assert.Equal(EstadoOrdenCompra.Autorizada, oc.Estado);
        Assert.False(oc.SolicitudesCancelacion.Single().Confirmada);
    }

    [Fact]
    public async Task P2_RecibirConCancelacionSolicitada_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var (id, _) = await SembrarP2(client, recepcion: true);
        await SolicitarP2(client, id);
        var response = await client.PostAsJsonAsync("/api/v1/almacen/recepciones/packing-list", new
        {
            OrdenCompraId = id, FechaMovimiento = "2026-10-09", PackingListBlobRef = "p2-prueba",
            Lineas = new[] { new { ArticuloId = Guid.Parse("00000007-0001-0000-0000-000000000001"), Cantidad = 1m, UbicacionId = Guid.NewGuid() } },
        });
        await AssertCodigoP2(response, HttpStatusCode.UnprocessableEntity, "OC_CANCELACION_SOLICITADA");
    }

    [Theory]
    [InlineData(true, "OC_EXCEDE_SALDO_RQ")]
    [InlineData(false, "OC_LINEA_RQ_ARTICULO_FIJO")]
    public async Task P2_LineaHeredada_NoExcedeNiCambiaArticulo(bool cantidad, string codigo)
    {
        var client = await CreateSuperAdminClientAsync();
        var (id, _) = await SembrarP2(client, recepcion: false, borrador: true);
        var detalleResponse = await client.GetAsync($"{EndpointBase}/{id}");
        var detalle = await ReadJsonAsync(detalleResponse);
        var lineaId = detalle.GetProperty("lineas")[0].GetProperty("id").GetGuid();
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{EndpointBase}/{id}/lineas/{lineaId}")
        {
            Content = cantidad ? JsonContent.Create(new { Cantidad = 11m }) : JsonContent.Create(new { ArticuloId = Guid.NewGuid() }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", detalleResponse.Headers.ETag?.ToString());
        await AssertCodigoP2(await client.SendAsync(request), HttpStatusCode.UnprocessableEntity, codigo);
    }

    [Fact]
    public async Task P2_DuplicarRechazadaConRq_BloqueaSegundoCompromiso()
    {
        var client = await CreateSuperAdminClientAsync();
        var (id, rqId) = await SembrarP2(client, recepcion: false, autorizar: false);
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == id);
            oc.Rechazar(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), "Revisar");
            await db.SaveChangesAsync();
        }
        var response = await client.PostAsJsonAsync($"{EndpointBase}/{id}/duplicar", new { SucursalCodigo = "MID", FolioAnio = 2026, FechaDocumento = "2026-10-09" });
        await AssertCodigoP2(response, HttpStatusCode.UnprocessableEntity, "OC_RECHAZADA_RQ_COMPROMETIDA");
        using var checkScope = _factory.Services.CreateScope();
        using var checkBypass = checkScope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        Assert.Equal(id, (await checkScope.ServiceProvider.GetRequiredService<ComprasDbContext>().Requisiciones.SingleAsync(r => r.Id == rqId)).ComprometidaEnOcId);
    }

    [Theory]
    [InlineData("crear", Dim3Elegibilidad.Inactiva)]
    [InlineData("crear", Dim3Elegibilidad.FueraDeAlcance)]
    [InlineData("agregar", Dim3Elegibilidad.Inactiva)]
    [InlineData("agregar", Dim3Elegibilidad.FueraDeAlcance)]
    [InlineData("duplicar", Dim3Elegibilidad.Inactiva)]
    [InlineData("duplicar", Dim3Elegibilidad.FueraDeAlcance)]
    public async Task P2_CecoInvalido_SeValidaEnLosTresHandlersConAlcance(string flujo, Dim3Elegibilidad elegibilidad)
    {
        var client = await CreateSuperAdminClientAsync();
        var (id, rqId) = await SembrarP2(client, recepcion: false, borrador: true);
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ComprasDbContext>();
        var oc = await db.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == id);
        oc.Cancelar(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), "Preparar prueba");
        (await db.Requisiciones.SingleAsync(r => r.Id == rqId)).LiberarDeOc();
        await db.SaveChangesAsync();
        var puerto = new Dim3InactivaP2(elegibilidad);
        var usuario = new UsuarioP2(Guid.NewGuid());
        var empresa = new EmpresaP2();
        var reloj = sp.GetRequiredService<IClock>();
        var publisher = sp.GetRequiredService<IPublisher>();
        var articulos = sp.GetRequiredService<IArticuloReadPort>();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(async () => {
            if (flujo == "crear")
                await new CrearOrdenCompraDesdeRequisicionHandler(db, puerto, sp.GetRequiredService<CompartidoDbContext>(), usuario, empresa, reloj, publisher, articulos)
                    .Handle(DesdeRqP2(rqId), default);
            else if (flujo == "duplicar")
                await new DuplicarOrdenCompraHandler(db, puerto, usuario, empresa, reloj, publisher)
                    .Handle(new(id, "MID", 2026, new DateOnly(2026, 10, 9)), default);
            else
                await new AgregarLineaDesdeRequisicionHandler(db, puerto, empresa, reloj, publisher,
                    sp.GetRequiredService<IDecimalesUnidadGuard>(), articulos).Handle(new(id, rqId), default);
        });
        Assert.Equal("CECO_INVALIDO", ex.Code);
        Assert.True(puerto.AplicaAlcance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task P2_DuplicarOConsolidar_CanceladaParcial_CompraSoloSeis_YBloqueaOtraOc(bool duplicar)
    {
        var client = await CreateSuperAdminClientAsync();
        var (id, rqId) = await SembrarP2(client, recepcion: true);
        await SolicitarP2(client, id);
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await new ResolverCancelacionOcHandler(scope.ServiceProvider.GetRequiredService<ComprasDbContext>(),
                new UsuarioP2(Guid.NewGuid()), scope.ServiceProvider.GetRequiredService<IClock>(),
                scope.ServiceProvider.GetRequiredService<IPublisher>())
                .Handle(new(id, true, "Confirmar saldo pendiente"), default);
        }
        Guid nuevaId;
        if (duplicar)
        {
            var response = await client.PostAsJsonAsync($"{EndpointBase}/{id}/duplicar",
                new { SucursalCodigo = "MID", FolioAnio = 2026, FechaDocumento = "2026-10-09" });
            response.EnsureSuccessStatusCode();
            nuevaId = (await ReadJsonAsync(response)).GetProperty("ordenCompraNuevaId").GetGuid();
        }
        else
        {
            var response = await client.PostAsJsonAsync(EndpointBase, ValidBody());
            response.EnsureSuccessStatusCode();
            nuevaId = (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
            (await client.PostAsJsonAsync($"{EndpointBase}/{nuevaId}/lineas/desde-requisicion",
                new { RequisicionId = rqId })).EnsureSuccessStatusCode();
        }
        var detalle = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{nuevaId}"));
        Assert.Equal(6m, detalle.GetProperty("lineas")[0].GetProperty("cantidad").GetDecimal());
        Assert.Equal(rqId, detalle.GetProperty("lineas")[0].GetProperty("requisicionId").GetGuid());
        await AssertCodigoP2(await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion", DesdeRqP2(rqId)),
            HttpStatusCode.UnprocessableEntity, "RQ_YA_COMPROMETIDA");
    }

    [Fact]
    public async Task P2_FacturarConCancelacionSolicitada_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var (id, _) = await SembrarP2(client, recepcion: true);
        await SolicitarP2(client, id);
        var response = await client.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas", new
        {
            OrdenCompraId = id, ProveedorId = ProveedorActivoId, SucursalId = SucursalIdFija,
            FechaDocumento = "2026-10-09T12:00:00Z", FechaContabilizacion = "2026-10-09T12:00:00Z",
            FechaVencimiento = "2026-10-30", Moneda = "MXN", Subtotal = 100m,
            Descuentos = 0m, ImpuestosTrasladados = 0m, Retenciones = 0m, Total = 100m,
            Lineas = new[] { new { ArticuloId = Guid.Parse("00000007-0001-0000-0000-000000000001"),
                Descripcion = "Prueba P2", Cantidad = 1m, ClaveUnidad = "H87", PrecioUnitario = 100m, Importe = 100m } },
        });
        await AssertCodigoP2(response, HttpStatusCode.UnprocessableEntity, "OC_ESTADO_NO_FACTURABLE");
    }

    private async Task<(Guid OcId, Guid RqId)> SembrarP2(HttpClient client, bool recepcion, bool autorizar = true, bool borrador = false)
    {
        var response = await client.PostAsJsonAsync(EndpointBase, ValidBody());
        response.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var oc = await db.OrdenesCompra.SingleAsync(o => o.Id == id);
        // Capturista distinto del super-admin firmante; se mantiene la identidad del documento.
        db.Entry(oc).Property(o => o.CompradorTitularId).CurrentValue = Guid.NewGuid();
        var numero = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999);
        var rq = new Requisicion(Guid.NewGuid(), oc.EmpresaId, Millet.Compras.Domain.Folio.Parse($"MID2026-{numero:D6}"), 2026,
            Clasificacion.MateriaPrima, oc.SucursalDestinoId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Prioridad.Normal, DateTimeOffset.UtcNow, null);
        var l = rq.AgregarLinea(Guid.NewGuid(), Guid.Parse("00000007-0001-0000-0000-000000000001"), 10, "PZA", Money.Mxn(100), centroCostoId: CentroCostoSeedId);
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        rq.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow, RequiereNivel.SoloN1);
        rq.RegistrarCubrimiento([new CubrimientoLinea(l.Id, 0, 10)], DateTimeOffset.UtcNow);
        oc.AgregarLineaDesdeRequisicion(Guid.NewGuid(), l.ArticuloId, 10, "PZA", 100, rq.DepartamentoId, rq.Id, l.Id, centroCostoId: CentroCostoSeedId);
        rq.ComprometerEnOc(id);
        if (!borrador)
        {
            oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
            if (autorizar)
            {
                oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow);
                oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, Guid.NewGuid(), DateTimeOffset.UtcNow);
            }
        }
        if (recepcion) oc.RegistrarRecepcionLinea(oc.Lineas.Single().Id, 4, DateTimeOffset.UtcNow);
        db.Requisiciones.Add(rq);
        await db.SaveChangesAsync();
        return (id, rq.Id);
    }

    private async Task SolicitarP2(HttpClient client, Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var motivo = await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().MotivosRechazo
            .FirstAsync(m => m.Activo && (m.AplicaA & MotivoRechazoAplicaA.Cancelacion) != 0);
        (await client.PostAsJsonAsync($"{EndpointBase}/{id}/cancelar-con-recepciones",
            new { MotivoCancelacionId = motivo.Id, MotivoCancelacionTexto = "Cancelar el faltante P2" })).EnsureSuccessStatusCode();
    }

    private static CrearOrdenCompraDesdeRequisicionCommand DesdeRqP2(Guid id) => new(id, "MID", 2026,
        ProveedorActivoId, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 9));
    private static void AgregarLineaP2(OrdenCompra oc) => oc.AgregarLineaManual(Guid.NewGuid(),
        Guid.Parse("00000007-0001-0000-0000-000000000001"), 10, "PZA", 100, Guid.NewGuid(), centroCostoId: CentroCostoSeedId);
    private static async Task AssertCodigoP2(HttpResponseMessage response, HttpStatusCode status, string codigo)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(codigo, await response.Content.ReadAsStringAsync());
    }
    private sealed class UsuarioP2(Guid id) : ICurrentUserContext { public Guid? UserId => id; public string? UserName => "Prueba P2"; }
    private sealed class EmpresaP2 : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaInicialId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
    private sealed class Dim3InactivaP2(Dim3Elegibilidad elegibilidad) : IDim3ElegibilidadPort
    {
        public bool AplicaAlcance { get; private set; }
        public Task<Dim3Elegibilidad> EvaluarAsync(Guid id, bool aplicarAlcance, CancellationToken ct)
        { AplicaAlcance = aplicarAlcance; return Task.FromResult(elegibilidad); }
    }
}
