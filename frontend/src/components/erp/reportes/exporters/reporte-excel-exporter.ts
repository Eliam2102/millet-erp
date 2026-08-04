/**
 * Exportador Excel cross-módulo para <c>&lt;ReporteShell&gt;</c>
 * (ADR-0036). Lazy-loaded igual que el de PDF: este módulo se importa
 * dinámicamente solo cuando el usuario hace click en "Descargar Excel"
 * (<c>exceljs</c> ~300KB gzipped).
 *
 * <para>Genera un workbook con una sola hoja, header con título +
 * filtros, tabla con tipos por columna (moneda como currency, número
 * como number, fecha como date) y footer con totales.</para>
 */
import ExcelJS from 'exceljs';

export interface ReporteParaExportar<TFila> {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: readonly {
    clave: string;
    etiqueta: string;
    tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
    alineacion?: string | null;
    anchoPx?: number | null;
  }[];
  filas: readonly TFila[];
  totales?: Record<string, number> | null;
}

/**
 * Genera un Blob XLSX y dispara la descarga en el browser.
 */
export async function descargarReporteExcel<TFila>(
  reporte: ReporteParaExportar<TFila>,
  nombreArchivo: string,
): Promise<void> {
  const wb = new ExcelJS.Workbook();
  wb.creator = 'Millet ERP';
  wb.created = new Date();
  const ws = wb.addWorksheet(truncarNombreHoja(reporte.titulo), {
    properties: { tabColor: { argb: 'FF1E40AF' } },
  });

  // ─── Header: título + filtros ──────────────────────────────────────
  const totalCols = reporte.columnas.length;
  ws.mergeCells(1, 1, 1, Math.max(totalCols, 1));
  const titulo = ws.getCell(1, 1);
  titulo.value = reporte.titulo;
  titulo.font = { bold: true, size: 14, color: { argb: 'FF18181B' } };
  titulo.alignment = { horizontal: 'left', vertical: 'middle' };

  ws.mergeCells(2, 1, 2, Math.max(totalCols, 1));
  const meta = ws.getCell(2, 1);
  meta.value = `Generado: ${new Date(reporte.generadoEn).toLocaleString('es-MX')}`;
  meta.font = { size: 9, color: { argb: 'FF71717A' } };

  const filtrosTexto = Object.entries(reporte.filtrosAplicados)
    .filter(([, v]) => v != null && v !== '')
    .map(([k, v]) => `${k}: ${v}`)
    .join(' · ');
  if (filtrosTexto) {
    ws.mergeCells(3, 1, 3, Math.max(totalCols, 1));
    const filtros = ws.getCell(3, 1);
    filtros.value = `Filtros: ${filtrosTexto}`;
    filtros.font = { size: 9, color: { argb: 'FF71717A' } };
  }

  const headerRowIndex = filtrosTexto ? 5 : 4;

  // ─── Columnas + estilos ────────────────────────────────────────────
  reporte.columnas.forEach((c, i) => {
    const col = ws.getColumn(i + 1);
    col.width = anchoColumna(c);
    col.numFmt = formatoNumerico(c.tipo);
  });

  // ─── Header de la tabla ────────────────────────────────────────────
  const headerRow = ws.getRow(headerRowIndex);
  reporte.columnas.forEach((c, i) => {
    const cell = headerRow.getCell(i + 1);
    cell.value = c.etiqueta;
    cell.font = { bold: true, color: { argb: 'FF18181B' } };
    cell.fill = {
      type: 'pattern',
      pattern: 'solid',
      fgColor: { argb: 'FFF4F4F5' },
    };
    cell.alignment = {
      horizontal: esNumerica(c.tipo) ? 'right' : 'left',
      vertical: 'middle',
    };
    cell.border = {
      top: { style: 'thin', color: { argb: 'FFD4D4D8' } },
      bottom: { style: 'thin', color: { argb: 'FFD4D4D8' } },
    };
  });

  // ─── Filas ─────────────────────────────────────────────────────────
  const obtenerCelda = (row: TFila, clave: string): unknown =>
    (row as unknown as Record<string, unknown>)[clave];

  reporte.filas.forEach((fila, i) => {
    const row = ws.getRow(headerRowIndex + 1 + i);
    reporte.columnas.forEach((c, j) => {
      const cell = row.getCell(j + 1);
      const valor = obtenerCelda(fila, c.clave);
      cell.value = valorParaExcel(valor, c.tipo);
      cell.alignment = {
        horizontal: esNumerica(c.tipo) ? 'right' : 'left',
      };
    });
  });

  // ─── Totales (opcional) ────────────────────────────────────────────
  if (reporte.totales && Object.keys(reporte.totales).length > 0) {
    const totRowIndex = headerRowIndex + 1 + reporte.filas.length;
    const totRow = ws.getRow(totRowIndex);
    reporte.columnas.forEach((c, j) => {
      const cell = totRow.getCell(j + 1);
      const total = reporte.totales?.[c.clave];
      if (j === 0 && total == null) {
        cell.value = 'Totales';
      } else if (total != null) {
        cell.value = valorParaExcel(total, c.tipo);
      }
      cell.font = { bold: true };
      cell.fill = {
        type: 'pattern',
        pattern: 'solid',
        fgColor: { argb: 'FFF4F4F5' },
      };
      cell.alignment = {
        horizontal: esNumerica(c.tipo) ? 'right' : 'left',
      };
      cell.border = {
        top: { style: 'thin', color: { argb: 'FFD4D4D8' } },
      };
    });
  }

  // ─── Freeze header + autofilter ────────────────────────────────────
  ws.views = [{ state: 'frozen', xSplit: 0, ySplit: headerRowIndex }];
  if (reporte.filas.length > 0) {
    ws.autoFilter = {
      from: { row: headerRowIndex, column: 1 },
      to: {
        row: headerRowIndex + reporte.filas.length,
        column: totalCols,
      },
    };
  }

  const buffer = await wb.xlsx.writeBuffer();
  const blob = new Blob([buffer], {
    type:
      'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = nombreArchivo.endsWith('.xlsx')
    ? nombreArchivo
    : `${nombreArchivo}.xlsx`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

// ─── Helpers ────────────────────────────────────────────────────────────────

function esNumerica(tipo: string): boolean {
  return tipo === 'numero' || tipo === 'moneda';
}

function anchoColumna(c: {
  tipo: string;
  anchoPx?: number | null;
}): number {
  if (c.anchoPx) return Math.max(8, Math.round(c.anchoPx / 7));
  switch (c.tipo) {
    case 'moneda':
      return 16;
    case 'numero':
      return 12;
    case 'fecha':
      return 12;
    default:
      return 24;
  }
}

function formatoNumerico(tipo: string): string {
  switch (tipo) {
    case 'moneda':
      return '"$"#,##0.00;[Red]-"$"#,##0.00';
    case 'numero':
      return '#,##0.####';
    case 'fecha':
      return 'yyyy-mm-dd';
    default:
      return 'General';
  }
}

function valorParaExcel(
  valor: unknown,
  tipo: string,
): string | number | Date | null {
  if (valor == null) return null;
  switch (tipo) {
    case 'moneda':
    case 'numero':
      return typeof valor === 'number' ? valor : Number(valor) || 0;
    case 'fecha':
      return typeof valor === 'string' ? new Date(valor) : String(valor);
    default:
      return String(valor);
  }
}

function truncarNombreHoja(titulo: string): string {
  // Excel: max 31 chars, no [ ] : * ? / \
  return titulo
    .replace(/[[\]:*?/\\]/g, '')
    .slice(0, 31)
    .trim() || 'Reporte';
}
