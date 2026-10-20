import { TabsEstado } from '@/components/erp/data/TabsEstado';

/** Adaptación visual de estados de dominio string al control numérico de F0. */
export function TabsContabilidad<T extends string>({
  estados,
  valor,
  onChange,
}: {
  estados: readonly { estado: T; label: string }[];
  valor: T | undefined;
  onChange: (estado: T | undefined) => void;
}) {
  const indice = estados.findIndex((e) => e.estado === valor);
  return (
    <TabsEstado
      opciones={estados.map((e, i) => ({ valor: i, label: e.label }))}
      valor={indice < 0 ? undefined : indice}
      conteos={undefined}
      onChange={(i) => onChange(i === undefined ? undefined : estados[i].estado)}
    />
  );
}
