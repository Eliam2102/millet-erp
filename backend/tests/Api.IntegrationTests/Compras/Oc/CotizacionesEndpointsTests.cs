using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Inicio;
using Millet.SharedKernel.Domain;
using Millet.Catalogos.Domain;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// F1-COM-02 (R1): registrar cotizaciones y ver el comparativo normalizado.
public partial class OrdenesCompraEndpointsTests
{
    private static readonly Guid TipoDocCotizacion = Guid.Parse("00000011-0006-0000-0000-000000000004");

    [Fact]
    public async Task Cotizaciones_RegistrarDos_ComparativoNormalizado()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var client = await CreateSuperAdminClientAsync();

        object Cuerpo(string moneda, string unidad, decimal factor, decimal precio) => new
        {
            ProveedorId = ProveedorActivoId, FolioProveedor = "COT-1", Moneda = moneda, FechaCotizacion = hoy,
            VigenteHasta = hoy.AddDays(10), CondicionesCredito = "30 días",
            Lineas = new[] { new { LineaRequisicionId = lineaId, UnidadCotizada = unidad, FactorAUnidadRq = factor, PrecioUnitario = precio, DiasEntrega = 5 } },
        };

        var mxn = await client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/cotizaciones", Cuerpo("MXN", "HOJA", 1m, 500m));
        Assert.Equal(HttpStatusCode.Created, mxn.StatusCode);
        var usd = await client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/cotizaciones", Cuerpo("USD", "M2", 0.4m, 10m));
        Assert.Equal(HttpStatusCode.Created, usd.StatusCode);
        var usdId = (await ReadJsonAsync(usd)).GetProperty("id").GetGuid();

        using var contenido = new MultipartFormDataContent();
        var archivo = new ByteArrayContent("%PDF-1.4\n%cotizacion\n"u8.ToArray());
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        contenido.Add(archivo, "archivo", "cotizacion.pdf");
        contenido.Add(new StringContent(TipoDocCotizacion.ToString()), "tipoDocumentoId");
        var subida = await client.PostAsync($"/api/v1/compras/cotizaciones/{usdId}/adjuntos", contenido);
        Assert.True(subida.StatusCode == HttpStatusCode.Created, await subida.Content.ReadAsStringAsync());

