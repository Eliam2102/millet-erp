using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Ingesta.ProcesarSolicitudAw;

/// <summary>
/// Matriz operación×estado de la ingesta A+W (§12.1 levantamiento). Reglas de oro:
/// idempotencia por <c>version</c> (doble candado), <b>la factura manda</b> (D20:
/// Modif/Cancel sobre <c>Facturado</c> → excepción/revisión manual), y
/// <c>Bloqueado</c> pospone. El write-back se hace al final con el resultado.
///
/// <para>
/// FAC-ING-PR3: las causas corregibles en catálogos del ERP
/// (<see cref="LecturaPedidoAw.EsperaConfiguracion"/> y cliente no
/// provisionable) también POSPONEN — la solicitud se queda en la cola y entra
/// sola cuando el operador corrige el catálogo, con tope de
/// <see cref="AwSolicitudesOptions.PospuestaMaxDias"/> días antes de escalar
/// a rechazo. La bandeja lleva UNA excepción abierta por pedido (upsert) que
/// se auto-resuelve cuando una versión posterior aplica.
/// </para>
/// </summary>
public sealed class ProcesarSolicitudAwHandler
    : IRequestHandler<ProcesarSolicitudAwCommand, ProcesarSolicitudAwResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAwSolicitudesReader _reader;
    private readonly IAwWriteBackPort _writeBack;
    private readonly IClientesReadPort _clientes;
    private readonly IProductosReadPort _productos;
    private readonly IMasterProvisioningPort _masterProvisioning;
    private readonly IClock _clock;
    private readonly AwSolicitudesOptions _opciones;

    public ProcesarSolicitudAwHandler(
        FacturacionDbContext db,
        IAwSolicitudesReader reader,
        IAwWriteBackPort writeBack,
        IClientesReadPort clientes,
        IProductosReadPort productos,
        IMasterProvisioningPort masterProvisioning,
        IClock clock,
        IOptions<AwSolicitudesOptions> opciones)
    {
        _db = db;
        _reader = reader;
        _writeBack = writeBack;
        _clientes = clientes;
        _productos = productos;
        _masterProvisioning = masterProvisioning;
        _clock = clock;
        _opciones = opciones.Value;
    }

    public async Task<ProcesarSolicitudAwResponse> Handle(
        ProcesarSolicitudAwCommand command,
        CancellationToken cancellationToken)
    {
        var sol = command.Solicitud;
        var empresaId = command.EmpresaId;
        var ahora = _clock.UtcNow;

        var control = await _db.IngestaControles
            .FirstOrDefaultAsync(c => c.Origen == OrigenPedido.Aw && c.ClaveNatural == sol.NumeroPedido, cancellationToken);

        // Idempotencia: versión ya aplicada → no reprocesar.
        if (control is not null && sol.Version <= control.UltimaVersionAplicada)
        {
            control.TocarLectura(ahora);
            await _db.SaveChangesAsync(cancellationToken);
            return await ResultadoAsync(sol, control.PedidoFacturableId, EstadoFacturacion(control.Estado),
                ResultadoSolicitudAw.Aplicada, "versión ya aplicada (idempotente)", cancellationToken);
        }

        PedidoFacturable? pedido = null;
        if (control?.PedidoFacturableId is Guid pid)
            pedido = await _db.PedidosFacturables.Include(p => p.Lineas).FirstOrDefaultAsync(p => p.Id == pid, cancellationToken);

        var resultado = sol.Operacion switch
        {
            OperacionAw.Alta => await ProcesarAltaAsync(empresaId, sol, pedido, control, ahora, cancellationToken),
            OperacionAw.Modificacion => await ProcesarModificacionAsync(empresaId, sol, pedido, control, ahora, cancellationToken),
            OperacionAw.Cancelacion => ProcesarCancelacion(empresaId, sol, pedido, control, ahora),
            _ => (ResultadoSolicitudAw.Error, (Guid?)null, "operación desconocida", (string?)null),
        };

        if (resultado.Item1 == ResultadoSolicitudAw.Aplicada)
            await ResolverExcepcionesAbiertasAsync(empresaId, sol.NumeroPedido, ahora, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return await ResultadoAsync(sol, resultado.Item2, resultado.Item4, resultado.Item1, resultado.Item3, cancellationToken);
    }

    private async Task<(ResultadoSolicitudAw, Guid?, string?, string?)> ProcesarAltaAsync(
        Guid empresaId, SolicitudAw sol, PedidoFacturable? pedido, IngestaControl? control,
        DateTimeOffset ahora, CancellationToken ct)
    {
        if (pedido is not null)
            return (ResultadoSolicitudAw.Aplicada, pedido.Id, "alta idempotente — el pedido ya existe", "SinFacturar");

        var lectura = await _reader.LeerDatosPedidoAsync(sol.NumeroPedido, ct);
        if (lectura.Datos is not DatosPedidoAw datos)
            return await PosponerORechazarAsync(empresaId, sol, control,
                lectura.Motivo, lectura.Detalle ?? "no se pudieron leer los datos del pedido",
                lectura.EsperaConfiguracion, hash: string.Empty,
                marcarControlExcepcion: true, pedidoId: null, estadoFacturacion: null, ahora, ct);

        // Origen A+W: si el cliente no está en el master, se auto-provisiona
        // desde la vista de A+W (§3.bis.6). Falla de provisión = corregible
        // (dar de alta el cliente) → misma semántica pospuesta que catálogos.
        var cliente = await _clientes.ResolverPorReferenciaAsync(datos.ClienteRef, ct)
                      ?? await _masterProvisioning.EnsureClienteDesdeAwAsync(datos.ClienteRef, ct);
        if (cliente is null)
            return await PosponerORechazarAsync(empresaId, sol, control,
                MotivoExcepcion.ClienteNoExiste,
                $"cliente '{datos.ClienteRef}' no existe en el master y no se pudo auto-provisionar desde A+W",
                esperaConfiguracion: true, hash: Hash(datos.PayloadCrudo),
                marcarControlExcepcion: true, pedidoId: null, estadoFacturacion: null, ahora, ct);

        var nuevo = PedidoFacturable.ImportarDesdeAw(
            empresaId, sol.NumeroPedido, datos.SucursalId, cliente.ClienteId, datos.ClienteNombre,
            datos.CanalVenta, datos.ComportamientoFiscal, datos.Moneda, datos.ObraId, datos.ObraNombre,
            datos.Comentarios, sol.Version, datos.EstadoOrigen, datos.Ranura);
        await AgregarLineasAsync(nuevo, datos.Lineas, ct);
        nuevo.RecalcularTotal();

        _db.PedidosFacturables.Add(nuevo);
        _db.PedidosFacturablesSnapshot.Add(PedidoFacturableSnapshot.Crear(nuevo.Id, datos.PayloadCrudo, ahora));
        UpsertControl(control, empresaId, sol, Hash(datos.PayloadCrudo), EstadoIngesta.Importado, nuevo.Id, ahora);

        return (ResultadoSolicitudAw.Aplicada, nuevo.Id, null, "SinFacturar");
    }

    private async Task<(ResultadoSolicitudAw, Guid?, string?, string?)> ProcesarModificacionAsync(
        Guid empresaId, SolicitudAw sol, PedidoFacturable? pedido, IngestaControl? control,
        DateTimeOffset ahora, CancellationToken ct)
    {
        if (pedido is null)
        {
            RegistrarExcepcion(empresaId, sol.NumeroPedido, MotivoExcepcion.Otro, "modificación sin un Alta previa");
            return (ResultadoSolicitudAw.Rechazada, null, "no existe pedido para modificar", null);
        }
        if (pedido.Estado == EstadoPedidoFacturable.Facturado)
        {
            RegistrarExcepcion(empresaId, sol.NumeroPedido, MotivoExcepcion.ModificacionSobreFacturado,
                "modificación sobre pedido facturado — la factura manda (D20)");
            return (ResultadoSolicitudAw.Rechazada, pedido.Id, "modificación sobre facturado → revisión manual", "Facturado");
        }
        if (pedido.Estado == EstadoPedidoFacturable.Bloqueado)
            return (ResultadoSolicitudAw.Pospuesta, pedido.Id, "pedido bloqueado (facturándose); reintentar al liberar", "SinFacturar");

        var lectura = await _reader.LeerDatosPedidoAsync(sol.NumeroPedido, ct);
        if (lectura.Datos is not DatosPedidoAw datos)
            // Sin control-Excepcion: el pedido ya existe importado con datos
            // válidos de una versión anterior — solo la modificación queda
            // pospuesta/rechazada, igual que los rechazos D20.
            return await PosponerORechazarAsync(empresaId, sol, control,
                lectura.Motivo, lectura.Detalle ?? "datos del pedido no disponibles",
                lectura.EsperaConfiguracion, hash: control?.HashContenido ?? string.Empty,
                marcarControlExcepcion: false, pedidoId: pedido.Id, estadoFacturacion: "SinFacturar", ahora, ct);

        var cliente = await _clientes.ResolverPorReferenciaAsync(datos.ClienteRef, ct)
                      ?? await _masterProvisioning.EnsureClienteDesdeAwAsync(datos.ClienteRef, ct);
        var clienteId = cliente?.ClienteId ?? pedido.ClienteId;

        pedido.RefrescarCabecera(clienteId, datos.ClienteNombre, datos.CanalVenta, datos.ComportamientoFiscal,
            datos.Moneda, datos.ObraId, datos.ObraNombre, datos.Comentarios, sol.Version, datos.EstadoOrigen,
            datos.Ranura);
        pedido.LimpiarLineas();
        await AgregarLineasAsync(pedido, datos.Lineas, ct);
        pedido.RecalcularTotal();

        _db.PedidosFacturablesSnapshot.Add(PedidoFacturableSnapshot.Crear(pedido.Id, datos.PayloadCrudo, ahora));
        control!.Aplicar(Hash(datos.PayloadCrudo), sol.Version, EstadoIngesta.Importado, pedido.Id, ahora);

        return (ResultadoSolicitudAw.Aplicada, pedido.Id, null, "SinFacturar");
    }

    private (ResultadoSolicitudAw, Guid?, string?, string?) ProcesarCancelacion(
        Guid empresaId, SolicitudAw sol, PedidoFacturable? pedido, IngestaControl? control, DateTimeOffset ahora)
    {
        if (pedido is null)
            return (ResultadoSolicitudAw.Rechazada, null, "no existe pedido para cancelar", null);
        if (pedido.Estado == EstadoPedidoFacturable.Facturado)
        {
            RegistrarExcepcion(empresaId, sol.NumeroPedido, MotivoExcepcion.CancelacionSobreFacturado,
                "cancelación sobre pedido facturado — la factura manda (D20)");
            return (ResultadoSolicitudAw.Rechazada, pedido.Id, "cancelación sobre facturado → cancelar CFDI por flujo SAT", "Facturado");
        }
        if (pedido.Estado == EstadoPedidoFacturable.Bloqueado)
            return (ResultadoSolicitudAw.Pospuesta, pedido.Id, "pedido bloqueado; reintentar al liberar", "SinFacturar");

        pedido.Cancelar();
        control!.Aplicar(control.HashContenido, sol.Version, EstadoIngesta.Cancelado, pedido.Id, ahora);
        return (ResultadoSolicitudAw.Aplicada, pedido.Id, null, "Cancelado");
    }

    private async Task AgregarLineasAsync(PedidoFacturable pedido, IReadOnlyList<LineaPedidoAw> lineas, CancellationToken ct)
    {
        foreach (var l in lineas)
        {
            var producto = await _productos.ResolverPorReferenciaAsync(l.ProductoRef, ct)
                           ?? await _masterProvisioning.EnsureArticuloDesdeAwAsync(l.ProductoRef, ct);
            pedido.AgregarLinea(
                productoId: producto?.ProductoId,
                productoDescripcion: l.Descripcion,
                claveProdServSat: l.ClaveProdServSat ?? producto?.ClaveProdServSat,
                claveUnidadSat: l.ClaveUnidadSat ?? producto?.ClaveUnidadSat,
                cantidad: l.Cantidad,
                precio: l.Precio,
                descuento: l.Descuento,
                requierePedimento: l.RequierePedimento,
                bomJson: l.BomJson,
                // Tasa del documento A+W (FAC-DET-PR2): en pedidos A+W la
                // tasa del origen manda — no se cae al master ni a la empresa.
                tasaIva: l.TasaIva);
        }
    }

    private void RegistrarExcepcion(Guid empresaId, string pedidoRef, MotivoExcepcion motivo, string detalle) =>
        _db.ExcepcionesImportacion.Add(ExcepcionImportacion.Crear(empresaId, OrigenPedido.Aw, pedidoRef, motivo, detalle));

    /// <summary>
    /// Veredicto para datos no disponibles (FAC-ING-PR3): causas corregibles
    /// en el ERP se POSPONEN (la solicitud sigue en la cola con resultado 3 y
    /// entra sola cuando el operador corrige el catálogo) hasta
    /// <see cref="AwSolicitudesOptions.PospuestaMaxDias"/> días desde la
    /// primera excepción; después escalan a rechazo. Datos defectuosos del
    /// origen se rechazan de inmediato, como antes. En pospuesta NO se toca
    /// <c>ingesta_control</c>: bumpear <c>UltimaVersionAplicada</c> haría que
    /// el reintento cayera en el candado de idempotencia.
    /// </summary>
    private async Task<(ResultadoSolicitudAw, Guid?, string?, string?)> PosponerORechazarAsync(
        Guid empresaId, SolicitudAw sol, IngestaControl? control,
        MotivoExcepcion motivo, string detalle, bool esperaConfiguracion, string hash,
        bool marcarControlExcepcion, Guid? pedidoId, string? estadoFacturacion,
        DateTimeOffset ahora, CancellationToken ct)
    {
        var excepcion = await ObtenerOAbrirExcepcionAsync(empresaId, sol.NumeroPedido, motivo, detalle, ct);
        // Entidad recién agregada: CreatedAt lo estampa el interceptor al
        // guardar — default(DateTimeOffset) significa "primera vez = ahora".
        var primeraVez = excepcion.CreatedAt == default ? ahora : excepcion.CreatedAt;

        if (esperaConfiguracion && ahora < primeraVez.AddDays(_opciones.PospuestaMaxDias))
        {
            excepcion.Actualizar(motivo, detalle);
            return (ResultadoSolicitudAw.Pospuesta, pedidoId,
                $"{detalle} — en espera de corrección en catálogos", estadoFacturacion);
        }

        if (esperaConfiguracion)
            detalle = $"{detalle} — sin corregir tras {_opciones.PospuestaMaxDias} días, escalado a rechazo";
        excepcion.Actualizar(motivo, detalle);
        if (marcarControlExcepcion)
            UpsertControl(control, empresaId, sol, hash, EstadoIngesta.Excepcion, pedidoId, ahora);
        return (ResultadoSolicitudAw.Rechazada, pedidoId, detalle, estadoFacturacion);
    }

    private async Task<ExcepcionImportacion> ObtenerOAbrirExcepcionAsync(
        Guid empresaId, string pedidoRef, MotivoExcepcion motivo, string detalle, CancellationToken ct)
    {
        var abierta = _db.ExcepcionesImportacion.Local.FirstOrDefault(
                e => e.EmpresaId == empresaId && e.Origen == OrigenPedido.Aw && e.PedidoRef == pedidoRef && !e.Resuelto)
            ?? await _db.ExcepcionesImportacion.FirstOrDefaultAsync(
                e => e.EmpresaId == empresaId && e.Origen == OrigenPedido.Aw && e.PedidoRef == pedidoRef && !e.Resuelto, ct);
        if (abierta is not null)
            return abierta;

        var nueva = ExcepcionImportacion.Crear(empresaId, OrigenPedido.Aw, pedidoRef, motivo, detalle);
        _db.ExcepcionesImportacion.Add(nueva);
        return nueva;
    }

    /// <summary>
    /// Al aplicar cualquier operación, las excepciones abiertas del pedido se
    /// resuelven solas (<c>ResueltoPor</c> null = sistema) — el operador no
    /// tiene que cerrar a mano lo que ya entró.
    /// </summary>
    private async Task ResolverExcepcionesAbiertasAsync(
        Guid empresaId, string pedidoRef, DateTimeOffset ahora, CancellationToken ct)
    {
        var abiertas = await _db.ExcepcionesImportacion
            .Where(e => e.EmpresaId == empresaId && e.Origen == OrigenPedido.Aw && e.PedidoRef == pedidoRef && !e.Resuelto)
            .ToListAsync(ct);
        foreach (var abierta in abiertas)
            abierta.Resolver(usuarioId: null, ahora);
    }

    private void UpsertControl(
        IngestaControl? control, Guid empresaId, SolicitudAw sol, string hash,
        EstadoIngesta estado, Guid? pedidoId, DateTimeOffset ahora)
    {
        if (control is null)
            _db.IngestaControles.Add(IngestaControl.Crear(empresaId, OrigenPedido.Aw, sol.NumeroPedido, hash, sol.Version, estado, pedidoId, ahora));
        else
            control.Aplicar(hash, sol.Version, estado, pedidoId, ahora);
    }

    private async Task<ProcesarSolicitudAwResponse> ResultadoAsync(
        SolicitudAw sol, Guid? pedidoId, string? estadoFacturacion,
        ResultadoSolicitudAw resultado, string? motivo, CancellationToken ct)
    {
        try
        {
            await _writeBack.EscribirResultadoAsync(
                new AwWriteBack(sol.SolicitudId, sol.NumeroPedido, pedidoId, estadoFacturacion, null, resultado, motivo), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ADR-0048 D3 (PR5): el write-back síncrono NO tumba la ingesta —
            // la aplicación ya quedó persistida (SaveChanges previo). Se
            // encola el reintento en ingesta_control (lo drena
            // WriteBackResultadoWorker). El adapter ya loggeó la causa.
            var control = _db.IngestaControles.Local
                    .FirstOrDefault(c => c.Origen == OrigenPedido.Aw && c.ClaveNatural == sol.NumeroPedido)
                ?? await _db.IngestaControles.FirstOrDefaultAsync(
                    c => c.Origen == OrigenPedido.Aw && c.ClaveNatural == sol.NumeroPedido, ct);
            if (control is not null)
            {
                control.SolicitarReintentoWriteBack(
                    sol.SolicitudId, resultado, motivo, estadoFacturacion, _clock.UtcNow);
                await _db.SaveChangesAsync(ct);
            }
            // Sin control (p.ej. Modificación huérfana rechazada): la fila de
            // la cola queda con resultado NULL y se relee en el siguiente
            // tick — la idempotencia re-emite el mismo resultado.
        }
        return new ProcesarSolicitudAwResponse(resultado, pedidoId, motivo);
    }

    private static string EstadoFacturacion(EstadoIngesta estado) => estado switch
    {
        EstadoIngesta.Facturado => "Facturado",
        EstadoIngesta.Cancelado => "Cancelado",
        _ => "SinFacturar",
    };

    private static string Hash(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
}
