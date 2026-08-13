using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDb>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply migrations on startup (creates our table in the existing DB) + seed 2 rows.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.Migrate();

    if (!db.Countries.Any())
    {
        db.Countries.AddRange(
            new Country { Name = new LocalizedText { En = "Oman", Ar = "عُمان" } },
            new Country { Name = new LocalizedText { En = "Saudi Arabia", Ar = "السعودية" } });
        db.SaveChanges();
    }
}

app.UseSwagger();
app.UseSwaggerUI();

// READ ALL
app.MapGet("/api/countries", async (AppDb db) => await db.Countries.ToListAsync());

// CREATE
app.MapPost("/api/countries", async (Country input, AppDb db) =>
{
    db.Countries.Add(input);
    await db.SaveChangesAsync();
    return Results.Created($"/api/countries/{input.Id}", input);
});

app.Run();


// ---------- the jsonb idea ----------

// A country's name in two languages.
public class LocalizedText
{
    public string En { get; set; } = "";
    public string Ar { get; set; } = "";
}

public class Country
{
    public int Id { get; set; }
    public LocalizedText Name { get; set; } = new();   // <- this whole object becomes ONE jsonb column
}

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Country> Countries => Set<Country>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // OwnsOne + ToJson() = store the Name object as a single jsonb column: {"en":"...","ar":"..."}
        b.Entity<Country>().OwnsOne(c => c.Name, n =>
        {
            n.ToJson();
            n.Property(p => p.En).HasJsonPropertyName("en");   // lowercase keys so name->>'en' works
            n.Property(p => p.Ar).HasJsonPropertyName("ar");
        });
    }
}
