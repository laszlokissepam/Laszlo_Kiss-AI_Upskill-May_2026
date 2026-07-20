using System.Reflection;
using GardenBuddy.Api.Controllers;
using GardenBuddy.Domain.Entities;
using GardenBuddy.Infrastructure.DependencyInjection;
using GardenBuddy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
const string IntegrationCorsPolicy = "IntegrationFrontend";

builder.Services.AddControllers(options =>
{
    options.Conventions.Add(new PublicControllerConvention());
});
builder.Services.AddCors(options =>
{
    options.AddPolicy(IntegrationCorsPolicy, corsPolicy =>
    {
        corsPolicy
            .WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<GardenBuddyDbContext>();
    try
    {
        await dbContext.Database.MigrateAsync();
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("PendingModelChangesWarning", StringComparison.Ordinal))
    {
        await dbContext.Database.EnsureCreatedAsync();
    }

    await SeedProductsIfNeededAsync(dbContext);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors(IntegrationCorsPolicy);
app.MapControllers();

app.Run();

static async Task SeedProductsIfNeededAsync(GardenBuddyDbContext dbContext)
{
    var products = new[]
    {
        new Product
        {
            Name = "Lavender",
            Category = "Plant",
            Description = "Fragrant perennial herb suitable for sunny balconies.",
            Price = 12.99m,
            StockQuantity = 120,
            SunlightRequirement = "Full Sun",
            WateringRequirement = "Low",
            Difficulty = "Beginner",
            IsIndoor = false,
            IsPerennial = true,
            PetSafetyInfo = "Use caution around pets."
        },
        new Product
        {
            Name = "Geranium",
            Category = "Plant",
            Description = "Colorful flowering plant for bright balconies.",
            Price = 9.49m,
            StockQuantity = 80,
            SunlightRequirement = "Full Sun",
            WateringRequirement = "Medium",
            Difficulty = "Beginner",
            IsIndoor = false,
            IsPerennial = false,
            PetSafetyInfo = "Keep away from cats and dogs."
        },
        new Product
        {
            Name = "Rosemary",
            Category = "Herb",
            Description = "Aromatic herb that prefers sunny, dry conditions.",
            Price = 7.99m,
            StockQuantity = 65,
            SunlightRequirement = "Full Sun",
            WateringRequirement = "Low",
            Difficulty = "Beginner",
            IsIndoor = true,
            IsPerennial = true,
            PetSafetyInfo = "Non-toxic in small amounts."
        },
        new Product
        {
            Name = "Peace Lily",
            Category = "Plant",
            Description = "Popular indoor plant that tolerates low light.",
            Price = 14.99m,
            StockQuantity = 40,
            SunlightRequirement = "Partial Shade",
            WateringRequirement = "Medium",
            Difficulty = "Beginner",
            IsIndoor = true,
            IsPerennial = true,
            PetSafetyInfo = "Toxic to pets."
        },
        new Product
        {
            Name = "Basil",
            Category = "Herb",
            Description = "Fast-growing culinary herb.",
            Price = 5.49m,
            StockQuantity = 90,
            SunlightRequirement = "Full Sun",
            WateringRequirement = "Medium",
            Difficulty = "Beginner",
            IsIndoor = true,
            IsPerennial = false,
            PetSafetyInfo = "Pet-friendly."
        }
    };

    var existingNames = await dbContext.Products
        .AsNoTracking()
        .Select(product => product.Name)
        .ToListAsync();

    var existingNameSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
    var missingProducts = products
        .Where(product => !existingNameSet.Contains(product.Name))
        .ToArray();

    if (missingProducts.Length == 0)
    {
        return;
    }

    await dbContext.Products.AddRangeAsync(missingProducts);
    await dbContext.SaveChangesAsync();
}

internal sealed class PublicControllerConvention : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        if (controller.ControllerType == typeof(DialController)
            || controller.ControllerType == typeof(KnowledgeController))
        {
            controller.ApiExplorer.IsVisible = false;
            controller.Actions.Clear();
        }
    }
}
