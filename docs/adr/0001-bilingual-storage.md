# ADR-0001: Bilingual (English/Arabic) text storage

- **Status:** Accepted
- **Date:** 2026-08-11
- **Deciders:** Backend team (with Web team sign-off on the JSON contract)
- **Supersedes:** —
- **Referenced by:** [Generic CRUD story](../stories/generic-crud.md), [Dynamic form story](../stories/dynamic-form.md)

## Context

Nearly every master in HealthIt has user-facing text (`name`, `description`, …)
that must exist in both **English** and **Arabic**. There are ~64 masters, and
the choice of how translations are stored is a cross-cutting concern:

- It is baked into every entity's schema and every migration.
- It shapes the **generic CRUD layer** (read/sort/filter/write once, reuse for
  all masters).
- It defines the **contract with the Angular dynamic form**, which renders one
  bilingual field pair per translatable field.

Retrofitting a different approach later means touching all ~64 masters, their
migrations, the generic repository, and the form contract **simultaneously**.
So the decision must be made **before the first entity is written**.

### Assumptions (explicit)

- **Database:** PostgreSQL (the `jsonb` option is Postgres-specific).
- **ORM:** EF Core (Npgsql provider).
- **Languages:** a **fixed, small set** — `en` and `ar`. Adding a third
  language is not a near-term requirement. This assumption is load-bearing; if
  languages become open-ended/tenant-configurable, revisit §"Consequences".

## Decision drivers

1. **Querying & sorting by localised name** — masters are listed, searched, and
   sorted by name in the user's current culture.
2. **Indexing** — sort/search must be index-backed, not sequential scans, at
   64-master scale.
3. **EF Core mapping ergonomics** — the mapping must be clean enough to apply
   uniformly across ~64 entities.
4. **Generic CRUD exposure** — one repository/service must read, write, sort,
   and filter localised text for *any* master without per-entity code.
5. **A single, agreed JSON shape** the Angular form can bind to.

## Considered options

### Option A — Side translation table

A single polymorphic table keyed by entity + field + culture:

```
Translations(EntityType text, EntityId uuid, Field text, Culture text, Value text)
PRIMARY KEY (EntityType, EntityId, Field, Culture)
```

The master row holds no text; every localised string lives as one row here.

### Option B — `jsonb` column per translatable field  ✅ chosen

Each translatable field is its own `jsonb` column on the master, holding both
cultures:

```
name jsonb  -->  {"en": "Diabetes", "ar": "السكري"}
```

## Decision outcome

**Chosen: Option B — one `jsonb` column per translatable field**, with the exact
shape `{"en": "...", "ar": "..."}`, and **B-tree expression indexes** on
`(name ->> 'en')` and `(name ->> 'ar')` for fields that are sorted/searched.

### Why (per driver)

| Driver | Option A — side table | Option B — jsonb per field ✅ |
|---|---|---|
| **Query/sort by localised name** | Requires a join to `Translations`, filtered by `EntityType`, `Field`, `Culture`, then order by `Value`. Every localised list query is a join; sorting/paging across the join is awkward and easy to get wrong. | No join — the value is on the row. Sort with `ORDER BY name ->> 'ar'`; filter with `name ->> 'en' ILIKE …`. Culture is a runtime key: `name ->> @culture`. |
| **Indexing** | Composite B-tree on `(EntityType, EntityId, Field, Culture)` serves lookups but **not** ordering master lists by localised value (the value column isn't leading, and it's shared across all entity types). | Targeted expression indexes per sorted field/culture: `((name ->> 'en'))`, `((name ->> 'ar'))`. Substring search via `pg_trgm` GIN on `(name ->> 'ar')`. A whole-column `GIN` index only helps `@>` containment — **not** ordering — so it is *not* the primary index here. |
| **EF Core mapping** | No real FK/navigation — `EntityId` is a soft, untyped reference EF Core can't model as a relationship. You end up with manual joins or a bespoke translation service. Fights the ORM. | Maps as an **owned type serialized to JSON** (`OwnsOne(... ).ToJson()`) or a value converter to `jsonb`. One reusable `LocalizedText` type applied to ~64 entities. EF translates `x.Name.Ar` to jsonb access. |
| **Generic CRUD** | Every write fans out to N rows in `Translations`; reads must re-assemble. The generic layer must special-case the translation table on create/update/delete/list. | The localised field is just another column/value on the entity. Generic create/update writes the whole row; generic list sorts by `name ->> @culture`. Minimal special-casing. |
| **Referential integrity** | No FK from `Translations` back to the master (polymorphic `EntityId`); orphaned rows are possible and must be policed in app code. | Translation is part of the row — it cannot be orphaned; it is deleted/updated atomically with its owner. |
| **Adding a language** | Insert rows; no schema change. **Advantage of A.** | A data migration to backfill the new key across rows. Acceptable given the fixed `en`/`ar` assumption; a cost, not a blocker. |

Option B wins on 4 of 5 decision drivers. Option A's only real advantage —
adding languages without a schema change — is not a driver we are optimising
for (languages are fixed at `en`/`ar`).

