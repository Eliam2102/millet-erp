using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Millet.Almacen.Application.AppAlmacenes;

/// <summary>
/// Renglón de <c>alm_carga_inicial</c> (contrato v9 §4): existencia de un momento, agrupada como los mensajes (artículo + medida +
/// lote + pedimento + etiqueta), en la llave de almacén <c>sucursal + tipo_almacen + linea</c>. Costo en pesos por m² (hojas) o por
/// pieza (herraje).
/// </summary>
public sealed record AlmCargaInicialRenglon(
    int CargaId, int Renglon, string Sucursal, string? TipoAlmacen, string? Linea, int ArticuloAw, int? ArticuloAlmacenAw,
    decimal? Ancho, decimal? Alto, string? Lote, string? Pedimento, string? Etiqueta, int Piezas, decimal? M2,
    decimal? CostoM2, decimal? CostoPieza, string? Moneda, DateTime? FechaCorte);

/// <summary>
/// Puerto de la tabla <c>alm_carga_inicial</c> (la app la escribe; el ERP solo lee y acusa por renglón APLICADO / ERROR en
/// <c>acuse_estado</c>, <c>acuse_texto</c>, <c>acuse_fecha</c>). Adaptadores en Integraciones.Aw: simulado y SQL.
/// </summary>
public interface IAlmCargaInicialOrigen
{
    /// <summary>La carga de número más alto (la que vale); null si no hay ninguna.</summary>
    Task<int?> UltimaCargaAsync(CancellationToken ct);

    /// <summary>La carga de número más alto con algún renglón APLICADO; null si no se ha aplicado ninguna.</summary>
    Task<int?> CargaAplicadaAsync(CancellationToken ct);

    /// <summary>Renglones de la carga sin acuse, en orden de renglón, hasta <paramref name="max"/>.</summary>
    Task<IReadOnlyList<AlmCargaInicialRenglon>> LeerPendientesAsync(int cargaId, int max, CancellationToken ct);

    /// <summary>Acuse del renglón (APLICADO o ERROR); sobre un renglón ya acusado no cambia nada.</summary>
    Task EscribirAcuseAsync(int cargaId, int renglon, string estado, string? texto, CancellationToken ct);
}

