import { pdf } from '@react-pdf/renderer';
import { BorradorCfdiDocument } from './BorradorCfdiDocument';
import type { BorradorCfdi } from '@/features/facturacion/components/vista-previa/modelo';

export async function descargarBorradorCfdi(datos: BorradorCfdi) {
  const blob = await pdf(<BorradorCfdiDocument datos={datos} />).toBlob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `borrador-${datos.tipo.toLowerCase()}.pdf`;
  document.body.appendChild(a);
  try { a.click(); } finally { a.remove(); URL.revokeObjectURL(url); }
}