        var get = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}/cotizaciones");
        get.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(get);
        var tc = json.GetProperty("tiposCambio").EnumerateArray().Single().GetProperty("valorEnMxn").GetDecimal();
        var cotizaciones = json.GetProperty("cotizaciones").EnumerateArray().ToList();
        Assert.Equal(2, cotizaciones.Count);
        var cMxn = cotizaciones.Single(c => c.GetProperty("moneda").GetString() == "MXN");
        var cUsd = cotizaciones.Single(c => c.GetProperty("moneda").GetString() == "USD");
        Assert.Equal(500.00m, cMxn.GetProperty("lineas")[0].GetProperty("precioMxnPorUnidadRq").GetDecimal());
        var esperadoUsd = Math.Round(25m * tc, 2, MidpointRounding.ToEven);
        Assert.Equal(esperadoUsd, cUsd.GetProperty("lineas")[0].GetProperty("precioMxnPorUnidadRq").GetDecimal());
        Assert.Equal(esperadoUsd < 500m, cUsd.GetProperty("lineas")[0].GetProperty("esMenorPrecio").GetBoolean());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, cUsd.GetProperty("archivoAdjuntoId").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, cMxn.GetProperty("archivoAdjuntoId").ValueKind);
        Assert.All(cotizaciones, c => Assert.True(c.GetProperty("vigente").GetBoolean()));
    }

    [Fact]
    public async Task Cotizaciones_SinPermiso_403_SinEfectos()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var client = _factory.CreateClientWithIdempotency();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos"));
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/cotizaciones", new
        {
            ProveedorId = ProveedorActivoId, Moneda = "MXN", FechaCotizacion = hoy, VigenteHasta = hoy.AddDays(5),
            Lineas = new[] { new { LineaRequisicionId = lineaId, UnidadCotizada = "HOJA", FactorAUnidadRq = 1m, PrecioUnitario = 500m, DiasEntrega = 5 } },
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        Assert.Equal(0, await db.Cotizaciones.CountAsync(c => c.RequisicionId == rqId));
    }

    [Fact]
    public async Task Cotizaciones_ElegirUsd_GeneraOcConPrecioYPdf()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var client = await CreateSuperAdminClientAsync();
        await RegistrarCotizacionAsync(client, rqId, lineaId, hoy, "MXN", "HOJA", 1m, 500m);
        var usdId = await RegistrarCotizacionAsync(client, rqId, lineaId, hoy, "USD", "M2", 0.4m, 10m);
        await SubirPdfCotizacionAsync(client, usdId);
        const string motivo = "Menor precio normalizado y entrega en 3 días";

        var crear = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion",
            DesdeRqCotizacion(rqId, hoy, "USD", usdId, motivo));
        Assert.True(crear.StatusCode == HttpStatusCode.Created, await crear.Content.ReadAsStringAsync());
        var ocId = (await ReadJsonAsync(crear)).GetProperty("ordenCompraId").GetGuid();

        var oc = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}"));
        Assert.Equal("USD", oc.GetProperty("moneda").GetString());
        Assert.Equal(25.0000m, oc.GetProperty("lineas")[0].GetProperty("precioUnitario").GetDecimal());
        var adjuntos = oc.GetProperty("adjuntos").EnumerateArray().ToList();
        Assert.Single(adjuntos);
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var tipoCotizacion = await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().TiposDocumentoOc
                .Where(t => t.Clave == "cotizacion").Select(t => t.Id).SingleAsync();
            Assert.Equal(tipoCotizacion, adjuntos[0].GetProperty("tipoDocumentoId").GetGuid());
        }
        var elegidaOc = oc.GetProperty("cotizacionElegida");
        Assert.Equal(motivo, elegidaOc.GetProperty("motivoEleccion").GetString());
        Assert.Equal(usdId, elegidaOc.GetProperty("cotizacionId").GetGuid());

        var comparativo = await ReadJsonAsync(await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}/cotizaciones"));
        var cotizaciones = comparativo.GetProperty("cotizaciones").EnumerateArray().ToList();
        Assert.Equal(2, cotizaciones.Count);
        var cUsd = cotizaciones.Single(c => c.GetProperty("moneda").GetString() == "USD");
        var cMxn = cotizaciones.Single(c => c.GetProperty("moneda").GetString() == "MXN");
        Assert.True(cUsd.GetProperty("elegida").GetBoolean());
        Assert.Equal(motivo, cUsd.GetProperty("motivoEleccion").GetString());
        Assert.Equal(ocId, cUsd.GetProperty("ordenCompraId").GetGuid());
        Assert.False(cMxn.GetProperty("elegida").GetBoolean());
    }

    [Fact]
    public async Task Cotizaciones_ElegirVencida_422_SinOc()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var client = await CreateSuperAdminClientAsync();
        var cotId = await RegistrarCotizacionAsync(client, rqId, lineaId, hoy.AddDays(-10), "MXN", "HOJA", 1m, 500m, vigenteHasta: hoy.AddDays(-1));
        await SubirPdfCotizacionAsync(client, cotId);

        var response = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion",
            DesdeRqCotizacion(rqId, hoy, "MXN", cotId, "Motivo suficiente de prueba"));

        await AssertCodigoP2(response, HttpStatusCode.UnprocessableEntity, "COT_VENCIDA");
        await AssertSinEfectosAsync(rqId, cotId);
    }

    [Fact]
    public async Task Cotizaciones_ElegirSinPermiso_403_SinEfectos()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var admin = await CreateSuperAdminClientAsync();
        var cotId = await RegistrarCotizacionAsync(admin, rqId, lineaId, hoy, "MXN", "HOJA", 1m, 500m);
        await SubirPdfCotizacionAsync(admin, cotId);
        var client = await CreateClientConPermisosAsync(
            PermisosCanonicos.ComprasOrdenesCrear, PermisosCanonicos.ComprasRequisicionesGestionarTodasSucursales);
        var sesion = await client.GetAsync("/api/auth/me");
        sesion.EnsureSuccessStatusCode();
        var usuarioId = (await ReadJsonAsync(sesion)).GetProperty("userId").GetGuid();
        try
        {
            var response = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion",
                DesdeRqCotizacion(rqId, hoy, "MXN", cotId, "Motivo suficiente de prueba"));
            await AssertCodigoP2(response, HttpStatusCode.Forbidden, "COT_SIN_PERMISO");
            await AssertSinEfectosAsync(rqId, cotId);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            var rolId = await db.UsuarioEmpresaRoles.Where(a => a.UsuarioId == usuarioId).Select(a => a.RolId).SingleAsync();
            await db.Usuarios.Where(u => u.Id == usuarioId).ExecuteDeleteAsync();
            await db.Roles.Where(r => r.Id == rolId).ExecuteDeleteAsync();
        }
    }

    private static async Task<Guid> RegistrarCotizacionAsync(
        HttpClient client, Guid rqId, Guid lineaId, DateOnly fecha, string moneda, string unidad, decimal factor, decimal precio,
        DateOnly? vigenteHasta = null)
    {
        var r = await client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/cotizaciones", new
        {
            ProveedorId = ProveedorActivoId, FolioProveedor = "COT-1", Moneda = moneda, FechaCotizacion = fecha,
            VigenteHasta = vigenteHasta ?? fecha.AddDays(10), CondicionesCredito = "30 días",
            Lineas = new[] { new { LineaRequisicionId = lineaId, UnidadCotizada = unidad, FactorAUnidadRq = factor, PrecioUnitario = precio, DiasEntrega = 3 } },
        });
        Assert.True(r.StatusCode == HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(r)).GetProperty("id").GetGuid();
    }

    private static async Task SubirPdfCotizacionAsync(HttpClient client, Guid cotizacionId)
    {
        using var contenido = new MultipartFormDataContent();
        var archivo = new ByteArrayContent("%PDF-1.4\n%cotizacion\n"u8.ToArray());
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        contenido.Add(archivo, "archivo", "cotizacion.pdf");
        contenido.Add(new StringContent(TipoDocCotizacion.ToString()), "tipoDocumentoId");
        var subida = await client.PostAsync($"/api/v1/compras/cotizaciones/{cotizacionId}/adjuntos", contenido);
        Assert.True(subida.StatusCode == HttpStatusCode.Created, await subida.Content.ReadAsStringAsync());
    }

    private static object DesdeRqCotizacion(Guid rqId, DateOnly hoy, string moneda, Guid cotizacionId, string motivo) => new
    {
        RequisicionId = rqId, SucursalCodigo = "MID", FolioAnio = (short)2026, ProveedorId = ProveedorActivoId,
        CondicionesPagoId = Guid.NewGuid(), UsoPrincipalId = Guid.NewGuid(), UsoCfdiClave = "G03", MetodoPago = "PPD", FormaPagoClave = "99", FechaDocumento = hoy, Moneda = moneda,
        TipoCambio = moneda == "MXN" ? (decimal?)null : 17.85m, CotizacionId = cotizacionId, MotivoEleccion = motivo,
    };

    /// <summary>La RQ sigue sin comprometer, sin OC con líneas de ella y la cotización sin elegir.</summary>
    private async Task AssertSinEfectosAsync(Guid rqId, Guid cotizacionId)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        Assert.Null((await db.Requisiciones.AsNoTracking().SingleAsync(r => r.Id == rqId)).ComprometidaEnOcId);
        Assert.False(await db.OrdenesCompra.AnyAsync(o => o.Lineas.Any(l => l.RequisicionId == rqId)));
        Assert.Null((await db.Cotizaciones.AsNoTracking().SingleAsync(c => c.Id == cotizacionId)).OrdenCompraId);
    }

    [Fact]
    public async Task Cotizaciones_OtraSucursal_403()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var admin = await CreateSuperAdminClientAsync();
        var cotId = await RegistrarCotizacionAsync(admin, rqId, lineaId, hoy, "MXN", "HOJA", 1m, 500m);
        // Usuario operativo: sin bypass de sucursal y sin UsuarioSucursal para la sucursal de la RQ.
        var client = await CreateClientConPermisosAsync(PermisosCanonicos.ComprasCotizacionesLeer);
        var usuarioId = await UsuarioIdDeAsync(client);
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            // El helper asocia al usuario con la sucursal de la RQ; aquí debe quedar sin esa asociación.
            await scope.ServiceProvider.GetRequiredService<IdentidadDbContext>().UsuarioSucursales
                .Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync();
        }
        try
        {
            var comparativo = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}/cotizaciones");
            Assert.Equal(HttpStatusCode.Forbidden, comparativo.StatusCode);
            Assert.Equal("SUCURSAL_NO_ASOCIADA", (await ReadJsonAsync(comparativo)).GetProperty("code").GetString());
            var adjuntos = await client.GetAsync($"/api/v1/compras/cotizaciones/{cotId}/adjuntos");
            Assert.Equal(HttpStatusCode.Forbidden, adjuntos.StatusCode);
        }
        finally
        {
            await LimpiarUsuarioAsync(usuarioId);
        }
    }

    [Fact]
    public async Task Cotizaciones_TrasElegir_NoSeEditanNiBorran()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var client = await CreateSuperAdminClientAsync();
        var otraId = await RegistrarCotizacionAsync(client, rqId, lineaId, hoy, "MXN", "HOJA", 1m, 500m);
        var elegidaId = await RegistrarCotizacionAsync(client, rqId, lineaId, hoy, "USD", "M2", 0.4m, 10m);
        await SubirPdfCotizacionAsync(client, elegidaId);
        var crear = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion",
            DesdeRqCotizacion(rqId, hoy, "USD", elegidaId, "Menor precio normalizado"));
        Assert.Equal(HttpStatusCode.Created, crear.StatusCode);

        var put = await client.PutAsJsonAsync($"/api/v1/compras/cotizaciones/{otraId}", new
        {
            ProveedorId = ProveedorActivoId, Moneda = "MXN", FechaCotizacion = hoy, VigenteHasta = hoy.AddDays(5),
            Lineas = new[] { new { LineaRequisicionId = lineaId, UnidadCotizada = "HOJA", FactorAUnidadRq = 1m, PrecioUnitario = 400m, DiasEntrega = 2 } },
        });
        await AssertCodigoP2(put, HttpStatusCode.UnprocessableEntity, "COT_COMPARATIVO_CERRADO");
        await AssertCodigoP2(await client.DeleteAsync($"/api/v1/compras/cotizaciones/{otraId}"),
            HttpStatusCode.UnprocessableEntity, "COT_COMPARATIVO_CERRADO");

        var lista = await ReadJsonAsync(await client.GetAsync($"/api/v1/compras/cotizaciones/{elegidaId}/adjuntos"));
        var adjuntoId = lista.EnumerateArray().Single().GetProperty("id").GetGuid();
        var baja = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/compras/cotizaciones/{elegidaId}/adjuntos/{adjuntoId}")
        {
            Content = JsonContent.Create(new { Motivo = "Se subió el archivo equivocado" }),
        };
        await AssertCodigoP2(await client.SendAsync(baja), HttpStatusCode.UnprocessableEntity, "COT_COMPARATIVO_CERRADO");
    }

    [Fact]
    public async Task Cotizaciones_ElegirSinPdf_422()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var client = await CreateSuperAdminClientAsync();
        var cotId = await RegistrarCotizacionAsync(client, rqId, lineaId, hoy, "MXN", "HOJA", 1m, 500m);

        var response = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion",
            DesdeRqCotizacion(rqId, hoy, "MXN", cotId, "Motivo suficiente de prueba"));

        await AssertCodigoP2(response, HttpStatusCode.UnprocessableEntity, "COT_SIN_ARCHIVO");
        await AssertSinEfectosAsync(rqId, cotId);
    }

    [Fact]
    public async Task Cotizaciones_PartidaAjena_422()
    {
        var (rqId, _) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/cotizaciones", new
        {
            ProveedorId = ProveedorActivoId, Moneda = "MXN", FechaCotizacion = hoy, VigenteHasta = hoy.AddDays(5),
            Lineas = new[] { new { LineaRequisicionId = Guid.NewGuid(), UnidadCotizada = "HOJA", FactorAUnidadRq = 1m, PrecioUnitario = 500m, DiasEntrega = 2 } },
        });

        await AssertCodigoP2(response, HttpStatusCode.UnprocessableEntity, "COT_PARTIDA_AJENA");
    }

    [Fact]
    public async Task Cotizaciones_OcElegida_TransmiteSinAdjuntoManual()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var client = await CreateSuperAdminClientAsync();
        var cotId = await RegistrarCotizacionAsync(client, rqId, lineaId, hoy, "MXN", "HOJA", 1m, 500m);
        await SubirPdfCotizacionAsync(client, cotId);
        var crear = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion",
            DesdeRqCotizacion(rqId, hoy, "MXN", cotId, "Único proveedor vigente"));
        Assert.Equal(HttpStatusCode.Created, crear.StatusCode);
        var ocId = (await ReadJsonAsync(crear)).GetProperty("ordenCompraId").GetGuid();

        var transmitir = await client.PostAsync($"{EndpointBase}/{ocId}/transmitir", null);

        var cuerpo = await transmitir.Content.ReadAsStringAsync();
        Assert.DoesNotContain("OC_COTIZACION_REQUERIDA", cuerpo);
        Assert.True(transmitir.StatusCode == HttpStatusCode.NoContent, cuerpo);
    }

    private static async Task<Guid> ElegirYCrearOcAsync(HttpClient client, Guid rqId, DateOnly hoy, Guid cotId, string motivo)
    {
        await SubirPdfCotizacionAsync(client, cotId);
        var crear = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion", DesdeRqCotizacion(rqId, hoy, "MXN", cotId, motivo));
        Assert.True(crear.StatusCode == HttpStatusCode.Created, await crear.Content.ReadAsStringAsync());
        return (await ReadJsonAsync(crear)).GetProperty("ordenCompraId").GetGuid();
    }

    private static async Task AssertComparativoReabiertoAsync(HttpClient client, Guid rqId, int elegidasEsperadas)
    {
        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}/cotizaciones"));
        Assert.Equal(elegidasEsperadas, json.GetProperty("cotizaciones").EnumerateArray().Count(c => c.GetProperty("elegida").GetBoolean()));
        Assert.Equal(elegidasEsperadas > 0, json.GetProperty("comparativoCerrado").GetBoolean());
        Assert.Equal(elegidasEsperadas == 0, json.GetProperty("puedeRegistrar").GetBoolean());
    }

    [Fact]
    public async Task Cotizaciones_ElegirCancelarOc_LiberaYPermiteReelegir()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var client = await CreateSuperAdminClientAsync();
        var aId = await RegistrarCotizacionAsync(client, rqId, lineaId, hoy, "MXN", "HOJA", 1m, 500m);
        var bId = await RegistrarCotizacionAsync(client, rqId, lineaId, hoy, "MXN", "HOJA", 1m, 450m);
        var ocId = await ElegirYCrearOcAsync(client, rqId, hoy, aId, "Menor tiempo de entrega");
        await AssertComparativoReabiertoAsync(client, rqId, elegidasEsperadas: 1);

        Guid motivoId;
        using (var scope = _factory.Services.CreateScope())
        {
            motivoId = await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().MotivosRechazo
                .Where(m => m.Activo && (m.AplicaA & MotivoRechazoAplicaA.Cancelacion) != 0 && !m.PermiteTextoLibre)
                .Select(m => m.Id).FirstAsync();
        }
        var cancelar = await client.PostAsJsonAsync($"{EndpointBase}/{ocId}/cancelar", new { MotivoCancelacionId = motivoId });
        Assert.True(cancelar.StatusCode == HttpStatusCode.NoContent, await cancelar.Content.ReadAsStringAsync());

        await AssertComparativoReabiertoAsync(client, rqId, elegidasEsperadas: 0);
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var a = await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().Cotizaciones.AsNoTracking()
                .Include(c => c.EleccionesAnteriores).SingleAsync(c => c.Id == aId);
            var previa = Assert.Single(a.EleccionesAnteriores);
            Assert.Equal(ocId, previa.OrdenCompraId);
            Assert.Equal("Menor tiempo de entrega", previa.Motivo);
            Assert.StartsWith("OC cancelada", previa.MotivoLiberacion);
        }

        // Reelegible: la segunda cotización genera una OC nueva y solo queda una elegida.
        await ElegirYCrearOcAsync(client, rqId, hoy, bId, "Mejor precio tras la cancelación");
        await AssertComparativoReabiertoAsync(client, rqId, elegidasEsperadas: 1);
    }

    [Fact]
    public async Task Cotizaciones_EliminarUltimaLineaDeOc_LiberaLaEleccion()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var client = await CreateSuperAdminClientAsync();
        var cotId = await RegistrarCotizacionAsync(client, rqId, lineaId, hoy, "MXN", "HOJA", 1m, 500m);
        var ocId = await ElegirYCrearOcAsync(client, rqId, hoy, cotId, "Único proveedor vigente");
        var oc = await ReadJsonAsync(await client.GetAsync($"{EndpointBase}/{ocId}"));
        var ocLineaId = oc.GetProperty("lineas")[0].GetProperty("id").GetGuid();

        var borrar = await client.DeleteAsync($"{EndpointBase}/{ocId}/lineas/{ocLineaId}");
        Assert.Equal(HttpStatusCode.NoContent, borrar.StatusCode);

        await AssertComparativoReabiertoAsync(client, rqId, elegidasEsperadas: 0);
    }

    [Fact]
    public async Task Cotizaciones_RqConCotizaciones_SinCotizacionId_422_ElegirDesdeComparativo()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var client = await CreateSuperAdminClientAsync();
        await RegistrarCotizacionAsync(client, rqId, lineaId, hoy, "MXN", "HOJA", 1m, 500m);

        var directa = await client.PostAsJsonAsync($"{EndpointBase}/desde-requisicion", new
        {
            RequisicionId = rqId, SucursalCodigo = "MID", FolioAnio = (short)2026, ProveedorId = ProveedorActivoId,
            CondicionesPagoId = Guid.NewGuid(), UsoPrincipalId = Guid.NewGuid(), UsoCfdiClave = "G03", MetodoPago = "PPD", FormaPagoClave = "99", FechaDocumento = hoy, Moneda = "MXN",
        });
        await AssertCodigoP2(directa, HttpStatusCode.UnprocessableEntity, "COT_ELEGIR_DESDE_COMPARATIVO");

        // OC vacía + agregar línea desde la RQ: mismo bloqueo.
        var vacia = await client.PostAsJsonAsync(EndpointBase, ValidBody());
        vacia.EnsureSuccessStatusCode();
        var vaciaId = (await ReadJsonAsync(vacia)).GetProperty("id").GetGuid();
        var agregar = await client.PostAsJsonAsync($"{EndpointBase}/{vaciaId}/lineas/desde-requisicion", new { RequisicionId = rqId });
        await AssertCodigoP2(agregar, HttpStatusCode.UnprocessableEntity, "COT_ELEGIR_DESDE_COMPARATIVO");

        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        Assert.Null((await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().Requisiciones.AsNoTracking()
            .SingleAsync(r => r.Id == rqId)).ComprometidaEnOcId);
    }

    [Fact]
    public async Task Cotizaciones_OcConCotizacion_NoCambiaProveedorNiMoneda()
    {
        var (rqId, lineaId) = await SembrarRqCotizable();
        var hoy = await RegistrarTipoCambioHoyAsync("USD", 17.85m);
        var client = await CreateSuperAdminClientAsync();
        var cotId = await RegistrarCotizacionAsync(client, rqId, lineaId, hoy, "MXN", "HOJA", 1m, 500m);
        var ocId = await ElegirYCrearOcAsync(client, rqId, hoy, cotId, "Único proveedor vigente");

        async Task<HttpResponseMessage> Patch(object body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"{EndpointBase}/{ocId}") { Content = JsonContent.Create(body) };
            return await client.SendAsync(request);
        }

        await AssertCodigoP2(await Patch(new { ProveedorId = Guid.NewGuid() }), HttpStatusCode.UnprocessableEntity, "OC_COTIZACION_FIJA");
        await AssertCodigoP2(await Patch(new { Moneda = "USD", TipoCambio = 17.85m }), HttpStatusCode.UnprocessableEntity, "OC_COTIZACION_FIJA");
        // Otros campos de cabecera siguen editables.
        Assert.Equal(HttpStatusCode.NoContent, (await Patch(new { Observaciones = "Entregar en andén 2" })).StatusCode);
    }

    private static async Task<Guid> UsuarioIdDeAsync(HttpClient client)
    {
        var sesion = await client.GetAsync("/api/auth/me");
        sesion.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(sesion)).GetProperty("userId").GetGuid();
    }

    private async Task LimpiarUsuarioAsync(Guid usuarioId)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var rolId = await db.UsuarioEmpresaRoles.Where(a => a.UsuarioId == usuarioId).Select(a => a.RolId).SingleAsync();
        await db.Usuarios.Where(u => u.Id == usuarioId).ExecuteDeleteAsync();
        await db.Roles.Where(r => r.Id == rolId).ExecuteDeleteAsync();
    }

    /// <summary>RQ EnSurtido con una línea por comprar (4 HOJA) y sin OC comprometida.</summary>
    private async Task<(Guid RqId, Guid LineaId)> SembrarRqCotizable()
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var numero = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 999999);
        var rq = new Requisicion(Guid.NewGuid(), EmpresaInicialId, Millet.Compras.Domain.Folio.Parse($"MID2026-{numero:D6}"), 2026,
            Clasificacion.MateriaPrima, SucursalIdFija, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Prioridad.Normal, DateTimeOffset.UtcNow, null);
        var l = rq.AgregarLinea(Guid.NewGuid(), Guid.Parse("00000007-0001-0000-0000-000000000001"), 4, "HOJA", Money.Mxn(100), centroCostoId: CentroCostoSeedId);
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        rq.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow, RequiereNivel.SoloN1);
        rq.RegistrarCubrimiento([new CubrimientoLinea(l.Id, 0, 4)], DateTimeOffset.UtcNow);
        db.Requisiciones.Add(rq);
        await db.SaveChangesAsync();
        return (rq.Id, l.Id);
    }

    private async Task<DateOnly> RegistrarTipoCambioHoyAsync(string codigoMoneda, decimal valor)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var hoy = FechasInicio.Hoy(scope.ServiceProvider.GetRequiredService<IClock>());
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var moneda = await db.Monedas.AsNoTracking().SingleAsync(m => m.Codigo == codigoMoneda);
        if (!await db.TiposCambio.AnyAsync(t => t.MonedaId == moneda.Id && t.Fecha == hoy))
        {
            db.TiposCambio.Add(new TipoCambio(Guid.NewGuid(), moneda.Id, hoy, valor));
            await db.SaveChangesAsync();
        }
        return hoy;
    }
}
