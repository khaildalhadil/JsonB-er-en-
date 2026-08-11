# Story: Angular dynamic form — bilingual field pair

> **The bilingual JSON contract is defined in [ADR-0001: Bilingual text storage](../adr/0001-bilingual-storage.md).**
> Agreed with the Web team in this story — not deferred.

## The contract

Every translatable field binds to a single object with **both keys always
present** (empty string = untranslated):

```json
{ "en": "English value", "ar": "القيمة بالعربية" }
```

- Keys are lowercase ISO-639-1: `en`, `ar`.
- The API always returns both keys, so the form never branches on a missing key.

## Form rendering

For each translatable field the dynamic form renders **one bilingual pair** —
two inputs (EN / AR) bound to `field.en` and `field.ar`:

```ts
interface LocalizedText { en: string; ar: string; }
```

- The AR input is RTL; the EN input is LTR.
- On submit, the pair is sent as the object above — the same shape the backend
  stores in the `jsonb` column.

## Acceptance

- The bilingual field pair reads and writes exactly `{ "en": ..., "ar": ... }`.
- Shape matches the DB/API contract in ADR-0001 (no client-side remapping).