/// <summary>
/// Aplica la carga inicial de existencias de la app (contrato v9 §4). Toma la carga de número más alto y aplica cada renglón como una
/// ENTRADA_MANUAL con el mismo manejador que <c>alm_mensaje</c> (sucursal, artículo, medida, llave de almacén, lote), idempotente por
/// <c>mensajeId = carga-inicial:{carga}:{renglón}</c>, al costo del renglón (<see cref="EsCargaInicial"/>). Acusa APLICADO o ERROR por
/// renglón. Si ya se aplicó otra carga, no aplica la nueva (se sumaría la existencia dos veces): la acusa en ERROR con el motivo.
/// Una falla del ERP no acusa: el renglón se reintenta en el siguiente ciclo. Apagado por omisión
/// (<see cref="AlmMensajeOptions.CargaInicialHabilitada"/>).
/// </summary>
public sealed class AlmCargaInicialLector(
    IAlmCargaInicialOrigen origen, IServiceScopeFactory scopes, IOptions<AlmMensajeOptions> options, ILogger<AlmCargaInicialLector> logger)
{
    public const string PrefijoMensaje = "carga-inicial:";
    public const string MotivoClave = "CARGA_INICIAL";

    /// <summary>El mensaje es un renglón de la carga inicial: entra al costo del renglón, no al promedio del ERP.</summary>
    public static bool EsCargaInicial(string mensajeId) => mensajeId.StartsWith(PrefijoMensaje, StringComparison.Ordinal);

    public async Task<(int Aplicados, int Errores)> ProcesarAsync(CancellationToken ct)
    {
        if (await origen.UltimaCargaAsync(ct) is not { } carga) return default;
        int aplicados = 0, errores = 0;
        if (await origen.CargaAplicadaAsync(ct) is { } aplicada && aplicada != carga)
        {
            // v11: una carga se puede regenerar, pero sumar la nueva sobre la ya aplicada duplicaría la existencia. El ERP no la
            // aplica y acusa ERROR por renglón (v11: «si el ERP no puede aplicar un mensaje, contesta ERROR»), para que Almacenes
            // lo vea en su acuse en vez de quedarse esperando. Reemplazarla exige reversar la anterior y eso lo decide Millet.
            logger.LogError("[AlmCargaInicial] La carga {Carga} no se aplica: ya se aplicó la carga {Aplicada}. Revisar con TI.", carga, aplicada);
            var texto = $"La carga {carga} no se aplica: el ERP ya aplicó la carga {aplicada} y sumarla duplicaría la existencia. "
                + "Avisen al equipo del ERP para reversar la carga anterior antes de regenerarla.";
            foreach (var r in await origen.LeerPendientesAsync(carga, Math.Max(1, options.Value.Lote), ct))
            {
                await origen.EscribirAcuseAsync(carga, r.Renglon, "ERROR", texto, ct);
                errores++;
            }
            return (0, errores);
        }
        foreach (var r in await origen.LeerPendientesAsync(carga, Math.Max(1, options.Value.Lote), ct))
        {
            AcuseApp acuse;
            try
            {
                using var doc = JsonDocument.Parse(Sobre(r));
                using var scope = scopes.CreateScope();
                acuse = await scope.ServiceProvider.GetRequiredService<IProcesadorMensajeApp>().ProcesarAsync(doc.RootElement, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // ponytail: sin tope de reintentos; un renglón que siempre truena se ve en el log en cada ciclo.
                logger.LogError(ex, "[AlmCargaInicial] Falla al aplicar el renglón {Renglon} de la carga {Carga}.", r.Renglon, carga);
                continue;
            }
            var ok = acuse.Estado is "RECIBIDO" or "DUPLICADO";
            await origen.EscribirAcuseAsync(carga, r.Renglon, ok ? "APLICADO" : "ERROR", ok ? null : acuse.Error, ct);
            if (ok) aplicados++; else errores++;
        }
        return (aplicados, errores);
    }

    /// <summary>El renglón como sobre ENTRADA_MANUAL de un grupo (misma forma que los mensajes de la app).</summary>
    public static string Sobre(AlmCargaInicialRenglon r) => JsonSerializer.Serialize(new
    {
        mensajeId = $"{PrefijoMensaje}{r.CargaId}:{r.Renglon}",
        tipo = "ENTRADA_MANUAL",
        version = 1,
        fecha = r.FechaCorte,
        origen = "AlmacenRQ_MP",
        referencia = $"CARGA-{r.CargaId}",
        datos = new
        {
            movimiento = $"CARGA-{r.CargaId}-{r.Renglon}",
            sucursal = r.Sucursal,
            motivo = $"Carga inicial {r.CargaId} renglón {r.Renglon}",
            motivoClave = MotivoClave,
            moneda = r.Moneda,
            piezas = r.Piezas,
            almacenes = new[]
            {
                new
                {
                    almacenGeneral = string.IsNullOrWhiteSpace(r.TipoAlmacen) ? null : new { tipo = r.TipoAlmacen, linea = r.Linea },
                    piezas = r.Piezas,
                    grupos = new[]
                    {
                        new
                        {
                            articuloAw = r.ArticuloAw, articuloAlmacenAw = r.ArticuloAlmacenAw, ancho = r.Ancho, alto = r.Alto,
                            lote = r.Lote, pedimento = r.Pedimento, etiqueta = r.Etiqueta, piezas = r.Piezas, m2 = r.M2,
                            costoM2 = r.CostoM2, costoPieza = r.CostoPieza,
                        },
                    },
                },
            },
        },
    });
}
