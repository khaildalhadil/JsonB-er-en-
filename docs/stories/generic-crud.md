# Story: Generic CRUD layer for masters

> **Bilingual fields follow [ADR-0001: Bilingual text storage](../adr/0001-bilingual-storage.md).**
> Localised text is a `jsonb` column per field shaped `{"en":"...","ar":"..."}`,
> mapped via the reusable `LocalizedText` owned type.

## What the generic layer must do with localised text

Per ADR-0001 §"Generic CRUD exposure":

- **Create / Update** — the localised field is part of the entity graph; persist
  the whole row. No fan-out to a side table.
- **List + sort by current culture** — order by the culture-selected expression:

  ```csharp
  var ordered = culture == "ar"
      ? query.OrderBy(x => x.Name.Ar)   // ORDER BY name ->> 'ar'
      : query.OrderBy(x => x.Name.En);  // ORDER BY name ->> 'en'
  ```

  backed by the `((name ->> 'en'))` / `((name ->> 'ar'))` expression indexes.
- **Search** — `x.Name.Ar.Contains(term)` → `name ->> 'ar' ILIKE '%term%'`,
  served by the `pg_trgm` GIN index.

## Acceptance

- No per-entity translation code; the same repository/service handles all ~64
  masters' localised fields.
- Sorting and searching by localised name are index-backed (see ADR-0001).
