using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Contabilidad.Application.SaldosIniciales;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// M-166 (F1-CON-13) contra Postgres real, como lo llamará el lote común de cargas (MediatR): la balanza de apertura se valida por
/// renglón y por archivo; el lote autorizado genera una sola póliza de apertura autorizada, con un movimiento por auxiliar en la
/// cuenta de clientes; el mismo archivo regresa la misma póliza y otro archivo se rechaza.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class AperturaSaldosInicialesIntegrationTests(WebApplicationFactory<Program> factory)
{
    private static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    [Fact]
    public async Task M166_valida_aplica_una_sola_apertura_y_responde_el_mismo_archivo()
    {
        var hoy = FechaContable.HoyLocal(DateTimeOffset.UtcNow);
        var corte = new DateOnly(hoy.Year, hoy.Month, DateTime.DaysInMonth(hoy.Year, hoy.Month));
        var suf = Sufijo();
        Guid? tipo = null;
        var clientes = new List<Guid>();
        try
        {
            var admin = await LoginAsync(factory);
            await AsegurarPeriodosAbiertosAsync(admin, hoy);
            var sucursal = $"AP{suf}";
            (await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new { Id = Guid.Empty, Clave = sucursal, Nombre = $"FIX Sucursal {suf}" }))
                .EnsureSuccessStatusCode();
            var activo = await CrearCuenta(admin, Codigo(suf, "1"), "FIX activo", tipo: "Titulo");
            await CrearCuenta(admin, Codigo(suf, "1.1"), "FIX bancos", Id(activo));
            await CrearCuenta(admin, Codigo(suf, "1.2"), "FIX clientes nacionales", Id(activo), control: "Clientes");
            var capital = await CrearCuenta(admin, Codigo(suf, "2"), "FIX capital", naturaleza: "Acreedora", tipo: "Titulo");
            await CrearCuenta(admin, Codigo(suf, "2.1"), "FIX capital social", Id(capital), naturaleza: "Acreedora");
            string bancos = Codigo(suf, "1.1"), cxc = Codigo(suf, "1.2"), social = Codigo(suf, "2.1");
            for (var i = 1; i <= 3; i++) clientes.Add(await SembrarClienteAsync($"AP{suf}-{i}"));
            var t = await admin.PostAsJsonAsync($"{Base}/tipos-documento", new { clave = $"FIX-{suf}", nombre = $"FIX Apertura {suf}", esPrueba = true, tipoSat = "Diario" });
            Assert.Equal(HttpStatusCode.Created, t.StatusCode);
            tipo = Id(await Json(t));

            await using var enEmpresa = factory.ConEmpresa(EmpresaBootstrapId);

            // ── CA1, CA2 y CA3: no cuadra, cuenta inexistente en su fila y detalle de clientes que no iguala el renglón ──
            FilaBalanzaApertura[] mala =
            [
                new(2, bancos, 1_000_300m, 0m),
                new(3, cxc, 850_000m, 0m),
                new(4, cxc, 500_000m, 0m, "Cliente", $"AP{suf}-1"),
                new(5, cxc, 300_000m, 0m, "Cliente", $"AP{suf}-2"),
                new(6, cxc, 49_000m, 0m, "Cliente", $"AP{suf}-3"),
                new(7, social, 0m, 1_850_000m),
                new(17, Codigo(suf, "9.9"), 0m, 0m),
            ];
            var v = await Enviar(enEmpresa, new ValidarBalanzaAperturaQuery(corte, mala, sucursal, tipo));
            Assert.False(v.PuedeAplicar);
            Assert.Equal(300m, v.Diferencia);
            Assert.Contains(v.Errores, e => e.Fila is null && e.Codigo == "AP_NO_CUADRA" && e.Mensaje.StartsWith("No cuadra por 300.00", StringComparison.Ordinal));
            Assert.Contains(v.Errores, e => e.Fila == 17 && e.Codigo == "AP_CUENTA_NO_EXISTE");
            Assert.Contains(v.Errores, e => e.Fila == 3 && e.Mensaje == "El detalle de clientes no iguala el renglón (diferencia 1,000.00).");

            // ── CA4: lote cuadrado y autorizado por otra persona → póliza de apertura autorizada, un movimiento por cliente ──
            FilaBalanzaApertura[] buena =
            [
                new(2, bancos, 1_000_000m, 0m),
                new(3, cxc, 850_000m, 0m),
                new(4, cxc, 500_000m, 0m, "Cliente", $"AP{suf}-1"),
                new(5, cxc, 300_000m, 0m, "Cliente", $"AP{suf}-2"),
                new(6, cxc, 50_000m, 0m, "Cliente", $"AP{suf}-3"),
                new(7, social, 0m, 1_850_000m),
            ];
            var ok = await Enviar(enEmpresa, new ValidarBalanzaAperturaQuery(corte, buena, sucursal, tipo));
            Assert.True(ok.PuedeAplicar, string.Join("; ", ok.Errores.Select(e => $"{e.Fila} {e.Codigo} {e.Mensaje}")));
            Assert.Equal((3, 3), (ok.Cuentas, ok.Auxiliares)); // bancos, clientes y capital; tres clientes

            var lote = Guid.NewGuid();
            var huella = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
            AplicarSaldosInicialesCommand Aplicar(string h, Guid l) => new(l, h, corte, Guid.NewGuid(), "FIX quien sube", Guid.NewGuid(), "FIX quien autoriza",
                buena, sucursal, tipo, $"FIX apertura {suf}");
            var aplicada = await Enviar(enEmpresa, Aplicar(huella, lote));
            Assert.False(aplicada.YaAplicada);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                var poliza = await db.Polizas.IgnoreQueryFilters().Include(p => p.Partidas).SingleAsync(p => p.Id == aplicada.PolizaId);
                Assert.Equal((OrigenPoliza.Apertura, EstadoPoliza.Autorizada, corte), (poliza.Origen, poliza.Estado, poliza.Fecha));
                Assert.Equal("FIX quien autoriza", poliza.AutorizadaPor);
                Assert.Equal((corte.Year, corte.Month), (poliza.PeriodoAnio, poliza.PeriodoNumero));
                Assert.Equal(1_850_000m, poliza.TotalCargos);
                Assert.Equal(clientes.Order(), poliza.Partidas.Where(p => p.ClienteId != null).Select(p => p.ClienteId!.Value).Order());
                Assert.Equal(lote, poliza.OrigenDocumentoId);
            }

            // ── CA5: el mismo archivo responde la apertura ya aplicada; otro archivo se rechaza ──
            var otraVez = await Enviar(enEmpresa, Aplicar(huella.ToLowerInvariant(), Guid.NewGuid()));
            Assert.Equal((aplicada.PolizaId, true), (otraVez.PolizaId, otraVez.YaAplicada));
            var distinta = await Assert.ThrowsAsync<ConflictException>(() => Enviar(enEmpresa, Aplicar(new string('f', 64), Guid.NewGuid())));
            Assert.Equal("AP_YA_HAY_APERTURA", distinta.Code);
            Assert.NotNull((await Enviar(enEmpresa, new ValidarBalanzaAperturaQuery(corte, buena, sucursal, tipo))).AperturaExistente);
            Assert.Equal(1, await AperturasAsync());
        }
        finally
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                if (tipo is { } tipoId)
                {
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.aperturas WHERE poliza_id IN (SELECT id FROM contabilidad.polizas WHERE tipo_operacion_id = {0})", tipoId);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE consumidor = 'POLIZA' AND referencia IN (SELECT id::text FROM contabilidad.polizas WHERE tipo_operacion_id = {0})", tipoId);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE tipo_operacion_id = {0}", tipoId);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.consecutivos_poliza WHERE tipo_operacion_id = {0}", tipoId);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.tipos_documento_contable WHERE id = {0}", tipoId);
                }
                var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
                foreach (var c in clientes) await compartido.Database.ExecuteSqlRawAsync("DELETE FROM compartido.clientes WHERE id = {0}", c);
            }
            await Limpiar(factory.Services, suf);
        }
    }

    private static async Task<T> Enviar<T>(WebApplicationFactory<Program> enEmpresa, IRequest<T> request)
    {
        using var scope = enEmpresa.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    private async Task<int> AperturasAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>().Aperturas.IgnoreQueryFilters().CountAsync();
    }

    private async Task<Guid> SembrarClienteAsync(string clave)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var c = new Cliente(Guid.CreateVersion7(), clave, $"FIX cliente {clave}", rfc: ("AP" + Guid.NewGuid().ToString("N")[..10]).ToUpperInvariant());
        db.Clientes.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }
}
