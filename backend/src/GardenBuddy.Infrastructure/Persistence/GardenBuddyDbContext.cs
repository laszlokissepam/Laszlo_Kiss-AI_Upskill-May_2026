using Microsoft.EntityFrameworkCore;

namespace GardenBuddy.Infrastructure.Persistence;

public class GardenBuddyDbContext(DbContextOptions<GardenBuddyDbContext> options) : DbContext(options)
{
}
