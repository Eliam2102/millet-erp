import type { ReactNode } from 'react';
import { Badge } from '@/components/ui/badge';
import { INDICADORES_UI } from '../lib/estados-ui';
export function IndicadorAlmacenBadge({
  estado,
  children,
}: {
  estado: keyof typeof INDICADORES_UI;
  children?: ReactNode;
}) {
  const info = INDICADORES_UI[estado];
  return <Badge variant={info.variant}>{children ?? info.texto}</Badge>;
}
