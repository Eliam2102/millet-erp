using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
// EF traduce ToUpper a SQL; StringComparison y ToUpperInvariant no se traducen.
#pragma warning disable CA1304, CA1311, CA1862
namespace Millet.DatosMaestros.Application.ProductosAw;

public sealed record ReglaFiscalAwDto(Guid Id,string Campo,string Valor,string Descripcion,FichaFiscalProducto Ficha,bool Activa,int Version);
public sealed record ListarReglasFiscalesAwQuery : IRequest<IReadOnlyList<ReglaFiscalAwDto>>;
public sealed class ListarReglasFiscalesAwHandler(CompartidoDbContext db) : IRequestHandler<ListarReglasFiscalesAwQuery,IReadOnlyList<ReglaFiscalAwDto>>
{
    public async Task<IReadOnlyList<ReglaFiscalAwDto>> Handle(ListarReglasFiscalesAwQuery request,CancellationToken cancellationToken) =>
        await db.Set<ReglaFiscalAw>().AsNoTracking().OrderBy(r=>r.Campo).ThenBy(r=>r.Valor)
            .Select(r=>new ReglaFiscalAwDto(r.Id,r.Campo,r.Valor,r.Descripcion,r.Ficha,r.Activa,r.Version)).ToListAsync(cancellationToken);
}
public sealed record GuardarReglaFiscalAwCommand(Guid? Id,string Campo,string Valor,string Descripcion,FichaFiscalProducto Ficha,bool Activa,int? Version) : IRequest<Guid>;
public sealed class GuardarReglaFiscalAwValidator : AbstractValidator<GuardarReglaFiscalAwCommand>
{
    public GuardarReglaFiscalAwValidator()
    {
        RuleFor(r=>r.Campo).Must(c=>c is "Tipo" or "Grupo" or "Wgr").WithMessage("Elige Tipo, Grupo o Wgr.");
        RuleFor(r=>r.Valor).NotEmpty().MaximumLength(100); RuleFor(r=>r.Descripcion).NotNull().MaximumLength(500); RuleFor(r=>r.Ficha).NotNull();
    }
}
public sealed class GuardarReglaFiscalAwHandler(CompartidoDbContext db, ICurrentUserContext user) : IRequestHandler<GuardarReglaFiscalAwCommand,Guid>
{
    public async Task<Guid> Handle(GuardarReglaFiscalAwCommand request, CancellationToken cancellationToken)
    {
        request.Ficha.Validar(true);
        var valor = request.Valor.Trim();
        if (await db.Set<ReglaFiscalAw>().AnyAsync(r=>r.Id!=request.Id && r.Campo==request.Campo && r.Valor.ToUpper()==valor.ToUpper(),cancellationToken))
            throw new BusinessRuleException("REGLA_FISCAL_DUPLICADA","Ya existe una regla para ese campo y valor.");
        var regla = request.Id is {} id ? await db.Set<ReglaFiscalAw>().SingleOrDefaultAsync(r=>r.Id==id,cancellationToken)
            ?? throw new EntityNotFoundException("REGLA_FISCAL_NO_ENCONTRADA","No existe esa regla fiscal.")
            : new ReglaFiscalAw(Guid.CreateVersion7(),request.Campo,valor,request.Descripcion,request.Ficha);
        if (request.Id is not null && request.Version != regla.Version)
            throw new ConflictException("REGLA_FISCAL_CAMBIO","La regla cambió. Recarga antes de guardar.");
        var antes = JsonSerializer.Serialize(new {regla.Campo,regla.Valor,regla.Descripcion,regla.Ficha,regla.Activa});
        if (request.Id is not null && (request.Campo != regla.Campo || !string.Equals(valor,regla.Valor,StringComparison.OrdinalIgnoreCase)))
            throw new BusinessRuleException("REGLA_FISCAL_IDENTIDAD","El campo y valor identifican la familia. Crea otra regla para otra familia.");
        regla.Editar(request.Campo,valor,request.Descripcion,request.Ficha,request.Activa);
        if(request.Id is null) db.Add(regla);
        var despues = JsonSerializer.Serialize(new {regla.Campo,regla.Valor,regla.Descripcion,regla.Ficha,regla.Activa});
        if (request.Id is null || antes != despues)
        {
            db.Add(new CambioFiscalAw(Guid.CreateVersion7(),regla.Id,"Regla",request.Id is null ? "Crear regla" : "Editar regla",antes,despues,user.UserId));
            // Recalcular solo los herederos; una excepción nunca se sobrescribe. Baja no elimina la ficha ya aplicada.
            if (request.Activa)
            {
                var productos = await db.ProductosAw.Where(p => p.OrigenFiscal != OrigenFiscalProducto.ExcepcionProducto
                    && (p.ReglaFiscalId == regla.Id || p.ReglaFiscalId == null)).ToListAsync(cancellationToken);
                productos = productos.Where(p => p.ReglaFiscalId == regla.Id || regla.Coincide(p)).ToList();
                foreach(var p in productos)
                {
                    var previo = JsonSerializer.Serialize(p.FichaFiscal()); p.AplicarReglaFiscal(regla, soloVacios: p.ReglaFiscalId != regla.Id);
                    db.Add(new CambioFiscalAw(Guid.CreateVersion7(),p.Id,"Producto","Actualizar regla",previo,JsonSerializer.Serialize(p.FichaFiscal()),user.UserId));
                }
            }
        }
        await db.SaveChangesAsync(cancellationToken); return regla.Id;
    }
}
public sealed record AccionFiscalProductoCommand(Guid Id,string Accion,int Version,FichaFiscalProducto? Ficha = null) : IRequest;
public sealed class AccionFiscalProductoValidator : AbstractValidator<AccionFiscalProductoCommand>
{
    public AccionFiscalProductoValidator()
    { RuleFor(c=>c.Id).NotEmpty(); RuleFor(c=>c.Version).GreaterThan(0); RuleFor(c=>c.Accion).Must(a=>a is "Confirmar" or "Excepcion" or "VolverRegla").WithMessage("Elige confirmar, excepción o volver a la regla."); }
}
public sealed class AccionFiscalProductoHandler(CompartidoDbContext db,ICurrentUserContext user) : IRequestHandler<AccionFiscalProductoCommand>
{
    public async Task Handle(AccionFiscalProductoCommand request,CancellationToken cancellationToken)
    {
        var usuario = user.UserId ?? throw new ForbiddenException("USUARIO_REQUERIDO","Inicia sesión para modificar la ficha fiscal.");
        var p = await db.ProductosAw.SingleOrDefaultAsync(p=>p.Id==request.Id,cancellationToken)
            ?? throw new EntityNotFoundException("PRODUCTO_AW_NO_ENCONTRADO","No existe ese producto.");
        if(p.Version!=request.Version) throw new ConflictException("PRODUCTO_FISCAL_CAMBIO","El producto cambió. Recarga antes de guardar.");
        var antes = JsonSerializer.Serialize(new { Ficha=p.FichaFiscal(),p.OrigenFiscal,p.EstadoFiscal,p.ConfirmadoPor,p.ConfirmadoEn });
        switch(request.Accion)
        {
            case "Confirmar": p.ConfirmarFiscal(usuario,DateTimeOffset.UtcNow); break;
            case "Excepcion": p.CapturarExcepcionFiscal(request.Ficha ?? throw new BusinessRuleException("FICHA_REQUERIDA","Captura los datos de la excepción.")); break;
            case "VolverRegla":
                var reglas = await db.Set<ReglaFiscalAw>().Where(r=>r.Activa).ToListAsync(cancellationToken);
                var regla = reglas.Where(r=>r.Coincide(p)).OrderByDescending(r=>r.Campo=="Wgr"?3:r.Campo=="Grupo"?2:1).FirstOrDefault()
                    ?? throw new BusinessRuleException("PRODUCTO_SIN_REGLA","No existe una regla activa para la familia del producto. Captura una excepción o crea la regla.");
                p.AplicarReglaFiscal(regla); break;
        }
        db.Add(new CambioFiscalAw(Guid.CreateVersion7(),p.Id,"Producto",request.Accion,antes,
            JsonSerializer.Serialize(new { Ficha=p.FichaFiscal(),p.OrigenFiscal,p.EstadoFiscal,p.ConfirmadoPor,p.ConfirmadoEn }),usuario));
        await db.SaveChangesAsync(cancellationToken);
    }
}
public sealed record HistorialFiscalAwQuery(Guid Id,string TipoEntidad) : IRequest<IReadOnlyList<CambioFiscalAw>>;
public sealed class HistorialFiscalAwHandler(CompartidoDbContext db) : IRequestHandler<HistorialFiscalAwQuery,IReadOnlyList<CambioFiscalAw>>
{
    public async Task<IReadOnlyList<CambioFiscalAw>> Handle(HistorialFiscalAwQuery request,CancellationToken cancellationToken) =>
        await db.Set<CambioFiscalAw>().AsNoTracking().Where(c=>c.EntidadId==request.Id && c.TipoEntidad==request.TipoEntidad).OrderByDescending(c=>c.CreatedAt).ToListAsync(cancellationToken);
}
