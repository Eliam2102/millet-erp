using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Contabilidad.Application.PublicPorts;
using Microsoft.Extensions.Options;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// Manejador único de los mensajes de la app de Almacenes: lo usan el endpoint HTTP (contrato v1, retirado por TI el 5-nov)
/// y el lector de <c>alm_mensaje</c> (<see cref="AlmMensajeLector"/>), de modo que ambos acusan igual. Idempotente por
/// <c>mensajeId</c>: el mismo mensaje dos veces = DUPLICADO con el mismo folio y sin aplicar nada.
/// Salida sin existencia (5-nov): acuse ERROR con folio de revisión y motivo; el mensaje queda marcado en la bandeja
/// (EN_REVISION) y la app, al reprocesarlo, lo vuelve a evaluar.
/// </summary>
public sealed class AlmacenesAppProcesador(
    AlmacenDbContext db, IComprasCrearRqAppPort rqApp, IAppSalidaResolverPort salidaResolver,
    IUsuarioServicioReadPort usuarioServicio, SalidaAppAlmacenesGuardado salidasGuardado,
    MermaProcesoAppGuardado mermasGuardado, IDimensionesSalidaInventarioPort dimensiones,
    IOptions<AlmMensajeOptions> opciones) : IProcesadorMensajeApp
{
    /// <summary>
    /// Tipos que, rechazados (ERROR/EN_REVISION), se reevalúan al llegar el reenvío del mismo mensajeId. La bandeja de
    /// «Mensajes de la app» lista sus ERROR con esta misma lista: un ERROR que frena la referencia siempre se ve.
    /// </summary>
    public static readonly string[] TiposReevaluables = [.. AlmacenesAppSalida.Tipos.Keys, .. AlmacenesAppMerma.Tipos, AlmacenesAppEntrada.Tipo,
        AlmacenesAppSalida.EntradaManual, AlmacenesAppCambio.Tipo];

    /// <summary>Forma del sobre (tipo, version, datos, mensajeId ≤ 64); null = válido.</summary>
    public static string? ErrorDeSobre(JsonElement root)
    {
        var id = Str(root, "mensajeId");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) return "mensajeId requerido (hasta 64)";
        var tipo = Str(root, "tipo");
        if (string.IsNullOrWhiteSpace(tipo) || tipo.Length > 100
            || !root.TryGetProperty("datos", out var datos) || datos.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("version", out var ver) || ver.ValueKind != JsonValueKind.Number)
            return "sobre inválido: tipo, version y datos son requeridos";
        return null;
    }

    private static string? Str(JsonElement root, string n) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    public async Task<AcuseApp> ProcesarAsync(JsonElement root, CancellationToken ct)
    {
        if (ErrorDeSobre(root) is { } malo) return new AcuseApp(Str(root, "mensajeId"), "ERROR", null, malo);
        var mensajeId = Str(root, "mensajeId")!;
        var tipo = Str(root, "tipo")!;
        var datos = root.GetProperty("datos");

        var previo = await db.MensajesApp.AsNoTracking().FirstOrDefaultAsync(m => m.MensajeId == mensajeId, ct);
        // A4.3-a: una salida rechazada (ERROR) o sin existencia (EN_REVISION, también acusada ERROR) quedó en la bandeja sin
        // mover saldo; cuando la app reprocesa el mismo mensajeId (ya corregido el dato o llegada la existencia) se evalúa
        // otra vez y sale de la bandeja.
        if (previo is { Estado: "ERROR" or SalidaAppAlmacenes.EnRevision } && TiposReevaluables.Contains(previo.Tipo))
        {
            await db.MensajesApp.Where(m => m.MensajeId == mensajeId && (m.Estado == "ERROR" || m.Estado == SalidaAppAlmacenes.EnRevision))
                .ExecuteDeleteAsync(ct);
            previo = null;
        }
        if (previo is null)
        {
            var referencia = Str(root, "referencia");
            string estado; string? folio, error;
            if (AlmacenesAppSalida.Tipos.ContainsKey(tipo))
            {
                // Las salidas mueven existencia: movimiento y MensajeApp se confirman juntos en el guardado.
                var (resuelta, errorSalida) = await AlmacenesAppSalida.ManejarAsync(tipo, datos, salidaResolver, usuarioServicio, ct,
                    dimensiones, opciones.Value.ExigirAreaEquipo);
                // Un ERROR de datos (artículo/medida/sucursal sin dar de alta, campo inválido) no descuenta nada y queda en la
                // bandeja de excepciones (A4.3-a) con folio de revisión; la app lo corrige y reprocesa el mismo mensajeId.
                if (resuelta is null) return await ErrorMarcadoAsync(mensajeId, tipo, referencia, root, errorSalida, ct);
                var g = await salidasGuardado.GuardarAsync(mensajeId, tipo, referencia, root.GetRawText(),
                    resuelta.EmpresaId, resuelta.UsuarioId, resuelta.Salidas, ct, resuelta.Origen);
                if (g.Estado == "ERROR") return new AcuseApp(mensajeId, g.Estado, g.Folio, g.Error);
                if (!g.Duplicado)
                    // Sin existencia (5-nov): ya no se acepta en silencio. El mensaje quedó EN_REVISION con su folio de
                    // revisión y la app recibe ERROR; los siguientes de la misma referencia esperan.
                    return g.Estado == SalidaAppAlmacenes.EnRevision
                        ? new AcuseApp(mensajeId, "ERROR", g.Folio, g.Error)
                        : new AcuseApp(mensajeId, g.Estado, g.Folio, g.Error);
                previo = await db.MensajesApp.AsNoTracking().FirstAsync(m => m.MensajeId == mensajeId, ct);
            }
            else if (tipo == AlmacenesAppEntrada.Tipo)
            {
                // M-159: sube existencia en el almacén general de la sucursal (contra una OC del ERP es su recepción); movimiento y MensajeApp se confirman juntos.
                var (resuelta, errorEntrada) = await AlmacenesAppEntrada.ManejarAsync(datos, salidaResolver, usuarioServicio, ct);
                if (resuelta is null) return await ErrorMarcadoAsync(mensajeId, tipo, referencia, root, errorEntrada, ct);
                var g = await salidasGuardado.GuardarEntradaAsync(mensajeId, tipo, referencia, root.GetRawText(),
                    resuelta.EmpresaId, resuelta.UsuarioId, resuelta.SucursalId, resuelta.Motivo, resuelta.Compra, resuelta.Entrada,
                    resuelta.Entradas, ct);
                if (!g.Duplicado) return new AcuseApp(mensajeId, g.Estado, g.Folio, g.Error);
                previo = await db.MensajesApp.AsNoTracking().FirstAsync(m => m.MensajeId == mensajeId, ct);
            }
            else if (tipo == AlmacenesAppSalida.EntradaManual)
            {
                // v9: material sin compra (sobrante, regreso de préstamo, devolución de venta) como ajuste positivo al promedio del ERP.
                var (resuelta, errorEntrada) = await AlmacenesAppSalida.ManejarAsync(tipo, datos, salidaResolver, usuarioServicio, ct);
                if (resuelta is null) return await ErrorMarcadoAsync(mensajeId, tipo, referencia, root, errorEntrada, ct);
                var primera = resuelta.Salidas[0];
                // La carga inicial (alm_carga_inicial, v9 §4) entra al costo de su renglón: es el costo de arranque.
                var carga = AlmCargaInicialLector.EsCargaInicial(mensajeId);
                var entradas = resuelta.Salidas.Select(s => new EntradaApp(s.ArticuloId, s.UnidadMedida, s.ProductoAwVarianteId, s.Cantidad,
                    s.AnchoMm, s.AltoMm, s.Lote, s.Almacen, s.CostoUnitarioMxn, carga)).ToList();
                var g = await salidasGuardado.GuardarEntradaAsync(mensajeId, tipo, referencia, root.GetRawText(),
                    resuelta.EmpresaId, resuelta.UsuarioId, primera.SucursalId, primera.Motivo, null, null, entradas, ct,
                    Millet.Almacen.Domain.Movimientos.TipoMovimiento.AjustePositivo, resuelta.Origen, primera.MotivoClave);
                if (!g.Duplicado) return new AcuseApp(mensajeId, g.Estado, g.Folio, g.Error);
                previo = await db.MensajesApp.AsNoTracking().FirstAsync(m => m.MensajeId == mensajeId, ct);
            }
            else if (tipo == AlmacenesAppCambio.Tipo)
            {
                // M-446: traslado entre almacenes de la sucursal (salida + entrada ligadas, sin póliza); sin existencia = ERROR REV.
                var (resuelto, errorCambio) = await AlmacenesAppCambio.ManejarAsync(datos, salidaResolver, usuarioServicio, ct);
                if (resuelto is null) return await ErrorMarcadoAsync(mensajeId, tipo, referencia, root, errorCambio, ct);
                var g = await salidasGuardado.GuardarTrasladoAsync(mensajeId, tipo, referencia, root.GetRawText(),
                    resuelto.EmpresaId, resuelto.UsuarioId, resuelto.Origen, resuelto.Destino, ct);
                if (!g.Duplicado)
                    return new AcuseApp(mensajeId, g.Estado == SalidaAppAlmacenes.EnRevision ? "ERROR" : g.Estado, g.Folio, g.Error);
                previo = await db.MensajesApp.AsNoTracking().FirstAsync(m => m.MensajeId == mensajeId, ct);
            }
            else if (AlmacenesAppMerma.Tipos.Contains(tipo))
            {
                // A4.4: desperdicio de mesa y merma de proceso; el desperdicio avisa a Contabilidad, la merma queda pendiente.
                var (resuelta, errorMerma) = await AlmacenesAppMerma.ManejarAsync(tipo, datos, salidaResolver, usuarioServicio, ct);
                if (resuelta is null) return await ErrorMarcadoAsync(mensajeId, tipo, referencia, root, errorMerma, ct);
                var g = await mermasGuardado.GuardarAsync(mensajeId, tipo, referencia, root.GetRawText(),
                    resuelta.EmpresaId, resuelta.Merma, ct);
                if (!g.Duplicado) return new AcuseApp(mensajeId, g.Estado, null, g.Error);
                previo = await db.MensajesApp.AsNoTracking().FirstAsync(m => m.MensajeId == mensajeId, ct);
            }
            else
            {
                // Punto de enchufe de handlers: un case por tipo.
                (estado, folio, error) = tipo switch
                {
                    "REQUISICION_ENVIADA" => await AlmacenesAppRequisicionEnviada.ManejarAsync(datos, rqApp, ct),
                    AlmacenesAppRequisicionEnviada.Cancelada or AlmacenesAppRequisicionEnviada.Reducida =>
                        await AlmacenesAppRequisicionEnviada.ManejarAvisoAsync(tipo, datos, rqApp, ct),
                    _ => ("ERROR", (string?)null, (string?)"tipo aún no soportado"),
                };
                // Un ERROR de requisición no se guarda: el reenvío (mismo mensajeId) se vuelve a evaluar.
                if (tipo.StartsWith("REQUISICION_", StringComparison.Ordinal) && estado == "ERROR")
                    return new AcuseApp(mensajeId, estado, folio, error);
                db.MensajesApp.Add(new MensajeApp(mensajeId, tipo,
                    referencia?.Length > 100 ? referencia[..100] : referencia, root.GetRawText(), estado, folio, error));
                try
                {
                    await db.SaveChangesAsync(ct);
                    return new AcuseApp(mensajeId, estado, folio, error);
                }
                catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
                {
                    db.ChangeTracker.Clear();
                    previo = await db.MensajesApp.AsNoTracking().FirstAsync(m => m.MensajeId == mensajeId, ct);
                }
            }
        }

        // Mismo folio (y error, si lo hubo) que la primera vez; nada nuevo se crea.
        return new AcuseApp(mensajeId, "DUPLICADO", previo.Folio, previo.Error);
    }

    /// <summary>ERROR de datos: queda en la bandeja con folio de revisión y no mueve saldo.</summary>
    private async Task<AcuseApp> ErrorMarcadoAsync(
        string mensajeId, string tipo, string? referencia, JsonElement root, string? error, CancellationToken ct)
    {
        var folio = SalidaAppAlmacenes.FolioRevision(mensajeId);
        db.MensajesApp.Add(new MensajeApp(mensajeId, tipo,
            referencia?.Length > 100 ? referencia[..100] : referencia, root.GetRawText(), "ERROR", folio,
            error?.Length > 1000 ? error[..1000] : error));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            db.ChangeTracker.Clear(); // otro envío del mismo mensajeId ganó la carrera; el acuse sigue siendo ERROR
        }
        return new AcuseApp(mensajeId, "ERROR", folio, error);
    }
}
