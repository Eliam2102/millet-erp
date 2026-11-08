using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Depositos;

/// <summary>Cuántas líneas de abono se confirmaron solas, quedaron en revisión o sin identificar.</summary>
public sealed record ResumenDepositosAutomaticos(int AutoConfirmados, int EnRevision, int SinIdentificar);

/// <summary>
/// 86e3ke6yn: después del emparejamiento, evalúa cada abono del extracto contra las propuestas de CxC
/// pendientes. Auto → confirma por el mismo camino que la confirmación manual; duda → deja el motivo en la
/// propuesta (bandeja existente); sin propuesta → crea el ingreso para «Depósitos sin identificar».
/// El llamador ya tiene el lock de <see cref="EjecutarBajoLockAsync{T}"/> y hace el SaveChanges final.
/// </summary>
public sealed class DepositoAutomaticoProcesador(
    TesoreriaDbContext db,
    IIntegrationEventPublisher publisher,
    IClock clock,
    ICurrentUserContext userContext,
    ICurrentEmpresaContext empresaContext,
    ICurrentUserPermissions permisos,
    IPeriodoContablePort periodos,
    ITesoreriaSucursalReadPort sucursales,
    IOptions<DepositoAutomaticoOptions> opciones,
    ITipoCambioPagoReadPort? tiposCambio = null)
{
    /// <summary>Mismo lock que confirmar/devolver depósitos: la carrera depósito↔devolución se serializa.</summary>
    public static async Task<T> EjecutarBajoLockAsync<T>(
        TesoreriaDbContext db, Func<CancellationToken, Task<T>> operacion, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return await operacion(ct);
        T? resultado = default;
        await Millet.SharedKernel.Infrastructure.Persistence.PostgresAdvisoryLock.ExecuteAsync(db,
            Devoluciones.DevolucionLectura.LockKey, async c => resultado = await operacion(c), ct);
        return resultado!;
    }

    public async Task<ResumenDepositosAutomaticos> ProcesarAsync(
        Conciliacion conciliacion, CuentaBancaria cuenta, IEnumerable<ExtractoLinea> lineas,
        bool crearSinIdentificar, CancellationToken ct)
    {
        var ingresos = lineas
            .Where(l => l.Sentido == SentidoMovimiento.Ingreso
                && l.Estado is EstadoExtractoLinea.Pendiente or EstadoExtractoLinea.EnRevision or EstadoExtractoLinea.Emparejada)
            .OrderBy(l => l.NumeroLinea).ToList();
        if (ingresos.Count == 0) return new(0, 0, 0);

        // Guarda de idempotencia: una línea que ya confirmó un depósito no se procesa otra vez.
        var ids = ingresos.Select(l => (Guid?)l.Id).ToList();
        var yaConfirmadas = (await db.DepositosConfirmacion.AsNoTracking()
                .Where(d => d.Estado == EstadoDepositoConfirmacion.Confirmada && ids.Contains(d.ExtractoLineaId))
                .Select(d => d.ExtractoLineaId!.Value).ToListAsync(ct)).ToHashSet();

        // Solo propuestas aún pendientes pueden ser candidatas.
        var candidatos = await db.DepositosConfirmacion
            .Where(d => d.Estado == EstadoDepositoConfirmacion.Pendiente && d.PropuestaCxcId != null)
            .ToListAsync(ct);

        var empresaId = empresaContext.Current ?? conciliacion.EmpresaId;
        var usuarioId = userContext.UserId ?? Guid.Empty;
        var puedeConfirmar = await permisos.TieneAsync(PermisosCanonicos.TesoreriaDepositosConfirmar, ct);
        var periodoAbierto = new Dictionary<(int, int), bool>();
        int auto = 0, revision = 0, sinIdentificar = 0;

        foreach (var linea in ingresos.Where(l => !yaConfirmadas.Contains(l.Id)))
        {
            var clave = (linea.Fecha.Year, linea.Fecha.Month);
            if (!periodoAbierto.TryGetValue(clave, out var abierto))
                periodoAbierto[clave] = abierto = await periodos.EstaAbiertoAsync(clave.Item1, clave.Item2, ct);

            Guid? movLinea = linea.Estado == EstadoExtractoLinea.Emparejada ? linea.MatchMovimientoId : null;
            var devolucion = movLinea is Guid m && await db.DevolucionesCliente.AnyAsync(d => d.MovimientoOrigenId == m
                && (d.Estado == EstadoDevolucionCliente.Solicitada || d.Estado == EstadoDevolucionCliente.Autorizada), ct);

            var resultado = ReglaDepositoAutomatico.Evaluar(linea, candidatos, opciones.Value,
                new ContextoDepositoAutomatico(usuarioId, cuenta.Moneda, abierto, movLinea, devolucion));

            switch (resultado)
            {
                case ResultadoReglaDeposito.Revision r:
                    foreach (var c in r.Candidatos) c.MarcarEnRevision(r.Motivo, linea.Id);
                    revision++;
                    break;

                case ResultadoReglaDeposito.AutoConfirmar a:
                    var motivo = !puedeConfirmar
                        ? "Quien importa el extracto no tiene permiso para confirmar depósitos."
                        : await ConfirmarAsync(conciliacion, cuenta, linea, a, movLinea, empresaId, usuarioId, ct);
                    if (motivo is null) auto++;
                    else
                    {
                        a.Deposito.MarcarEnRevision(motivo, linea.Id);
                        revision++;
                    }
                    break;

                default:
                    // Sin propuesta: el ingreso entra al libro para que CxC lo identifique cuando llegue el detalle.
                    if (crearSinIdentificar && linea.Estado == EstadoExtractoLinea.Pendiente && abierto)
                    {
                        try
                        {
                            // Tipo Cliente (cliente por identificar): es el criterio de la bandeja «Sin identificar».
                            var mov = MovimientoBancario.RegistrarIngreso(empresaId, cuenta, linea.Monto, linea.Fecha,
                                linea.Referencia, null, BeneficiarioTipo.Cliente, null, usuarioId, clock.UtcNow);
                            EmparejarNuevo(conciliacion, linea, mov, usuarioId, "Alta automática de depósito",
                                "Sin propuesta pendiente con esa referencia: queda en Depósitos sin identificar.");
                            db.MovimientosBancarios.Add(mov);
                            sinIdentificar++;
                        }
                        catch (BusinessRuleException)
                        {
                            // cuenta inactiva o fecha previa al saldo inicial: la línea queda pendiente, como hoy
                        }
                    }
                    break;
            }
        }

        return new(auto, revision, sinIdentificar);
    }

    /// <summary>Devuelve null si confirmó; si no, el motivo para mandarlo a revisión.</summary>
    private async Task<string?> ConfirmarAsync(
        Conciliacion conciliacion, CuentaBancaria cuenta, ExtractoLinea linea, ResultadoReglaDeposito.AutoConfirmar auto,
        Guid? movLinea, Guid empresaId, Guid usuarioId, CancellationToken ct)
    {
        var d = auto.Deposito;
        MovimientoBancario? mov;
        decimal aplicadoPrevio = 0m;
        if (movLinea is Guid id)
        {
            mov = await db.MovimientosBancarios.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (mov is null) return "No se encontró el ingreso emparejado con la línea.";
            aplicadoPrevio = (await DepositoDisponible.AplicadoPorMovimientoAsync(db, [mov], ct)).GetValueOrDefault(mov.Id);
        }
        else
        {
            // Sucursal como en la confirmación manual: la única del documento; si no es única, lo decide un humano.
            var sucursal = (await sucursales.ObtenerAsync("deposito", d.Id, ct))?.Sucursales;
            if (sucursal is not { Count: 1 }) return "No se pudo determinar una sucursal única para el ingreso.";
            try
            {
                mov = MovimientoBancario.RegistrarIngreso(empresaId, cuenta, linea.Monto, linea.Fecha, linea.Referencia,
                    null, BeneficiarioTipo.Cliente, d.ClienteId, usuarioId, clock.UtcNow, sucursal[0]);
            }
            catch (BusinessRuleException ex)
            {
                return ex.Message;
            }
        }

        d.MarcarConfirmacionAutomatica(auto.ReglaTexto, linea.Id);
        try
        {
            await ConfirmacionDeposito.ConfirmarContraMovimientoAsync(db, publisher, clock, empresaId, usuarioId, d, mov,
                movimientoNuevo: movLinea is null, aplicadoPrevio, ct, tiposCambio: tiposCambio);
        }
        catch (BusinessRuleException ex)
        {
            return ex.Message; // el dominio rechazó (p. ej. movimiento ya aplicado): revisión, no error de importación
        }

        var facturas = DepositoMapper.ParsePayload(d.FacturasJson); // la regla exige exactamente una
        var detalle = $"{auto.ReglaTexto}. Propuesta {d.PropuestaCxcId}. Factura {facturas[0].Folio ?? facturas[0].FacturaVentaId.ToString()}.";
        if (movLinea is null)
            EmparejarNuevo(conciliacion, linea, mov, usuarioId, "Regla automática", detalle);
        else
            conciliacion.RegistrarEnBitacora(linea.Id, mov.Id, "Regla automática", detalle, usuarioId, clock.UtcNow);
        return null;
    }

    private void EmparejarNuevo(Conciliacion conciliacion, ExtractoLinea linea, MovimientoBancario mov,
        Guid usuarioId, string accion, string detalle)
    {
        var ahora = clock.UtcNow;
        linea.Emparejar(mov.Id, MatchTipo.AltaAsistida, linea.Monto, usuarioId, ahora);
        mov.MarcarConciliado();
        conciliacion.RegistrarEnBitacora(linea.Id, mov.Id, accion, detalle, usuarioId, ahora);
    }
}
