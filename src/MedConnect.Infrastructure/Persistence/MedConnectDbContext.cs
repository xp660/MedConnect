using MedConnect.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedConnect.Infrastructure.Persistence;

public class MedConnectDbContext : DbContext
{
    public MedConnectDbContext(DbContextOptions<MedConnectDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<ScheduleSlot> ScheduleSlots => Set<ScheduleSlot>();
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MedConnectDbContext).Assembly);
    }
}
