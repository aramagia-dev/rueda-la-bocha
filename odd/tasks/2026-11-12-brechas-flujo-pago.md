# Ticket 2026-11-12-brechas-flujo-pago — B1/B2/B3 + auditoría flujo real

- Rama: `ramiro/Padi` (sin tocar `develop`/`main`; sin commit/push automático).
- Aprobado en turno real: ticket (§754→§755 sí), plan (§759→§760 sí).
- Alcance: B1 reserva pagada que re-ofrece pago; B2 Mis reservas sin estados ni filtros; B3 Confirmar crea sin pagar; B0 auditoría propuesto vs real (doble modelo Bookings directo vs Holds + PadiPayments vieja).
- Status: implementado → review PASS → QA APTO. Aceptación humana: PENDIENTE.
- Transiciones: 2026-11-12 implement→review(PASS)→verify(QA APTO)→deliver (aceptación humana pendiente).
- Pendientes: aceptación humana; commit/push a ramiro/Padi (orden explícita); ruido 500 en tabs viejas verificado como proceso anterior (log actual limpio); sin `aria-current` en nav de filtro (mejora menor).
- B4 (2026-11-12): holds Pending vigentes visibles en Mine con Retomar pago + filtro Pendientes — implementado → review PASS → QA APTO. Aceptación humana: PENDIENTE.
- Dato verificado 2026-11-12: booking #10 figura Cancelled por acción del usuario (7 cancelaciones manuales 01:36:04–01:36:20 UTC incl. #5,#6,#8,#9,#10,#11,#12; luego pagó #13 y #14 que siguen Active). No es defecto ni regresión: CancelledAtUtc consta en DB y ningún código cancela solo.
