using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.ComercioExterior.Application;
using Millet.ComercioExterior.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Infrastructure.PublicAdapters;
public sealed class CatalogosCeReadAdapter(CompartidoDbContext db, IdentidadDbContext identidad, IClock clock, IUsuarioSucursalReadPort sucursales) : ICatalogosCeReadPort
{
    public async Task<IReadOnlyList<ReferenciaCe>> ReferenciasAsync(string tipo, string q, CancellationToken ct)
    {
        if (tipo == "clientes")
            return await db.Clientes.AsNoTracking().Where(x => x.RazonSocial.Contains(q) || x.Clave.Contains(q)).OrderBy(x => x.RazonSocial).Take(50).Select(x => new ReferenciaCe(x.Id, x.Clave + " · " + x.RazonSocial)).ToListAsync(ct);
        if (tipo == "proveedores")
            return await db.Proveedores.AsNoTracking().Where(x => x.RazonSocial.Contains(q) || x.Clave.Contains(q)).OrderBy(x => x.RazonSocial).Take(50).Select(x => new ReferenciaCe(x.Id, x.Clave + " · " + x.RazonSocial)).ToListAsync(ct);
        throw new BusinessRuleException("CE_REFERENCIA_INVALIDA", "Tipo de referencia desconocido.");
    }

    public const string Entidad = "expediente_ce";
    public async Task<Guid> IncotermDdpAsync(CancellationToken ct) => await db.Incoterms.Where(i => i.Codigo == "DDP").Select(i => (Guid?)i.Id).SingleOrDefaultAsync(ct)
        ?? throw new BusinessRuleException("CE_INCOTERM_REQUERIDO", "No está configurado el Incoterm DDP.");
    public async Task ValidarAsync(Guid incoterm, string moneda, Guid sucursal, Guid responsable, CancellationToken ct)
    {
        if (!await db.Incoterms.AnyAsync(x => x.Id == incoterm && x.Estatus == Millet.Catalogos.Domain.EstatusCatalogo.Activo, ct))
            throw new BusinessRuleException("CE_INCOTERM_REQUERIDO", "Seleccione un Incoterm del catálogo.");
        if (!await db.Monedas.AnyAsync(x => x.Codigo == moneda && x.Activa, ct))
            throw new BusinessRuleException("CE_MONEDA_INVALIDA", "Seleccione una moneda ISO activa.");
        if (!await db.Sucursales.AnyAsync(x => x.Id == sucursal, ct))
            throw new BusinessRuleException("CE_SUCURSAL_INVALIDA", "Seleccione una sucursal de la empresa.");
        if (!await identidad.Usuarios.AnyAsync(x => x.Id == responsable && x.Activo && identidad.UsuarioEmpresaRoles.Any(r => r.UsuarioId == x.Id), ct))
            throw new BusinessRuleException("CE_RESPONSABLE_REQUERIDO", "Seleccione un responsable existente.");
    }

    public async Task ValidarResponsableAsync(Guid sucursal, Guid responsable, CancellationToken ct)
    {
        if (!await identidad.Usuarios.AnyAsync(x => x.Id == responsable && x.Activo && identidad.UsuarioEmpresaRoles.Any(r => r.UsuarioId == x.Id), ct)
            || !await sucursales.EstaAsociadoAsync(responsable, sucursal, ct))
            throw new BusinessRuleException("CE_RESPONSABLE_REQUERIDO", "Seleccione un usuario activo con acceso a la sucursal del expediente.");
    }

    public async Task ValidarParticipanteAsync(Guid? proveedor, Guid? cliente, CancellationToken ct)
    {
        if (proveedor is Guid p && !await db.Proveedores.AnyAsync(x => x.Id == p, ct) || cliente is Guid c && !await db.Clientes.AnyAsync(x => x.Id == c, ct))
            throw new BusinessRuleException("CE_PARTICIPANTE_INVALIDO", "La referencia de cliente o proveedor no existe.");
    }

    public async Task<IReadOnlyList<RequisitoDocumento>> RequisitosAsync(TipoExpediente tipo, CancellationToken ct)
    {
        var prefijo = tipo == TipoExpediente.Importacion ? "IMP_" : "EXP_";
        var tipos = await db.AdjuntoTiposDocumento.AsNoTracking().Where(x => x.TipoEntidad == Entidad && x.Activo && x.Codigo.StartsWith(prefijo)).OrderBy(x => x.Orden).ToListAsync(ct);
        if (tipos.Count == 0)
            throw new BusinessRuleException("CE_REQUISITOS_NO_CONFIGURADOS", "El catálogo de requisitos no está configurado para esta operación.");
        // PLATFORM-TODO(<CeRequisitoCondicional>): condicional fijo por clave hasta configurar la regla.
        return tipos.Select(x => new RequisitoDocumento(x.Id, x.Codigo, x.Nombre, x.Obligatorio, x.Codigo is "IMP_REGULATORIOS" or "EXP_ORIGEN" or "EXP_ISF" or "EXP_CARTA_PORTE")).ToArray();
    }

    public async Task<IReadOnlyList<Guid>> AdjuntosVigentesAsync(Guid expediente, CancellationToken ct)
    {
        var hoy = clock.HoyLocal();
        return await db.Adjuntos.Where(x => x.TipoEntidad == Entidad && x.EntidadId == expediente && x.BajaEn == null && (x.VigenteHasta == null || x.VigenteHasta >= hoy)).Select(x => x.Id).ToListAsync(ct);
    }

    public async Task ValidarAdjuntoAsync(Guid expediente, Guid tipo, Guid adjunto, CancellationToken ct)
    {
        if (!await db.Adjuntos.AnyAsync(x => x.Id == adjunto && x.TipoEntidad == Entidad && x.EntidadId == expediente && x.TipoDocumentoId == tipo && x.BajaEn == null, ct) || !(await AdjuntosVigentesAsync(expediente, ct)).Contains(adjunto))
            throw new BusinessRuleException("CE_ADJUNTO_INVALIDO", "El archivo debe estar vigente, ser de este tipo y pertenecer al expediente.");
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NombresAsync(IEnumerable<Guid> ids, CancellationToken ct) => await identidad.Usuarios.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Nombre, ct);
    public async Task<IReadOnlyDictionary<Guid, string>> SucursalesAsync(IEnumerable<Guid> ids, CancellationToken ct) => await db.Sucursales.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Nombre, ct);
    public async Task<IReadOnlyDictionary<Guid, string>> IncotermsAsync(IEnumerable<Guid> ids, CancellationToken ct) => await db.Incoterms.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Codigo, ct);
    public async Task<IReadOnlyDictionary<Guid, string>> NombresTercerosAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var proveedores = await db.Proveedores.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.RazonSocial, ct);
        var clientes = await db.Clientes.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.RazonSocial, ct);
        foreach (var cliente in clientes) proveedores.TryAdd(cliente.Key, cliente.Value);
        return proveedores;
    }
}