## Implementation notes (the exact contract)

### Column type

`jsonb` (not `json`, not `hstore`) — `jsonb` is binary, deduplicated, supports
the `->>` operator and GIN/expression indexes we rely on.

### JSON shape — agreed with the Web team **in this story**

```json
{ "en": "English value", "ar": "القيمة بالعربية" }
```

- Keys are **lowercase ISO-639-1** culture codes: `en`, `ar`.
- Both keys are **always present**; an empty string (`""`) means "not yet
  translated". The API never returns a missing key, so the form always has both
  inputs to bind.
- This is the shape the **Angular dynamic form** binds its bilingual field pair
  to, and the shape the API accepts on write. The form renders two inputs
  (EN / AR) that read/write `field.en` and `field.ar`.

### EF Core mapping (one reusable value object)

```csharp
public sealed class LocalizedText
{
    public string En { get; set; } = "";
    public string Ar { get; set; } = "";
}

// In each entity's IEntityTypeConfiguration:
builder.OwnsOne(e => e.Name, nav =>
{
    nav.ToJson();                                   // -> jsonb column with Npgsql
    nav.Property(p => p.En).HasJsonPropertyName("en");
    nav.Property(p => p.Ar).HasJsonPropertyName("ar");
});
```

`HasJsonPropertyName` pins the JSON keys to lowercase `en`/`ar` so the stored
shape, the `->>` index expressions, and the Angular contract all agree.

> For a truly dynamic N-language layer you could instead map
> `Dictionary<string,string>` to `jsonb` (Npgsql-native) and access
> `x.Name[culture]`. We reject that here in favour of the strongly-typed
> `LocalizedText` because languages are fixed and the value object gives the
> generic layer and the form a stable contract. Revisit only if the fixed-language
> assumption breaks.

### Index strategy

Create expression indexes **only on fields that are sorted or searched**
(typically `name`), per culture:

```sql
-- Sorting / equality by localised name
CREATE INDEX ix_country_name_en ON masters.country ((name ->> 'en'));
CREATE INDEX ix_country_name_ar ON masters.country ((name ->> 'ar'));

-- Substring search (ILIKE '%term%') — trigram GIN on the extracted text
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX ix_country_name_ar_trgm
  ON masters.country USING gin ((name ->> 'ar') gin_trgm_ops);
```

- **Arabic collation:** for correct alphabetical ordering use an ICU collation,
  and build the index with the same collation so the sort is index-backed:
  `... ((name ->> 'ar') COLLATE "ar-x-icu")` and
  `ORDER BY name ->> 'ar' COLLATE "ar-x-icu"`.
- A whole-column `GIN (name jsonb_path_ops)` index is **not** created by default;
  it serves `@>` containment queries we don't need for master lists.

### Generic CRUD exposure

- **Write:** the localised field is part of the entity graph; generic
  create/update persists the whole row — no fan-out.
- **List/sort by current culture:** map the runtime culture to the expression:

```csharp
IQueryable<T> ordered = culture == "ar"
    ? query.OrderBy(x => x.Name.Ar)
    : query.OrderBy(x => x.Name.En);   // -> ORDER BY name ->> 'ar' | 'en'
```

- **Filter/search:** `query.Where(x => x.Name.Ar.Contains(term))` →
  `name ->> 'ar' ILIKE '%term%'`, served by the trigram index above.

## Consequences

### Positive

- Fewer joins; localised reads are single-row. Simpler, faster list/sort/search.
- One reusable `LocalizedText` type and one config helper across ~64 masters.
- Generic CRUD stays generic — no per-entity translation plumbing.
- Translations can't be orphaned; they're atomic with their owner row.
- One JSON shape shared by DB, API, and the Angular form.

### Negative / costs

- **Adding a language** (e.g. `fr`) is a data migration to backfill the new key
  across rows and add expression indexes where needed — vs. pure inserts in
  Option A. Accepted, given fixed `en`/`ar`.
- **"Both languages present"** cannot be a DB constraint as cheaply as a `NOT
  NULL`; enforce it in validation (and optionally a `CHECK (name ? 'en' AND name
  ? 'ar')`).
- Each translatable field is its own `jsonb` column — many columns on wide
  masters (acceptable; they're independent and individually indexable).
- Cross-field / cross-master full-text search needs deliberate indexing rather
  than one central table.

### Neutral

- The API always emits both keys (empty string for untranslated), so clients
  never branch on a missing key.

## Rejected option

**Option A (side `Translations` table) is rejected.** Its normalisation and
zero-schema-change language addition don't outweigh: a join on every localised
read, awkward index-backed sorting/paging, no referential integrity to the
master (polymorphic `EntityId`), poor EF Core fit (no navigation), and a generic
CRUD layer forced to special-case translation fan-out on every write.

## References

- This ADR is the source of truth for the bilingual **JSON shape** and **column
  type**. The [Generic CRUD story](../stories/generic-crud.md) and the
  [Dynamic form story](../stories/dynamic-form.md) both link here.
- Michael Nygard, "Documenting Architecture Decisions" (ADR format).
