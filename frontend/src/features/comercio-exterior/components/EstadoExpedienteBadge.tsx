import { Badge } from '@/components/ui/badge';
export function EstadoExpedienteBadge({ estado }: { estado: 1 | 2 }) {
  return (
    <Badge variant={estado === 1 ? 'info' : 'neutral'}>
      {estado === 1 ? 'Abierto' : 'Cancelado'}
    </Badge>
  );
}
