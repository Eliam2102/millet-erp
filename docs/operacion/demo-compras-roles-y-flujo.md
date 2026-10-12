# Demo de Compras: roles y flujo

> **Reemplazado.** Este documento tenía errores (permisos faltantes en los roles
> de Compras y una regla de firmas equivocada). La versión correcta y completa,
> que cubre Maestros, Compras, Almacén y Cuentas por Pagar con Tesorería, está en
> [`demo-geovany-usuarios-roles-y-datos.md`](./demo-geovany-usuarios-roles-y-datos.md).

Flujo de Compras (resumen, detalle en el documento nuevo):

1. Geovany crea la requisición en `/compras/requisiciones`.
2. fio (N1) y Mariana (N2) la autorizan en `/compras/pendientes`.
3. Geovany crea la OC desde la requisición en `/compras/ordenes`.
4. Se adjunta la cotización y se envía a autorización.
5. fio aprueba Nivel 1 y Mariana Nivel 2 en `/compras/ordenes/pendientes-autorizacion`.
   Quien crea la OC no puede firmarla, y N1 y N2 deben ser personas distintas.
6. Almacén registra la recepción parcial en `/almacen/recepciones`.
7. En el detalle de la OC, las columnas **Recibido** y **Por surtir** se actualizan.
