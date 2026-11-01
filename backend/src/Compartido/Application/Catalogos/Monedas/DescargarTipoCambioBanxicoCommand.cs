using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Catalogos.Application.Monedas;

/// <summary>
/// K10.6: descarga el FIX de Banxico (USD) y los valores de EUR y CAD, y guarda en
/// <c>compartido.tipos_cambio</c> el valor de cada fecha de aplicación según
/// <see cref="EscaleraTipoCambio"/>, incluidos los inhábiles. Llega hasta el último día que
/// ya se puede calcular (mañana después de las 12:00), así una factura temprano nunca se
/// queda sin TC. Repetirlo es seguro: solo agrega lo que falta o corrige sus propios valores.
/// </summary>
public sealed record DescargarTipoCambioBanxicoCommand : IRequest<DescargaTipoCambioResultado>;

public sealed record DescargaTipoCambioResultado(
    DateOnly CalendarioConocidoHasta, IReadOnlyList<DescargaTipoCambioMoneda> Monedas);

/// <summary>
/// <c>Conflictos</c>: fechas con un valor capturado a mano (o eliminado) distinto del de
/// Banxico; no se sobrescriben. <c>UltimaFecha</c>: última fecha de aplicación calculada.
/// </summary>
public sealed record DescargaTipoCambioMoneda(
    string Moneda, int Registrados, int Corregidos, IReadOnlyList<DateOnly> Conflictos, DateOnly? UltimaFecha,
    bool MonedaNoRegistrada = false);

/// <summary>Claves SIE por moneda (pesos por unidad, por fecha de determinación).</summary>
public static class SeriesBanxico
{
    public const string FixUsd = "SF43718";
    public const string Eur = "SF46410";
    public const string Cad = "SF60632";

    public static readonly IReadOnlyList<(string Moneda, string Serie)> PorMoneda =
        [("USD", FixUsd), ("EUR", Eur), ("CAD", Cad)];
}

public sealed class DescargarTipoCambioBanxicoHandler(
    CompartidoDbContext db, IBanxicoSiePort banxico, IClock clock)
    : IRequestHandler<DescargarTipoCambioBanxicoCommand, DescargaTipoCambioResultado>
{
    /// <summary>Días de FIX que se piden: cubren el puente más largo (Semana Santa + fin de semana).</summary>
    public const int DiasConsulta = 15;

    /// <summary>Días hacia atrás que se rellenan si el job no corrió (servidor apagado).</summary>
    public const int DiasRelleno = 7;

    private static readonly TimeZoneInfo ZonaMexico = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    public async Task<DescargaTipoCambioResultado> Handle(
        DescargarTipoCambioBanxicoCommand command, CancellationToken cancellationToken)
    {
        var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, ZonaMexico).DateTime);
        var series = await banxico.ObtenerSeriesAsync(
            SeriesBanxico.PorMoneda.Select(s => s.Serie).ToArray(), hoy.AddDays(-DiasConsulta), hoy, cancellationToken);

        if (!series.TryGetValue(SeriesBanxico.FixUsd, out var fixUsd) || fixUsd.Count == 0)
            throw new BusinessRuleException("BANXICO_SIN_FIX",
                $"Banxico no devolvió el FIX de los últimos {DiasConsulta} días.");

        var habiles = fixUsd.Keys.ToHashSet();
        var conocidoHasta = EscaleraTipoCambio.CalendarioConocidoHasta(habiles, hoy);

        var monedas = await db.Monedas.AsNoTracking()
            .Where(m => m.Activa)
            .ToDictionaryAsync(m => m.Codigo, m => m.Id, cancellationToken);

        var resultado = new List<DescargaTipoCambioMoneda>();
        foreach (var (codigo, serie) in SeriesBanxico.PorMoneda)
        {
            if (!monedas.TryGetValue(codigo, out var monedaId))
            {
                resultado.Add(new DescargaTipoCambioMoneda(codigo, 0, 0, [], null, MonedaNoRegistrada: true));
                continue;
            }

            var valores = series.GetValueOrDefault(serie) ?? new Dictionary<DateOnly, decimal>();
            var aplicaciones = EscaleraTipoCambio.Aplicar(
                valores, habiles, conocidoHasta, hoy.AddDays(-DiasRelleno), conocidoHasta.AddDays(1));
            resultado.Add(await GuardarAsync(codigo, monedaId, aplicaciones, cancellationToken));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new DescargaTipoCambioResultado(conocidoHasta, resultado);
    }

    private async Task<DescargaTipoCambioMoneda> GuardarAsync(
        string codigo, Guid monedaId, IReadOnlyList<AplicacionTipoCambio> aplicaciones, CancellationToken ct)
    {
        if (aplicaciones.Count == 0) return new DescargaTipoCambioMoneda(codigo, 0, 0, [], null);

        var fechas = aplicaciones.Select(a => a.Fecha).ToArray();
        // Incluye los eliminados: el índice único (moneda, fecha) no los excluye.
        var existentes = await db.TiposCambio.IgnoreQueryFilters()
            .Where(t => t.MonedaId == monedaId && fechas.Contains(t.Fecha))
            .ToDictionaryAsync(t => t.Fecha, ct);

        int registrados = 0, corregidos = 0;
        var conflictos = new List<DateOnly>();
        foreach (var a in aplicaciones)
        {
            if (!existentes.TryGetValue(a.Fecha, out var tc))
            {
                db.TiposCambio.Add(new TipoCambio(Guid.CreateVersion7(), monedaId, a.Fecha, a.Valor, OrigenTipoCambio.Banxico));
                registrados++;
            }
            else if (tc.ValorEnMxn == a.Valor && tc.DeletedAt is null)
            {
                continue;
            }
            else if (tc.Origen == OrigenTipoCambio.Banxico && tc.DeletedAt is null)
            {
                tc.CorregirValorBanxico(a.Valor);
                corregidos++;
            }
            else
            {
                conflictos.Add(a.Fecha);
            }
        }

        return new DescargaTipoCambioMoneda(codigo, registrados, corregidos, conflictos, aplicaciones[^1].Fecha);
    }
}
