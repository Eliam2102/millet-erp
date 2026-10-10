using System.Globalization;
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

public sealed record ImportarFiscalAwCommand(string Csv) : IRequest<ImportarFiscalAwResultado>;
public sealed record ErrorImportacionFiscal(int Fila,string Mensaje);
public sealed record ImportarFiscalAwResultado(int Aplicadas,int SinCambios,IReadOnlyList<ErrorImportacionFiscal> Errores);
public sealed class ImportarFiscalAwValidator : AbstractValidator<ImportarFiscalAwCommand>
{
    public ImportarFiscalAwValidator() { RuleFor(c=>c.Csv).NotEmpty().MaximumLength(2_000_000); }
}
public static class CsvFiscalAw
{
    // RFC 4180: comillas dobles, comas y saltos de línea dentro de campos.
    public static IReadOnlyList<string[]> Leer(string csv)
    {
        var filas = new List<string[]>(); var fila = new List<string>(); var campo = new System.Text.StringBuilder(); var comillas = false;
        for(var i=0;i<csv.Length;i++)
        {
            var c=csv[i];
            if(c=='"') { if(comillas && i+1<csv.Length && csv[i+1]=='"') { campo.Append('"'); i++; } else comillas=!comillas; }
            else if(!comillas && c==',') { fila.Add(campo.ToString()); campo.Clear(); }
            else if(!comillas && c is '\r' or '\n')
            {
                if(c=='\r' && i+1<csv.Length && csv[i+1]=='\n') i++;
                fila.Add(campo.ToString()); campo.Clear(); if(fila.Any(v=>v.Length>0)) filas.Add(fila.ToArray()); fila.Clear();
            }
            else campo.Append(c);
        }
        if(comillas) throw new BusinessRuleException("CSV_INVALIDO","El CSV tiene un campo con comillas sin cerrar.");
        if(campo.Length>0 || fila.Count>0) { fila.Add(campo.ToString()); filas.Add(fila.ToArray()); }
        return filas;
    }
}
public sealed class ImportarFiscalAwHandler(CompartidoDbContext db,ISender sender) : IRequestHandler<ImportarFiscalAwCommand,ImportarFiscalAwResultado>
{
    public async Task<ImportarFiscalAwResultado> Handle(ImportarFiscalAwCommand request,CancellationToken cancellationToken)
    {
        var filas=CsvFiscalAw.Leer(request.Csv); var errores=new List<ErrorImportacionFiscal>(); var aplicadas=0; var iguales=0;
        var encabezados=new[]{"clase","campo","valor","descripcion","claveProdServSat","claveUnidadSat","objetoImp","tasaIvaTraslado","tasaRetencionIva","tasaRetencionIsr"};
        if(filas.Count==0 || !filas[0].Select(s=>s.Trim().TrimStart('\uFEFF')).SequenceEqual(encabezados))
            throw new BusinessRuleException("CSV_ENCABEZADO_INVALIDO","Usa el encabezado: "+string.Join(',',encabezados));
        for(var i=1;i<filas.Count;i++)
        {
            // Una transacción por fila; fallos no dejan cambios parcialmente aplicados ni entidades rastreadas.
            await using var tx=await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var f=filas[i].Select(v => v.Trim()).ToArray(); if(f.Length!=10) throw new BusinessRuleException("CSV_COLUMNAS","La fila debe tener diez columnas.");
                decimal? Tasa(string v) => string.IsNullOrWhiteSpace(v) ? null : decimal.Parse(v,CultureInfo.InvariantCulture);
                var ficha=new FichaFiscalProducto(f[4].Trim(),f[5].Trim(),f[6].Trim(),Tasa(f[7]),Tasa(f[8]),Tasa(f[9])); ficha.Validar(true);
                if(f[0]=="Regla")
                {
                    var campo = f[1]; var valor = f[2].ToUpper();
                    var r=await db.Set<ReglaFiscalAw>().SingleOrDefaultAsync(r=>r.Campo==campo && r.Valor.ToUpper()==valor,cancellationToken);
                    if(r is not null && r.Ficha==ficha && r.Descripcion==f[3] && r.Activa) iguales++;
                    else { await sender.Send(new GuardarReglaFiscalAwCommand(r?.Id,f[1],f[2],f[3],ficha,true,r?.Version),cancellationToken); aplicadas++; }
                }
                else if(f[0]=="Excepcion")
                {
                    var p=await db.ProductosAw.SingleOrDefaultAsync(p=>p.ReferenciaExterna==f[2],cancellationToken)
                        ?? throw new BusinessRuleException("PRODUCTO_AW_NO_ENCONTRADO","No existe el producto con referencia "+f[2]);
                    if(p.OrigenFiscal==OrigenFiscalProducto.ExcepcionProducto && p.FichaFiscal()==ficha) iguales++;
                    else { await sender.Send(new AccionFiscalProductoCommand(p.Id,"Excepcion",p.Version,ficha),cancellationToken); aplicadas++; }
                }
                else throw new BusinessRuleException("CSV_CLASE","La clase debe ser Regla o Excepcion.");
                await tx.CommitAsync(cancellationToken);
            }
            catch(Exception e) when(e is BusinessRuleException or FormatException or OverflowException or DbUpdateException or Millet.SharedKernel.Application.Exceptions.ValidationException)
            { await tx.RollbackAsync(cancellationToken); db.ChangeTracker.Clear(); errores.Add(new(i+1,e is DbUpdateException ? "La fila entró en conflicto con otro cambio. Revisa el dato y reintenta." : e.Message)); }
        }
        return new(aplicadas,iguales,errores);
    }
}
