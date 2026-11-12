import type { ReporteParaExportar } from '@/components/erp/reportes/exporters/ReporteDocument';
import { resultadosConteo, type Conteo } from '../api/conservacion';

export function reporteConteo(c: Conteo, sucursal: string = c.sucursalId): ReporteParaExportar<Record<string, unknown>> {
  return {
    titulo: 'Inventario físico de activos fijos',
    generadoEn: new Date().toISOString(),
    filtrosAplicados: {
      Conteo: c.id,
      Sucursal: sucursal,
      Ubicación: c.ubicacion ?? 'Todas',
      Apertura: c.createdAt,
      Cierre: c.cerradoEn,
    },
    columnas: [
      { clave: 'numero', etiqueta: 'Activo', tipo: 'texto' },
      { clave: 'descripcionEsperada', etiqueta: 'Descripción', tipo: 'texto', anchoPdf: 2 },
      { clave: 'resultadoTexto', etiqueta: 'Resultado', tipo: 'texto' },
      { clave: 'ubicacionEsperada', etiqueta: 'Ubicación esperada', tipo: 'texto' },
      { clave: 'ubicacionReal', etiqueta: 'Ubicación real', tipo: 'texto' },
      { clave: 'evidencia', etiqueta: 'Evidencia', tipo: 'texto', anchoPdf: 2 },
      { clave: 'autorizacion', etiqueta: 'Autorización / propuesta', tipo: 'texto', anchoPdf: 2 },
    ],
    filas: c.partidas.map((p) => ({
      ...p,
      resultadoTexto: resultadosConteo[p.resultado],
      autorizacion: [p.autorizadoPor, p.evidenciaAutorizacion, p.propuesta]
        .filter(Boolean)
        .join(' · '),
    })),
  };
}
