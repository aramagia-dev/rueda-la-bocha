# Ticket 2026-11-12-pago-sin-impacto — Pago Padi sin impacto + precio $1 + E2E real

- Rama: `ramiro/Padi` (sin tocar `develop`/`main`; sin commit/push automático).
- Aprobado en turno real: ticket (§719→§720 sí), plan (§725 presentado, §724 "apruebo el plan").
- Objetivo: un pago real en Padi (plata debitada) no creó la reserva. Diagnosticar causa raíz, corregir, bajar precio a $1 (rama demo) y probar con Playwright el pago real y la creación de la reserva.
- Alcance: WHEN pago Succeeded del enlace del hold THEN crear reserva Active (mismo enlace+importe+moneda); WHEN vencido/discrepante/ajeno THEN no crear + evidencia para conciliación; precio $1 reversible solo demo; E2E con cuenta de prueba provista (credenciales solo entorno local, nada al repo).
- Fuera de alcance: reembolsos, webhooks, precio permanente, dinero fuera de la cuenta de prueba.
- Status: implementado → review FAIL (H1–H4) → corregido → review PASS → QA APTO con pago real $1. Aceptación humana: PENDIENTE.
- Commits: sin commit (cambios en disco sin commitear, ver entrega). Servidor dev :5097 corriendo con binario nuevo + clave (PID 23764 al entregar).
- Transiciones: 2026-11-12 implement→review(FAIL H1-H4)→implement→review(PASS)→verify(QA APTO)→deliver (aceptación humana pendiente).
- Pendientes: aceptación humana; commit/push a ramiro/Padi (requiere orden explícita); `IsUniqueViolation` solo matchea "UNIQUE" (SQLite OK; en SQL Server un conflicto caería a 500 en vez de 409); `PadiPaymentId` solo visible en DB, no en UI (conciliación manual por DB).
