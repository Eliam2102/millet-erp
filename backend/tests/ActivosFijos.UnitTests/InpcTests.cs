using ClosedXML.Excel;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
namespace Millet.ActivosFijos.UnitTests;
public sealed class InpcTests
{
    [Theory] [InlineData(0,1,100)] [InlineData(2026,13,100)] [InlineData(2026,1,0)] [InlineData(2026,1,-1)]
    public void Rechaza_indices_invalidos(int anio,int mes,decimal valor) => Assert.Throws<BusinessRuleException>(() => new InpcMensual(Guid.NewGuid(),anio,mes,valor));
    [Fact] public void Excel_dos_columnas_ficticias_sin_formulas()
    {
        static byte[] Archivo(bool formula = false, bool mesInvalido = false)
        {
            using var libro = new XLWorkbook(); var hoja = libro.AddWorksheet("FICTICIO");
            hoja.Cell(1,1).Value = "AAAA-MM"; hoja.Cell(1,2).Value = "valor";
            hoja.Cell(2,1).Value = mesInvalido ? "01/2026" : "2026-01"; hoja.Cell(2,2).Value = 100.123456;
            if (formula) hoja.Cell(2,2).FormulaA1 = "100+3";
            using var stream = new MemoryStream(); libro.SaveAs(stream); return stream.ToArray();
        }
        Assert.Equal(new CapturaInpc(2026,1,100.123456m), FiscalServicio.LeerExcel(Archivo()).Single());
        Assert.Throws<BusinessRuleException>(() => FiscalServicio.LeerExcel(Archivo(formula: true)));
        Assert.Throws<BusinessRuleException>(() => FiscalServicio.LeerExcel(Archivo(mesInvalido: true)));
    }
    [Fact] public void Migracion_copia_tasa_previa_y_solo_actualiza_no_confirmadas()
    {
        var migracion = new Millet.ActivosFijos.Infrastructure.Persistence.Migrations.P12TasasFiscalInpc();
        Assert.DoesNotContain(migracion.UpOperations, o => o is UpdateDataOperation or DeleteDataOperation or DropTableOperation);
        var sql = migracion.UpOperations.OfType<SqlOperation>().ToList();
        Assert.Contains("tasa_contable_anual = tasa_fiscal_anual", sql[0].Sql);
        Assert.Contains("c.tasa_confirmada = false", sql[1].Sql);
        Assert.Contains("('156', 33, 33, true)", sql[1].Sql);
    }
}
