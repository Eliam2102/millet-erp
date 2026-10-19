import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';

const TODOS = '__todos__';

export interface TabEstadoOpcion {
  /** Valor numérico del enum de estado del listado. */
  valor: number;
  label: string;
}

interface TabsEstadoProps {
  /** Estados en el orden del flujo; «Todos» se antepone. */
  opciones: readonly TabEstadoOpcion[];
  /** Estado seleccionado (<c>undefined</c> = Todos). */
  valor: number | undefined;
  onChange: (estado: number | undefined) => void;
  /** Conteos por estado del backend; <c>undefined</c> mientras cargan. */
  conteos: Record<number, number> | undefined;
  'aria-label'?: string;
}

/**
 * <c>aria-controls</c> se anula: no hay panel por pestaña (la tabla de abajo se filtra),
 * y Radix apuntaría a un id inexistente.
 *
 * Tabs de vista por estado con conteo (plantilla 5.1). Los conteos vienen de
 * <c>GET …/conteos-por-estado</c>, con los demás filtros de la bandeja
 * aplicados, de modo que cada número coincide con lo que mostraría esa pestaña.
 */
export function TabsEstado({
  opciones,
  valor,
  onChange,
  conteos,
  'aria-label': ariaLabel = 'Filtrar por estado',
}: TabsEstadoProps) {
  const todos = conteos ? opciones.reduce((s, o) => s + (conteos[o.valor] ?? 0), 0) : undefined;
  return (
    <Tabs
      value={valor === undefined ? TODOS : String(valor)}
      onValueChange={(v) => onChange(v === TODOS ? undefined : Number(v))}
    >
      <TabsList aria-label={ariaLabel} className="gap-3 px-1">
        <TabsTrigger value={TODOS} count={todos} aria-controls={undefined}>
          Todos
        </TabsTrigger>
        {opciones.map((o) => (
          <TabsTrigger
            key={o.valor}
            value={String(o.valor)}
            count={conteos ? (conteos[o.valor] ?? 0) : undefined}
            aria-controls={undefined}
          >
            {o.label}
          </TabsTrigger>
        ))}
      </TabsList>
    </Tabs>
  );
}
