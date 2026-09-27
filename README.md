# Bilingual (English / Arabic) data with PostgreSQL `jsonb` — ASP.NET Core

A small, focused example of storing **bilingual text** (English + Arabic) in a single PostgreSQL `jsonb` column with **EF Core 10**, plus the architecture decision record (ADR) behind it.

Instead of a separate translations table, every translatable field is stored as one JSON object:

```json
{ "en": "Oman", "ar": "عُمان" }
```

## Why `jsonb` instead of a translations table?

| | Side `Translations` table | `jsonb` column per field ✅ |
|---|---|---|
| Sort / filter by localized name | Needs a join every time | `ORDER BY name ->> 'ar'` directly on the row |
| Indexing | Can't index ordering by translated value | Expression indexes on `(name ->> 'en')` / `(name ->> 'ar')` |
| EF Core mapping | No real relationship (polymorphic id) | `OwnsOne(...).ToJson()`, one reusable `LocalizedText` type |
| Integrity | Orphaned translations possible | Translation lives and dies with its row |
| Adding a language | No schema change | Data backfill (acceptable: languages are fixed to `en`/`ar`) |

The full reasoning is in [ADR-0001: Bilingual text storage](docs/adr/0001-bilingual-storage.md).

## How it works

```csharp
public class LocalizedText
{
    public string En { get; set; } = "";
    public string Ar { get; set; } = "";
}

public class Country
{
    public int Id { get; set; }
    public LocalizedText Name { get; set; } = new();   // stored as ONE jsonb column
}

// OnModelCreating
b.Entity<Country>().OwnsOne(c => c.Name, n =>
{
    n.ToJson();
    n.Property(p => p.En).HasJsonPropertyName("en");   // lowercase keys so name->>'en' works
    n.Property(p => p.Ar).HasJsonPropertyName("ar");
});
```

EF Core translates `x.Name.Ar` into `name ->> 'ar'`, so sorting and searching by the user's language stays simple.

## Endpoints

| Method | Route | Description |
|---|---|---|
| GET | `/api/countries` | List all countries |
| POST | `/api/countries` | Create a country |

Example request:

```json
POST /api/countries
{ "name": { "en": "Kuwait", "ar": "الكويت" } }
```

Swagger UI: http://localhost:5124/swagger

## Run locally

Requirements: .NET 10 SDK and PostgreSQL.

1. Create `src/HealthIt.Api/appsettings.Development.json` (it's git-ignored) with your connection string:

   ```json
   {
     "ConnectionStrings": {
       "Default": "Host=localhost;Port=5432;Database=Store;Username=postgres;Password=YOUR_PASSWORD"
     }
   }
   ```

2. Run the API. Migrations are applied on startup, and two countries are seeded:

   ```bash
   dotnet run --project src/HealthIt.Api
   ```

## Docs

- [ADR-0001: Bilingual text storage](docs/adr/0001-bilingual-storage.md): the decision, options and trade-offs
- [Story: Generic CRUD layer](docs/stories/generic-crud.md): sorting and searching by culture
- [Story: Angular dynamic form](docs/stories/dynamic-form.md): the `{ en, ar }` form contract

## Tech stack

ASP.NET Core 10 Minimal API · EF Core 10 · Npgsql · PostgreSQL `jsonb` · Swagger
