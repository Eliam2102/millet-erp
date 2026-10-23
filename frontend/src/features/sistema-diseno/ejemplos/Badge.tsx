import { Badge } from '@/components/ui/badge';

export default function Ejemplo() {
  return (
    <div className="flex flex-wrap gap-2">
      {(
        [
          'success',
          'warning',
          'danger',
          'info',
          'neutral',
          'default',
          'secondary',
          'destructive',
          'outline',
        ] as const
      ).map((variant) => (
        <Badge key={variant} variant={variant}>
          {variant}
        </Badge>
      ))}
    </div>
  );
}
