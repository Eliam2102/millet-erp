using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Infrastructure.Persistence;
using Nc = Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor;

namespace Millet.Api.IntegrationTests.P6;

// Reutiliza preparación, usuarios, periodo abierto y limpieza de P6. Las sucursales son del seed ficticio.
public sealed partial class P6SucursalEndpointsTests
{
    public static TheoryData<string, string, string, HttpStatusCode> EscriturasP6b => new()
    {
        { "anticipo", "cancelar", PermisosCanonicos.CuentasPorPagarAnticiposCapturar, HttpStatusCode.NoContent },
        { "anticipo", "amortizar-nc", PermisosCanonicos.CuentasPorPagarAnticiposCapturar, HttpStatusCode.NoContent },
        { "nota_cargo", "cancelar", PermisosCanonicos.CuentasPorPagarNotasCargoCrear, HttpStatusCode.NoContent },
        { "nota_cargo", "aplicar", PermisosCanonicos.CuentasPorPagarNotasCargoAplicar, HttpStatusCode.OK },
        { "nota_cargo", "formalizar", PermisosCanonicos.CuentasPorPagarNotasCargoAplicar, HttpStatusCode.NoContent },
        { "nota_credito", "cancelar", PermisosCanonicos.CuentasPorPagarNotasCreditoCapturar, HttpStatusCode.NoContent },
        { "factura", "aplicar-nc", PermisosCanonicos.CuentasPorPagarNotasCreditoCapturar, HttpStatusCode.OK },
        { "factura", "aplicar-anticipo", PermisosCanonicos.CuentasPorPagarAnticiposCapturar, HttpStatusCode.OK },
        { "factura", "enviar-revision", PermisosCanonicos.CuentasPorPagarFacturasEnviarRevision, HttpStatusCode.OK },
        { "pasivo", "solicitar-cancelacion", PermisosCanonicos.TesoreriaPasivosSolicitarCancelacion, HttpStatusCode.Accepted },
    };

    [Theory]
    [MemberData(nameof(EscriturasP6b))]
    public async Task P6b_ruta_ajena_403_sin_mutacion_y_propias_y_corporativas_persisten(
        string tipo, string accion, string permiso, HttpStatusCode esperado)
    {
        await using var datos = await PrepararP6bAsync(accion);
        var antes = await EstadoP6bAsync(datos.Ajenos);
        using var denegada = await EnviarP6bAsync(datos.Operativo, tipo, accion, datos.Ajenos);
        Assert.Equal(HttpStatusCode.Forbidden, denegada.StatusCode);
        Assert.Contains("SUCURSAL_NO_ASOCIADA", await denegada.Content.ReadAsStringAsync());
        Assert.Equal(antes, await EstadoP6bAsync(datos.Ajenos));
        using var propia = await EnviarP6bAsync(datos.Operativo, tipo, accion, datos.Propios);
        Assert.Equal(esperado, propia.StatusCode);
        using var corporativa = await EnviarP6bAsync(datos.Corporativo, tipo, accion, datos.Ajenos);
        Assert.Equal(esperado, corporativa.StatusCode);
        // La respuesta de éxito debe corresponder a un cambio persistido, incluida la Outbox de Tesorería.
        Assert.NotEqual(antes, await EstadoP6bAsync(datos.Ajenos));
        await QuitarPermisoP6bAsync(datos, permiso);
        using var sinPermiso = await EnviarP6bAsync(datos.Operativo, tipo, accion, datos.Ajenos);
        Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);
        Assert.DoesNotContain("SUCURSAL_NO_", await sinPermiso.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("anticipo", "amortizar-nc", "nc07")]
    [InlineData("nota_cargo", "aplicar", "factura")]
    [InlineData("nota_cargo", "formalizar", "nc03")]
    [InlineData("factura", "aplicar-nc", "nota_credito")]
    [InlineData("factura", "aplicar-anticipo", "anticipo")]
    public async Task P6b_documento_propio_no_autoriza_referencia_secundaria_ajena(
        string tipo, string accion, string secundaria)
    {
        await using var datos = await PrepararP6bAsync(accion);
        var referencias = new Dictionary<string, Guid>(datos.Propios) { [secundaria] = datos.Ajenos[secundaria] };
        using var response = await EnviarP6bAsync(datos.Operativo, tipo, accion, datos.Propios, referencias);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("SUCURSAL_NO_ASOCIADA", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task P6b_aplicar_cargo_sin_factura_en_body_valida_la_factura_persistida()
    {
        await using var datos = await PrepararP6bAsync("aplicar");
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            (await db.NotasCargo.FindAsync(datos.Propios["nota_cargo"]))!.VincularFactura(datos.Ajenos["factura"]);
            await db.SaveChangesAsync();
        }
        var antes = await EstadoP6bAsync(datos.Ajenos);
        using var request = new HttpRequestMessage(HttpMethod.Post, Ruta("nota_cargo", datos.Propios["nota_cargo"]) + "/aplicar")
        { Content = JsonContent.Create(new { }) };
        request.Headers.Add("X-Expected-Version", (await VersionP6bAsync("nota_cargo", datos.Propios["nota_cargo"])).ToString());
        using var response = await datos.Operativo.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(antes, await EstadoP6bAsync(datos.Ajenos));
    }

    [Fact]
    public async Task P6b_anticipo_sin_OC_solo_puede_cancelarse_con_alcance_corporativo()
    {
        await using var datos = await PrepararAsync();
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var anticipo = (await db.AnticiposProveedor.FindAsync(datos.Propios["anticipo"]))!;
            db.Entry(anticipo).Property(x => x.OrdenCompraId).CurrentValue = null;
            await db.SaveChangesAsync();
        }
        using var operativo = await EnviarP6bAsync(datos.Operativo, "anticipo", "cancelar", datos.Propios);
        Assert.Equal(HttpStatusCode.Forbidden, operativo.StatusCode);
        Assert.Contains("SUCURSAL_NO_DETERMINADA", await operativo.Content.ReadAsStringAsync());
        using var corporativo = await EnviarP6bAsync(datos.Corporativo, "anticipo", "cancelar", datos.Propios);
        Assert.Equal(HttpStatusCode.NoContent, corporativo.StatusCode);
    }

