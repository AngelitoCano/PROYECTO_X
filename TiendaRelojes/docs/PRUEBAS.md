# Plan de pruebas manuales

## Preparación
Ejecutar `docker compose up --build -d`; comprobar `docker compose ps` y `docker compose logs --tail=100`.

## Casos
| ID | Prueba | Resultado esperado |
|---|---|---|
| P01 | Abrir tienda | Se muestran 4 relojes iniciales |
| P02 | Buscar «Rose» | Se filtra el catálogo |
| P03 | Añadir al carrito | El contador y total aumentan |
| P04 | Registrar pedido con stock | Estado pasa de pending-stock a awaiting-payment |
| P05 | Simular pago aprobado | Pedido paid y stock disminuye |
| P06 | Simular pago rechazado | Pedido payment-failed y se libera reserva |
| P07 | Intentar más unidades que stock | Pedido rechazado por inventario o cliente impide añadir |
| P08 | Ajustar stock desde administración | Cantidad cambia y se registra movimiento |
| P09 | Registrar un reloj | Nuevo reloj aparece en catálogo |
| P10 | Reiniciar contenedores sin -v | Los datos permanecen |

## Observaciones
Los casos son un **plan de verificación**, no resultados certificados. Requieren ejecución en Docker. Probar también mensajes duplicados, reinicios a mitad del flujo y cancelaciones: estas situaciones requieren mejoras antes de un uso real.
