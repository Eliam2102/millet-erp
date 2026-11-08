using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Timbrado.ReintentarTimbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Facturacion.UnitTests.NotasCredito;

/// <summary>
/// Regla D10 (Manual de Sucursales / Contraste U-1 · 86e3yxn2p):
/// Valida que <see cref="NotaCreditoPendienteRetryWorker"/> reintente automáticamente
/// cada 15 minutos las notas de crédito ligadas a una factura hasta un tope de 3 intentos.
/// </summary>
public sealed class NotaCreditoPendienteRetryWorkerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeSenderTracking : ISender
    {
        public List<object> Enviados { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            Enviados.Add(request);
            if (request is ReintentarTimbradoCommand c)
            {
                return Task.FromResult((TResponse)(object)new ReintentarTimbradoResponse(
                    c.ComprobanteId, "NotaCredito", "Timbrado", "99999999-9999-9999-9999-999999999999", "NC-000001", null, null, 1));
            }
            throw new NotImplementedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest
        {
            Enviados.Add(request);
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken ct = default)
        {
            if (request is ReintentarTimbradoCommand c)
            {
                Enviados.Add(c);
                return Task.FromResult<object?>(new ReintentarTimbradoResponse(
                    c.ComprobanteId, "NotaCredito", "Timbrado", "99999999-9999-9999-9999-999999999999", "NC-000001", null, null, 1));
            }
            throw new NotImplementedException();
        }
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class SimpleOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable OnChange(Action<T, string?> listener) => null!;
    }

    private static (NotaCreditoPendienteRetryWorker Worker, ServiceProvider Sp, FakeSenderTracking Sender) Armar(
        string dbName, Guid empresaId, DateTimeOffset ahora, NotaCreditoPendienteRetryOptions? opciones = null)
    {
        var services = new ServiceCollection();
        var sender = new FakeSenderTracking();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(empresaId));
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IClock>(new FakeClock(ahora));
        services.AddSingleton<ISender>(sender);
        var sp = services.BuildServiceProvider();

        var monitor = new SimpleOptionsMonitor<NotaCreditoPendienteRetryOptions>(opciones ?? new NotaCreditoPendienteRetryOptions
        {
            Disabled = false,
            IntervalMinutes = 15,
            MaxAttempts = 3,
            BatchSize = 20
        });

        var worker = new NotaCreditoPendienteRetryWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            monitor,
            NullLogger<NotaCreditoPendienteRetryWorker>.Instance);

        return (worker, sp, sender);
    }

    private static NotaCredito CrearNotaFallida(Guid empresaId, Guid facturaId, string errorCodigo = "PAC-01", DateTimeOffset? updatedAt = null)
    {
        var nc = NotaCredito.CrearRanura(
            empresaId: empresaId,
            folio: $"NC-{Guid.NewGuid():N}"[..10],
            folioNumero: 1,
            sucursalId: Guid.NewGuid(),
            cajaId: null,
            usuarioEmisorId: null,
            receptor: new DatosFiscalesReceptor("AAA010101AAA", "Cliente FIX", "601", "97000", "G03", "MEX", false),
            emisor: new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120"),
            formaPago: "01",
            moneda: "MXN",
            tipoCambio: null,
            periodoAnio: 2026,
            periodoMes: 11,
            canalVentaId: (short)1,
            facturaRelacionadaId: facturaId,
            montoTotal: 580m,
            tasaIva: 0.16m,
            descripcion: "Descuento ranura");

        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbradoFallido(errorCodigo, "Fallo de conexión con PAC");
        if (updatedAt is { } dt)
        {
            typeof(Millet.SharedKernel.Domain.BaseEntity).GetProperty(nameof(Millet.SharedKernel.Domain.BaseEntity.UpdatedAt))?.SetValue(nc, dt);
        }
        return nc;
    }

    [Fact]
    public async Task Reintenta_nota_en_timbrado_fallido_ligada_a_factura_despues_de_15_minutos()
    {
        var empresaId = Guid.NewGuid();
        var facturaId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var hace20Min = Ahora.AddMinutes(-20);

        var (worker, sp, sender) = Armar(dbName, empresaId, Ahora);
        await using var _ = sp;

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var nc = CrearNotaFallida(empresaId, facturaId, "PAC-500");
            db.NotasCredito.Add(nc);
            db.Entry(nc).Property(x => x.UpdatedAt).CurrentValue = hace20Min;

            // 1 intento previo registrado al fallar la emisión inicial
            db.BitacorasIntentoTimbrado.Add(BitacoraIntentoTimbrado.Registrar(
                empresaId, nc.Id, 1, ResultadoIntentoTimbrado.Fallido, "PAC-500", "Error PAC", hace20Min));
            await db.SaveChangesAsync();
        }

        var reintentadas = await worker.TickAsync(CancellationToken.None);

        reintentadas.Should().Be(1);
        sender.Enviados.Should().ContainSingle()
            .Which.Should().BeOfType<ReintentarTimbradoCommand>();
    }

    [Fact]
    public async Task Detiene_reintentos_automaticos_al_alcanzar_el_maximo_de_3_intentos()
    {
        var empresaId = Guid.NewGuid();
        var facturaId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var hace20Min = Ahora.AddMinutes(-20);

        var (worker, sp, sender) = Armar(dbName, empresaId, Ahora);
        await using var _ = sp;

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var nc = CrearNotaFallida(empresaId, facturaId, "PAC-500", hace20Min);
            db.NotasCredito.Add(nc);

            // Ya tiene 3 intentos registrados (1 inicial + 2 reintentos previos)
            for (var i = 1; i <= 3; i++)
            {
                db.BitacorasIntentoTimbrado.Add(BitacoraIntentoTimbrado.Registrar(
                    empresaId, nc.Id, i, ResultadoIntentoTimbrado.Fallido, "PAC-500", "Error PAC", hace20Min));
            }
            await db.SaveChangesAsync();
        }

        var reintentadas = await worker.TickAsync(CancellationToken.None);

        reintentadas.Should().Be(0, "alcanzó el límite de 3 intentos de la regla D10");
        sender.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task No_reintenta_si_no_ha_transcurrido_el_intervalo_de_15_minutos()
    {
        var empresaId = Guid.NewGuid();
        var facturaId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var hace5Min = Ahora.AddMinutes(-5); // Solo han pasado 5 minutos

        var (worker, sp, sender) = Armar(dbName, empresaId, Ahora);
        await using var _ = sp;

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var nc = CrearNotaFallida(empresaId, facturaId, "PAC-500", hace5Min);
            db.NotasCredito.Add(nc);

            db.BitacorasIntentoTimbrado.Add(BitacoraIntentoTimbrado.Registrar(
                empresaId, nc.Id, 1, ResultadoIntentoTimbrado.Fallido, "PAC-500", "Error PAC", hace5Min));
            await db.SaveChangesAsync();
        }

        var reintentadas = await worker.TickAsync(CancellationToken.None);

        reintentadas.Should().Be(0, "aún no pasan 15 minutos desde el último intento");
        sender.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task No_reintenta_si_la_nota_no_esta_ligada_a_factura()
    {
        var empresaId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var hace20Min = Ahora.AddMinutes(-20);

        var (worker, sp, sender) = Armar(dbName, empresaId, Ahora);
        await using var _ = sp;

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            // Nota de devolución de anticipo (FacturaRelacionadaId = null)
            var nc = NotaCredito.CrearDevolucionAnticipo(
                empresaId: empresaId, folio: "NC-ANTICIPO", folioNumero: 1, sucursalId: Guid.NewGuid(),
                usuarioEmisorId: null,
                receptor: new DatosFiscalesReceptor("AAA010101AAA", "Cliente FIX", "601", "97000", "G03", "MEX", false),
                emisor: new DatosFiscalesEmisor("MIL010101AAA", "Millet", "601", "76120"),
                formaPago: "01", moneda: "MXN", tipoCambio: null, periodoAnio: 2026, periodoMes: 11,
                canalVentaId: (short)1, anticipoId: Guid.NewGuid(), montoTotal: 580m, tasaIva: 0.16m,
                banco: "BBVA", clabe: "032180000118359719", titular: "Cliente FIX");
            nc.MarcarTimbradoEnProceso();
            nc.MarcarTimbradoFallido("PAC-500", "Error");
            typeof(Millet.SharedKernel.Domain.BaseEntity).GetProperty(nameof(Millet.SharedKernel.Domain.BaseEntity.UpdatedAt))?.SetValue(nc, hace20Min);
            db.NotasCredito.Add(nc);
            await db.SaveChangesAsync();
        }

        var reintentadas = await worker.TickAsync(CancellationToken.None);

        reintentadas.Should().Be(0, "la regla D10 solo aplica a NC ligadas a factura timbrada");
        sender.Enviados.Should().BeEmpty();
    }

    [Fact]
    public async Task No_reintenta_si_el_error_es_ambiguo_requiere_confirmacion_manual()
    {
        var empresaId = Guid.NewGuid();
        var facturaId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var hace20Min = Ahora.AddMinutes(-20);

        var (worker, sp, sender) = Armar(dbName, empresaId, Ahora);
        await using var _ = sp;

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var nc = CrearNotaFallida(empresaId, facturaId, "PAC_TIMEOUT", hace20Min);
            db.NotasCredito.Add(nc);

            db.BitacorasIntentoTimbrado.Add(BitacoraIntentoTimbrado.Registrar(
                empresaId, nc.Id, 1, ResultadoIntentoTimbrado.Fallido, "PAC_TIMEOUT", "Timeout", hace20Min));
            await db.SaveChangesAsync();
        }

        var reintentadas = await worker.TickAsync(CancellationToken.None);

        reintentadas.Should().Be(0, "PAC_TIMEOUT es ambiguo y exige validación manual");
        sender.Enviados.Should().BeEmpty();
    }
}
