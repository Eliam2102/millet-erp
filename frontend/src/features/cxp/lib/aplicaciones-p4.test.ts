import { describe, expect, it } from 'vitest';
import { calcularAplicacion } from './aplicaciones-p4';
describe('aplicaciones P4', () => {
  it('muestra saldo antes/después de un anticipo', () => {
    expect(calcularAplicacion(50000,10000,10000,'MXN','MXN')).toEqual({ saldoAntes:50000,saldoDespues:40000,descuento:10000,error:null });
  });
  it('rechaza moneda distinta y montos inválidos', () => {
    expect(calcularAplicacion(100,100,10,'MXN','USD').error).toContain('moneda');
    for (const monto of [0,-1,NaN,Infinity]) expect(calcularAplicacion(100,100,monto,'MXN','MXN').error).toBeTruthy();
    expect(calcularAplicacion(100,50,60,'MXN','MXN').error).toContain('documento');
    expect(calcularAplicacion(50,100,60,'MXN','MXN').error).toContain('factura');
  });
  it('NC fiscal reconoce el cargo sin descontarlo dos veces', () => {
    expect(calcularAplicacion(40000,10000,10000,'MXN','MXN',10000)).toMatchObject({saldoDespues:40000,descuento:0,error:null});
    expect(calcularAplicacion(40000,15000,15000,'MXN','MXN',10000).saldoDespues).toBe(35000);
  });
});
