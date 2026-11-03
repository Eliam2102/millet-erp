using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using static Millet.DatosMaestros.Application.Cargas.CargaMaestrosDatos;

namespace Millet.DatosMaestros.Application.Cargas;

public sealed class ClientesCarga(CompartidoDbContext db, ICurrentUserPermissions permisos) : IAdaptadorCargaMaestros
{
    public string Catalogo => "clientes";
    public DbContext Db => db;
    public IReadOnlyList<ColumnaCarga> Columnas { get; } = [
        new("Clave", "Obligatoria para alta; hasta 20 caracteres. Si falta, se usa Referencia externa."),
        new("Referencia externa", "Opcional. Sólo se asigna al crear; no modifica la correlación con A+W.", "ReferenciaExterna"),
        new("Razón social", "Obligatoria para alta; hasta 254 caracteres.", "RazonSocial"),
        new("RFC", "Opcional; 12 o 13 caracteres. Único salvo genéricos SAT."),
        new("Clasificación", "Obligatoria para alta: nombre o identificador de una clasificación activa.", "ClasificacionId"),
        new("Régimen fiscal", "Opcional; clave SAT c_RegimenFiscal de tres dígitos.", "RegimenFiscal"),
        new("Código postal fiscal", "Opcional; cinco dígitos.", "CodigoPostalFiscal"),
        new("Uso CFDI", "Opcional; clave SAT c_UsoCFDI.", "UsoCfdiDefault"),
        new("Forma de pago", "Opcional; clave SAT c_FormaPago de dos dígitos.", "FormaPagoDefault"),
        new("Método de pago", "Opcional; PUE o PPD.", "MetodoPagoDefault"),
        new("Moneda", "Opcional; ISO 4217. Para alta, MXN por omisión.", "MonedaDefault"),
        new("Email", "Opcional; correo válido."), new("Teléfono", "Opcional; hasta 50 caracteres.", "Telefono")];
    public IReadOnlyList<string> LlavesArchivo(FilaMaestros fila)
    {
        var clave = fila.Texto("Clave") ?? fila.Texto("Referencia externa"); var rfc = fila.Texto("RFC");
        return (clave is null ? Array.Empty<string>() : new[] { "clave:" + clave })
            .Concat(fila.Texto("Referencia externa") is { } referencia ? ["referencia:" + referencia] : [])
            .Concat(RfcCliente.EsLigable(rfc) ? ["rfc:" + rfc!.ToUpperInvariant()] : []).ToList();
    }
    public async Task<PlanMaestros> PrepararAsync(FilaMaestros fila, CancellationToken ct)
    {
        var f = fila;
        var clave = f.Texto("Clave"); var referencia = f.Texto("Referencia externa");
        if (clave is null && referencia is null) throw Error("Clave", "Capture la clave o la referencia externa.");
        var p = clave is not null ? await db.Clientes.AsNoTracking().SingleOrDefaultAsync(x => x.Clave == clave, ct)
            : await db.Clientes.AsNoTracking().SingleOrDefaultAsync(x => x.ReferenciaExterna == referencia, ct);
        var rfc = f.Texto("RFC")?.ToUpperInvariant();
        if (RfcCliente.EsLigable(rfc) && (await ClientesPorRfc.BuscarAsync(db, [rfc!], p?.Id, ct)).Count > 0)
            throw Error("RFC", "El RFC pertenece a otro cliente.");
        var clasificacionTexto = f.Texto("Clasificación");
        Guid? clasificacion = p?.ClasificacionId;
        if (clasificacionTexto is not null)
        {
            if (Guid.TryParse(clasificacionTexto, out var identificador)) clasificacion = identificador;
            else
            {
                var candidatas = await db.Set<ClasificacionCliente>().Where(x => x.Nombre == clasificacionTexto && x.Activa).Select(x => x.Id).Take(2).ToListAsync(ct);
                clasificacion = candidatas.Count == 1 ? candidatas[0] : throw Error("Clasificación", "Use el nombre de una clasificación activa o su identificador si hay varias con el mismo nombre.");
            }
        }
        await ClasificacionClienteGuard.ValidarAsync(db, clasificacion, p?.ClasificacionId, ct);
        var razon = f.Texto("Razón social"); var regimen = f.Texto("Régimen fiscal"); var cp = f.Texto("Código postal fiscal");
        var uso = f.Texto("Uso CFDI"); var forma = f.Texto("Forma de pago"); var metodo = f.Texto("Método de pago");
        var moneda = f.Texto("Moneda"); var email = f.Texto("Email"); var telefono = f.Texto("Teléfono");
        object comando = p is null ? new CrearClienteCommand(clave ?? referencia!, f.Requerido("Razón social"), referencia, rfc, regimen, cp, uso, forma, metodo, moneda ?? "MXN", false, email, telefono, ClasificacionId: clasificacion)
            : new ActualizarClienteCommand(p.Id, razon, rfc, regimen, cp, uso, forma, metodo, moneda, Email: email, Telefono: telefono, ClasificacionId: clasificacion);
        if (comando is ActualizarClienteCommand actualizar) await ActualizarClienteHandler.ValidarPermisoFiscalAsync(p!, actualizar, permisos, ct);
        if (p is null && referencia is not null && await db.Clientes.AnyAsync(x => x.ReferenciaExterna == referencia, ct))
            throw Error("Referencia externa", "La referencia externa ya pertenece a otro cliente.");
        return Plan(p, comando, new[] { "clave:" + (clave ?? referencia), p is null ? "nuevo:" + (clave ?? referencia) : "id:" + p.Id }
            .Concat(RfcCliente.EsLigable(rfc) ? ["rfc:" + rfc] : []),
            ("Razón social", p?.RazonSocial, razon), ("RFC", p?.Rfc, rfc), ("Clasificación", p?.ClasificacionId, clasificacion),
            ("Régimen fiscal", p?.RegimenFiscal, regimen), ("Código postal fiscal", p?.CodigoPostalFiscal, cp),
            ("Uso CFDI", p?.UsoCfdiDefault, uso), ("Forma de pago", p?.FormaPagoDefault, forma), ("Método de pago", p?.MetodoPagoDefault, metodo),
            ("Moneda", p?.MonedaDefault, moneda), ("Email", p?.Email, email), ("Teléfono", p?.Telefono, telefono));
    }
}
