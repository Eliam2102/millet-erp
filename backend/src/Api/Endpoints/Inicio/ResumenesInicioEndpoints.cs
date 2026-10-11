using MediatR;
using Millet.Api.Auth;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Inicio;
using ComprasInicio = Millet.Compras.Application.Inicio;
using AlmacenInicio = Millet.Almacen.Application.Inicio;
using CxpInicio = Millet.CuentasPorPagar.Application.Inicio;
using CxcInicio = Millet.CuentasPorCobrar.Application.Inicio;
using TesoreriaInicio = Millet.Tesoreria.Application.Inicio;
using FacturacionInicio = Millet.Facturacion.Application.Inicio;
using ContabilidadInicio = Millet.Contabilidad.Application.Inicio;

namespace Millet.Api.Endpoints.Inicio;

/// <summary>El host conecta las consultas; cada módulo lee exclusivamente sus tablas o sus puertos.</summary>
public static class ResumenesInicioEndpoints
{
    public static IEndpointRouteBuilder MapResumenesInicioEndpoints(this IEndpointRouteBuilder app)
    {
        Map(app, "/compras/inicio/requisiciones", PermisosCanonicos.ComprasRequisicionesLeer, (id, periodo) => new ComprasInicio.ResumenInicioQuery("requisiciones", id));
        Autorizar(Map(app, "/compras/inicio/autorizar-rq", PermisosCanonicos.ComprasRequisicionesLeer, (id, periodo) => new ComprasInicio.ResumenInicioQuery("autorizar-rq", id)),
            PermisosCanonicos.ComprasRequisicionesAutorizarNivel1, PermisosCanonicos.ComprasRequisicionesAutorizarNivel2);
        Autorizar(Map(app, "/compras/inicio/autorizar-oc", PermisosCanonicos.ComprasOrdenesLeer, (id, periodo) => new ComprasInicio.ResumenInicioQuery("autorizar-oc", id)),
            PermisosCanonicos.ComprasOrdenesAutorizarNivel1, PermisosCanonicos.ComprasOrdenesAutorizarNivel2);
        Map(app, "/compras/inicio/recepciones", PermisosCanonicos.ComprasOrdenesLeer, (id, periodo) => new ComprasInicio.ResumenInicioQuery("recepciones", id));
        Map(app, "/compras/inicio/circuito", PermisosCanonicos.ComprasOrdenesLeer, (id, periodo) => new ComprasInicio.ResumenInicioQuery("circuito", id) { Periodo = periodo });
        Map(app, "/almacen/inicio/recepciones", PermisosCanonicos.AlmacenEntradasLeer, (id, periodo) => new AlmacenInicio.ResumenInicioQuery("recepciones", id));
        Map(app, "/almacen/inicio/conteos", PermisosCanonicos.AlmacenInventariosLeer, (id, periodo) => new AlmacenInicio.ResumenInicioQuery("conteos", id));
        Map(app, "/almacen/inicio/vales", PermisosCanonicos.AlmacenSalidasLeerTodas, (id, periodo) => new AlmacenInicio.ResumenInicioQuery("vales", id));
        Map(app, "/cuentas-por-pagar/inicio/facturas", PermisosCanonicos.CuentasPorPagarFacturasLeer, (id, periodo) => new CxpInicio.ResumenInicioQuery(id));
        Map(app, "/tesoreria/inicio/pagos", PermisosCanonicos.TesoreriaPasivosVer, (id, periodo) => new TesoreriaInicio.ResumenInicioQuery("pagos", id));
        Map(app, "/tesoreria/inicio/bancos", PermisosCanonicos.TesoreriaCuentasVer, (id, periodo) => new TesoreriaInicio.ResumenInicioQuery("bancos", id));
        Map(app, "/tesoreria/inicio/depositos", PermisosCanonicos.TesoreriaDepositosConfirmar, (id, periodo) => new TesoreriaInicio.ResumenInicioQuery("depositos", id));
        Map(app, "/tesoreria/inicio/flujo", PermisosCanonicos.TesoreriaMovimientosVer, (id, periodo) => new TesoreriaInicio.ResumenInicioQuery("flujo", id));
        Map(app, "/cuentas-por-cobrar/inicio/vencido", PermisosCanonicos.CuentasPorCobrarCarteraLeer, (id, periodo) => new CxcInicio.ResumenInicioQuery("vencido", id));
        Map(app, "/cuentas-por-cobrar/inicio/circuito", PermisosCanonicos.CuentasPorCobrarCarteraLeer, (id, periodo) => new CxcInicio.ResumenInicioQuery("circuito", id) { Periodo = periodo });
        Map(app, "/facturacion/inicio/pedidos", PermisosCanonicos.FacturacionFacturasLeer, (id, periodo) => new FacturacionInicio.ResumenInicioQuery("pedidos", id));
        Map(app, "/facturacion/inicio/timbrado", PermisosCanonicos.FacturacionFacturasLeer, (id, periodo) => new FacturacionInicio.ResumenInicioQuery("timbrado", id));
        Map(app, "/facturacion/inicio/circuito", PermisosCanonicos.FacturacionFacturasLeer, (id, periodo) => new FacturacionInicio.ResumenInicioQuery("circuito", id) { Periodo = periodo });
        Map(app, "/contabilidad/inicio/periodo", PermisosCanonicos.ContabilidadPeriodoLeer, (_, _) => new ContabilidadInicio.ResumenInicioQuery());
        return app;
    }

    private static RouteHandlerBuilder Map(IEndpointRouteBuilder app, string ruta, string permiso,
        Func<Guid?, PeriodoInicio, IRequest<ResumenInicioResponse>> query) => app.MapGet("/api/v1" + ruta,
        async (Guid? sucursalId, PeriodoInicio? periodo, IMediator mediator, CancellationToken ct) => Results.Ok(await mediator.Send(query(sucursalId, periodo ?? PeriodoInicio.Mes), ct)))
        .RequireAuthorization()
        .WithMetadata(new RequirePermissionAttribute(permiso))
        .WithName("Inicio_" + ruta.Trim('/').Replace('/', '_'))
        .WithSummary("Consulta un resumen del Inicio con el permiso y alcance del recurso.")
        .WithTags("Inicio")
        .Produces<ResumenInicioResponse>()
        .ProducesProblem(StatusCodes.Status403Forbidden);

    private static void Autorizar(RouteHandlerBuilder builder, string n1, string n2) => builder.AddEndpointFilter(async (context, next) =>
    {
        var permisos = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserPermissions>();
        var ct = context.HttpContext.RequestAborted;
        if (!await permisos.TieneAsync(n1, ct) && !await permisos.TieneAsync(n2, ct))
            throw new ForbiddenException("INICIO_AUTORIZACION_DENEGADA", "No tienes permiso para autorizar este recurso.");
        return await next(context);
    });
}