    [Fact]
    public async Task P6b_vincular_NC_valida_ambos_documentos_y_NC_sin_origen_requiere_corporativo()
    {
        await using var datos = await PrepararP6bAsync("");
        using var ajena = await EnviarP6bAsync(datos.Operativo, "nota_credito", "vincular-factura", datos.Ajenos);
        Assert.Equal(HttpStatusCode.Forbidden, ajena.StatusCode);
        var referencias = new Dictionary<string, Guid>(datos.Propios) { ["factura"] = datos.Ajenos["factura"] };
        using var destinoAjeno = await EnviarP6bAsync(datos.Operativo, "nota_credito", "vincular-factura", datos.Propios, referencias);
        Assert.Equal(HttpStatusCode.Forbidden, destinoAjeno.StatusCode);
        // Una NC propia ya vinculada pasa el control territorial y conserva la validación de estado de P4.
        using var propia = await EnviarP6bAsync(datos.Operativo, "nota_credito", "vincular-factura", datos.Propios);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, propia.StatusCode);
        Assert.Contains("NC_NO_EN_ESPERA", await propia.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var factura = (await db.FacturasProveedor.FindAsync(datos.Propios["factura"]))!;
            var nc = NuevaNcP6b(factura.UuidCfdi!, null, TipoRelacionCfdi.NotaCredito);
            datos.Propios["nc_sin_origen"] = nc.Id;
            db.Add(nc); await db.SaveChangesAsync();
        }
        var sinOrigen = new Dictionary<string, Guid>(datos.Propios) { ["nota_credito"] = datos.Propios["nc_sin_origen"] };
        using var sinAlcance = await EnviarP6bAsync(datos.Operativo, "nota_credito", "vincular-factura", sinOrigen);
        Assert.Equal(HttpStatusCode.Forbidden, sinAlcance.StatusCode);
        Assert.Contains("SUCURSAL_NO_DETERMINADA", await sinAlcance.Content.ReadAsStringAsync());
        using var corporativa = await EnviarP6bAsync(datos.Corporativo, "nota_credito", "vincular-factura", sinOrigen);
        Assert.Equal(HttpStatusCode.OK, corporativa.StatusCode);
        await QuitarPermisoP6bAsync(datos, PermisosCanonicos.CuentasPorPagarNotasCreditoCapturar);
        using var sinPermiso = await EnviarP6bAsync(datos.Operativo, "nota_credito", "vincular-factura", datos.Ajenos);
        Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);
        Assert.DoesNotContain("SUCURSAL_NO_", await sinPermiso.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("GET", PermisosCanonicos.CuentasPorPagarAnticiposLeer)]
    [InlineData("PUT", PermisosCanonicos.CuentasPorPagarAnticiposCapturar)]
    public async Task P6b_serie_es_configuracion_por_proveedor_y_exige_su_permiso(string metodo, string permiso)
    {
        await using var datos = await PrepararAsync();
        // Usa un proveedor simulado exclusivo para evitar cambiar configuración del proveedor compartido.
        var proveedor = Guid.NewGuid();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Millet.CuentasPorPagar.Domain.Ports.DatosMaestros.IProveedorReadPort>();
            services.AddSingleton<Millet.CuentasPorPagar.Domain.Ports.DatosMaestros.IProveedorReadPort>(new ProveedorSerieP6b(proveedor));
        }));
        using var client = app.CreateClientWithIdempotency();
        client.DefaultRequestHeaders.Authorization = datos.Operativo.DefaultRequestHeaders.Authorization;
        try
        {
            foreach (var corporativo in new[] { false, true })
            {
                client.DefaultRequestHeaders.Authorization = (corporativo ? datos.Corporativo : datos.Operativo).DefaultRequestHeaders.Authorization;
                using var response = await EnviarSerieP6bAsync(client, metodo, proveedor);
                Assert.Equal(metodo == "GET" ? HttpStatusCode.OK : HttpStatusCode.NoContent, response.StatusCode);
            }
            await QuitarPermisoP6bAsync(datos, permiso);
            using (var scope = app.Services.CreateScope())
                await scope.ServiceProvider.GetRequiredService<IPermissionCache>().InvalidateAllForUserAsync(datos.UsuarioId);
            client.DefaultRequestHeaders.Authorization = datos.Operativo.DefaultRequestHeaders.Authorization;
            using var denegada = await EnviarSerieP6bAsync(client, metodo, proveedor);
            Assert.Equal(HttpStatusCode.Forbidden, denegada.StatusCode);
        }
        finally
        {
            using var scope = app.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().ConfiguracionesAnticipoProveedor
                .Where(x => x.ProveedorId == proveedor).ExecuteDeleteAsync();
        }
    }

    private static async Task<HttpResponseMessage> EnviarSerieP6bAsync(HttpClient client, string metodo, Guid proveedor)
    {
        using var request = new HttpRequestMessage(new HttpMethod(metodo), $"/api/v1/cuentas-por-pagar/anticipos/serie/{proveedor}");
        if (metodo == "PUT") request.Content = JsonContent.Create(new { Serie = "FANT-P6B" });
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task P6b_REPP_filtra_pendientes_y_valida_factura_y_pagos_antes_de_registrar()
    {
        await using var datos = await PrepararAsync();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Millet.Tesoreria.Domain.Ports.IReppXmlBlobStorage>();
            services.AddSingleton<Millet.Tesoreria.Domain.Ports.IReppXmlBlobStorage, BlobReppP6b>();
        }));
        using var operativo = app.CreateClientWithIdempotency();
        using var corporativo = app.CreateClientWithIdempotency();
        operativo.DefaultRequestHeaders.Authorization = datos.Operativo.DefaultRequestHeaders.Authorization;
        corporativo.DefaultRequestHeaders.Authorization = datos.Corporativo.DefaultRequestHeaders.Authorization;
        using (var scope = factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
            foreach (var mapa in new[] { datos.Propios, datos.Ajenos })
            {
                var factura = (await db.FacturasProveedor.FindAsync(mapa["factura"]))!;
                var pasivo = (await tes.PasivosPendientesPago.FindAsync(mapa["pasivo"]))!;
                pasivo.ActualizarDesdeEvento(116m, 0m, "MXN", null, pasivo.FechaVencimiento,
                    Guid.Parse(factura.UuidCfdi!), factura.FolioProveedor, DateTimeOffset.UtcNow, "PPD");
            }
            await tes.SaveChangesAsync();
        }
        var ruta = $"/api/v1/tesoreria/repp-pendientes?proveedorId={Proveedor}&limit=500";
        var propios = await operativo.GetStringAsync(ruta);
        Assert.Contains(datos.Propios["pago"].ToString(), propios);
        Assert.DoesNotContain(datos.Ajenos["pago"].ToString(), propios);
        var todos = await corporativo.GetStringAsync(ruta);
        Assert.Contains(datos.Propios["pago"].ToString(), todos);
        Assert.Contains(datos.Ajenos["pago"].ToString(), todos);
        using var ajena = await RegistrarReppP6bAsync(operativo, datos.Ajenos);
        Assert.Equal(HttpStatusCode.Forbidden, ajena.StatusCode);
        var referencias = new Dictionary<string, Guid>(datos.Propios) { ["pago"] = datos.Ajenos["pago"] };
        using var pagoAjeno = await RegistrarReppP6bAsync(operativo, referencias);
        Assert.Equal(HttpStatusCode.Forbidden, pagoAjeno.StatusCode);
        Assert.Contains("SUCURSAL_NO_ASOCIADA", await pagoAjeno.Content.ReadAsStringAsync());
        using var propia = await RegistrarReppP6bAsync(operativo, datos.Propios);
        Assert.Equal(HttpStatusCode.Created, propia.StatusCode);
        using var corporativa = await RegistrarReppP6bAsync(corporativo, datos.Ajenos);
        Assert.Equal(HttpStatusCode.Created, corporativa.StatusCode);
        await QuitarPermisoP6bAsync(datos, PermisosCanonicos.TesoreriaReppRegistrar);
        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IPermissionCache>().InvalidateAllForUserAsync(datos.UsuarioId);
        using var sinPermiso = await RegistrarReppP6bAsync(operativo, datos.Ajenos);
        Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);
        Assert.DoesNotContain("SUCURSAL_NO_", await sinPermiso.Content.ReadAsStringAsync());
        using var listadoDenegado = await operativo.GetAsync(ruta);
        Assert.Equal(HttpStatusCode.Forbidden, listadoDenegado.StatusCode);
        using var verificar = factory.Services.CreateScope();
        using var empresa = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var tesFinal = verificar.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        Assert.Equal(2, await tesFinal.ReppsProveedorRecibidos.CountAsync(x =>
            x.FacturaProveedorId == datos.Propios["factura"] || x.FacturaProveedorId == datos.Ajenos["factura"]));
    }

    private async Task<HttpResponseMessage> RegistrarReppP6bAsync(HttpClient client, Dictionary<string, Guid> mapa)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var factura = (await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().FacturasProveedor.FindAsync(mapa["factura"]))!;
        var proveedor = await scope.ServiceProvider.GetRequiredService<Millet.Tesoreria.Domain.Ports.DatosMaestros.IProveedorBancoReadPort>().ObtenerAsync(Proveedor, default);
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow); var uuid = Guid.NewGuid();
        var xml = $"""
            <cfdi:Comprobante xmlns:cfdi="http://www.sat.gob.mx/cfd/4" xmlns:p="http://www.sat.gob.mx/Pagos20" xmlns:t="http://www.sat.gob.mx/TimbreFiscalDigital" Version="4.0" TipoDeComprobante="P" Fecha="{fecha:yyyy-MM-dd}T12:00:00">
              <cfdi:Emisor Rfc="{proveedor!.Rfc}"/><cfdi:Complemento><t:TimbreFiscalDigital UUID="{uuid}"/>
              <p:Pagos Version="2.0"><p:Pago FechaPago="{fecha:yyyy-MM-dd}T12:00:00" MonedaP="MXN" Monto="5">
                <p:DoctoRelacionado IdDocumento="{factura.UuidCfdi}" MonedaDR="MXN" NumParcialidad="1" ImpSaldoAnt="116" ImpPagado="5" ImpSaldoInsoluto="111"/>
              </p:Pago></p:Pagos></cfdi:Complemento>
            </cfdi:Comprobante>
            """;
        return await client.PostAsJsonAsync("/api/v1/tesoreria/repp-recibidos", new
        {
            FacturaProveedorId = factura.Id, UuidComplemento = uuid, FechaComplemento = fecha,
            XmlBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(xml)),
            Pagos = new[] { new { PagoId = mapa["pago"], Importe = 5m } },
        });
    }

    private sealed class BlobReppP6b : Millet.Tesoreria.Domain.Ports.IReppXmlBlobStorage
    {
        public Task<string> GuardarXmlAsync(string uuid, DateOnly fechaComplemento, Stream contenido, CancellationToken cancellationToken) => Task.FromResult("P6b-ficticio/" + uuid);
        public Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken cancellationToken) => Task.FromResult<Stream?>(null);
    }

    private async Task<Datos> PrepararP6bAsync(string accion)
    {
        var datos = await PrepararAsync();
        try
        {
            using var scope = factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
            foreach (var mapa in new[] { datos.Propios, datos.Ajenos })
            {
                var factura = (await db.FacturasProveedor.FindAsync(mapa["factura"]))!;
                var anticipo = (await db.AnticiposProveedor.FindAsync(mapa["anticipo"]))!;
                var nc07 = NuevaNcP6b(anticipo.UuidCfdi, null, TipoRelacionCfdi.AmortizacionAnticipo);
                nc07.VincularAnticipoOrigen(anticipo.Id, DateTimeOffset.UtcNow);
                var nc03 = NuevaNcP6b(factura.UuidCfdi!, factura.Id, TipoRelacionCfdi.Devolucion);
                mapa["nc07"] = nc07.Id; mapa["nc03"] = nc03.Id;
                db.AddRange(nc07, nc03);
                var cargo = (await db.NotasCargo.FindAsync(mapa["nota_cargo"]))!;
                if (accion is "aplicar" or "formalizar") cargo.Autorizar(datos.UsuarioId, DateTimeOffset.UtcNow);
                if (accion == "formalizar")
                {
                    factura.AplicarNotaCargo(cargo.Monto, cargo.Id, DateOnly.FromDateTime(DateTime.UtcNow));
                    cargo.Aplicar(datos.UsuarioId, DateTimeOffset.UtcNow);
                }
            }
            await db.SaveChangesAsync();
            return datos;
        }
        catch { await datos.DisposeAsync(); throw; }
    }

    private static Nc NuevaNcP6b(string uuidRelacion, Guid? factura, TipoRelacionCfdi relacion) => Nc.Capturar(
        Empresa, null, Guid.NewGuid().ToString(), Proveedor, "P6b ficticio", null, DateTimeOffset.UtcNow, "MXN", null,
        10m, 0m, 0m, 10m, relacion == TipoRelacionCfdi.Devolucion ? TipoNotaCredito.Devolucion :
            relacion == TipoRelacionCfdi.AmortizacionAnticipo ? TipoNotaCredito.AmortizacionAnticipo : TipoNotaCredito.Descuento,
        relacion, uuidRelacion, factura, null, DateTimeOffset.UtcNow);

    private async Task<HttpResponseMessage> EnviarP6bAsync(HttpClient client, string tipo, string accion,
        Dictionary<string, Guid> mapa, Dictionary<string, Guid>? referencias = null)
    {
        referencias ??= mapa;
        var id = mapa[tipo == "pasivo" ? "factura" : tipo];
        object body = accion switch
        {
            "cancelar" or "solicitar-cancelacion" => new { Motivo = "Prueba ficticia P6b" },
            "amortizar-nc" => new { NotaCreditoId = referencias["nc07"], NcVersionEsperada = await VersionP6bAsync("nota_credito", referencias["nc07"]), Monto = 5m },
            "formalizar" => new { NotaCreditoId = referencias["nc03"] },
            "aplicar" or "vincular-factura" => new { FacturaOrigenId = referencias["factura"] },
            "aplicar-nc" => new { NotaCreditoId = referencias["nota_credito"], NotaCreditoVersionEsperada = await VersionP6bAsync("nota_credito", referencias["nota_credito"]), Monto = 5m },
            "aplicar-anticipo" => new { AnticipoId = referencias["anticipo"], AnticipoVersionEsperada = await VersionP6bAsync("anticipo", referencias["anticipo"]), Monto = 5m },
            "enviar-revision" => await BodyRevisionP6bAsync(),
            _ => throw new ArgumentException(accion),
        };
        var ruta = tipo == "pasivo" ? $"/api/v1/tesoreria/pasivos-pendientes/{id}" : Ruta(tipo, id);
        using var request = new HttpRequestMessage(HttpMethod.Post, ruta + "/" + accion) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Expected-Version", (await VersionP6bAsync(tipo == "pasivo" ? "factura" : tipo, id)).ToString());
        return await client.SendAsync(request);
    }

    private async Task<object> BodyRevisionP6bAsync()
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var motivo = await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().MotivosRevision
            .Where(x => x.Activo && x.Codigo != "FALTA_REPP").Select(x => x.Id).FirstAsync();
        var dependencias = await scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Domain.Ports.Administracion.IDependenciaRevisoraReadPort>().ListarAsync(default);
        return new { MotivoRevisionId = motivo, DependenciaRevisoraId = dependencias.First(x => x.Activa).Id };
    }

    private async Task<int> VersionP6bAsync(string tipo, Guid id)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        return tipo switch
        {
            "anticipo" => await db.AnticiposProveedor.Where(x => x.Id == id).Select(x => x.Version).SingleAsync(),
            "nota_credito" => await db.NotasCreditoProveedor.Where(x => x.Id == id).Select(x => x.Version).SingleAsync(),
            "nota_cargo" => await db.NotasCargo.Where(x => x.Id == id).Select(x => x.Version).SingleAsync(),
            _ => await db.FacturasProveedor.Where(x => x.Id == id).Select(x => x.Version).SingleAsync(),
        };
    }

    private async Task<string> EstadoP6bAsync(Dictionary<string, Guid> mapa)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
        var tes = scope.ServiceProvider.GetRequiredService<TesoreriaDbContext>();
        var factura = mapa["factura"]; var ids = mapa.Values.ToArray();
        return JsonSerializer.Serialize(new
        {
            Factura = await db.FacturasProveedor.AsNoTracking().Where(x => x.Id == factura).Select(x => new { x.Version, x.Estado, x.SaldoPendiente }).SingleAsync(),
            Anticipos = await db.AnticiposProveedor.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Version, x.Estado, x.MontoAmortizado }).OrderBy(x => x.Id).ToArrayAsync(),
            Notas = await db.NotasCreditoProveedor.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Version, x.Estado, x.MontoAplicado, x.FacturaOrigenId }).OrderBy(x => x.Id).ToArrayAsync(),
            Cargo = await db.NotasCargo.AsNoTracking().Where(x => x.Id == mapa["nota_cargo"]).Select(x => new { x.Version, x.Estado, x.NotaCreditoProveedorId }).SingleAsync(),
            Outbox = (await IdsOutboxAsync(tes.OutboxEntries, [factura])).Length,
        });
    }

    private async Task QuitarPermisoP6bAsync(Datos datos, string codigo)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var id = await db.Permisos.Where(x => x.Codigo == codigo).Select(x => x.Id).SingleAsync();
        await db.RolPermisos.Where(x => x.RolId == datos.RolId && x.PermisoId == id).ExecuteDeleteAsync();
        await scope.ServiceProvider.GetRequiredService<IPermissionCache>().InvalidateAllForUserAsync(datos.UsuarioId);
    }

    private sealed class ProveedorSerieP6b(Guid id) : Millet.CuentasPorPagar.Domain.Ports.DatosMaestros.IProveedorReadPort
    {
        public Task<Millet.CuentasPorPagar.Domain.Ports.DatosMaestros.ProveedorDto?> ObtenerAsync(Guid proveedor, CancellationToken ct) =>
            Task.FromResult<Millet.CuentasPorPagar.Domain.Ports.DatosMaestros.ProveedorDto?>(proveedor == id ? new(id, "FIX010101ABC", "Proveedor ficticio P6b", null, false, true) : null);
        public Task<Millet.CuentasPorPagar.Domain.Ports.DatosMaestros.ProveedorDto?> ObtenerPorRfcAsync(string rfc, CancellationToken ct) => ObtenerAsync(id, ct);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(x => x, _ => "Proveedor ficticio P6b"));
    }
}
