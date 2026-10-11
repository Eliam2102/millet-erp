using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Application.Listar;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Inicio;

namespace Millet.Compras.Application.Inicio;

public sealed record ResumenInicioQuery(string Pieza, Guid? SucursalId = null)
    : IRequest<ResumenInicioResponse>, ISucursalSeleccionadaQuery
{
    public PeriodoInicio Periodo { get; init; } = PeriodoInicio.Mes;
    public string PermisoTodasSucursales => Pieza is "requisiciones" or "autorizar-rq"
        ? "compras.requisiciones.leer-todas-sucursales" : "compras.ordenes.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}
public sealed class ResumenInicioHandler(ComprasDbContext db, ICurrentUserContext user,
    ICurrentUserPermissions permisos, IClock clock, IMediator mediator) : IRequestHandler<ResumenInicioQuery, ResumenInicioResponse>
{
    public async Task<ResumenInicioResponse> Handle(ResumenInicioQuery request, CancellationToken cancellationToken)
    {
        var rq = db.Requisiciones.AsNoTracking()
            .Where(r => request.SucursalesPermitidas == null || (request.SucursalesPermitidas ?? Array.Empty<Guid>()).Contains(r.SucursalId));
        if (request.Pieza == "requisiciones")
        {
            var total = await rq.CountAsync(r => r.RequisitanteId == user.UserId &&
                (r.Estado == EstadoRequisicion.Borrador || r.Estado == EstadoRequisicion.EnAutorizacion ||
                 r.Estado == EstadoRequisicion.Autorizada || r.Estado == EstadoRequisicion.EnSurtido), cancellationToken);
            return new(total, total, total == 0 ? "Sin requisiciones propias abiertas" : $"{total} requisiciones propias abiertas");
        }
        if (request.Pieza == "autorizar-rq")
        {
            var n1 = await permisos.TieneAsync("compras.requisiciones.autorizar-nivel1", cancellationToken);
            var n2 = await permisos.TieneAsync("compras.requisiciones.autorizar-nivel2", cancellationToken);
            var pendientes = PendientesAutorizacionLectura.Requisiciones(rq, n1, n2);
            var corte = clock.UtcNow.AddHours(-48);
            var total = await pendientes.CountAsync(cancellationToken);
            var atrasadas = await pendientes.CountAsync(r => r.FechaSolicitud < corte, cancellationToken);
            return new(total, total, $"{total} requisiciones · {atrasadas} con más de 48 h", atrasadas);
        }
        var oc = db.OrdenesCompra.AsNoTracking()
            .Where(o => request.SucursalesPermitidas == null || (request.SucursalesPermitidas ?? Array.Empty<Guid>()).Contains(o.SucursalDestinoId));
        // Un lector operativo de Compras ve las órdenes a su cargo; la lectura corporativa conserva todas.
        var corporativo = await permisos.TieneAsync("compras.ordenes.leer-todas-sucursales", cancellationToken);
        if (!corporativo && request.Pieza is "circuito" or "recepciones") oc = oc.Where(o => o.CompradorTitularId == user.UserId);
        if (request.Pieza == "autorizar-oc")
        {
            var n1 = await permisos.TieneAsync("compras.ordenes.autorizar-nivel1", cancellationToken);
            var n2 = await permisos.TieneAsync("compras.ordenes.autorizar-nivel2", cancellationToken);
            var pendientes = PendientesAutorizacionLectura.Ordenes(oc, n1, n2);
            var corte = clock.UtcNow.AddHours(-48);
            var total = await pendientes.CountAsync(cancellationToken);
            var atrasadas = await pendientes.CountAsync(o => o.CreatedAt < corte, cancellationToken);
            return new(total, total, $"{total} órdenes · {atrasadas} con más de 48 h desde captura", atrasadas);
        }
        var hoy = FechasInicio.Hoy(clock);
        var desde = FechasInicio.Desde(request.Periodo, hoy);
        if (request.Pieza == "circuito") oc = oc.Where(o => o.FechaDocumento >= desde && o.FechaDocumento <= hoy);
        var abiertas = oc.Where(o => o.Estado == EstadoOrdenCompra.Autorizada);
        var recibir = abiertas.Where(o => o.Lineas.Any(l => !l.EsServicio && l.CantidadRecibida < l.Cantidad));
        var recepciones = await recibir.CountAsync(cancellationToken);
        var parciales = await recibir.CountAsync(o => o.SubEstadoRecepcion == SubEstadoRecepcion.Parcial, cancellationToken);
        if (request.Pieza == "recepciones") return new(recepciones, recepciones, $"{recepciones} órdenes propias por recibir · {parciales} parciales");
        var porAutorizar = await oc.CountAsync(o => o.Estado == EstadoOrdenCompra.EnAutorizacionJefeCompras || o.Estado == EstadoOrdenCompra.EnAutorizacionDireccion, cancellationToken);
        var totalAbiertas = await abiertas.CountAsync(cancellationToken);
        var porPagar = await abiertas.CountAsync(o => o.SubEstadoPago != SubEstadoPago.Pagada, cancellationToken);
        var etapas = new List<EtapaInicio>();
        if (await permisos.TieneAsync("compras.requisiciones.leer", cancellationToken))
        {
            // Su permiso y bypass se evalúan de nuevo en el pipeline del recurso RQ.
            var propias = await mediator.Send(new ResumenInicioQuery("requisiciones", request.SucursalId), cancellationToken);
            etapas.Add(new("Tus RQ", propias.Total));
        }
        etapas.AddRange([new("Por autorizar", porAutorizar), new("OC abiertas", totalAbiertas), new("Por recibir", recepciones), new("Por pagar", porPagar)]);
        return new(totalAbiertas, totalAbiertas, $"OC del {desde:dd/MM} al {hoy:dd/MM} · tus RQ abiertas · sin montos",
            Etapas: etapas);
    }
}

public sealed class ResumenInicioValidator : FluentValidation.AbstractValidator<ResumenInicioQuery>
{
    public ResumenInicioValidator()
    {
        RuleFor(request => request.Periodo).IsInEnum();
        RuleFor(request => request.SucursalId).NotEqual(Guid.Empty).When(request => request.SucursalId.HasValue);
        RuleFor(request => request.Pieza).Must(p => new[] { "requisiciones", "autorizar-rq", "autorizar-oc", "recepciones", "circuito" }.Contains(p));
    }
}
