import { Button } from '@/components/ui/button';

export default function Ejemplo() {
  return (
    <div className="flex flex-wrap gap-3">
      {(
        [
          'default',
          'secondary',
          'outline',
          'ghost',
          'secondary-danger',
          'destructive',
          'link',
        ] as const
      ).map((variant) => (
        <Button key={variant} variant={variant}>
          {variant}
        </Button>
      ))}
      <Button size="sm">Pequeño</Button>
      <Button size="lg">Formulario</Button>
      <Button size="icon" aria-label="Agregar ejemplo">
        +
      </Button>
      <Button disabled title="Selecciona una recepción para autorizar">
        Autorizar
      </Button>
    </div>
  );
}
