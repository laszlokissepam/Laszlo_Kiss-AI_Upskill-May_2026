using Microsoft.EntityFrameworkCore;
using GardenBuddy.Domain.Entities;

namespace GardenBuddy.Infrastructure.Persistence;

public class GardenBuddyDbContext(DbContextOptions<GardenBuddyDbContext> options) : DbContext(options)
{
	public DbSet<Product> Products => Set<Product>();
}
