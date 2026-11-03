using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.DatosMaestros.Domain;
using static Millet.DatosMaestros.Application.Cargas.CargaMaestrosDatos;

namespace Millet.DatosMaestros.Application.Cargas;

public sealed class ProductosAwFiscalCarga(CompartidoDbContext db) : IAdaptadorCargaMaestros
{
    public string Catalogo => "productos-aw-fiscal";
    public DbContext Db => db;
    public IReadOnlyList<ColumnaCarga> Columnas { get; } = [
        new("Clase", "Regla o Excepcion; vacío equivale a Excepcion.", "clase"),
        new("Campo", "Para Regla: Tipo, Grupo o Wgr.", "campo"),
        new("Referencia externa", "Referencia de producto existente; para Regla, valor de la familia.", "valor", "ReferenciaExterna"),
        new("Descripción", "Opcional; descripción de la regla.", "descripcion"),
        new("Clave de producto SAT", "Clave SAT c_ClaveProdServ de ocho dígitos. Obligatoria si no existe ficha previa.", "claveProdServSat"),
        new("Clave de unidad SAT", "Clave SAT c_ClaveUnidad. Obligatoria si no existe ficha previa.", "claveUnidadSat"),
        new("Objeto de impuesto", "Clave SAT c_ObjetoImp: 01 a 08.", "objetoImp"),
        new("Tasa IVA", "Decimal entre 0 y 1; por ejemplo 0.16.", "tasaIvaTraslado"),
        new("Retención IVA", "Decimal entre 0 y 1.", "tasaRetencionIva"),
        new("Retención ISR", "Decimal entre 0 y 1.", "tasaRetencionIsr")];
    public IReadOnlyList<string> LlavesArchivo(FilaMaestros fila)
    {
        return fila.Texto("Referencia externa") is { } referencia
            ? [(fila.Texto("Clase") ?? "Excepcion") + ":" + fila.Texto("Campo") + ":" + referencia] : [];
    }
    public async Task<PlanMaestros> PrepararAsync(FilaMaestros fila, CancellationToken ct)
    {
        var f = fila;
        var referencia = f.Requerido("Referencia externa"); var clase = f.Texto("Clase") ?? "Excepcion";
        var campo = f.Texto("Campo"); var descripcion = f.Texto("Descripción");
        var producto = clase == "Excepcion" ? await db.ProductosAw.AsNoTracking().SingleOrDefaultAsync(x => x.ReferenciaExterna == referencia, ct) : null;
        var reglas = clase == "Regla" ? await db.Set<ReglaFiscalAw>().AsNoTracking().Where(x => x.Campo == campo).ToListAsync(ct) : [];
        var regla = reglas.SingleOrDefault(x => string.Equals(x.Valor, referencia, StringComparison.OrdinalIgnoreCase));
        if (clase == "Excepcion" && producto is null) throw Error("Referencia externa", "No existe ese producto. Sincronícelo antes de completar su ficha fiscal.");
        if (clase is not "Regla" and not "Excepcion") throw Error("Clase", "Use Regla o Excepcion.");
        var anterior = producto?.FichaFiscal() ?? regla?.Ficha;
        decimal? Tasa(string columna, decimal? actual) => f.Texto(columna) is { } v ? decimal.TryParse(v, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var tasa) ? tasa : throw Error(columna, "Use una tasa decimal como 0.16.") : actual;
        var ficha = new FichaFiscalProducto(f.Texto("Clave de producto SAT") ?? anterior?.ClaveProdServSat ?? "", f.Texto("Clave de unidad SAT") ?? anterior?.ClaveUnidadSat ?? "", f.Texto("Objeto de impuesto") ?? anterior?.ObjetoImp ?? "", Tasa("Tasa IVA", anterior?.TasaIvaTraslado), Tasa("Retención IVA", anterior?.TasaRetencionIva), Tasa("Retención ISR", anterior?.TasaRetencionIsr));
        if (ficha.CampoInvalido(true) is { } columna) throw Error(columna, "Revise este campo: " + Columnas.Single(c => c.Nombre == columna).Ayuda);
        ficha.Validar(true);
        object comando = producto is not null ? new AccionFiscalProductoCommand(producto.Id, "Excepcion", producto.Version, ficha)
            : new GuardarReglaFiscalAwCommand(regla?.Id, f.Requerido("Campo"), referencia, descripcion ?? regla?.Descripcion ?? "", ficha, true, regla?.Version);
        return Plan(producto as object ?? regla, comando, [clase + ":" + campo + ":" + referencia],
            ("Clave de producto SAT", anterior?.ClaveProdServSat, ficha.ClaveProdServSat), ("Clave de unidad SAT", anterior?.ClaveUnidadSat, ficha.ClaveUnidadSat),
            ("Objeto de impuesto", anterior?.ObjetoImp, ficha.ObjetoImp), ("Tasa IVA", anterior?.TasaIvaTraslado, ficha.TasaIvaTraslado),
            ("Retención IVA", anterior?.TasaRetencionIva, ficha.TasaRetencionIva), ("Retención ISR", anterior?.TasaRetencionIsr, ficha.TasaRetencionIsr),
            ("Descripción", regla?.Descripcion, clase == "Regla" ? descripcion : null),
            ("Origen fiscal", producto?.OrigenFiscal, producto is not null ? OrigenFiscalProducto.ExcepcionProducto : null),
            ("Activa", regla?.Activa, clase == "Regla" ? true : null));
    }
}
